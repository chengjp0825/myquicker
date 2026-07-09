using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Aurora.Domain.DTO;
using Aurora.Domain.Runtime;
using Aurora.Interop;
using Aurora.Services;
using static Aurora.Interop.NativeMethods;
using Button = System.Windows.Controls.Button;
using DomainPoint = Aurora.Domain.Runtime.Point;
using WpfPoint = System.Windows.Point;

namespace Aurora.UI;

/// <summary>
/// 唤醒菜单（全局单例，预热常驻）。显式实现 <see cref="IMenuPresenter"/>，
/// 只负责渲染与动画，不参与唤醒策略。显隐走「屏幕外瞬移 + Opacity」，禁用 Show/Hide/Visibility，
/// 避开 DWM 表面重新分配延迟。极速唤醒渲染规范见 docs/03-ui-and-styling.md §7。
/// </summary>
public partial class MainWindow : Window, IMenuPresenter
{
    private ActionExecutor _executor;
    private CommandContext _commandContext;
    private Preferences _preferences;

    private EventHandler? _opened;
    private EventHandler? _closed;
    private EventHandler? _dismissRequested;

    /// <summary>
    /// Invoked when the gear button is clicked (wired by App to open the
    /// settings center).
    /// </summary>
    public Action? OpenSettingsAction { get; set; }

    /// <summary>菜单当前是否可见。窗口预热后 IsVisible 恒为 true，故用独立标志跟踪显隐。</summary>
    private bool _isAwake;

    /// <summary>关闭动画进行中，防止重复触发 Dismiss。</summary>
    private bool _isClosing;

    /// <summary>动作执行中，防止用户在菜单关闭动画期间重复点击其他按钮。</summary>
    private bool _actionExecutionInProgress;

    // KI-12：低级键盘钩子（菜单可见时挂载，Esc/Enter/方向键）。
    private IntPtr _keyboardHookId = IntPtr.Zero;
    private NativeMethods.LowLevelKeyboardProc? _keyboardProc;
    private int _focusedButtonIndex = -1;

    // KI-13：WinEvent 钩子（常驻，监听前台变化，菜单可见时 Alt-Tab 触发 Dismiss）。
    private IntPtr _foregroundHook = IntPtr.Zero;
    private NativeMethods.WinEventProc? _winEventProc;

    /// <summary>
    /// 运行时构造函数：所有依赖由组合根注入，View 内部禁止自行构造服务。
    /// </summary>
    public MainWindow(
        ActionExecutor executor,
        CommandContext commandContext,
        Preferences preferences,
        List<MenuGroup> menuGroups)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _commandContext = commandContext ?? throw new ArgumentNullException(nameof(commandContext));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));

        InitializeComponent();

        // 为唤醒动画准备缩放变换（默认 1,1）
        RootBorder.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
        RootBorder.RenderTransform = new ScaleTransform(1, 1);

        // 关键视觉参数从统一配置注入；ApplyMenuSettings 同时供 SettingsWindow 应用后即时刷新。
        ApplyMenuSettings(_preferences.Menu);
        // 预绑定动作列表（构造注入，无 IO）。唤醒时不再重绑。
        RefreshActions(menuGroups ?? new List<MenuGroup>());
    }

    /// <summary>
    /// 设置保存后重新绑定运行时依赖：组合根重建 ActionExecutor、CommandContext 与菜单设置后，
    /// 通过此接缝刷新主窗口，避免就地修补已有运行时对象。
    /// </summary>
    internal void RebindRuntime(
        ActionExecutor executor,
        CommandContext commandContext,
        Preferences preferences,
        List<MenuGroup> menuGroups)
    {
        _executor = executor ?? throw new ArgumentNullException(nameof(executor));
        _commandContext = commandContext ?? throw new ArgumentNullException(nameof(commandContext));
        _preferences = preferences ?? throw new ArgumentNullException(nameof(preferences));
        ApplyMenuSettings(_preferences.Menu);
        RefreshActions(menuGroups ?? new List<MenuGroup>());
    }

    #region IMenuPresenter（显式实现，避免与 Window/UIElement 成员冲突）

    /// <summary>菜单当前是否处于唤醒（可见）状态。</summary>
    internal bool IsAwake => _isAwake;

    bool IMenuPresenter.IsVisible => _isAwake;

    event EventHandler? IMenuPresenter.Opened
    {
        add => _opened += value;
        remove => _opened -= value;
    }

    event EventHandler? IMenuPresenter.Closed
    {
        add => _closed += value;
        remove => _closed -= value;
    }

    event EventHandler? IMenuPresenter.DismissRequested
    {
        add => _dismissRequested += value;
        remove => _dismissRequested -= value;
    }

    void IMenuPresenter.ShowAt(DomainPoint location) => WakeUp(location);

    void IMenuPresenter.Dismiss() => DismissMenu();

    private void RaiseDismissRequested() => _dismissRequested?.Invoke(this, EventArgs.Empty);

    #endregion

    /// <summary>
    /// 把 Menu 组参数刷到当前窗口（尺寸/背景/圆角/按钮色）。
    /// 构造时与设置页“应用设置”后共用此路径，使 Menu 改动无需重启即可生效。
    /// </summary>
    internal void ApplyMenuSettings(MenuSettings menu)
    {
        // 窗口 = 内容区 + 24 DIP（左右/上下各 12 DIP 投影边距）
        Width = menu.Width + 24;
        Height = menu.Height + 24;
        RootBorder.Background = BrushHelper.SafeToBrush(menu.Background, System.Windows.Media.Brushes.Transparent);
        RootBorder.CornerRadius = new CornerRadius(menu.CornerRadius);
        ShadowBorder.CornerRadius = new CornerRadius(menu.CornerRadius + 12);
        // 按钮背景色经 DynamicResource 注入样式（MenuButtonStyle 引用 MenuButtonBackgroundBrush/...Hover）。
        Resources["MenuButtonBackgroundBrush"] = BrushHelper.SafeToBrush(menu.ButtonBackground, System.Windows.Media.Brushes.Transparent);
        Resources["MenuButtonHoverBackgroundBrush"] = BrushHelper.SafeToBrush(menu.ButtonHoverBackground, System.Windows.Media.Brushes.Transparent);

        const double rootPaddingH = 24; // 左 14 + 右 10
        const double rootPaddingV = 24; // 上 12 + 下 12
        const double bottomBarHeight = 44;
        const double buttonMargin = 10; // 左右或上下各 5
        const double safetyGap = 2;     // 防止顶满边界导致 WrapPanel 提前换行

        int columns = Math.Clamp(menu.GridColumns, 2, 3);
        int visibleRows = columns; // 2×2 或 3×3

        double contentWidth = Math.Max(menu.Width - rootPaddingH, 1);
        double actionsHeight = Math.Max(menu.Height - rootPaddingV - bottomBarHeight, 1);

        double cellWidth = contentWidth / columns;
        double cellHeight = actionsHeight / visibleRows;

        double buttonWidth = cellWidth - buttonMargin - safetyGap;
        double buttonHeight = cellHeight - buttonMargin - safetyGap;

        Resources["MenuButtonWidth"] = Math.Max(buttonWidth, 32);
        Resources["MenuButtonHeight"] = Math.Max(buttonHeight, 32);
    }

    /// <summary>从传入的 MenuGroups 重绑动作列表（无 IO）。构造时与设置页保存后调用。</summary>
    internal void RefreshActions(List<MenuGroup> menuGroups)
    {
        ActionsControl.ItemsSource = menuGroups.SelectMany(g => g.Actions).ToList();
    }

    /// <summary>
    /// Once the native HWND exists, mark the window as No-Activate so it
    /// never steals focus from the application the user is working in.
    /// </summary>
    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).Handle;
        int exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_NOACTIVATE);

        // KI-13：挂载 WinEvent 钩子监听前台窗口变化（常驻）。菜单可见时 Alt-Tab/Win+D 触发 Dismiss。
        // WINEVENT_OUTOFCONTEXT：回调在挂载线程消息循环（主 UI 线程）；SKIPOWNPROCESS：跳过自身进程。
        _winEventProc = OnForegroundChanged;
        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _winEventProc, 0, 0,
            NativeMethods.WINEVENT_OUTOFCONTEXT | NativeMethods.WINEVENT_SKIPOWNPROCESS);
    }

    /// <summary>KI-13：前台窗口变化时，若菜单可见则请求关闭（Alt-Tab/Win+D 不再滞留菜单）。</summary>
    private void OnForegroundChanged(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
    {
        if (!_isAwake)
            return;
        // 回调在主 UI 线程消息循环；防御性用 Dispatcher。
        Dispatcher.BeginInvoke(() => RaiseDismissRequested());
    }

    /// <summary>
    /// 唤醒：定位 + 淡入缩放动画 + 重申置顶不抢焦。零 IO、零 Show（docs/03 §7.3）。
    /// </summary>
    private void WakeUp(DomainPoint location)
    {
        // location 是 WakeOrchestrator 夹取后的内容左上角 DIP（基于 MenuWidth 算）。
        // KI-14：RootBorder Margin=12，窗口左 = 内容左 - 12，使内容中心对齐光标且贴边不裁。
        Left = location.X - 12;
        Top = location.Y - 12;

        // KI-16：已可见时二次唤醒，仅瞬移位置，不重放动画（避免闪）。
        if (_isAwake)
            return;

        // 清除可能残留的动画，确保每次从初始状态开始
        _isClosing = false;
        BeginAnimation(OpacityProperty, null);
        var scale = (ScaleTransform)RootBorder.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        scale.ScaleX = 0.95;
        scale.ScaleY = 0.95;
        Opacity = 0;
        _isAwake = true;

        // KI-12：挂载键盘钩子（菜单可见时拦 Esc/Enter/方向键）+ 聚焦第一个按钮。
        EnsureKeyboardHookMounted();
        Dispatcher.BeginInvoke(new Action(FocusFirstButton), System.Windows.Threading.DispatcherPriority.Background);

        var storyboard = BuildOpenStoryboard();
        storyboard.Completed += (_, _) => _opened?.Invoke(this, EventArgs.Empty);
        storyboard.Begin();

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    /// <summary>
    /// 关闭菜单：播放淡出缩小动画后丢出屏幕外，并触发 Closed 事件。
    /// 动画期间再次调用会被忽略，避免状态冲突。
    /// </summary>
    private void DismissMenu()
    {
        if (!_isAwake || _isClosing)
            return;

        _isClosing = true;

        var storyboard = BuildCloseStoryboard();
        storyboard.Completed += (_, _) =>
        {
            BeginAnimation(OpacityProperty, null);
            var scale = (ScaleTransform)RootBorder.RenderTransform;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

            Opacity = 0;
            Left = -9999;
            Top = -9999;
            _isAwake = false;
            _isClosing = false;

            // KI-12：菜单关闭后卸载键盘钩子（仅在菜单可见时才监听键盘）。
            EnsureKeyboardHookUnmounted();

            _closed?.Invoke(this, EventArgs.Empty);
        };
        storyboard.Begin();
    }

    /// <summary>菜单内容区在当前屏幕坐标系下的边界（已含 DPI 逻辑坐标转换）。</summary>
    internal Rect ContentBounds
    {
        get
        {
            var contentBounds = RootBorder.TransformToAncestor(this)
                .TransformBounds(new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight));
            contentBounds.Offset(Left, Top);
            return contentBounds;
        }
    }

    /// <summary>
    /// A menu button was clicked: request dismissal first, then run the action.
    /// (Button clicks land inside the window, so OnAnyMouseDown won't sleep it.)
    /// </summary>
    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ActionItem item)
        {
            if (_actionExecutionInProgress)
                return;

            _actionExecutionInProgress = true;
            _ = ExecuteActionAsync(item);
        }
    }

    /// <summary>
    /// 异步执行动作：先请求关闭菜单并等待关闭动画完成，再离屏执行命令，最后回到 UI 线程处理结果。
    /// 等待 Closed 可确保截图等覆盖层出现时菜单已完全离开屏幕，避免把菜单截进底图或遮挡覆盖层。
    /// 使用 fire-and-forget Task 而非 async void，避免未处理异常直接崩进程。
    /// </summary>
    private async Task ExecuteActionAsync(ActionItem item)
    {
        try
        {
            await WaitForMenuClosedAsync().ConfigureAwait(false);

            ActionResult result = await _executor.ExecuteAsync(_commandContext, item).ConfigureAwait(false);
            await Dispatcher.InvokeAsync(() => HandleActionResult(result));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 执行动作失败: {ex.Message}");
            await Dispatcher.InvokeAsync(() => Toast.Show("动作执行失败", 3000));
        }
        finally
        {
            _actionExecutionInProgress = false;
        }
    }

    /// <summary>
    /// 等待菜单完全关闭（<see cref="_closed"/> 事件）。
    /// 若菜单已关闭则立即返回；否则发起 <see cref="DismissRequested"/> 并订阅 Closed。
    /// </summary>
    private Task WaitForMenuClosedAsync()
    {
        if (!_isAwake)
            return Task.CompletedTask;

        var tcs = new TaskCompletionSource<object?>();
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            _closed -= handler;
            tcs.TrySetResult(null);
        };
        _closed += handler;
        RaiseDismissRequested();
        return tcs.Task;
    }

    /// <summary>
    /// 解释 ActionExecutor 返回的纯数据结果，仅处理轻量 UI 副作用（Toast）。
    /// 截图工作流已由 <see cref="SnippingCommand"/> 内部触发，不再在此解释。
    /// </summary>
    private void HandleActionResult(ActionResult result)
    {
        if (result.ToastMessage is not null)
            Toast.Show(result.ToastMessage, 3000);
    }

    /// <summary>
    /// Gear button: request dismissal, then open the settings center.
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        RaiseDismissRequested();
        OpenSettingsAction?.Invoke();
    }

    /// <summary>
    /// 物理屏幕坐标（Point，像素）转逻辑坐标（DIP），
    /// 供外部点击检测复用，统一 DPI 处理入口。
    /// </summary>
    internal WpfPoint ToLogical(DomainPoint physical)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            return source.CompositionTarget.TransformFromDevice.Transform(new WpfPoint(physical.X, physical.Y));
        return new WpfPoint(physical.X, physical.Y);
    }

    private Storyboard BuildOpenStoryboard()
    {
        var storyboard = new Storyboard();

        var opacityAnimation = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(opacityAnimation, this);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacityAnimation);

        var scaleXAnimation = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleXAnimation, RootBorder);
        Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("RenderTransform.ScaleX"));
        storyboard.Children.Add(scaleXAnimation);

        var scaleYAnimation = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(150))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(scaleYAnimation, RootBorder);
        Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("RenderTransform.ScaleY"));
        storyboard.Children.Add(scaleYAnimation);

        return storyboard;
    }

    private Storyboard BuildCloseStoryboard()
    {
        var storyboard = new Storyboard();

        var opacityAnimation = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(opacityAnimation, this);
        Storyboard.SetTargetProperty(opacityAnimation, new PropertyPath(OpacityProperty));
        storyboard.Children.Add(opacityAnimation);

        var scaleXAnimation = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleXAnimation, RootBorder);
        Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("RenderTransform.ScaleX"));
        storyboard.Children.Add(scaleXAnimation);

        var scaleYAnimation = new DoubleAnimation(1, 0.95, TimeSpan.FromMilliseconds(120))
        {
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(scaleYAnimation, RootBorder);
        Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("RenderTransform.ScaleY"));
        storyboard.Children.Add(scaleYAnimation);

        return storyboard;
    }

    // -----------------------------------------------------------------------
    // KI-12/KI-13 钩子清理
    // -----------------------------------------------------------------------

    /// <summary>窗口关闭时清理所有钩子（KI-12 键盘 + KI-13 WinEvent）。</summary>
    protected override void OnClosed(EventArgs e)
    {
        EnsureKeyboardHookUnmounted();
        if (_foregroundHook != IntPtr.Zero)
        {
            NativeMethods.UnhookWinEvent(_foregroundHook);
            _foregroundHook = IntPtr.Zero;
            _winEventProc = null;
        }
        base.OnClosed(e);
    }

    // ---------------- KI-12 键盘钩子 ----------------

    private void EnsureKeyboardHookMounted()
    {
        if (_keyboardHookId != IntPtr.Zero)
            return;
        _keyboardProc = KeyboardHookCallback;
        using var process = Process.GetCurrentProcess();
        var mainModule = process.MainModule
            ?? throw new InvalidOperationException("Could not obtain the process main module.");
        IntPtr hMod = NativeMethods.GetModuleHandle(mainModule.ModuleName);
        _keyboardHookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_KEYBOARD_LL, _keyboardProc, hMod, 0);
    }

    private void EnsureKeyboardHookUnmounted()
    {
        if (_keyboardHookId == IntPtr.Zero)
            return;
        NativeMethods.UnhookWindowsHookEx(_keyboardHookId);
        _keyboardHookId = IntPtr.Zero;
        _keyboardProc = null;
    }

    private IntPtr KeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode < 0)
            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

        // KI-3 同款异常保护：原生回调异常会摘钩，兜底放行。
        try
        {
            if (!_isAwake)
                return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

            int msg = wParam.ToInt32();
            if (msg != NativeMethods.WM_KEYDOWN && msg != NativeMethods.WM_SYSKEYDOWN)
                return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);

            var info = Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
            int vk = info.vkCode;

            bool handled = false;
            if (vk == NativeMethods.VK_ESCAPE)
            {
                Dispatcher.BeginInvoke(() => RaiseDismissRequested());
                handled = true;
            }
            else if (vk == NativeMethods.VK_RETURN)
            {
                Dispatcher.BeginInvoke(() => GetFocusedButton()?.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)));
                handled = true;
            }
            else if (vk == NativeMethods.VK_LEFT)
            {
                Dispatcher.BeginInvoke(() => MoveFocusedButton(-1));
                handled = true;
            }
            else if (vk == NativeMethods.VK_RIGHT)
            {
                Dispatcher.BeginInvoke(() => MoveFocusedButton(1));
                handled = true;
            }
            else if (vk == NativeMethods.VK_UP)
            {
                int cols = Math.Clamp(_preferences.Menu.GridColumns, 2, 3);
                Dispatcher.BeginInvoke(() => MoveFocusedButton(-cols));
                handled = true;
            }
            else if (vk == NativeMethods.VK_DOWN)
            {
                int cols = Math.Clamp(_preferences.Menu.GridColumns, 2, 3);
                Dispatcher.BeginInvoke(() => MoveFocusedButton(cols));
                handled = true;
            }

            return handled ? (IntPtr)1 : NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[KeyboardHook] exception swallowed: {ex.GetType().Name}: {ex.Message}");
            return NativeMethods.CallNextHookEx(_keyboardHookId, nCode, wParam, lParam);
        }
    }

    /// <summary>聚焦第一个动作按钮（键盘导航起点）。容器生成后调用。</summary>
    private void FocusFirstButton()
    {
        _focusedButtonIndex = 0;
        FocusButton(_focusedButtonIndex);
    }

    private void FocusButton(int index)
    {
        if (index < 0 || index >= ActionsControl.Items.Count)
            return;
        _focusedButtonIndex = index;
        var container = ActionsControl.ItemContainerGenerator.ContainerFromIndex(index);
        var button = container is null ? null : FindVisualChild<Button>(container);
        button?.Focus();
    }

    private void MoveFocusedButton(int delta)
    {
        if (_focusedButtonIndex < 0)
        {
            FocusFirstButton();
            return;
        }
        int newIndex = _focusedButtonIndex + delta;
        newIndex = Math.Clamp(newIndex, 0, Math.Max(0, ActionsControl.Items.Count - 1));
        FocusButton(newIndex);
    }

    private Button? GetFocusedButton()
    {
        if (_focusedButtonIndex < 0 || _focusedButtonIndex >= ActionsControl.Items.Count)
            return null;
        var container = ActionsControl.ItemContainerGenerator.ContainerFromIndex(_focusedButtonIndex);
        return container is null ? null : FindVisualChild<Button>(container);
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) return t;
            var result = FindVisualChild<T>(child);
            if (result is not null) return result;
        }
        return null;
    }
}

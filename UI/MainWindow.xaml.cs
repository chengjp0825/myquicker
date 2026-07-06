using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.Interop;
using MyQuicker.Services;
using static MyQuicker.Interop.NativeMethods;
using Button = System.Windows.Controls.Button;
using DomainPoint = MyQuicker.Domain.Runtime.Point;
using WpfPoint = System.Windows.Point;

namespace MyQuicker.UI;

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

        int columns = Math.Clamp(menu.GridColumns, 2, 3);
        int visibleRows = columns; // 2×2 或 3×3

        double contentWidth = Math.Max(menu.Width - rootPaddingH, 1);
        double actionsHeight = Math.Max(menu.Height - rootPaddingV - bottomBarHeight, 1);

        double cellWidth = contentWidth / columns;
        double cellHeight = actionsHeight / visibleRows;

        double buttonWidth = cellWidth - buttonMargin;
        double buttonHeight = cellHeight - buttonMargin;

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
    }

    /// <summary>
    /// 唤醒：定位 + 淡入缩放动画 + 重申置顶不抢焦。零 IO、零 Show（docs/03 §7.3）。
    /// </summary>
    private void WakeUp(DomainPoint location)
    {
        // location 已由 WakeOrchestrator 计算为 DIP 且夹取到屏幕内。
        Left = location.X;
        Top = location.Y;

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
}

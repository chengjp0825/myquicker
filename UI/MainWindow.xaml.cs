using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using MyQuicker.Interop;
using MyQuicker.Models;
using MyQuicker.Services;
using static MyQuicker.Interop.NativeMethods;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace MyQuicker.UI;

/// <summary>
/// 唤醒菜单（全局单例，预热常驻）。显隐走「屏幕外瞬移 + Opacity」，禁用 Show/Hide/Visibility，
/// 避开 DWM 表面重新分配延迟。极速唤醒渲染规范见 docs/03-ui-and-styling.md §7。
/// </summary>
public partial class MainWindow : Window
{
    private readonly ActionExecutor _executor;

    /// <summary>
    /// Invoked when the gear button is clicked (wired by App to open the
    /// settings center).
    /// </summary>
    public Action? OpenSettingsAction { get; set; }

    /// <summary>菜单当前是否可见。窗口预热后 IsVisible 恒为 true，故用独立标志跟踪显隐。</summary>
    private bool _isAwake;

    public MainWindow()
    {
        InitializeComponent();

        // 为唤醒动画准备缩放变换（默认 1,1）
        RootBorder.RenderTransformOrigin = new Point(0.5, 0.5);
        RootBorder.RenderTransform = new ScaleTransform(1, 1);

        _executor = new ActionExecutor();

        // 关键视觉参数从统一配置注入；ApplyMenuSettings 同时供 SettingsWindow 应用后即时刷新。
        ApplyMenuSettings(SettingsManager.Instance.Settings.Menu);
        // 预绑定动作列表（内存缓存，无 IO）。唤醒时不再重绑。
        RefreshActions();
    }

    /// <summary>
    /// 把 Menu 组参数刷到当前窗口（尺寸/背景/圆角/按钮色）。
    /// 构造时与设置页“应用设置”后共用此路径，使 Menu 改动无需重启即可生效。
    /// </summary>
    internal void ApplyMenuSettings(MenuSettings menu)
    {
        Width = menu.Width + 24;
        Height = menu.Height + 24;
        RootBorder.Background = BrushHelper.ToBrush(menu.Background);
        RootBorder.CornerRadius = new CornerRadius(menu.CornerRadius);
        ShadowBorder.CornerRadius = new CornerRadius(menu.CornerRadius + 12);
        // 按钮背景色经 DynamicResource 注入样式（MenuButtonStyle 引用 MenuButtonBackgroundBrush/...Hover）。
        Resources["MenuButtonBackgroundBrush"] = BrushHelper.ToBrush(menu.ButtonBackground);
        Resources["MenuButtonHoverBackgroundBrush"] = BrushHelper.ToBrush(menu.ButtonHoverBackground);
    }

    /// <summary>从 ActionStore 内存缓存重绑动作列表（无 IO）。构造时与设置页保存后调用。</summary>
    internal void RefreshActions()
    {
        ActionsControl.ItemsSource = _executor.GetActions();
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
    /// Hook event handler: wake the menu centered on the cursor. Per SPEC.md §4.2.
    /// 已唤醒时再次唤醒无效（防重入）；截屏覆盖层开启时不抢唤醒。
    /// </summary>
    internal void OnHookWakeupClick(object? sender, POINT e)
    {
        if (_isAwake)
            return;

        // 截屏覆盖层开启时不抢唤醒，避免自定义截图时画圈寻位/按键误触菜单。
        if (System.Windows.Application.Current?.Windows.OfType<ScreenshotWindow>().Any() == true)
            return;

        // 设置页开启时不抢唤醒，避免编辑配置时画圈/按键误触菜单、再经齿轮开出第二个设置页。
        if (System.Windows.Application.Current?.Windows.OfType<SettingsWindow>().Any() == true)
            return;

        WakeUp(e);
    }

    /// <summary>
    /// 唤醒：光标居中定位 + 淡入缩放动画 + 重申置顶不抢焦。零 IO、零 Show（docs/03 §7.3）。
    /// </summary>
    private void WakeUp(POINT e)
    {
        PositionAtCursor(e); // 先定位再显，避免可见后跳位

        // 清除可能残留的动画，确保每次从初始状态开始
        BeginAnimation(OpacityProperty, null);
        var scale = (ScaleTransform)RootBorder.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        scale.ScaleX = 0.95;
        scale.ScaleY = 0.95;
        Opacity = 0;
        _isAwake = true;

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

        storyboard.Begin();

        var hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
    }

    /// <summary>
    /// 睡眠：透明 + 丢出屏幕外。禁用 Hide()/Visibility（docs/03 §7.3）。
    /// </summary>
    internal void Sleep()
    {
        BeginAnimation(OpacityProperty, null);
        var scale = (ScaleTransform)RootBorder.RenderTransform;
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, null);

        Opacity = 0;
        Left = -9999;
        Top = -9999;
        _isAwake = false;
    }

    /// <summary>
    /// Hook event handler: if any mouse button is pressed outside the
    /// window bounds while we are awake, sleep the menu. The click itself
    /// is not blocked, so it also reaches the underlying application.
    /// </summary>
    internal void OnAnyMouseDown(object? sender, POINT e)
    {
        if (!_isAwake)
            return;

        var p = ToLogical(e);
        var contentBounds = RootBorder.TransformToAncestor(this)
            .TransformBounds(new Rect(0, 0, RootBorder.ActualWidth, RootBorder.ActualHeight));
        contentBounds.Offset(Left, Top);
        if (!contentBounds.Contains(p))
            Sleep();
    }

    /// <summary>
    /// A menu button was clicked: sleep the menu first, then run the action.
    /// (Button clicks land inside the window, so OnAnyMouseDown won't sleep it.)
    /// </summary>
    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        Sleep();

        if (sender is Button btn && btn.DataContext is ActionItem item)
        {
            _executor.Execute(item);
        }
    }

    /// <summary>
    /// Gear button: sleep the menu, then open the settings center.
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Sleep();
        OpenSettingsAction?.Invoke();
    }

    /// <summary>
    /// 物理屏幕坐标（POINT，像素）转逻辑坐标（DIP），
    /// 供 OnAnyMouseDown 与 PositionAtCursor 复用，统一 DPI 处理入口。
    /// </summary>
    private Point ToLogical(POINT physical)
    {
        var source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
            return source.CompositionTarget.TransformFromDevice.Transform(new Point(physical.X, physical.Y));
        return new Point(physical.X, physical.Y);
    }

    private void PositionAtCursor(POINT e)
    {
        var p = ToLogical(e);
        // 用 ActualWidth/Height（预热后已布局完成），未布局时回退 Width/Height。
        double w = ActualWidth > 0 ? ActualWidth : Width;
        double h = ActualHeight > 0 ? ActualHeight : Height;
        Left = p.X - w / 2;
        Top = p.Y - h / 2;
    }
}

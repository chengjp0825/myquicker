using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using MyQuicker.Interop;
using MyQuicker.Models;
using MyQuicker.Services;
using static MyQuicker.Interop.NativeMethods;
using Button = System.Windows.Controls.Button;
using Point = System.Windows.Point;

namespace MyQuicker.UI;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private readonly ActionExecutor _executor;

    /// <summary>
    /// Invoked when the gear button is clicked (wired by App to open the
    /// settings center).
    /// </summary>
    public Action? OpenSettingsAction { get; set; }

    public MainWindow()
    {
        InitializeComponent();

        _executor = new ActionExecutor();

        // 关键视觉参数从统一配置注入（Per SPEC 重构 Step 3）。
        // 按钮背景色经 DynamicResource 注入样式（MenuButtonStyle 引用 MenuButtonBackgroundBrush/...Hover）。
        var menu = SettingsManager.Instance.Settings.Menu;
        Width = menu.Width;
        Height = menu.Height;
        RootBorder.Background = BrushHelper.ToBrush(menu.Background);
        RootBorder.CornerRadius = new CornerRadius(menu.CornerRadius);
        Resources["MenuButtonBackgroundBrush"] = BrushHelper.ToBrush(menu.ButtonBackground);
        Resources["MenuButtonHoverBackgroundBrush"] = BrushHelper.ToBrush(menu.ButtonHoverBackground);
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
    /// Hook event handler: position the window centered on the cursor and
    /// show it (without activating). Per SPEC.md §4.2.
    /// </summary>
    internal void OnHookWakeupClick(object? sender, POINT e)
    {
        PositionAtCursor(e); // place before show (avoids flicker once hwnd exists)

        // Hot-reload actions from disk on every wake-up so edits to
        // actions.json are reflected without restarting the app.
        ActionsControl.ItemsSource = _executor.GetActions();

        Show();
        PositionAtCursor(e); // refine DPI now that the hwnd exists
    }

    /// <summary>
    /// Hook event handler: if any mouse button is pressed outside the
    /// window bounds while we are visible, hide the menu. The click itself
    /// is not blocked, so it also reaches the underlying application.
    /// </summary>
    internal void OnAnyMouseDown(object? sender, POINT e)
    {
        if (!IsVisible)
            return;

        var p = ToLogical(e);
        if (p.X < Left || p.X > Left + Width || p.Y < Top || p.Y > Top + Height)
            Hide();
    }

    /// <summary>
    /// A menu button was clicked: hide the menu first, then run the action.
    /// (Button clicks land inside the window, so OnAnyMouseDown won't hide it.)
    /// </summary>
    private void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();

        if (sender is Button btn && btn.DataContext is ActionItem item)
        {
            _executor.Execute(item);
        }
    }

    /// <summary>
    /// Gear button: hide the menu, then open the settings center.
    /// </summary>
    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        Hide();
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
        Left = p.X - Width / 2;
        Top = p.Y - Height / 2;
    }
}

using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyQuicker.Interop;
using MyQuicker.Services;
using static MyQuicker.Interop.NativeMethods;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using Clipboard = System.Windows.Clipboard;

namespace MyQuicker.UI;

/// <summary>
/// Full-screen snipping overlay: hover to auto-detect window edges, or
/// left-drag to select a custom region. ESC / right-click cancels. The
/// captured crop is written to the clipboard on mouse-up. Per SPEC 8B.
/// </summary>
public partial class ScreenshotWindow : Window
{
    private readonly BitmapSource _baseImage;
    private readonly Rectangle _bounds;
    private bool _isDragging;
    private bool _isPotentialDrag;
    private Point _mouseDownPoint;

    /// <summary>
    /// 寻边扫描到的目标窗口矩形（窗口本地坐标，与 e.GetPosition(this) 同帧）。
    /// 用于"智能快照"：松开时若未拖拽且有红框，则截红框区域。null 表示无有效目标。
    /// </summary>
    private Rect? _currentSelection;

    /// <summary>判定"点击 vs 拖拽"的位移阈值（DIP）。超过即升级为拖拽。值取自 SettingsModel.Snipping.DragThreshold。</summary>
    private readonly double DragThreshold = SettingsManager.Instance.Settings.Snipping.DragThreshold;

    public ScreenshotWindow(BitmapSource source, Rectangle bounds)
    {
        InitializeComponent();

        _baseImage = source;
        _bounds = bounds;

        // Physical-coordinate strong binding: span every monitor exactly.
        Left = bounds.X;
        Top = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        BackgroundImage.Source = source;
        ScreenGeometry.Rect = new Rect(0, 0, bounds.Width, bounds.Height);

        // 关键视觉参数从统一配置注入（Per SPEC 重构 Step 3）。
        var snipping = SettingsManager.Instance.Settings.Snipping;
        Background = BrushHelper.ToBrush(snipping.OverlayBackground);
        MaskPath.Fill = BrushHelper.ToBrush(snipping.MaskColor);
        HighlightBorder.BorderBrush = BrushHelper.ToBrush(snipping.BorderColor);
        HighlightBorder.BorderThickness = new Thickness(snipping.BorderThickness);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
            Close();
        base.OnPreviewKeyDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        Close();
        base.OnMouseRightButtonDown(e);
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Console.WriteLine("DEBUG: MouseDown Triggered");
        Console.Out.Flush();
        e.Handled = true; // 阻止事件继续下传给底层窗口

        EnsureHitTestVisible();

        // 解耦点击与拖拽：按下时只记录起点，默认不进入拖拽；
        // 是否升级为拖拽由 OnMouseMove 的阈值判定决定。寻边逻辑继续运行。
        _mouseDownPoint = e.GetPosition(this);
        _isPotentialDrag = false;
        _isDragging = false;
        // 不在此处 CaptureMouse：仅当跨过阈值升级为拖拽时才捕获，保证点击/拖拽原子解耦。

        base.OnMouseLeftButtonDown(e);
    }

    /// <summary>
    /// 移除窗口的 WS_EX_TRANSPARENT（若被寻边逻辑临时加上而未还原），
    /// 使窗口重新接收鼠标命中测试。
    /// </summary>
    private void EnsureHitTestVisible()
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex & ~NativeMethods.WS_EX_TRANSPARENT);
        Console.WriteLine("DEBUG: WS_EX_TRANSPARENT cleared");
        Console.Out.Flush();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);

        // 阈值判定：按下但尚未拖拽时，位移超过阈值则升级为拖拽模式。
        if (!_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            double delta = (p - _mouseDownPoint).Length;
            if (delta > DragThreshold)
            {
                _isDragging = true;
                _isPotentialDrag = true;
                _currentSelection = null;   // 清空寻边红框，切换到拖拽选区
                CaptureMouse();             // 升级为拖拽时才捕获，保证松开事件可达
            }
        }

        if (_isDragging)
        {
            // 拖拽模式：从按下点到当前点画选区（min/abs 归一化，支持反向拖拽）。
            ApplySelection(new Rect(
                Math.Min(_mouseDownPoint.X, p.X),
                Math.Min(_mouseDownPoint.Y, p.Y),
                Math.Abs(p.X - _mouseDownPoint.X),
                Math.Abs(p.Y - _mouseDownPoint.Y)));
            this.Cursor = Cursors.Cross;
        }
        else
        {
            // 寻边模式（无论是否按下，只要未升级为拖拽，就继续寻边）。
            NativeMethods.GetCursorPos(out POINT pt);
            IntPtr target = WindowUnderCursor(pt);
            if (target == IntPtr.Zero || IsDesktopWindow(target))
            {
                ClearSelection();               // 无有效目标（含桌面背景）：清空选区
                this.Cursor = Cursors.Cross;
            }
            else
            {
                RECT rect;
                if (NativeMethods.DwmGetWindowAttribute(target, NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS,
                        out rect, Marshal.SizeOf<RECT>()) != 0)
                {
                    // DWM unavailable (older OS / stripped window): fall back.
                    NativeMethods.GetWindowRect(target, out rect);
                }

                // 物理窗口矩形转窗口本地坐标（96dpi 下与 DIP 1:1，与 e.GetPosition(this) 同帧）。
                Rect selection = new Rect(rect.Left - _bounds.X, rect.Top - _bounds.Y,
                                          rect.Right - rect.Left, rect.Bottom - rect.Top);
                _currentSelection = selection;       // 寻边成功：实时存储
                ApplySelection(selection);

                // 视觉引导：悬停在寻边矩形内显示手型（可快照），否则准星（将拖拽）。
                this.Cursor = selection.Contains(p) ? Cursors.Hand : Cursors.Cross;
            }
        }

        base.OnMouseMove(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        Console.WriteLine("DEBUG: MouseUp Triggered");
        Console.Out.Flush();

        if (_isPotentialDrag)
        {
            // 手动拖拽截图：对最终选区矩形（按下点 → 松开点）截图。
            Console.WriteLine("DEBUG: Mode B - Manual Drag Capture");
            Console.Out.Flush();
            Point p = e.GetPosition(this);
            double x0 = Math.Min(_mouseDownPoint.X, p.X);
            double y0 = Math.Min(_mouseDownPoint.Y, p.Y);
            double w = Math.Abs(p.X - _mouseDownPoint.X);
            double h = Math.Abs(p.Y - _mouseDownPoint.Y);
            SettleSelection(new Rect(x0, y0, w, h));
        }
        else if (_currentSelection.HasValue)
        {
            // 智能快照：未拖拽且有红框 → 截红框区域。
            Console.WriteLine("DEBUG: Mode A - Smart Snapshot");
            Console.Out.Flush();
            SettleSelection(_currentSelection.Value);
        }
        else
        {
            // 空点（无红框且未拖拽）：不操作。
            Console.WriteLine("DEBUG: Empty Click - No Capture");
            Console.Out.Flush();
        }

        if (_isDragging)
            ReleaseMouseCapture();
        _isDragging = false;
        _isPotentialDrag = false;

        Close();   // 无论哪种模式，松开左键后必须关闭截图窗口
        base.OnMouseLeftButtonUp(e);
    }

    /// <summary>Applies the selection rect to the cutout geometry and the red border.</summary>
    private void ApplySelection(Rect r)
    {
        CutoutGeometry.Rect = r;

        HighlightBorder.Visibility = System.Windows.Visibility.Visible;
        HighlightBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        HighlightBorder.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        HighlightBorder.Margin = new Thickness(r.X, r.Y, 0, 0);
        HighlightBorder.Width = r.Width;
        HighlightBorder.Height = r.Height;
    }

    /// <summary>
    /// 结算选区：按窗口本地坐标（= 底图像素）裁剪、写剪贴板、钉贴图。
    /// 智能快照（模式 A）与手动拖拽（模式 B 松开时）共用此路径。
    /// </summary>
    private void SettleSelection(Rect selection)
    {
        double x0 = selection.X;
        double y0 = selection.Y;
        double w = selection.Width;
        double h = selection.Height;

        Console.WriteLine($"DEBUG: Capture Rect - w={w}, h={h}");
        Console.Out.Flush();

        if (w > 0 && h > 0)
        {
            // 窗口本地坐标与底图像素 1:1，直接裁剪。
            var crop = new CroppedBitmap(_baseImage, new Int32Rect((int)x0, (int)y0, (int)w, (int)h));
            crop.Freeze();

            // 结算：写剪贴板 + 钉一张贴图常驻窗口。
            // 选区左上角的绝对屏幕坐标 = 虚拟屏原点 + 窗口本地坐标，让贴图落在截图原位。
            // 用 Show()（非模态）打开，确保截图罩 Close() 后贴图窗口仍存活。
            Clipboard.SetImage(crop);
            new PinWindow(crop, _bounds.X + x0, _bounds.Y + y0).Show();
        }
    }

    /// <summary>寻边失败时清空选区：清 _currentSelection、撤回镂空与红框。</summary>
    private void ClearSelection()
    {
        _currentSelection = null;
        CutoutGeometry.Rect = new Rect(0, 0, 0, 0); // 无镂空，整屏暗罩
        HighlightBorder.Visibility = Visibility.Hidden;
    }

    /// <summary>
    /// 判断窗口是否为桌面背景（Progman/WorkerW）。这类窗口不应作为寻边目标，
    /// 否则桌面空白处会被当作"整屏窗口"，使空点变成整屏快照。
    /// </summary>
    private static bool IsDesktopWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return false;
        var sb = new StringBuilder(256);
        NativeMethods.GetClassName(hwnd, sb, sb.Capacity);
        string cls = sb.ToString();
        return cls == "Progman" || cls == "WorkerW";
    }

    /// <summary>
    /// Returns the top-level window under the cursor, temporarily marking
    /// our own overlay as hit-test-transparent (WS_EX_TRANSPARENT) so
    /// WindowFromPoint sees the window beneath us instead of returning
    /// our overlay (which is Topmost and fully opaque).
    /// </summary>
    private IntPtr WindowUnderCursor(POINT pt)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex | NativeMethods.WS_EX_TRANSPARENT);
        IntPtr target = NativeMethods.WindowFromPoint(pt);
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex); // restore immediately
        return target;
    }
}

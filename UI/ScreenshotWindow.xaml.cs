using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyQuicker.Interop;
using MyQuicker.Models;
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

    /// <summary>DIP↔物理像素 缩放系数（取窗口实际渲染 DPI，docs/02 §5）。窗口本地 DIP 与底图物理像素间转换用。初始为 GetDpiForMonitor 估计值，SourceInitialized/DpiChanged 用 TransformToDevice 修正。</summary>
    private double _scaleX;
    private double _scaleY;

    private bool _isDragging;
    private bool _isPotentialDrag;
    private Point _mouseDownPoint;

    /// <summary>
    /// 寻边扫描到的目标窗口矩形（窗口本地坐标，与 e.GetPosition(this) 同帧）。
    /// 用于"智能快照"：松开时若未拖拽且有红框，则截红框区域。null 表示无有效目标。
    /// </summary>
    private Rect? _currentSelection;

    /// <summary>上次寻边命中的窗口句柄，用于"目标变化时"才写日志，避免刷屏。</summary>
    private IntPtr _lastEdgeTarget = IntPtr.Zero;

    /// <summary>判定"点击 vs 拖拽"的位移阈值（DIP）。超过即升级为拖拽。值取自 SettingsModel.Snipping.DragThreshold。</summary>
    private readonly double DragThreshold = SettingsManager.Instance.Settings.Snipping.DragThreshold;

    public ScreenshotWindow(BitmapSource source, Rectangle bounds)
    {
        InitializeComponent();

        _baseImage = source;
        _bounds = bounds;

        // 按 bounds 所在显示器取真实 DPI（主副屏缩放不一致时各屏分别取，docs/02 §5）。
        var (sx, sy) = DpiHelper.ScaleForBounds(bounds);
        _scaleX = sx;
        _scaleY = sy;

        // 物理像素 → DIP：bounds 为物理像素，WPF 窗口 Left/Top/Width/Height 为 DIP。
        // 非 100% 缩放下必须除以 DPI 系数，否则覆盖层被放大、底图拉伸（docs/02 §5）。
        Left = bounds.X / _scaleX;
        Top = bounds.Y / _scaleY;
        Width = bounds.Width / _scaleX;
        Height = bounds.Height / _scaleY;

        BackgroundImage.Source = source;
        ScreenGeometry.Rect = new Rect(0, 0, Width, Height);

        Debug.WriteLine($"DEBUG: ScreenshotWindow bounds={bounds} scale=({_scaleX:F3},{_scaleY:F3}) window=({Left:F1},{Top:F1},{Width:F1}x{Height:F1})");

        // 关键视觉参数从统一配置注入（Per SPEC 重构 Step 3）。
        // 红框厚度（2px）与覆盖层背景色（Black）已硬编码，不再可配。
        var snipping = SettingsManager.Instance.Settings.Snipping;
        Background = System.Windows.Media.Brushes.Black;
        // 暗罩恒为黑色，浓度（alpha）可配：0.4 → #66000000。
        MaskPath.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(
            (byte)(255 * snipping.MaskAlpha), 0, 0, 0));
        HighlightBorder.BorderBrush = BrushHelper.ToBrush(snipping.BorderColor);
        HighlightBorder.BorderThickness = new Thickness(2);

        // HWND 创建后用实际渲染 DPI（TransformToDevice）修正 scale——AllowsTransparency 等
        // 场景下渲染 DPI 可能与 GetDpiForMonitor 不同（如固定为主屏 DPI）。用实际值才能保证
        // 显示与裁剪一致。SourceInitialized 在首次渲染前触发，重设尺寸无闪烁。
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>HWND 创建后：物理坐标强制定位 + 用实际渲染 DPI 重算尺寸，并订阅 DPI 变化。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // SetWindowPos 以物理坐标强制 HWND 到 bounds，确保落在目标显示器（不受 WPF DIP
        // 跨混合 DPI 定位歧义影响），并触发该显示器 DPI 赋值。
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, _bounds.X, _bounds.Y,
            _bounds.Width, _bounds.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        ApplyRenderMetrics();
        DpiChanged += OnDpiChanged;
    }

    /// <summary>窗口跨显示器后 DPI 变化：重算尺寸保持 1:1（如初始放置触发 WM_DPICHANGED）。</summary>
    private void OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        ApplyRenderMetrics();
    }

    /// <summary>
    /// 用窗口实际渲染 DPI（<see cref="PresentationSource.CompositionTarget.TransformToDevice"/>）
    /// 重算 _scaleX/Y 与 Left/Top/Width/Height/ScreenGeometry。裁剪与显示共用此 scale，
    /// 保证框选区域与裁出图像 1:1。
    /// </summary>
    private void ApplyRenderMetrics()
    {
        var src = PresentationSource.FromVisual(this);
        if (src?.CompositionTarget is not null)
        {
            var m = src.CompositionTarget.TransformToDevice;
            _scaleX = m.M11;
            _scaleY = m.M22;
        }

        Left = _bounds.X / _scaleX;
        Top = _bounds.Y / _scaleY;
        Width = _bounds.Width / _scaleX;
        Height = _bounds.Height / _scaleY;
        ScreenGeometry.Rect = new Rect(0, 0, Width, Height);

        Debug.WriteLine($"DEBUG: ApplyRenderMetrics renderScale=({_scaleX:F3},{_scaleY:F3}) physBounds={_bounds} window=({Left:F1},{Top:F1},{Width:F1}x{Height:F1})");
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
        Debug.WriteLine("DEBUG: MouseDown Triggered");
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
        Debug.WriteLine("DEBUG: WS_EX_TRANSPARENT cleared");
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
                if (_lastEdgeTarget != IntPtr.Zero)
                    Debug.WriteLine("DEBUG: EdgeDetect -> none (desktop/empty)");
                _lastEdgeTarget = IntPtr.Zero;
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

                // 物理窗口矩形 → DIP 窗口本地坐标（与 e.GetPosition(this) 同帧）。
                // 寻边矩形为物理像素，除以 DPI 系数转 DIP 后才能与 DIP 选区/几何混用。
                Rect selection = new Rect((rect.Left - _bounds.X) / _scaleX, (rect.Top - _bounds.Y) / _scaleY,
                                          (rect.Right - rect.Left) / _scaleX, (rect.Bottom - rect.Top) / _scaleY);

                // 目标变化时才记录，避免每帧刷屏；便于排查"巡边失败"。
                if (target != _lastEdgeTarget)
                {
                    _lastEdgeTarget = target;
                    Debug.WriteLine($"DEBUG: EdgeDetect -> hwnd=0x{target.ToInt64():X} physRect={rect} dipSel={selection}");
                }
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
        Debug.WriteLine("DEBUG: MouseUp Triggered");

        try
        {
            if (_isPotentialDrag)
            {
                // 手动拖拽截图：对最终选区矩形（按下点 → 松开点）截图。
                Debug.WriteLine("DEBUG: Mode B - Manual Drag Capture");
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
                Debug.WriteLine("DEBUG: Mode A - Smart Snapshot");
                SettleSelection(_currentSelection.Value);
            }
            else
            {
                // 空点（无红框且未拖拽）：不操作。
                Debug.WriteLine("DEBUG: Empty Click - No Capture");
            }
        }
        finally
        {
            // 无论是否抛异常，截图罩必须释放鼠标捕获并关闭。
            if (_isDragging)
                ReleaseMouseCapture();
            _isDragging = false;
            _isPotentialDrag = false;

            Close();   // 无论哪种模式，松开左键后必须关闭截图窗口
        }
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
        // 严格夹取到 base-image 边界：寻边窗口可能超出虚拟屏、拖拽可能产生负值，
        // 越界矩形会让 CroppedBitmap 抛 ArgumentException。夹取后只裁可见部分。
        // 选区为 DIP（窗口本地），底图为物理像素：乘 DPI 系数转物理后再裁剪。
        int x = Math.Clamp((int)(selection.X * _scaleX), 0, _baseImage.PixelWidth);
        int y = Math.Clamp((int)(selection.Y * _scaleY), 0, _baseImage.PixelHeight);
        int w = Math.Clamp((int)(selection.Width * _scaleX), 0, _baseImage.PixelWidth - x);
        int h = Math.Clamp((int)(selection.Height * _scaleY), 0, _baseImage.PixelHeight - y);

        Debug.WriteLine($"DEBUG: Capture Rect - x={x}, y={y}, w={w}, h={h}");

        if (w <= 0 || h <= 0)
            return;

        // 窗口本地坐标与底图像素 1:1，直接裁剪。
        var crop = new CroppedBitmap(_baseImage, new Int32Rect(x, y, w, h));
        crop.Freeze();

        // 结算：按 AfterScreenshot 决定写剪贴板 / 钉贴图 / 两者。
        // 选区左上角的绝对屏幕坐标 = 虚拟屏原点 + 窗口本地坐标，让贴图落在截图原位。
        // 用 Show()（非模态）打开，确保截图罩 Close() 后贴图窗口仍存活。
        var after = SettingsManager.Instance.Settings.Snipping.AfterScreenshot;

        if (after != SnippingAfterScreenshot.PinOnly)
        {
            try
            {
                Clipboard.SetImage(crop);
            }
            catch (Exception ex)
            {
                // 剪贴板被其它进程独占（RDP/剪贴板管理器）：不阻断流程，弹 toast 告知用户。
                Debug.WriteLine($"ERROR: 写剪贴板失败: {ex.Message}");
                Toast.Show("⚠ 剪贴板被占用，截图未复制", 3000);
            }
        }

        if (after != SnippingAfterScreenshot.CopyOnly)
        {
            // 传截图同屏 scale，保证贴图 1:1（贴图与截图同显示器，DPI 一致）。
            new PinWindow(crop, _bounds.X + x, _bounds.Y + y, _scaleX, _scaleY).Show();
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

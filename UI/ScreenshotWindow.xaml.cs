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
using MyQuicker.Domain.DTO;
using MyQuicker.Services;
using static MyQuicker.Interop.NativeMethods;
using Cursors = System.Windows.Input.Cursors;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
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
    private readonly SnippingSettings _snippingSettings;

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

    /// <summary>判定"点击 vs 拖拽"的位移阈值（DIP）。超过即升级为拖拽。</summary>
    private readonly double _dragThreshold;

    /// <summary>覆盖层当前是否处于点击穿透状态（寻边模式）。</summary>
    private bool _overlayTransparent;

    private HwndSource? _hwndSource;

    // ----- 圆形鼠标像素放大镜 -----
    private const int LoupeDiameter = 130;
    private CroppedBitmap? _currentLoupeCrop;

    private double _loupeZoomLevel;
    private int _loupeSourceSize;
    private double _loupePixelSize; // 每个物理像素在放大镜中占据的 DIP 边长

    /// <summary>用户成功选区后返回的物理屏幕坐标矩形；取消或未选区时为 null。</summary>
    public Rectangle? SelectedBounds { get; private set; }

    public ScreenshotWindow(BitmapSource source, Rectangle bounds, SnippingSettings snippingSettings)
    {
        if (snippingSettings is null)
            throw new ArgumentNullException(nameof(snippingSettings));

        InitializeComponent();

        _baseImage = source;
        _bounds = bounds;
        _snippingSettings = snippingSettings;
        _dragThreshold = snippingSettings.DragThreshold;

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
        // 红框厚度（2px）已硬编码，不再可配；覆盖层背景色在 XAML 中设为 Black。
        // 暗罩恒为黑色，浓度（alpha）可配：0.4 → #66000000。
        double alpha = Math.Clamp(snippingSettings.MaskAlpha, 0.0, 1.0);
        MaskPath.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(255 * alpha), 0, 0, 0));
        HighlightBorder.BorderBrush = BrushHelper.ToBrush(snippingSettings.BorderColor);
        HighlightBorder.BorderThickness = new Thickness(2);

        // HWND 创建后用 GetDpiForWindow(hwnd) 取确定性 DPI——AllowsTransparency=False
        // 配合 per-monitor V2 manifest 后，该值即为窗口所在显示器真实缩放，彻底解决 KI-1。
        SourceInitialized += OnSourceInitialized;
        Unloaded += OnUnloaded;

        ApplyLoupeZoomMetrics();
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

        ApplyRenderMetrics(hwnd);
        DpiChanged += OnDpiChanged;

        // 限制鼠标在当前截图范围内，防止 CurrentMonitor 模式下鼠标逃出覆盖层。
        var rc = new NativeMethods.RECT
        {
            Left = _bounds.X,
            Top = _bounds.Y,
            Right = _bounds.Right,
            Bottom = _bounds.Bottom
        };
        NativeMethods.ClipCursor(ref rc);

        _hwndSource = HwndSource.FromHwnd(hwnd);
    }

    /// <summary>窗口跨显示器后 DPI 变化：重算尺寸保持 1:1（如初始放置触发 WM_DPICHANGED）。</summary>
    private void OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        ApplyRenderMetrics(hwnd);
    }

    /// <summary>
    /// 用窗口实际渲染 DPI（<see cref="PresentationSource.CompositionTarget.TransformToDevice"/>）
    /// 重算 _scaleX/Y 与 Left/Top/Width/Height/ScreenGeometry。裁剪与显示共用此 scale，
    /// 保证框选区域与裁出图像 1:1。
    /// </summary>
    private void ApplyRenderMetrics(IntPtr hwnd)
    {
        var (sx, sy) = DpiHelper.ScaleForWindow(hwnd);
        _scaleX = sx;
        _scaleY = sy;

        // 兜底：API 不可用或返回值异常时回退到实际渲染 DPI。
        if (_scaleX <= 0 || _scaleY <= 0)
        {
            var src = PresentationSource.FromVisual(this);
            if (src?.CompositionTarget is not null)
            {
                var m = src.CompositionTarget.TransformToDevice;
                _scaleX = m.M11;
                _scaleY = m.M22;
            }
        }

        Left = _bounds.X / _scaleX;
        Top = _bounds.Y / _scaleY;
        Width = _bounds.Width / _scaleX;
        Height = _bounds.Height / _scaleY;
        ScreenGeometry.Rect = new Rect(0, 0, Width, Height);

        // scale 变化后，放大镜的 DIP 尺寸必须重新反向补偿，以保持 130×130 物理像素恒定。
        ApplyLoupeZoomMetrics();

        Debug.WriteLine($"DEBUG: ApplyRenderMetrics renderScale=({_scaleX:F3},{_scaleY:F3}) physBounds={_bounds} window=({Left:F1},{Top:F1},{Width:F1}x{Height:F1})");
    }

    /// <summary>窗口关闭时释放图片源、Brush、几何、鼠标捕获与事件订阅，避免资源泄漏。</summary>
    protected override void OnClosed(EventArgs e)
    {
        BackgroundImage.Source = null;
        MaskPath.Fill = null;
        HighlightBorder.BorderBrush = null;
        CutoutGeometry.Rect = new Rect(0, 0, 0, 0);

        if (_isDragging)
            ReleaseMouseCapture();

        SourceInitialized -= OnSourceInitialized;
        DpiChanged -= OnDpiChanged;
        Unloaded -= OnUnloaded;

        // 必须释放全局鼠标锁，防止截图窗口异常退出后光标仍被限制。
        NativeMethods.ClipCursor(IntPtr.Zero);

        // 释放放大镜高频临时引用。
        ClearLoupeSource();
        MagnifierLoupe.Visibility = Visibility.Collapsed;
        MagnifierInfoPanel.Visibility = Visibility.Collapsed;

        base.OnClosed(e);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        NativeMethods.ClipCursor(IntPtr.Zero);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_isDragging)
                ReleaseMouseCapture();
            NativeMethods.ClipCursor(IntPtr.Zero);
            ClearLoupeSource();
            MagnifierLoupe.Visibility = Visibility.Collapsed;
        MagnifierInfoPanel.Visibility = Visibility.Collapsed;
            Close();
            e.Handled = true;
            return;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        if (_isDragging)
            ReleaseMouseCapture();
        NativeMethods.ClipCursor(IntPtr.Zero);
        ClearLoupeSource();
        MagnifierLoupe.Visibility = Visibility.Collapsed;
        MagnifierInfoPanel.Visibility = Visibility.Collapsed;
        Close();
        e.Handled = true;
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
        SetOverlayTransparent(false);
    }

    /// <summary>切换覆盖层点击穿透状态，仅在真正变化时才调用 SetWindowLong。</summary>
    private void SetOverlayTransparent(bool transparent)
    {
        if (_overlayTransparent == transparent) return;

        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        int ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        if (transparent) ex |= NativeMethods.WS_EX_TRANSPARENT;
        else ex &= ~NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        _overlayTransparent = transparent;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        Point p = e.GetPosition(this);

        // 放大镜与信息面板：在寻边和拖拽阶段均保持可见，直到鼠标松开或取消才隐藏。
        MagnifierLoupe.Visibility = Visibility.Visible;
        MagnifierInfoPanel.Visibility = Visibility.Visible;
        UpdateMagnifierLoupe(p);

        // 阈值判定：按下但尚未拖拽时，位移超过阈值则升级为拖拽模式。
        if (!_isDragging && e.LeftButton == MouseButtonState.Pressed)
        {
            double delta = (p - _mouseDownPoint).Length;
            if (delta > _dragThreshold)
            {
                _isDragging = true;
                _isPotentialDrag = true;
                _currentSelection = null;   // 清空寻边红框，切换到拖拽选区
                CaptureMouse();             // 升级为拖拽时才捕获，保证松开事件可达
            }
        }

        if (_isDragging)
        {
            // 拖拽模式：窗口必须接收命中，不能点击穿透。
            SetOverlayTransparent(false);

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
            // 寻边模式：需要点击穿透才能看到覆盖层下面的窗口。
            SetOverlayTransparent(true);

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
            // 无论是否抛异常，截图罩必须释放鼠标捕获、释放鼠标锁、隐藏放大镜并关闭。
            if (_isDragging)
                ReleaseMouseCapture();
            _isDragging = false;
            _isPotentialDrag = false;

            MagnifierLoupe.Visibility = Visibility.Collapsed;
            MagnifierInfoPanel.Visibility = Visibility.Collapsed;
            ClearLoupeSource();

            NativeMethods.ClipCursor(IntPtr.Zero);
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
    /// 结算选区：把 DIP 窗口本地选区转换为物理屏幕坐标，写入 <see cref="SelectedBounds"/>，
    /// 并设置 <see cref="Window.DialogResult"/> = true。裁剪、剪贴板与钉贴图由调用方（工作流）负责。
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

        // 选区左上角的绝对屏幕坐标 = 虚拟屏原点 + 窗口本地坐标。
        SelectedBounds = new Rectangle(_bounds.X + x, _bounds.Y + y, w, h);
        DialogResult = true;
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

    // -----------------------------------------------------------------------
    // Circular magnifier loupe
    // -----------------------------------------------------------------------

    /// <summary>把 MagnifierZoomPreset 转换为 DIP 尺寸，并按当前显示器 DPI 反向补偿，确保物理投影恒为 130×130 物理像素。</summary>
    private void ApplyLoupeZoomMetrics()
    {
        _loupeSourceSize = MagnifierMetrics.GetSourceSize(_snippingSettings.MagnifierZoomPreset);
        _loupeZoomLevel = _snippingSettings.MagnifierZoomPreset switch
        {
            MagnifierZoomPreset.Large => 13.0,
            MagnifierZoomPreset.Small => 5.0,
            _ => 10.0 // Medium
        };
        _loupePixelSize = _loupeZoomLevel;

        // 放大镜物理尺寸固定 130×130；除以当前 scale 得到应设置的 DIP 值，以抵消 WPF 自动 DPI 拉伸。
        double loupeWidthDip = LoupeDiameter / _scaleX;
        double loupeHeightDip = LoupeDiameter / _scaleY;
        MagnifierLoupe.Width = loupeWidthDip;
        MagnifierLoupe.Height = loupeHeightDip;
        MagnifierLoupe.CornerRadius = new CornerRadius(Math.Min(loupeWidthDip, loupeHeightDip) / 2);

        MagnifierImage.Width = loupeWidthDip;
        MagnifierImage.Height = loupeHeightDip;

        var (blockWidthDip, blockHeightDip, cursorBlockStartDipX, cursorBlockStartDipY) =
            MagnifierMetrics.ComputeBlockMetrics(LoupeDiameter, _loupeSourceSize, _scaleX, _scaleY);

        PixelGridBrush.Viewport = new Rect(0, 0, blockWidthDip, blockHeightDip);
        VerticalPixelIndicator.Width = blockWidthDip;
        VerticalPixelIndicator.Height = loupeHeightDip;
        HorizontalPixelIndicator.Width = loupeWidthDip;
        HorizontalPixelIndicator.Height = blockHeightDip;
        CenterPixelHighlight.Width = blockWidthDip;
        CenterPixelHighlight.Height = blockHeightDip;

        // 关键修复：十字带与中心高亮框必须跟随“鼠标所在的实际像素块”，
        // 而不是放大镜几何中心。Medium(source_size=13) 两种基准重合；
        // Small/Large 为偶数，按几何居中会让十字带跨在两个像素块之间。
        VerticalPixelIndicator.Margin = new Thickness(cursorBlockStartDipX, 0, 0, 0);
        HorizontalPixelIndicator.Margin = new Thickness(0, cursorBlockStartDipY, 0, 0);
        CenterPixelHighlight.Margin = new Thickness(cursorBlockStartDipX, cursorBlockStartDipY, 0, 0);

        // 圆形边框内嵌在内容区之上，其 StrokeThickness 也要按 DPI 反向补偿，保持视觉 2 物理像素。
        LoupeBorderRing.Width = loupeWidthDip;
        LoupeBorderRing.Height = loupeHeightDip;
        LoupeBorderRing.StrokeThickness = 2.0 / Math.Max(_scaleX, _scaleY);
    }

    /// <summary>实时更新放大镜：以鼠标物理像素为中心裁出动态源区域，NearestNeighbor 放大显示。</summary>
    private void UpdateMagnifierLoupe(Point mouseDip)
    {
        if (_baseImage is null || _baseImage.PixelWidth == 0 || _baseImage.PixelHeight == 0)
            return;

        // DIP → 物理像素。
        int physX = (int)(mouseDip.X * _scaleX);
        int physY = (int)(mouseDip.Y * _scaleY);

        // 以鼠标为中心计算动态物理源矩形，并夹取到图片边界；始终保持正方形，确保与网格对齐。
        int half = _loupeSourceSize / 2;
        int srcX = Math.Clamp(physX - half, 0, Math.Max(0, _baseImage.PixelWidth - _loupeSourceSize));
        int srcY = Math.Clamp(physY - half, 0, Math.Max(0, _baseImage.PixelHeight - _loupeSourceSize));
        int srcW = _loupeSourceSize;
        int srcH = _loupeSourceSize;

        // 兜底：图片本身小于源区域时按实际尺寸裁剪（极罕见）。
        if (_baseImage.PixelWidth < _loupeSourceSize || _baseImage.PixelHeight < _loupeSourceSize)
        {
            srcW = Math.Min(_loupeSourceSize, _baseImage.PixelWidth);
            srcH = Math.Min(_loupeSourceSize, _baseImage.PixelHeight);
        }

        if (srcW <= 0 || srcH <= 0)
            return;

        var sourceRect = new Int32Rect(srcX, srcY, srcW, srcH);
        _currentLoupeCrop?.Freeze();
        _currentLoupeCrop = new CroppedBitmap(_baseImage, sourceRect);
        _currentLoupeCrop.Freeze();
        MagnifierImage.Source = _currentLoupeCrop;

        // 中心像素颜色采样。
        var color = GetPixelColor(_baseImage, physX, physY);
        var brush = new SolidColorBrush(color);
        brush.Freeze();

        MagnifierCoordText.Visibility = _snippingSettings.ShowMagnifierCoordinates ? Visibility.Visible : Visibility.Collapsed;
        MagnifierCoordText.Text = $"X:{physX} Y:{physY}";

        MagnifierColorRow.Visibility = _snippingSettings.ShowMagnifierColor ? Visibility.Visible : Visibility.Collapsed;
        MagnifierColorSwatch.Background = brush;
        MagnifierColorText.Text = $"#{color.R:X2}{color.G:X2}{color.B:X2}";

        PositionLoupe(mouseDip);

        // 诊断日志：输出放大镜与像素块的运行时尺寸。
        double dbgBlockWidthPhys = LoupeDiameter / (double)_loupeSourceSize;
        double dbgBlockHeightPhys = LoupeDiameter / (double)_loupeSourceSize;
        double dbgBlockWidthDip = dbgBlockWidthPhys / _scaleX;
        double dbgBlockHeightDip = dbgBlockHeightPhys / _scaleY;
        Debug.WriteLine($"DEBUG: Loupe=({MagnifierLoupe.Width:F3}x{MagnifierLoupe.Height:F3}) " +
            $"Actual=({MagnifierLoupe.ActualWidth:F3}x{MagnifierLoupe.ActualHeight:F3}) " +
            $"BlockDip=({dbgBlockWidthDip:F3}x{dbgBlockHeightDip:F3}) " +
            $"VertW={VerticalPixelIndicator.Width:F3}({VerticalPixelIndicator.ActualWidth:F3}) " +
            $"HorzH={HorizontalPixelIndicator.Height:F3}({HorizontalPixelIndicator.ActualHeight:F3}) " +
            $"Center=({CenterPixelHighlight.Width:F3}x{CenterPixelHighlight.Height:F3}) " +
            $"CenterActual=({CenterPixelHighlight.ActualWidth:F3}x{CenterPixelHighlight.ActualHeight:F3})");
    }

    /// <summary>
    /// 按配置方向初始化放大镜位置，并在越界时执行四角溢出反转，
    /// 确保放大镜始终完整可见且不遮挡鼠标。
    /// </summary>
    private void PositionLoupe(Point mouseDip)
    {
        // 当前窗口客户区即覆盖的屏幕区域（DIP）。
        double rightEdge = Width;
        double bottomEdge = Height;

        // 放大镜当前 DIP 尺寸（已按 DPI 反向补偿）。
        double loupeWidthDip = MagnifierLoupe.ActualWidth > 0 ? MagnifierLoupe.ActualWidth : LoupeDiameter / _scaleX;
        double loupeHeightDip = MagnifierLoupe.ActualHeight > 0 ? MagnifierLoupe.ActualHeight : LoupeDiameter / _scaleY;

        double offset = 25;
        double loupeX;
        double loupeY;

        switch (_snippingSettings.MagnifierPosition)
        {
            case MagnifierPosition.BottomLeft:
                loupeX = mouseDip.X - loupeWidthDip - offset;
                loupeY = mouseDip.Y + offset;
                break;
            case MagnifierPosition.TopRight:
                loupeX = mouseDip.X + offset;
                loupeY = mouseDip.Y - loupeHeightDip - offset;
                break;
            case MagnifierPosition.TopLeft:
                loupeX = mouseDip.X - loupeWidthDip - offset;
                loupeY = mouseDip.Y - loupeHeightDip - offset;
                break;
            default: // BottomRight
                loupeX = mouseDip.X + offset;
                loupeY = mouseDip.Y + offset;
                break;
        }

        // 水平越界 → 翻转到鼠标另一侧。
        if (loupeX + loupeWidthDip > rightEdge)
            loupeX = mouseDip.X - loupeWidthDip - offset;
        else if (loupeX < 0)
            loupeX = mouseDip.X + offset;

        // 垂直越界 → 翻转到鼠标另一侧。
        if (loupeY + loupeHeightDip > bottomEdge)
            loupeY = mouseDip.Y - loupeHeightDip - offset;
        else if (loupeY < 0)
            loupeY = mouseDip.Y + offset;

        MagnifierLoupe.Margin = new Thickness(loupeX, loupeY, 0, 0);

        // 信息面板：紧贴放大镜下方，水平居中，并做边界夹取。
        double infoSpacing = 6;
        double infoWidth = MagnifierInfoPanel.ActualWidth > 0 ? MagnifierInfoPanel.ActualWidth : loupeWidthDip;
        double infoHeight = MagnifierInfoPanel.ActualHeight > 0 ? MagnifierInfoPanel.ActualHeight : 24;
        double infoX = loupeX + (loupeWidthDip - infoWidth) / 2;
        double infoY = loupeY + loupeHeightDip + infoSpacing;

        if (infoX < 0) infoX = 0;
        if (infoX + infoWidth > rightEdge) infoX = Math.Max(0, rightEdge - infoWidth);
        if (infoY + infoHeight > bottomEdge)
            infoY = loupeY - infoHeight - infoSpacing; // 下方空间不足时翻到放大镜上方

        MagnifierInfoPanel.Margin = new Thickness(infoX, infoY, 0, 0);
    }

    /// <summary>读取 BitmapSource 指定物理像素的颜色（BGRA → ARGB）。</summary>
    private static System.Windows.Media.Color GetPixelColor(BitmapSource source, int x, int y)
    {
        x = Math.Clamp(x, 0, source.PixelWidth - 1);
        y = Math.Clamp(y, 0, source.PixelHeight - 1);

        var pixel = new byte[4];
        source.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);
        return System.Windows.Media.Color.FromArgb(pixel[3], pixel[2], pixel[1], pixel[0]);
    }

    /// <summary>释放放大镜当前引用的裁剪位图。</summary>
    private void ClearLoupeSource()
    {
        MagnifierImage.Source = null;
        _currentLoupeCrop = null;
    }

    /// <summary>
    /// Returns the top-level window under the cursor, temporarily marking
    /// our own overlay as hit-test-transparent (WS_EX_TRANSPARENT) so
    /// WindowFromPoint sees the window beneath us instead of returning
    /// our overlay (which is Topmost and fully opaque).
    /// </summary>
    private IntPtr WindowUnderCursor(POINT pt)
    {
        bool wasTransparent = _overlayTransparent;
        SetOverlayTransparent(true);
        try
        {
            return NativeMethods.WindowFromPoint(pt);
        }
        finally
        {
            SetOverlayTransparent(wasTransparent);
        }
    }
}

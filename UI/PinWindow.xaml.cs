using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;
using MyQuicker.Services;
using Clipboard = System.Windows.Clipboard;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using Rectangle = System.Windows.Shapes.Rectangle;
using Ellipse = System.Windows.Shapes.Ellipse;
using Shape = System.Windows.Shapes.Shape;
using ShapePath = System.Windows.Shapes.Path;
using TextBox = System.Windows.Controls.TextBox;

namespace MyQuicker.UI;

/// <summary>
/// 贴图常驻窗口：把一张截图钉在桌面上，可拖拽、缩放、旋转、镜像、调透明度，
/// 并提供右键菜单与基础批注（画框/画圆/箭头/文字）。左键双击关闭。Per SPEC 8C (PinEngine).
/// 批注状态机与光栅化导出见 docs/02-interaction-engine.md §8。
/// </summary>
public partial class PinWindow : Window
{
    private readonly BitmapSource _source;

    // 旋转累计角度（每次 +90°）与水平镜像开关
    private int _rotationStep;
    private bool _mirrored;

    // 每次旋转的步进角度（度），固定 90°（原 PinSettings.RotationStepDegrees 已移除：
    // 非 90° 会破坏 90/270 宽高互换逻辑）
    private const double _rotationStepDegrees = 90;

    // 原始物理像素尺寸（重置大小用）
    private readonly double _naturalWidth;
    private readonly double _naturalHeight;

    /// <summary>贴图内容左上角目标屏幕物理坐标（截图结算传入，ReapplyMetrics 基位）。</summary>
    private readonly double _screenX;
    private readonly double _screenY;

    /// <summary>DIP↔物理像素 缩放系数。初始为 ScreenshotWindow 传入值，SourceInitialized/DpiChanged 用 TransformToDevice 修正为窗口实际渲染 DPI（docs/02 §5）。</summary>
    private double _scaleX;
    private double _scaleY;

    /// <summary>当前旋转角度（0/90/180/270）。步进固定 90°。</summary>
    private double RotationAngle => (_rotationStep % 4) * _rotationStepDegrees;

    // ----- 批注状态机（docs/02 §8.1）-----
    private enum EditMode { None, Rect, Circle, Arrow, Text }
    private EditMode _editMode = EditMode.None;

    /// <summary>批注模式总开关（右键「批注 ▸ 批注模式」）。关闭时工具栏不存在、Canvas 击穿。</summary>
    private bool _annotationModeEnabled;

    /// <summary>当前批注颜色画刷，由工具栏颜色预设切换。</summary>
    private Brush _currentBrush = Brushes.Red;

    /// <summary>画笔粗细（px），由工具栏粗细预设切换；作用于框/圆/箭头描边。</summary>
    private double _strokeThickness = 2;

    // 拖拽绘制：起点 + 当前临时形状（Rectangle / Ellipse / ShapePath）
    private Point _dragStart;
    private Shape? _activeShape;

    /// <summary>箭头头部边长（px）：max(8, 粗细×3)。</summary>
    private double ArrowHeadLen => Math.Max(8, _strokeThickness * 3);

    /// <param name="screenX">贴图左上角目标屏幕横坐标（物理像素，来自截图结算）。</param>
    /// <param name="screenY">贴图左上角目标屏幕纵坐标（物理像素，来自截图结算）。</param>
    /// <param name="scaleX">DIP↔物理像素 X 系数（截图所在显示器 DPI，保证贴图 1:1）。</param>
    /// <param name="scaleY">DIP↔物理像素 Y 系数。</param>
    /// <param name="pinSettings">贴图视觉参数配置。</param>
    public PinWindow(BitmapSource source, double screenX, double screenY, double scaleX, double scaleY, PinSettings pinSettings)
    {
        if (pinSettings is null)
            throw new ArgumentNullException(nameof(pinSettings));

        InitializeComponent();

        _screenX = screenX;
        _screenY = screenY;
        _scaleX = scaleX;
        _scaleY = scaleY;

        // 关键视觉参数从统一配置注入（Per SPEC 重构 Step 3）。
        // 最小宽高（40×40）、阴影模糊半径（14）、旋转步进（90°）已硬编码，不再可配。
        MinWidth = 40;
        MinHeight = 40;
        PinBorder.BorderBrush = BrushHelper.ToBrush(pinSettings.BorderColor);
        ShadowEffect.BlurRadius = 14;
        Opacity = pinSettings.DefaultOpacity;
        SyncOpacityMenu(pinSettings.DefaultOpacity); // 不透明度菜单勾选与 DefaultOpacity 同步

        // 默认置顶 / 默认阴影（Per SPEC 8C）：覆盖 XAML 的 True 默认与菜单勾选状态。
        Topmost = pinSettings.DefaultTopmost;
        TopmostMenuItem.IsChecked = pinSettings.DefaultTopmost;
        ShadowMenuItem.IsChecked = pinSettings.DefaultShowShadow;
        PinImage.Effect = pinSettings.DefaultShowShadow ? ShadowEffect : null;

        _source = source;
        _naturalWidth = source.PixelWidth;
        _naturalHeight = source.PixelHeight;

        PinImage.Source = source;

        // 用传入 scale 初算 PinImage 尺寸/定位；SourceInitialized 再用窗口实际渲染 DPI 修正。
        ReapplyMetrics();

        // 默认外观（docs/03 §6）：默认显示边界 + 默认批注模式，均可配置。
        _annotationModeEnabled = pinSettings.DefaultAnnotationMode;
        AnnotationModeItem.IsChecked = _annotationModeEnabled;
        if (pinSettings.DefaultShowBorder)
        {
            BorderMenuItem.IsChecked = true;
            ApplyBorder(2);
        }
        ApplyAnnotationState();

        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>HWND 创建后：物理坐标强制定位 + 用确定性窗口 DPI 重算尺寸，订阅 DPI 变化。</summary>
    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // SetWindowPos 以物理坐标（贴图内容左上角）强制 HWND 落在目标显示器，触发该屏 DPI 赋值。
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, (int)_screenX, (int)_screenY, 0, 0,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOSIZE);

        ReapplyMetrics(hwnd);
        DpiChanged += OnDpiChanged;
    }

    /// <summary>窗口跨显示器后 DPI 变化：重算尺寸保持 1:1。</summary>
    private void OnDpiChanged(object sender, System.Windows.DpiChangedEventArgs e)
    {
        IntPtr hwnd = new WindowInteropHelper(this).Handle;
        ReapplyMetrics(hwnd);
    }

    /// <summary>用构造时传入的 scale 初算尺寸（HWND 尚未创建）。</summary>
    private void ReapplyMetrics()
    {
        ApplyMetricsCore();
    }

    /// <summary>
    /// 用确定性窗口 DPI（<see cref="DpiHelper.ScaleForWindow"/>）重算 PinImage 尺寸、Left/Top、窗口外接尺寸。
    /// 裁剪为物理像素，PinImage 用 actualScale 定尺寸 + Stretch=Fill → 渲染 ×actualScale = 物理像素 1:1，
    /// 与框选物理尺寸一致（无论窗口被赋何 DPI）。
    /// </summary>
    private void ReapplyMetrics(IntPtr hwnd)
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

        ApplyMetricsCore();
    }

    private void ApplyMetricsCore()
    {
        double bx = PinBorder.BorderThickness.Left; // 均匀边框（DIP）
        PinImage.Width = _naturalWidth / _scaleX;
        PinImage.Height = _naturalHeight / _scaleY;
        // 内容左上角对齐 (screenX, screenY)；边框向外生长，窗口左上角反向偏移 bx。
        Left = _screenX / _scaleX - bx;
        Top = _screenY / _scaleY - bx;

        ApplyTransform(); // 旋转/镜像 + ApplyWindowSize（按 _scaleX/Y 重算窗口外接尺寸）

        Debug.WriteLine($"DEBUG: PinWindow ReapplyMetrics renderScale=({_scaleX:F3},{_scaleY:F3}) natural={_naturalWidth}x{_naturalHeight} screen=({_screenX},{_screenY}) border={bx}");
    }

    // -----------------------------------------------------------------------
    // 拖拽与双击关闭
    // -----------------------------------------------------------------------

    /// <summary>
    /// 左键按下：双击关闭，否则交给 WPF 原生 <see cref="Window.DragMove"/>。
    /// 批注模式（Rect/Circle/Arrow/Text）下 Canvas 接管命中并 e.Handled，本回调不触发。
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount >= 2)
        {
            Close();
            return;
        }

        DragMove();
    }

    // -----------------------------------------------------------------------
    // 工具栏 Hover 淡入 / 淡出（仅批注模式开启时，docs/03 §6）
    // -----------------------------------------------------------------------

    private void Window_MouseEnter(object sender, MouseEventArgs e)
    {
        if (_annotationModeEnabled) FadeInToolbar();
    }

    private void Window_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_annotationModeEnabled) FadeOutToolbar();
    }

    private void FadeInToolbar()
    {
        AnnotationToolbar.IsHitTestVisible = true;
        AnnotationToolbar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(1, TimeSpan.FromMilliseconds(150)));
    }

    private void FadeOutToolbar()
    {
        AnnotationToolbar.IsHitTestVisible = false;
        AnnotationToolbar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.FromMilliseconds(150)));
    }

    private void ForceHideToolbar()
    {
        AnnotationToolbar.IsHitTestVisible = false;
        AnnotationToolbar.BeginAnimation(OpacityProperty,
            new DoubleAnimation(0, TimeSpan.Zero));
    }

    // -----------------------------------------------------------------------
    // 批注工具栏：模式 / 粗细 / 颜色切换
    // -----------------------------------------------------------------------

    /// <summary>工具切换：按 Tag 解析 EditMode，并按批注模式开关 + 模式决定 Canvas 命中。</summary>
    private void Tool_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s && Enum.TryParse<EditMode>(s, out var m))
        {
            _editMode = m;
            ApplyAnnotationState();
        }
    }

    /// <summary>画笔粗细切换：Tag 为数字串（2/4/6）。</summary>
    private void PenSize_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s
            && double.TryParse(s, CultureInfo.InvariantCulture, out double v))
            _strokeThickness = v;
    }

    /// <summary>颜色切换：Tag 为 hex 串，经 BrushHelper 转 Brush。</summary>
    private void Color_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is RadioButton rb && rb.Tag is string s)
            _currentBrush = BrushHelper.ToBrush(s);
    }

    /// <summary>Canvas 命中可见性 = 批注模式开启 且 当前工具非 None。</summary>
    private void ApplyAnnotationState()
    {
        AnnotationCanvas.IsHitTestVisible = _annotationModeEnabled && _editMode != EditMode.None;
    }

    // -----------------------------------------------------------------------
    // 右键「批注」子菜单
    // -----------------------------------------------------------------------

    /// <summary>批注模式开关：切换工具栏存在性与 Canvas 命中。</summary>
    private void AnnotationMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            _annotationModeEnabled = mi.IsChecked;
            if (!_annotationModeEnabled)
            {
                _editMode = EditMode.None;
                ToolPointer.IsChecked = true; // 回到指针，触发 Tool_Checked→ApplyAnnotationState
                ForceHideToolbar();
            }
            else
            {
                ApplyAnnotationState();
                FadeInToolbar();
            }
        }
    }

    /// <summary>清除 Canvas 上所有批注。</summary>
    private void ClearAnnotations_Click(object sender, RoutedEventArgs e)
    {
        AnnotationCanvas.Children.Clear();
    }

    // -----------------------------------------------------------------------
    // 批注状态机：Canvas 鼠标事件（docs/02 §8.1）
    // -----------------------------------------------------------------------

    /// <summary>Canvas 左键按下：Rect/Circle 建临时 Rectangle/Ellipse，Arrow 建临时 Path，Text 生成 TextBox。</summary>
    private void AnnotationCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_editMode == EditMode.Rect || _editMode == EditMode.Circle)
        {
            _dragStart = e.GetPosition(AnnotationCanvas);
            Shape sh = _editMode == EditMode.Rect
                ? new Rectangle { Stroke = _currentBrush, StrokeThickness = _strokeThickness, Fill = Brushes.Transparent, Stretch = Stretch.Fill }
                : new Ellipse { Stroke = _currentBrush, StrokeThickness = _strokeThickness, Fill = Brushes.Transparent, Stretch = Stretch.Fill };
            Canvas.SetLeft(sh, _dragStart.X);
            Canvas.SetTop(sh, _dragStart.Y);
            AnnotationCanvas.Children.Add(sh);
            _activeShape = sh;
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
        }
        else if (_editMode == EditMode.Arrow)
        {
            _dragStart = e.GetPosition(AnnotationCanvas);
            var p = new ShapePath
            {
                Stroke = _currentBrush,
                StrokeThickness = _strokeThickness,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Data = ArrowGeometry(_dragStart, _dragStart, ArrowHeadLen)
            };
            AnnotationCanvas.Children.Add(p);
            _activeShape = p;
            AnnotationCanvas.CaptureMouse();
            e.Handled = true;
        }
        else if (_editMode == EditMode.Text)
        {
            SpawnTextEditor(e.GetPosition(AnnotationCanvas));
            e.Handled = true;
        }
    }

    /// <summary>Rect 实时 min/abs 归一化宽高；Circle 取 min(|dx|,|dy|) 作直径（真圆）；Arrow 重建几何。</summary>
    private void AnnotationCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_activeShape is null) return;
        var pt = e.GetPosition(AnnotationCanvas);
        if (_editMode == EditMode.Rect)
        {
            Canvas.SetLeft(_activeShape, Math.Min(_dragStart.X, pt.X));
            Canvas.SetTop(_activeShape, Math.Min(_dragStart.Y, pt.Y));
            _activeShape.Width = Math.Abs(pt.X - _dragStart.X);
            _activeShape.Height = Math.Abs(pt.Y - _dragStart.Y);
        }
        else if (_editMode == EditMode.Circle)
        {
            // 真圆：直径 = min(|dx|,|dy|)，从起点向拖拽方向扩展
            double dx = pt.X - _dragStart.X;
            double dy = pt.Y - _dragStart.Y;
            double size = Math.Min(Math.Abs(dx), Math.Abs(dy));
            Canvas.SetLeft(_activeShape, dx >= 0 ? _dragStart.X : _dragStart.X - size);
            Canvas.SetTop(_activeShape, dy >= 0 ? _dragStart.Y : _dragStart.Y - size);
            _activeShape.Width = size;
            _activeShape.Height = size;
        }
        else if (_editMode == EditMode.Arrow && _activeShape is ShapePath p)
        {
            p.Data = ArrowGeometry(_dragStart, pt, ArrowHeadLen);
        }
    }

    /// <summary>松开定型：过小（&lt;3px）移除。</summary>
    private void AnnotationCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_activeShape is null) return;
        AnnotationCanvas.ReleaseMouseCapture();

        bool tooSmall = _editMode == EditMode.Arrow
            ? (e.GetPosition(AnnotationCanvas) - _dragStart).Length < 3
            : _activeShape.Width < 3 || _activeShape.Height < 3;
        if (tooSmall)
            AnnotationCanvas.Children.Remove(_activeShape);
        _activeShape = null;
        e.Handled = true;
    }

    /// <summary>Text 模式：在点击处生成无边框可编辑 TextBox，失焦转固定 TextBlock。</summary>
    private void SpawnTextEditor(Point pt)
    {
        var tb = new TextBox
        {
            Foreground = _currentBrush,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            CaretBrush = _currentBrush,
            FontSize = 14,
            MinWidth = 30,
            Padding = new Thickness(2, 0, 2, 0)
        };
        Canvas.SetLeft(tb, pt.X);
        Canvas.SetTop(tb, pt.Y);
        AnnotationCanvas.Children.Add(tb);
        tb.LostFocus += (s, _) => CommitTextEditor(tb);
        tb.Focus();
        Keyboard.Focus(tb);
    }

    /// <summary>TextBox 失焦：非空则转为同位置同样式的 TextBlock，空则移除。</summary>
    private void CommitTextEditor(TextBox tb)
    {
        if (string.IsNullOrWhiteSpace(tb.Text))
        {
            AnnotationCanvas.Children.Remove(tb);
            return;
        }
        var blk = new TextBlock
        {
            Text = tb.Text,
            Foreground = tb.Foreground,
            FontSize = tb.FontSize,
            FontFamily = tb.FontFamily,
            Padding = tb.Padding
        };
        Canvas.SetLeft(blk, Canvas.GetLeft(tb));
        Canvas.SetTop(blk, Canvas.GetTop(tb));
        int idx = AnnotationCanvas.Children.IndexOf(tb);
        AnnotationCanvas.Children.RemoveAt(idx);
        AnnotationCanvas.Children.Insert(idx, blk);
    }

    /// <summary>箭头几何：主线 + 末端 V 形箭头（箭头长 = headLen，张角 60°）。</summary>
    private static PathGeometry ArrowGeometry(Point start, Point end, double headLen)
    {
        var geo = new PathGeometry();
        var main = new PathFigure { StartPoint = start };
        main.Segments.Add(new LineSegment(end, true));
        geo.Figures.Add(main);

        double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
        const double Back = System.Math.PI * 5.0 / 6.0; // 150°
        var p1 = new Point(end.X + headLen * Math.Cos(angle + Back), end.Y + headLen * Math.Sin(angle + Back));
        var p2 = new Point(end.X + headLen * Math.Cos(angle - Back), end.Y + headLen * Math.Sin(angle - Back));
        var head = new PathFigure { StartPoint = p1 };
        head.Segments.Add(new LineSegment(end, true));
        head.Segments.Add(new LineSegment(p2, true));
        geo.Figures.Add(head);
        return geo;
    }

    // -----------------------------------------------------------------------
    // 右键菜单：置顶 / 显示阴影 / 显示边界 / 重置大小 / 不透明度 / 旋转 / 镜像
    // -----------------------------------------------------------------------

    /// <summary>置顶：切换 Topmost。</summary>
    private void Topmost_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            Topmost = mi.IsChecked;
    }

    /// <summary>显示阴影：在 DropShadowEffect 与 null 之间切换。</summary>
    private void Shadow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            PinImage.Effect = mi.IsChecked ? ShadowEffect : null;
    }

    /// <summary>显示边界：在无边框与 2px 灰边框之间切换，边框向外生长。</summary>
    private void Border_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
            ApplyBorder(mi.IsChecked ? 2 : 0);
    }

    /// <summary>应用边框厚度：设置 PinBorder，经 ReapplyMetrics 重算 Left/Top（边框向外生长）与窗口外接尺寸。</summary>
    private void ApplyBorder(double thickness)
    {
        PinBorder.BorderThickness = new Thickness(thickness);
        ReapplyMetrics();
    }

    /// <summary>重置大小：恢复 1:1 像素比例，窗口尺寸回归当前旋转方向的外接矩形。</summary>
    private void ResetSize_Click(object sender, RoutedEventArgs e)
    {
        ApplyTransform();
    }

    /// <summary>不透明度子菜单：0.3 / 0.5 / 0.8 / 1.0。点击设 Opacity 并同步勾选。</summary>
    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s && double.TryParse(s, out double op))
        {
            Opacity = op;
            SyncOpacityMenu(op);
        }
    }

    /// <summary>按当前不透明度同步菜单勾选（构造时与点击时共用，Tag 值精确匹配）。</summary>
    private void SyncOpacityMenu(double opacity)
    {
        foreach (var child in OpacityMenuItem.Items)
            if (child is MenuItem m && m.Tag is string s && double.TryParse(s, out double v))
                m.IsChecked = Math.Abs(v - opacity) < 0.001;
    }

    /// <summary>旋转：每次顺时针 90 度，窗口宽高随外接矩形互换。</summary>
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        _rotationStep = (_rotationStep + 1) % 4;
        ApplyTransform();
    }

    /// <summary>镜像：水平翻转。</summary>
    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        _mirrored = !_mirrored;
        ApplyTransform();
    }

    /// <summary>
    /// 把旋转角度与镜像状态同步到 ScaleTransform / RotateTransform，
    /// 并随即重算窗口尺寸，使其始终紧贴图片旋转后的外接矩形。
    /// </summary>
    private void ApplyTransform()
    {
        RotateTransform.Angle = RotationAngle;
        ScaleTransform.ScaleX = _mirrored ? -1 : 1;
        ScaleTransform.ScaleY = 1;
        ApplyWindowSize();
    }

    /// <summary>
    /// 按当前旋转角度与边框厚度计算窗口外接尺寸：
    /// 90/270 度时图片外接矩形宽高互换。窗口 = 图片外接矩形 + 两侧边框，
    /// 而 ContentRoot 通过 Margin=border 向内缩，图片内容面积恒为 imgW×imgH，
    /// 边框向外生长、不侵占图片内容。窗口边缘始终紧贴（边框 + 图片），无多余留白。
    /// imgW/imgH 为物理像素，需除以 DPI 系数转 DIP 后再设窗口尺寸（docs/02 §5）。
    /// </summary>
    private void ApplyWindowSize()
    {
        bool swapped = (_rotationStep % 2) == 1;
        double imgW = (swapped ? _naturalHeight : _naturalWidth) / _scaleX;
        double imgH = (swapped ? _naturalWidth : _naturalHeight) / _scaleY;
        double border = PinBorder.BorderThickness.Left; // 均匀边框（DIP）

        Width = imgW + 2 * border;
        Height = imgH + 2 * border;

        // ContentRoot 内缩 border = 图片视觉区 = AnnotationCanvas 铺满区 = RenderTargetBitmap 导出根。
        ContentRoot.Margin = new Thickness(border);
    }

    // -----------------------------------------------------------------------
    // 光栅化导出：复制 / 另存为 / 作为文件打开（docs/02 §8.2）
    // -----------------------------------------------------------------------

    /// <summary>
    /// 把 ContentRoot（图片 + 批注）光栅化为 BitmapSource：
    /// 1. 摘阴影——渲染前 PinImage.Effect=null + InvalidateVisual，渲染后恢复，防阴影烤入；
    /// 2. DPI 缩放——按系统 DPI 放大像素维度，避免高 DPI 屏糊；
    /// 3. 渲染根 ContentRoot 天然排除 AnnotationToolbar（在其外）与 PinBorder。
    /// </summary>
    private BitmapSource RenderComposite()
    {
        var savedEffect = PinImage.Effect;
        PinImage.Effect = null;
        try
        {
            // 强制 flush 阴影摘除，避免 Effect=null 未即时生效把阴影烤入位图
            PinImage.InvalidateVisual();
            ContentRoot.UpdateLayout();
            var dpi = VisualTreeHelper.GetDpi(ContentRoot);
            int w = (int)(ContentRoot.ActualWidth * dpi.DpiScaleX);
            int h = (int)(ContentRoot.ActualHeight * dpi.DpiScaleY);
            if (w <= 0 || h <= 0) return _source; // 兜底：尺寸异常回退原图

            var rtb = new RenderTargetBitmap(w, h, dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
            rtb.Render(ContentRoot);
            var result = (BitmapSource)rtb;
            result.Freeze();
            return result;
        }
        finally
        {
            PinImage.Effect = savedEffect;
        }
    }

    /// <summary>复制图片：光栅化复合图写入剪贴板（剪贴板被独占时静默）。批注保存即走此路径。</summary>
    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetImage(RenderComposite()); }
        catch { /* 剪贴板被独占不阻断 */ }
    }

    /// <summary>另存为...：光栅化复合图按所选格式（PNG/JPEG）保存到用户选择的路径。</summary>
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        _ = SaveAsAsync();
    }

    private async Task SaveAsAsync()
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片 (*.png)|*.png|JPEG 图片 (*.jpg;*.jpeg)|*.jpg;*.jpeg",
            FileName = "screenshot.png",
        };
        if (dlg.ShowDialog() != true)
            return;

        string fileName = dlg.FileName;
        BitmapSource composite = RenderComposite();
        await Task.Run(() =>
        {
            try
            {
                bool isJpeg = fileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                           || fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
                BitmapEncoder encoder = isJpeg ? new JpegBitmapEncoder() : new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(composite));
                using var fs = new FileStream(fileName, FileMode.Create);
                encoder.Save(fs);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: 保存图片失败: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>作为文件打开：光栅化复合图写临时缓存文件后用系统默认程序打开。</summary>
    private void OpenAsFile_Click(object sender, RoutedEventArgs e)
    {
        _ = OpenAsFileAsync();
    }

    private async Task OpenAsFileAsync()
    {
        string path = Path.Combine(Path.GetTempPath(), $"myquicker_pin_{System.Guid.NewGuid():N}.png");
        BitmapSource composite = RenderComposite();
        await Task.Run(() =>
        {
            try
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(composite));
                using var fs = new FileStream(path, FileMode.Create);
                encoder.Save(fs);

                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"ERROR: 作为文件打开失败: {ex.Message}");
            }
        }).ConfigureAwait(false);
    }

    /// <summary>窗口关闭时释放图片源、Brush、批注元素、鼠标捕获与事件订阅，避免资源泄漏。</summary>
    protected override void OnClosed(EventArgs e)
    {
        PinImage.Source = null;
        PinImage.ClearValue(System.Windows.Controls.Image.EffectProperty);
        PinBorder.BorderBrush = null;
        AnnotationCanvas.Children.Clear();
        _currentBrush = null!;

        if (AnnotationCanvas.IsMouseCaptured)
            AnnotationCanvas.ReleaseMouseCapture();

        SourceInitialized -= OnSourceInitialized;
        DpiChanged -= OnDpiChanged;

        base.OnClosed(e);
    }

    /// <summary>关闭。</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

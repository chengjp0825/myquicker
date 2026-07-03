using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MyQuicker.Services;
using Clipboard = System.Windows.Clipboard;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace MyQuicker.UI;

/// <summary>
/// 贴图常驻窗口：把一张截图钉在桌面上，可拖拽、缩放、旋转、镜像、调透明度，
/// 并提供右键菜单。左键双击关闭。Per SPEC 8C (PinEngine).
/// </summary>
public partial class PinWindow : Window
{
    private readonly BitmapSource _source;

    // 旋转累计角度（每次 +RotationStepDegrees）与水平镜像开关
    private int _rotationStep;
    private bool _mirrored;

    // 每次旋转的步进角度（度），来自统一配置
    private readonly double _rotationStepDegrees = SettingsManager.Instance.Settings.Pin.RotationStepDegrees;

    // 原始物理像素尺寸（重置大小用）
    private readonly double _naturalWidth;
    private readonly double _naturalHeight;

    /// <summary>当前旋转角度（0/90/180/270）。步进值取自 SettingsModel.Pin.RotationStepDegrees。</summary>
    private double RotationAngle => (_rotationStep % 4) * _rotationStepDegrees;

    /// <param name="screenX">贴图左上角目标屏幕横坐标（物理像素，来自截图结算）。</param>
    /// <param name="screenY">贴图左上角目标屏幕纵坐标（物理像素，来自截图结算）。</param>
    public PinWindow(BitmapSource source, double screenX, double screenY)
    {
        InitializeComponent();

        // 关键视觉参数从统一配置注入（Per SPEC 重构 Step 3）。
        var pin = SettingsManager.Instance.Settings.Pin;
        MinWidth = pin.MinWidth;
        MinHeight = pin.MinHeight;
        PinBorder.BorderBrush = BrushHelper.ToBrush(pin.BorderColor);
        ShadowEffect.BlurRadius = pin.ShadowBlurRadius;
        Opacity = pin.DefaultOpacity;

        _source = source;
        _naturalWidth = source.PixelWidth;
        _naturalHeight = source.PixelHeight;

        PinImage.Source = source;

        // 先定位 Left/Top，再由 ApplyTransform → ApplyWindowSize 设定宽高，
        // 确保窗口左上角对齐选区、宽高紧贴图片外接矩形（含边框）。
        Left = screenX;
        Top = screenY;

        ApplyTransform();
    }

    // -----------------------------------------------------------------------
    // 拖拽与双击关闭
    // -----------------------------------------------------------------------

    /// <summary>
    /// 左键按下：双击关闭，否则交给 WPF 原生 <see cref="Window.DragMove"/>。
    /// DragMove 走系统模态移动循环，逐帧由系统定位窗口，无手算坐标平移，故无抖动。
    /// </summary>
    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // 左键双击 → 关闭窗口
        if (e.ClickCount >= 2)
        {
            Close();
            return;
        }

        DragMove();
    }

    // -----------------------------------------------------------------------
    // 右键菜单：置顶 / 显示阴影 / 重置大小 / 不透明度 / 旋转 / 镜像
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

    /// <summary>重置大小：恢复 1:1 像素比例，窗口尺寸回归当前旋转方向的外接矩形。</summary>
    private void ResetSize_Click(object sender, RoutedEventArgs e)
    {
        ApplyTransform(); // 内部已重置缩放并同步窗口尺寸
    }

    /// <summary>不透明度子菜单：0.3 / 0.5 / 0.8 / 1.0。</summary>
    private void Opacity_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi && mi.Tag is string s && double.TryParse(s, out double op))
            Opacity = op;

        // 同步勾选状态：同一父菜单下只勾当前项
        if (sender is MenuItem current && current.Parent is MenuItem parent)
        {
            foreach (var child in parent.Items)
                if (child is MenuItem m)
                    m.IsChecked = ReferenceEquals(m, current);
        }
    }

    /// <summary>旋转：每次顺时针 90 度，窗口宽高随外接矩形互换。</summary>
    private void Rotate_Click(object sender, RoutedEventArgs e)
    {
        _rotationStep = (_rotationStep + 1) % 4;
        ApplyTransform(); // 旋转后由 ApplyWindowSize 按 90/270 互换宽高
    }

    /// <summary>镜像：水平翻转。</summary>
    private void Mirror_Click(object sender, RoutedEventArgs e)
    {
        _mirrored = !_mirrored;
        ApplyTransform();
    }

    /// <summary>显示边界：在无边框与 2px 灰边框之间切换，边框向外生长。</summary>
    private void Border_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuItem mi)
        {
            double oldBorder = PinBorder.BorderThickness.Left;
            double newBorder = mi.IsChecked ? 2 : 0;
            PinBorder.BorderThickness = new Thickness(newBorder);

            // 边框向外生长：窗口左上角反向偏移 newBorder-oldBorder，
            // 使图片内容的屏幕坐标保持不变（边框对称外扩）。
            Left -= (newBorder - oldBorder);
            Top -= (newBorder - oldBorder);

            ApplyWindowSize(); // 重算窗口外接尺寸 + 图片 Margin 内缩
        }
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
    /// 而图片自身通过 Margin=border 向内缩，面积恒为 imgW×imgH，边框向外生长、
    /// 不侵占图片内容。窗口边缘始终紧贴（边框 + 图片），无多余留白。
    /// </summary>
    private void ApplyWindowSize()
    {
        bool swapped = (_rotationStep % 2) == 1;
        double imgW = swapped ? _naturalHeight : _naturalWidth;
        double imgH = swapped ? _naturalWidth : _naturalHeight;
        double border = PinBorder.BorderThickness.Left; // 均匀边框

        // 窗口整体向外扩张，容纳外圈边框；图片内容面积保持 imgW×imgH 不变。
        Width = imgW + 2 * border;
        Height = imgH + 2 * border;

        // 图片内缩 border：把外圈让给边框，确保边框不覆盖图片像素。
        PinImage.Margin = new Thickness(border);
    }

    // -----------------------------------------------------------------------
    // 右键菜单：复制 / 另存为 / 作为文件打开 / 关闭
    // -----------------------------------------------------------------------

    /// <summary>复制图片：写入系统剪贴板。</summary>
    private void CopyImage_Click(object sender, RoutedEventArgs e)
    {
        Clipboard.SetImage(_source);
    }

    /// <summary>另存为...：用 PngBitmapEncoder 保存到用户选择的路径。</summary>
    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Filter = "PNG 图片 (*.png)|*.png",
            FileName = "screenshot.png",
        };
        if (dlg.ShowDialog() != true)
            return;

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(_source));
        using var fs = new FileStream(dlg.FileName, FileMode.Create);
        encoder.Save(fs);
    }

    /// <summary>作为文件打开：写临时缓存文件后用系统默认程序打开。</summary>
    private void OpenAsFile_Click(object sender, RoutedEventArgs e)
    {
        string path = Path.Combine(Path.GetTempPath(), $"myquicker_pin_{System.Guid.NewGuid():N}.png");
        try
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(_source));
            using var fs = new FileStream(path, FileMode.Create);
            encoder.Save(fs);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true,
            });
        }
        catch
        {
            // 打开失败时静默：贴图窗口本身不受影响
        }
    }

    /// <summary>关闭。</summary>
    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

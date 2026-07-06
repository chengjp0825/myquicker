using System.Drawing;
using System.Drawing.Imaging;
using System.Linq;
using System.Windows.Forms;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;

namespace MyQuicker.Services;

/// <summary>
/// Captures a full base image spanning the capture scope and returns it as a
/// disposable <see cref="CapturedImage"/> domain object. Per SPEC step 8A.
/// </summary>
internal sealed class ScreenshotCaptureService : IScreenshotCaptureService
{
    private readonly SnippingSettings _settings;

    public ScreenshotCaptureService(SnippingSettings settings)
    {
        _settings = settings ?? throw new System.ArgumentNullException(nameof(settings));
    }

    /// <summary>
    /// Captures the chosen scope into a single bitmap and wraps it in a
    /// <see cref="CapturedImage"/>. The caller owns the bitmap and must
    /// dispose it after converting or consuming the pixels.
    /// </summary>
    public CapturedImage Capture()
    {
        var (bounds, fallback) = ComputeBounds();
        return CaptureCore(bounds, fallback);
    }

    /// <summary>
    /// 异步采集截图范围，避免 UI 线程被 <see cref="System.Drawing.Graphics.CopyFromScreen"/> 阻塞。
    /// 调用方仍需在消费完毕后显式释放返回的 <see cref="CapturedImage"/ >。
    /// </summary>
    public Task<CapturedImage> CaptureAsync()
    {
        var (bounds, fallback) = ComputeBounds();
        return Task.Run(() => CaptureCore(bounds, fallback));
    }

    private static CapturedImage CaptureCore(Rectangle bounds, bool fallback)
    {
        var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            // Copy the full capture scope (source origin may be negative). 物理像素 1:1 抓取，
            // 不受 DPI 缩放影响——底图始终为屏幕真实物理分辨率，保证截图质量。
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bitmap.Size, CopyPixelOperation.SourceCopy);
        }

        return new CapturedImage(bitmap, bounds, fallback);
    }

    /// <summary>
    /// 按 <see cref="SnippingSettings.CaptureScope"/> 计算截图范围：
    /// <see cref="SnippingCaptureScope.AllMonitors"/> 取所有显示器并集（跨屏拼接），
    /// <see cref="SnippingCaptureScope.CurrentMonitor"/> 取光标所在显示器。X/Y 可能为负
    /// （显示器在主屏左/上方时）。AllMonitors 且主副屏 DPI 不一致时回退为光标所在屏
    /// （单 WPF 窗口无法跨混合 DPI 1:1 渲染，docs/02 §5）。
    /// </summary>
    private (Rectangle Bounds, bool FallbackToCurrent) ComputeBounds()
    {
        Screen[] screens = Screen.AllScreens;
        var scope = _settings.CaptureScope;

        if (scope == SnippingCaptureScope.CurrentMonitor)
            return (CurrentMonitorBounds(screens), false);

        // AllMonitors：仅当所有显示器 DPI 一致时跨屏拼接；混合 DPI 回退光标所在屏。
        if (!DpiHelper.AllScreensSameScale())
            return (CurrentMonitorBounds(screens), true);

        return (ComputeVirtualBounds(screens), false);
    }

    /// <summary>光标所在显示器矩形；光标不在任何屏（罕见）时回退虚拟屏。</summary>
    private static Rectangle CurrentMonitorBounds(Screen[] screens)
    {
        var cursor = Cursor.Position;
        foreach (Screen s in screens)
            if (s.Bounds.Contains(cursor))
                return s.Bounds;
        return ComputeVirtualBounds(screens);
    }

    /// <summary>
    /// 计算包围所有屏幕的最小矩形（跨屏虚拟屏）。X/Y 可能为负。
    /// </summary>
    private static Rectangle ComputeVirtualBounds(Screen[] screens)
    {
        int xMin = screens.Min(s => s.Bounds.X);
        int yMin = screens.Min(s => s.Bounds.Y);
        int xMax = screens.Max(s => s.Bounds.Right);
        int yMax = screens.Max(s => s.Bounds.Bottom);
        return new Rectangle(xMin, yMin, xMax - xMin, yMax - yMin);
    }
}

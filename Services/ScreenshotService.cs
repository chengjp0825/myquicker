using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Media.Imaging;
using MyQuicker.Interop;
using MyQuicker.Models;

namespace MyQuicker.Services;

/// <summary>
/// Captures a full base image spanning the capture scope and returns
/// it as a WPF BitmapSource along with the physical-screen bounds. Per
/// SPEC step 8A.
/// </summary>
internal sealed class ScreenshotService
{
    /// <summary>
    /// Captures the chosen scope into a single bitmap and converts it to a
    /// frozen BitmapSource, freeing the intermediate GDI handle.
    /// </summary>
    /// <returns>
    /// <c>Source</c>：全物理像素底图（32bppArgb，高保真）；
    /// <c>Bounds</c>：物理屏矩形（X/Y 可能为负）；
    /// <c>FallbackToCurrent</c>：AllMonitors 因主副屏 DPI 不一致无法跨屏而回退为光标所在屏。
    /// </returns>
    public (BitmapSource Source, Rectangle Bounds, bool FallbackToCurrent) Capture()
    {
        var (bounds, fallback) = ComputeBounds();

        using var bmp = new Bitmap(bounds.Width, bounds.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            // Copy the full capture scope (source origin may be negative). 物理像素 1:1 抓取，
            // 不受 DPI 缩放影响——底图始终为屏幕真实物理分辨率，保证截图质量。
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bmp.Size, CopyPixelOperation.SourceCopy);
        }

        IntPtr hBitmap = bmp.GetHbitmap();
        try
        {
            BitmapSource source = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

            source.Freeze(); // force a copy so the HBITMAP can be released safely
            return (source, bounds, fallback);
        }
        finally
        {
            // Core memory constraint: release the unmanaged GDI handle immediately.
            NativeMethods.DeleteObject(hBitmap);
        }
    }

    /// <summary>
    /// 按 <see cref="SnippingSettings.CaptureScope"/> 计算截图范围：
    /// <see cref="SnippingCaptureScope.AllMonitors"/> 取所有显示器并集（跨屏拼接），
    /// <see cref="SnippingCaptureScope.CurrentMonitor"/> 取光标所在显示器。X/Y 可能为负
    /// （显示器在主屏左/上方时）。AllMonitors 且主副屏 DPI 不一致时回退为光标所在屏
    /// （单 WPF 窗口无法跨混合 DPI 1:1 渲染，docs/02 §5）。
    /// </summary>
    private static (Rectangle Bounds, bool FallbackToCurrent) ComputeBounds()
    {
        Screen[] screens = Screen.AllScreens;
        var scope = SettingsManager.Instance.Settings.Snipping.CaptureScope;

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
        var cursor = System.Windows.Forms.Cursor.Position;
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

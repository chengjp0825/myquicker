using System.Drawing;
using System.Windows.Forms;
using MyQuicker.Interop;

namespace MyQuicker.Services;

/// <summary>
/// DIP（WPF 逻辑像素）↔ 物理像素 的缩放系数，按目标显示器取真实 DPI。Per docs/02 §5。
/// </summary>
/// <remarks>
/// WPF 在 Per-Monitor V2 感知下，窗口 <c>Left/Top/Width/Height</c> 以 DIP 为单位，
/// 而 <c>Screen.Bounds</c> / <c>CopyFromScreen</c> / <c>DwmGetWindowAttribute</c> /
/// 截图底图均为物理像素。主副屏缩放不一致时，必须按截图所在显示器取其 DPI，
/// 否则覆盖层被放大/缩小、底图拉伸、寻边红框错位、裁剪与框选不一致（docs/02 §5）。
/// 100% 缩放下系数为 1.0，行为与旧行为一致。
/// </remarks>
internal static class DpiHelper
{
    /// <summary>主屏 DIP→物理像素 X 系数（兜底用，GetDpiForMonitor 失败时回退）。</summary>
    public static double PrimaryScaleX { get; }

    /// <summary>主屏 DIP→物理像素 Y 系数。</summary>
    public static double PrimaryScaleY { get; }

    static DpiHelper()
    {
        try
        {
            using var g = Graphics.FromHwnd(IntPtr.Zero); // 桌面 DC → 主屏 DPI
            PrimaryScaleX = g.DpiX / 96.0;
            PrimaryScaleY = g.DpiY / 96.0;
        }
        catch
        {
            // GDI+ 初始化失败（极罕见，如 RDP/无头环境）：回退 1.0。
            PrimaryScaleX = 1.0;
            PrimaryScaleY = 1.0;
        }
    }

    /// <summary>取包含指定物理点的显示器的 DPI 缩放（DIP→物理）。失败回退主屏。</summary>
    public static (double sx, double sy) ScaleForPoint(NativeMethods.POINT pt)
    {
        IntPtr hmon = NativeMethods.MonitorFromPoint(pt, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return ScaleForHmonitor(hmon);
    }

    /// <summary>取包含 <paramref name="bounds"/> 中心的显示器的 DPI 缩放。bounds 为物理像素。</summary>
    public static (double sx, double sy) ScaleForBounds(Rectangle bounds)
    {
        var center = new NativeMethods.POINT { X = bounds.X + bounds.Width / 2, Y = bounds.Y + bounds.Height / 2 };
        return ScaleForPoint(center);
    }

    /// <summary>所有显示器是否 DPI 缩放一致（混合 DPI 检测，用于 AllMonitors 是否可跨屏）。</summary>
    public static bool AllScreensSameScale()
    {
        double? first = null;
        foreach (Screen s in Screen.AllScreens)
        {
            var (sx, _) = ScaleForBounds(s.Bounds);
            if (first is null) first = sx;
            else if (Math.Abs(sx - first.Value) > 0.01) return false;
        }
        return true;
    }

    /// <summary>
    /// 取指定窗口 HWND 所在显示器的 DPI 缩放。相比 <see cref="ScaleForBounds"/ >，
    /// 此方法不依赖透明窗的 WPF 渲染 DPI，能确定性解决 KI-1 中副屏缩放误判问题。
    /// </summary>
    public static (double sx, double sy) ScaleForWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return (PrimaryScaleX, PrimaryScaleY);
        uint dpi = NativeMethods.GetDpiForWindow(hwnd);
        if (dpi == 0) return (PrimaryScaleX, PrimaryScaleY);
        double scale = dpi / 96.0;
        return (scale, scale);
    }

    private static (double sx, double sy) ScaleForHmonitor(IntPtr hmon)
    {
        if (hmon == IntPtr.Zero) return (PrimaryScaleX, PrimaryScaleY);
        int hr = NativeMethods.GetDpiForMonitor(hmon, NativeMethods.MDT_EFFECTIVE_DPI, out uint dx, out uint dy);
        if (hr != 0 || dx == 0) return (PrimaryScaleX, PrimaryScaleY);
        return (dx / 96.0, (dy == 0 ? dx : dy) / 96.0);
    }
}

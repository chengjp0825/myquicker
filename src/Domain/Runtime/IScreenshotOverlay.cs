using System.Drawing;
using System.Threading.Tasks;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// 截图覆盖层 seam：在全屏底图上让用户选择目标区域，返回物理屏幕坐标系中的矩形。
/// 使用 GDI+ <see cref="Bitmap"/> 作为输入，保证领域层与 WPF 解耦。
/// </summary>
public interface IScreenshotOverlay
{
    /// <summary>
    /// 显示截图覆盖层并异步等待用户完成一次区域选择。
    /// </summary>
    /// <param name="fullImage">全屏底图（物理像素 1:1）。</param>
    /// <param name="fullBounds">底图在虚拟屏幕坐标系中的边界（物理像素）。</param>
    /// <returns>
    /// 用户确认的选区（物理屏幕坐标，与 <paramref name="fullBounds"/> 同坐标系）；
    /// 取消或无效选区时返回 <c>null</c>。
    /// </returns>
    Task<Rectangle?> SelectRegionAsync(Bitmap fullImage, Rectangle fullBounds);
}

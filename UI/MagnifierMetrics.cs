using MyQuicker.Domain.DTO;

namespace MyQuicker.UI;

/// <summary>
/// 像素放大镜的布局计算助手：把“物理源像素块”映射为 WPF DIP 尺寸与偏移，
/// 保证浅蓝十字带 / 中心高亮框与 <see cref="ScreenshotWindow"/> 中实际放大的像素块对齐。
/// </summary>
internal static class MagnifierMetrics
{
    /// <summary>由放大倍率预设得到裁剪源区的物理像素边长（source_size）。</summary>
    public static int GetSourceSize(MagnifierZoomPreset preset) => preset switch
    {
        MagnifierZoomPreset.Large => 10,
        MagnifierZoomPreset.Small => 26,
        _ => 13 // Medium
    };

    /// <summary>
    /// 计算放大镜内单个像素块的 DIP 尺寸，以及“鼠标所在像素块”在放大镜内的 DIP 偏移。
    /// 放大镜物理直径固定，source_size 把直径切成 source_size 个等宽像素块；
    /// 鼠标像素在源区中的索引为 <c>source_size / 2</c>（整数除法），
    /// 因此其左上角物理坐标为 <c>(source_size / 2) * (diameter / source_size)</c>。
    /// </summary>
    public static (double blockWidthDip, double blockHeightDip, double cursorBlockStartDipX, double cursorBlockStartDipY)
        ComputeBlockMetrics(int loupeDiameter, int sourceSize, double scaleX, double scaleY)
    {
        double blockWidthPhys = loupeDiameter / (double)sourceSize;
        double blockHeightPhys = loupeDiameter / (double)sourceSize;

        double blockWidthDip = blockWidthPhys / scaleX;
        double blockHeightDip = blockHeightPhys / scaleY;

        int half = sourceSize / 2;
        double cursorBlockStartDipX = (half * blockWidthPhys) / scaleX;
        double cursorBlockStartDipY = (half * blockHeightPhys) / scaleY;

        return (blockWidthDip, blockHeightDip, cursorBlockStartDipX, cursorBlockStartDipY);
    }
}

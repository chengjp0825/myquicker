using MyQuicker.Domain.DTO;
using MyQuicker.UI;
using Xunit;

namespace MyQuicker.Tests.UI;

/// <summary>
/// 放大镜十字带 / 中心高亮框必须与鼠标下的实际像素块对齐。
/// 这些测试锁定 MagnifierMetrics 的布局计算，防止改回“几何居中”的实现。
/// </summary>
public class MagnifierAlignmentTests
{
    private const int LoupeDiameter = 130;

    [Theory]
    [InlineData(MagnifierZoomPreset.Small, 1.0)]
    [InlineData(MagnifierZoomPreset.Small, 1.25)]
    [InlineData(MagnifierZoomPreset.Small, 1.5)]
    [InlineData(MagnifierZoomPreset.Small, 1.75)]
    [InlineData(MagnifierZoomPreset.Small, 2.0)]
    [InlineData(MagnifierZoomPreset.Medium, 1.0)]
    [InlineData(MagnifierZoomPreset.Medium, 1.25)]
    [InlineData(MagnifierZoomPreset.Medium, 1.5)]
    [InlineData(MagnifierZoomPreset.Medium, 1.75)]
    [InlineData(MagnifierZoomPreset.Medium, 2.0)]
    [InlineData(MagnifierZoomPreset.Large, 1.0)]
    [InlineData(MagnifierZoomPreset.Large, 1.25)]
    [InlineData(MagnifierZoomPreset.Large, 1.5)]
    [InlineData(MagnifierZoomPreset.Large, 1.75)]
    [InlineData(MagnifierZoomPreset.Large, 2.0)]
    public void ComputeBlockMetrics_CrosshairBandOverlapsCursorPixelBlock(MagnifierZoomPreset preset, double scale)
    {
        int sourceSize = MagnifierMetrics.GetSourceSize(preset);
        var (blockWidthDip, blockHeightDip, cursorBlockStartDipX, cursorBlockStartDipY) =
            MagnifierMetrics.ComputeBlockMetrics(LoupeDiameter, sourceSize, scale, scale);

        int half = sourceSize / 2;
        double expectedBlockPhys = LoupeDiameter / (double)sourceSize;
        double expectedCursorStartPhys = half * expectedBlockPhys;

        // 1) DIP 块尺寸反向乘回 scale 后必须等于一个物理像素块。
        Assert.Equal(expectedBlockPhys, blockWidthDip * scale, precision: 9);
        Assert.Equal(expectedBlockPhys, blockHeightDip * scale, precision: 9);

        // 2) 十字带左上角必须对齐到鼠标所在像素块的左上角。
        Assert.Equal(expectedCursorStartPhys, cursorBlockStartDipX * scale, precision: 9);
        Assert.Equal(expectedCursorStartPhys, cursorBlockStartDipY * scale, precision: 9);

        // 3) 重合度 100%：十字带区域 [start, start+block] 与光标像素块完全一致。
        double blockLeftDip = expectedCursorStartPhys / scale;
        double blockRightDip = blockLeftDip + expectedBlockPhys / scale;

        Assert.Equal(blockLeftDip, cursorBlockStartDipX, precision: 9);
        Assert.Equal(blockRightDip, cursorBlockStartDipX + blockWidthDip, precision: 9);
        Assert.Equal(blockLeftDip, cursorBlockStartDipY, precision: 9);
        Assert.Equal(blockRightDip, cursorBlockStartDipY + blockHeightDip, precision: 9);
    }

    [Theory]
    [InlineData(MagnifierZoomPreset.Small)]
    [InlineData(MagnifierZoomPreset.Large)]
    public void GeometricCentering_MissesCursorPixelBlock(MagnifierZoomPreset preset)
    {
        int sourceSize = MagnifierMetrics.GetSourceSize(preset);
        double blockPhys = LoupeDiameter / (double)sourceSize;

        double geometricStartPhys = (LoupeDiameter - blockPhys) / 2.0;
        double cursorStartPhys = (sourceSize / 2) * blockPhys;

        Assert.NotEqual(geometricStartPhys, cursorStartPhys);
    }
}

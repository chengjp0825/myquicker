using System.Windows.Media;
using MyQuicker.UI;
using Xunit;

namespace MyQuicker.Tests.UI;

public class BrushHelperTests
{
    [Theory]
    [InlineData("#FFFF0000")]
    [InlineData("#80FF0000")]
    [InlineData("Red")]
    [InlineData("Black")]
    public void ToBrush_WithValidColor_ReturnsFrozenBrush(string value)
    {
        var brush = BrushHelper.ToBrush(value);

        Assert.NotNull(brush);
        Assert.True(brush.IsFrozen);
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    [InlineData("not-a-color")]
    public void ToBrush_WithInvalidColor_Throws(string value)
    {
        Assert.ThrowsAny<System.Exception>(() => BrushHelper.ToBrush(value));
    }

    [Theory]
    [InlineData("#FFFF0000")]
    [InlineData("Red")]
    public void TryToBrush_WithValidColor_ReturnsTrueAndFrozenBrush(string value)
    {
        bool ok = BrushHelper.TryToBrush(value, out var brush);

        Assert.True(ok);
        Assert.NotNull(brush);
        Assert.True(brush.IsFrozen);
    }

    [Theory]
    [InlineData("#GGGGGG")]
    [InlineData("")]
    public void TryToBrush_WithInvalidColor_ReturnsFalse(string value)
    {
        bool ok = BrushHelper.TryToBrush(value, out var brush);

        Assert.False(ok);
        Assert.Null(brush);
    }

    [Fact]
    public void SafeToBrush_WithInvalidColor_ReturnsFallback()
    {
        var fallback = Brushes.Blue;

        var brush = BrushHelper.SafeToBrush("#GGGGGG", fallback);

        Assert.Same(fallback, brush);
    }

    [Fact]
    public void SafeToBrush_WithValidColor_ReturnsParsedBrushNotFallback()
    {
        var fallback = Brushes.Blue;

        var brush = BrushHelper.SafeToBrush("#FFFF0000", fallback);

        Assert.NotSame(fallback, brush);
        Assert.True(brush.IsFrozen);
    }
}

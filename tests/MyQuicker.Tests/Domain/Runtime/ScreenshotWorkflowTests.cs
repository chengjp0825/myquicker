using System;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using MyQuicker.Domain.Runtime;
using MyQuicker.Services;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime;

public class ScreenshotWorkflowTests
{
    [StaFact]
    public async Task RunAsync_WithSelection_PinsCroppedImage()
    {
        var capture = new FakeScreenshotCaptureService();
        var overlay = new FakeScreenshotOverlay
        {
            NextResult = new Rectangle(2, 3, 6, 5)
        };
        var pin = new FakeScreenshotPinService();
        var toast = new FakeToastService();
        var workflow = CreateWorkflow(capture, overlay, pin, toast);

        await workflow.RunAsync();

        Assert.Equal(1, overlay.CallCount);
        Assert.Equal(1, pin.CallCount);
        Assert.NotNull(pin.LastSource);
        Assert.Equal(6, pin.LastSource!.Width);
        Assert.Equal(5, pin.LastSource.Height);
        Assert.Equal(new Rectangle(2, 3, 6, 5), pin.LastPhysicalBounds);
        Assert.Empty(toast.Messages);
    }

    [StaFact]
    public async Task RunAsync_WithoutSelection_DoesNotPin()
    {
        var capture = new FakeScreenshotCaptureService();
        var overlay = new FakeScreenshotOverlay { NextResult = null };
        var pin = new FakeScreenshotPinService();
        var toast = new FakeToastService();
        var workflow = CreateWorkflow(capture, overlay, pin, toast);

        await workflow.RunAsync();

        Assert.Equal(1, overlay.CallCount);
        Assert.Equal(0, pin.CallCount);
    }

    [StaFact]
    public async Task RunAsync_WithCancellationBeforeCapture_ThrowsOperationCanceledException()
    {
        var capture = new FakeScreenshotCaptureService();
        var overlay = new FakeScreenshotOverlay();
        var pin = new FakeScreenshotPinService();
        var toast = new FakeToastService();
        var workflow = CreateWorkflow(capture, overlay, pin, toast);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => workflow.RunAsync(cts.Token));

        Assert.Equal(0, overlay.CallCount);
        Assert.Equal(0, pin.CallCount);
    }

    [StaFact]
    public async Task RunAsync_WhenCaptureFails_ShowsToast()
    {
        var capture = new ThrowingScreenshotCaptureService();
        var overlay = new FakeScreenshotOverlay();
        var pin = new FakeScreenshotPinService();
        var toast = new FakeToastService();
        var workflow = CreateWorkflow(capture, overlay, pin, toast);

        await workflow.RunAsync();

        Assert.Equal(0, overlay.CallCount);
        Assert.Equal(0, pin.CallCount);
        Assert.Single(toast.Messages);
        Assert.Contains("截图失败", toast.Messages[0].Message);
    }

    private static ScreenshotWorkflow CreateWorkflow(
        IScreenshotCaptureService capture,
        FakeScreenshotOverlay overlay,
        FakeScreenshotPinService pin,
        FakeToastService toast)
    {
        return new ScreenshotWorkflow(capture, overlay, pin, toast);
    }

    private sealed class ThrowingScreenshotCaptureService : IScreenshotCaptureService
    {
        public CapturedImage Capture() => throw new InvalidOperationException("capture failed");

        public Task<CapturedImage> CaptureAsync() => Task.FromException<CapturedImage>(new InvalidOperationException("capture failed"));
    }
}

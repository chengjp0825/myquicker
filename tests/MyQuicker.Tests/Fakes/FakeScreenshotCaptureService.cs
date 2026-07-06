using System.Drawing;
using System.Drawing.Imaging;
using MyQuicker.Services;

namespace MyQuicker.Tests.Fakes;

/// <summary>IScreenshotCaptureService 的轻量级手写 Mock。</summary>
internal sealed class FakeScreenshotCaptureService : IScreenshotCaptureService
{
    public bool FallbackToCurrent { get; set; }

    public CapturedImage Capture()
    {
        // 使用极小位图即可满足断言；调用方测试负责 Dispose。
        var bitmap = new Bitmap(10, 10, PixelFormat.Format32bppArgb);
        return new CapturedImage(bitmap, new Rectangle(0, 0, 10, 10), FallbackToCurrent);
    }

    public Task<CapturedImage> CaptureAsync()
    {
        return Task.FromResult(Capture());
    }
}

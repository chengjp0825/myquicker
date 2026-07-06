using System.Drawing;
using System.Threading.Tasks;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>IScreenshotPinService 的手写假实现，用于 ScreenshotWorkflow 测试。</summary>
internal sealed class FakeScreenshotPinService : IScreenshotPinService
{
    public Bitmap? LastSource { get; private set; }
    public Rectangle? LastPhysicalBounds { get; private set; }
    public int CallCount { get; private set; }

    public Task PinAsync(Bitmap source, Rectangle physicalBounds)
    {
        CallCount++;
        LastSource = source;
        LastPhysicalBounds = physicalBounds;
        return Task.CompletedTask;
    }
}

using System.Threading;
using System.Threading.Tasks;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>ScreenshotWorkflow 的手写假实现，用于断言 RunAsync 是否被触发。</summary>
internal class FakeScreenshotWorkflow : ScreenshotWorkflow
{
    public int StartCount { get; private set; }

    public FakeScreenshotWorkflow()
        : base(
            new FakeScreenshotCaptureService(),
            new FakeScreenshotOverlay(),
            new FakeScreenshotPinService(),
            new FakeToastService())
    {
    }

    public override Task RunAsync(CancellationToken cancellationToken = default)
    {
        StartCount++;
        return Task.CompletedTask;
    }
}

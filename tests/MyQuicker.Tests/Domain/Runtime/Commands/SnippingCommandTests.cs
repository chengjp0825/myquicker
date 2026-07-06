using System.Collections.Generic;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class SnippingCommandTests
{
    [StaFact]
    public void Execute_WithWorkflow_ReturnsStartedProcessAndStartsWorkflow()
    {
        var workflow = new FakeScreenshotWorkflow();
        var ctx = CreateContext(workflow);
        var command = new SnippingCommand();

        ActionResult result = command.Execute(ctx, new Dictionary<string, string>());

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Equal(1, workflow.StartCount);
    }

    [StaFact]
    public void Execute_WithoutWorkflow_ReturnsLaunchFailed()
    {
        var ctx = new CommandContext(new FakeProcessLauncher(), new FakeScreenshotCaptureService());
        var command = new SnippingCommand();

        ActionResult result = command.Execute(ctx, new Dictionary<string, string>());

        Assert.Equal(ActionOutcomeKind.LaunchFailed, result.Kind);
        Assert.Equal("截图工作流未配置", result.ToastMessage);
    }

    [StaFact]
    public async Task ExecuteAsync_WithWorkflow_ReturnsStartedProcessAndStartsWorkflow()
    {
        var workflow = new FakeScreenshotWorkflow();
        var ctx = CreateContext(workflow);
        var command = new SnippingCommand();

        ActionResult result = await command.ExecuteAsync(ctx, new Dictionary<string, string>());

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Equal(1, workflow.StartCount);
    }

    private static CommandContext CreateContext(FakeScreenshotWorkflow workflow)
    {
        return new CommandContext(
            new FakeProcessLauncher(),
            new FakeScreenshotCaptureService(),
            workflow,
            new FakeToastService());
    }
}

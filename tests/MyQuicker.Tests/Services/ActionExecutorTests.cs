using System;
using System.Collections.Generic;
using System.ComponentModel;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Services;

public class ActionExecutorTests
{
    [Fact]
    public void Execute_WithSysSnipping_ReturnsStartedProcess()
    {
        var workflow = new FakeScreenshotWorkflow();
        var registry = CreateRegistryWithSnipping(workflow);
        var executor = new ActionExecutor(registry, Array.Empty<CommandDefinition>());
        var ctx = CreateContext(workflow);
        var item = new ActionItem { Name = "截图", CommandId = "sys:snipping" };

        var result = executor.Execute(ctx, item);

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Equal(1, workflow.StartCount);
    }

    [Theory]
    [InlineData(null, "动作未配置命令")]
    [InlineData("", "动作未配置命令")]
    [InlineData("   ", "动作未配置命令")]
    [InlineData("动作A", "动作「动作A」未配置命令")]
    public void Execute_WithEmptyCommand_ReturnsEmptyCommandAndToast(string? name, string expectedToast)
    {
        var registry = new CommandRegistry();
        var executor = new ActionExecutor(registry, Array.Empty<CommandDefinition>());
        var ctx = new CommandContext(new FakeProcessLauncher(), new FakeScreenshotCaptureService());
        var item = new ActionItem { Name = name ?? string.Empty, CommandId = "" };

        var result = executor.Execute(ctx, item);

        Assert.Equal(ActionOutcomeKind.EmptyCommand, result.Kind);
        Assert.Equal(expectedToast, result.ToastMessage);
    }

    [Fact]
    public void Execute_WithUnknownSystemCommand_ReturnsUnknownSystemCommand()
    {
        var registry = new CommandRegistry();
        var executor = new ActionExecutor(registry, Array.Empty<CommandDefinition>());
        var ctx = new CommandContext(new FakeProcessLauncher(), new FakeScreenshotCaptureService());
        var item = new ActionItem { Name = "未知", CommandId = "sys:unknown" };

        var result = executor.Execute(ctx, item);

        Assert.Equal(ActionOutcomeKind.UnknownSystemCommand, result.Kind);
        Assert.Equal("未知指令：sys:unknown", result.ToastMessage);
    }

    [Fact]
    public void Execute_WhenCommandThrows_ReturnsLaunchFailedAndDoesNotPropagate()
    {
        const string commandId = "cmd:chrome";
        const string chromePath = "C:\\Program Files\\Chrome\\chrome.exe";
        var launcher = new FakeProcessLauncher(new Win32Exception(2, "找不到文件"));
        var registry = new CommandRegistry();
        registry.Register(commandId, new LaunchApplicationCommand());
        var commands = new List<CommandDefinition>
        {
            new() { Id = commandId, Type = CommandType.LaunchApplication, Target = chromePath },
        };
        var executor = new ActionExecutor(registry, commands);
        var ctx = new CommandContext(launcher, new FakeScreenshotCaptureService());
        var item = new ActionItem { Name = "启动", CommandId = commandId, Arguments = "--arg" };

        var result = executor.Execute(ctx, item);

        Assert.Equal(ActionOutcomeKind.LaunchFailed, result.Kind);
        Assert.Equal($"无法启动：{commandId}", result.ToastMessage);
        Assert.Equal(commandId, result.ErrorCommand);
    }

    [Fact]
    public void Execute_WithValidExternalCommand_ReturnsStartedProcessAndPassesArguments()
    {
        const string commandId = "cmd:chrome";
        const string chromePath = "C:\\Program Files\\Chrome\\chrome.exe";
        var launcher = new FakeProcessLauncher();
        var registry = new CommandRegistry();
        registry.Register(commandId, new LaunchApplicationCommand());
        var commands = new List<CommandDefinition>
        {
            new() { Id = commandId, Type = CommandType.LaunchApplication, Target = chromePath },
        };
        var executor = new ActionExecutor(registry, commands);
        var ctx = new CommandContext(launcher, new FakeScreenshotCaptureService());
        var item = new ActionItem { Name = "浏览器", CommandId = commandId, Arguments = "--incognito" };

        var result = executor.Execute(ctx, item);

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Null(result.ToastMessage);
        Assert.Single(launcher.Launched);
        Assert.Equal((chromePath, "--incognito"), launcher.Launched[0]);
    }

    [Fact]
    public void Execute_WithUnregisteredExternalCommand_ReturnsLaunchFailed()
    {
        var registry = new CommandRegistry();
        var executor = new ActionExecutor(registry, Array.Empty<CommandDefinition>());
        var ctx = new CommandContext(new FakeProcessLauncher(), new FakeScreenshotCaptureService());
        var item = new ActionItem { Name = "启动", CommandId = "cmd:unregistered" };

        var result = executor.Execute(ctx, item);

        Assert.Equal(ActionOutcomeKind.LaunchFailed, result.Kind);
        Assert.Equal("无法启动：cmd:unregistered", result.ToastMessage);
        Assert.Equal("cmd:unregistered", result.ErrorCommand);
    }

    [Fact]
    public async Task ExecuteAsync_WithSysSnipping_ReturnsStartedProcess()
    {
        var workflow = new FakeScreenshotWorkflow();
        var registry = CreateRegistryWithSnipping(workflow);
        var executor = new ActionExecutor(registry, Array.Empty<CommandDefinition>());
        var ctx = CreateContext(workflow);
        var item = new ActionItem { Name = "截图", CommandId = "sys:snipping" };

        var result = await executor.ExecuteAsync(ctx, item);

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Equal(1, workflow.StartCount);
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyCommand_ReturnsEmptyCommandWithoutThrowing()
    {
        var registry = new CommandRegistry();
        var executor = new ActionExecutor(registry, Array.Empty<CommandDefinition>());
        var ctx = new CommandContext(new FakeProcessLauncher(), new FakeScreenshotCaptureService());
        var item = new ActionItem { Name = "空", CommandId = "" };

        var result = await executor.ExecuteAsync(ctx, item);

        Assert.Equal(ActionOutcomeKind.EmptyCommand, result.Kind);
    }

    private static CommandContext CreateContext(FakeScreenshotWorkflow workflow)
    {
        return new CommandContext(
            new FakeProcessLauncher(),
            new FakeScreenshotCaptureService(),
            workflow,
            new FakeToastService());
    }

    private static CommandRegistry CreateRegistryWithSnipping(FakeScreenshotWorkflow workflow)
    {
        var registry = new CommandRegistry();
        registry.Register("sys:snipping", new SnippingCommand());
        return registry;
    }
}

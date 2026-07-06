using System;
using System.Collections.Generic;
using System.Security;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class LaunchApplicationCommandTests
{
    [Fact]
    public void Execute_WithAbsoluteExecutablePath_ReturnsStartedProcessAndPassesArguments()
    {
        var launcher = new FakeProcessLauncher();
        var ctx = CreateContext(launcher);
        var parameters = new Dictionary<string, string>
        {
            ["target"] = "C:\\Program Files\\Chrome\\chrome.exe",
            ["arguments"] = "--incognito https://example.com"
        };
        var command = new LaunchApplicationCommand();

        ActionResult result = command.Execute(ctx, parameters);

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Single(launcher.Launched);
        Assert.Equal(("C:\\Program Files\\Chrome\\chrome.exe", "--incognito https://example.com"), launcher.Launched[0]);
    }

    [Fact]
    public void Execute_WithEmptyArguments_ReturnsStartedProcessAndEmptyArguments()
    {
        var launcher = new FakeProcessLauncher();
        var ctx = CreateContext(launcher);
        var parameters = new Dictionary<string, string>
        {
            ["target"] = "C:\\Windows\\notepad.exe"
        };
        var command = new LaunchApplicationCommand();

        ActionResult result = command.Execute(ctx, parameters);

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Single(launcher.Launched);
        Assert.Equal(("C:\\Windows\\notepad.exe", string.Empty), launcher.Launched[0]);
    }

    [Fact]
    public void Execute_WithMissingPath_ThrowsArgumentException()
    {
        var ctx = CreateContext(new FakeProcessLauncher());
        var parameters = new Dictionary<string, string>();
        var command = new LaunchApplicationCommand();

        Assert.Throws<ArgumentException>(() => command.Execute(ctx, parameters));
    }

    [Fact]
    public void Execute_WithRelativePath_ThrowsSecurityException()
    {
        var ctx = CreateContext(new FakeProcessLauncher());
        var parameters = new Dictionary<string, string> { ["target"] = "..\\evil.exe" };
        var command = new LaunchApplicationCommand();

        Assert.Throws<SecurityException>(() => command.Execute(ctx, parameters));
    }

    [Fact]
    public void Execute_WithShellMetacharactersInPath_ThrowsSecurityException()
    {
        var ctx = CreateContext(new FakeProcessLauncher());
        var parameters = new Dictionary<string, string> { ["target"] = "cmd.exe & del file" };
        var command = new LaunchApplicationCommand();

        Assert.Throws<SecurityException>(() => command.Execute(ctx, parameters));
    }

    private static CommandContext CreateContext(IProcessLauncher launcher)
    {
        return new CommandContext(launcher, new FakeScreenshotCaptureService());
    }
}

using System;
using System.Collections.Generic;
using System.Security;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class OpenUrlCommandTests
{
    [Theory]
    [InlineData("https://example.com")]
    [InlineData("http://localhost:8080")]
    public void Execute_WithAllowedScheme_ReturnsStartedProcessAndPassesUrl(string url)
    {
        var launcher = new FakeProcessLauncher();
        var ctx = CreateContext(launcher);
        var parameters = new Dictionary<string, string> { ["target"] = url };
        var command = new OpenUrlCommand();

        ActionResult result = command.Execute(ctx, parameters);

        Assert.Equal(ActionOutcomeKind.StartedProcess, result.Kind);
        Assert.Single(launcher.Launched);
        Assert.Equal((url, string.Empty), launcher.Launched[0]);
    }

    [Theory]
    [InlineData("file:///C:/secret.txt")]
    [InlineData("javascript:alert(1)")]
    [InlineData("ftp://example.com")]
    [InlineData("data:text/html,<script>")]
    public void Execute_WithDisallowedScheme_ThrowsSecurityException(string url)
    {
        var ctx = CreateContext(new FakeProcessLauncher());
        var parameters = new Dictionary<string, string> { ["target"] = url };
        var command = new OpenUrlCommand();

        Assert.Throws<SecurityException>(() => command.Execute(ctx, parameters));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("")]
    public void Execute_WithInvalidUrl_ThrowsArgumentException(string url)
    {
        var ctx = CreateContext(new FakeProcessLauncher());
        var parameters = new Dictionary<string, string> { ["target"] = url };
        var command = new OpenUrlCommand();

        Assert.Throws<ArgumentException>(() => command.Execute(ctx, parameters));
    }

    [Fact]
    public void Execute_WithMissingUrl_ThrowsArgumentException()
    {
        var ctx = CreateContext(new FakeProcessLauncher());
        var parameters = new Dictionary<string, string>();
        var command = new OpenUrlCommand();

        Assert.Throws<ArgumentException>(() => command.Execute(ctx, parameters));
    }

    private static CommandContext CreateContext(IProcessLauncher launcher)
    {
        return new CommandContext(launcher, new FakeScreenshotCaptureService());
    }
}

using System.Collections.Generic;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class UserCommandStoreTests
{
    [Fact]
    public void Register_AddsLaunchAndUrlCommands()
    {
        var registry = new CommandRegistry();
        var commands = new List<CommandDefinition>
        {
            new() { Id = "cmd:app", Type = CommandType.LaunchApplication, Target = "C:\\app.exe" },
            new() { Id = "cmd:url", Type = CommandType.OpenUrl, Target = "https://example.com" },
        };

        UserCommandStore.Register(registry, commands);

        Assert.NotNull(registry.Lookup("cmd:app"));
        Assert.NotNull(registry.Lookup("cmd:url"));
    }
}

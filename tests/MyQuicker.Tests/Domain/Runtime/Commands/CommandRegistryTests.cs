using System;
using System.Collections.Generic;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class CommandRegistryTests
{
    [Fact]
    public void Lookup_RegisteredKey_ReturnsCommand()
    {
        var registry = new CommandRegistry();
        var command = new TestCommand();

        registry.Register("test:cmd", command);

        Assert.Same(command, registry.Lookup("test:cmd"));
    }

    [Fact]
    public void Lookup_UnregisteredKey_ReturnsNull()
    {
        var registry = new CommandRegistry();

        ICommand? result = registry.Lookup("missing:cmd");

        Assert.Null(result);
    }

    [Fact]
    public void Register_DuplicateKey_OverwritesWithLatest()
    {
        var registry = new CommandRegistry();
        var first = new TestCommand();
        var second = new TestCommand();

        registry.Register("test:cmd", first);
        registry.Register("test:cmd", second);

        Assert.Same(second, registry.Lookup("test:cmd"));
    }

    [Fact]
    public void Register_NullKey_ThrowsArgumentNullException()
    {
        var registry = new CommandRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register(null!, new TestCommand()));
    }

    [Fact]
    public void Register_NullCommand_ThrowsArgumentNullException()
    {
        var registry = new CommandRegistry();

        Assert.Throws<ArgumentNullException>(() => registry.Register("test:cmd", null!));
    }

    private sealed class TestCommand : ICommand
    {
        public ActionResult Execute(CommandContext context, Dictionary<string, string> parameters)
        {
            return new ActionResult(ActionOutcomeKind.StartedProcess);
        }
    }
}

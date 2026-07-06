using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime.Commands;
using MyQuicker.Services;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime.Commands;

public class BuiltInCommandProviderTests
{
    [Fact]
    public void Register_ProvidesSysSnippingCommand()
    {
        var registry = new CommandRegistry();

        BuiltInCommandProvider.Register(registry);

        var command = registry.Lookup("sys:snipping");
        Assert.NotNull(command);
        Assert.IsType<SnippingCommand>(command);
    }
}

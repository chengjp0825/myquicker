using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using MyQuicker.Domain.DTO;
using MyQuicker.UI;
using Xunit;

namespace MyQuicker.Tests;

public class AppBootstrapperTests
{
    private static readonly Assembly MyQuickerAssembly = typeof(AppBootstrapper).Assembly;

    [Fact]
    public void ActionStore_TypeDoesNotExist()
    {
        // Regression: the static ActionStore cache was removed in Phase 2.
        var type = MyQuickerAssembly.GetType("MyQuicker.Services.ActionStore", throwOnError: false);

        Assert.Null(type);
    }

    [Fact]
    public void MainWindow_ConstructorAcceptsMenuGroups()
    {
        var ctor = typeof(MainWindow).GetConstructors()
            .Single(c => c.IsPublic);

        var parameters = ctor.GetParameters();

        Assert.Contains(parameters, p => p.ParameterType == typeof(List<MenuGroup>));
    }

    [Fact]
    public void MainWindow_RefreshActionsAcceptsMenuGroups()
    {
        var method = typeof(MainWindow).GetMethod("RefreshActions", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("RefreshActions method not found.");

        var parameters = method.GetParameters();

        Assert.Single(parameters);
        Assert.Equal(typeof(List<MenuGroup>), parameters[0].ParameterType);
    }

    [Fact]
    public void AppBootstrapper_DoesNotReferenceActionStore()
    {
        // The private BuildRuntime method must no longer reference the removed ActionStore type.
        var buildRuntime = typeof(AppBootstrapper).GetMethod("BuildRuntime", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("BuildRuntime method not found.");

        var methodBody = buildRuntime.GetMethodBody()
            ?? throw new InvalidOperationException("Could not obtain BuildRuntime method body.");

        var referencedTypes = methodBody.LocalSignatureMetadataToken == 0
            ? Array.Empty<Type>()
            : methodBody.LocalVariables.Select(v => v.LocalType).ToArray();

        Assert.DoesNotContain(referencedTypes, t => t?.FullName == "MyQuicker.Services.ActionStore");
    }
}

using System;
using System.Collections.Generic;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;
using MyQuicker.Services;

namespace MyQuicker.Tests.Services;

public class SettingsBuilderTests
{
    [Fact]
    public void Build_ClampsMenuDimensions()
    {
        var builder = new SettingsBuilder();
        var menu = new MenuSettings { Width = 50, Height = 3000, CornerRadius = 200 };

        var settings = builder.Build(
            new TriggerBinding(),
            new List<MenuGroup>(),
            new List<CommandDefinition>(),
            new SnippingSettings(),
            menu,
            new PinSettings());

        Assert.Equal(100, settings.Preferences.Menu.Width);
        Assert.Equal(2000, settings.Preferences.Menu.Height);
        Assert.Equal(100, settings.Preferences.Menu.CornerRadius);
    }

    [Fact]
    public void Build_ClampsSnippingAndPinValues()
    {
        var builder = new SettingsBuilder();
        var snipping = new SnippingSettings { DragThreshold = 0, MaskAlpha = 1.5 };
        var pin = new PinSettings { DefaultOpacity = 0.0 };

        var settings = builder.Build(
            new TriggerBinding(),
            new List<MenuGroup>(),
            new List<CommandDefinition>(),
            snipping,
            new MenuSettings(),
            pin);

        Assert.Equal(1, settings.Preferences.Snipping.DragThreshold);
        Assert.Equal(1.0, settings.Preferences.Snipping.MaskAlpha);
        Assert.Equal(0.1, settings.Preferences.Pin.DefaultOpacity);
    }

    [Fact]
    public void Build_NullArguments_ThrowsArgumentNullException()
    {
        var builder = new SettingsBuilder();
        var binding = new TriggerBinding();
        var groups = new List<MenuGroup>();
        var commands = new List<CommandDefinition>();
        var snipping = new SnippingSettings();
        var menu = new MenuSettings();
        var pin = new PinSettings();

        Assert.Throws<ArgumentNullException>(() => builder.Build(null!, groups, commands, snipping, menu, pin));
        Assert.Throws<ArgumentNullException>(() => builder.Build(binding, null!, commands, snipping, menu, pin));
        Assert.Throws<ArgumentNullException>(() => builder.Build(binding, groups, null!, snipping, menu, pin));
        Assert.Throws<ArgumentNullException>(() => builder.Build(binding, groups, commands, null!, menu, pin));
        Assert.Throws<ArgumentNullException>(() => builder.Build(binding, groups, commands, snipping, null!, pin));
        Assert.Throws<ArgumentNullException>(() => builder.Build(binding, groups, commands, snipping, menu, null!));
    }

    [Fact]
    public void BuildTriggerBinding_CircleGestureUsesNegativeOne()
    {
        var builder = new SettingsBuilder();
        var binding = builder.BuildTriggerBinding(3, intercept: false, circleSensitivityIndex: 1);

        Assert.Equal(TriggerType.CircleGesture, binding.Type);
        Assert.Null(binding.WakeupMessage);
        Assert.Null(binding.XButtonData);
        Assert.False(binding.InterceptWakeupKey);
        Assert.Equal(CircleSensitivity.Medium, binding.CircleSensitivity);
    }

    [Fact]
    public void BuildTriggerBinding_MiddleButton()
    {
        var builder = new SettingsBuilder();
        var binding = builder.BuildTriggerBinding(0, intercept: true, circleSensitivityIndex: 2);

        Assert.Equal(TriggerType.Button, binding.Type);
        Assert.Equal(NativeMethods.WM_MBUTTONDOWN, binding.WakeupMessage);
        Assert.Null(binding.XButtonData);
        Assert.True(binding.InterceptWakeupKey);
    }

    [Fact]
    public void BuildTriggerBinding_SideButton()
    {
        var builder = new SettingsBuilder();
        var binding = builder.BuildTriggerBinding(1, intercept: false, circleSensitivityIndex: 0);

        Assert.Equal(TriggerType.Button, binding.Type);
        Assert.Equal(NativeMethods.WM_XBUTTONDOWN, binding.WakeupMessage);
        Assert.Equal(1, binding.XButtonData);
        Assert.False(binding.InterceptWakeupKey);
    }

    [Fact]
    public void BuildTriggerBinding_UsesCustomResolver()
    {
        var builder = new SettingsBuilder(index => index * 10);

        var binding = builder.BuildTriggerBinding(5, intercept: true, circleSensitivityIndex: 0);

        Assert.Equal(TriggerType.Button, binding.Type);
        Assert.Equal(50, binding.WakeupMessage);
    }

    [Fact]
    public void WakeupKeyIndexToMessage_MapsExpectedIndices()
    {
        Assert.Equal(NativeMethods.WM_MBUTTONDOWN, SettingsBuilder.WakeupKeyIndexToMessage(0));
        Assert.Equal(NativeMethods.WM_XBUTTONDOWN, SettingsBuilder.WakeupKeyIndexToMessage(1));
        Assert.Equal(NativeMethods.WM_RBUTTONDOWN, SettingsBuilder.WakeupKeyIndexToMessage(2));
        Assert.Equal(-1, SettingsBuilder.WakeupKeyIndexToMessage(3));
        Assert.Equal(NativeMethods.WM_MBUTTONDOWN, SettingsBuilder.WakeupKeyIndexToMessage(99));
    }

    [Fact]
    public void Build_DoesNotMutateInputPreferenceObjects()
    {
        var builder = new SettingsBuilder();
        var menu = new MenuSettings { Width = 50, Height = 3000, CornerRadius = 200 };
        var snipping = new SnippingSettings { DragThreshold = 0, MaskAlpha = 1.5 };
        var pin = new PinSettings { DefaultOpacity = 0.0 };

        _ = builder.Build(
            new TriggerBinding(),
            new List<MenuGroup>(),
            new List<CommandDefinition>(),
            snipping,
            menu,
            pin);

        Assert.Equal(50, menu.Width);
        Assert.Equal(3000, menu.Height);
        Assert.Equal(200, menu.CornerRadius);
        Assert.Equal(0, snipping.DragThreshold);
        Assert.Equal(1.5, snipping.MaskAlpha);
        Assert.Equal(0.0, pin.DefaultOpacity);
    }

    [Fact]
    public void Build_DefaultGridColumnsIsThree()
    {
        var builder = new SettingsBuilder();

        var settings = builder.Build(
            new TriggerBinding(),
            new List<MenuGroup>(),
            new List<CommandDefinition>(),
            new SnippingSettings(),
            new MenuSettings(),
            new PinSettings());

        Assert.Equal(3, settings.Preferences.Menu.GridColumns);
    }

    [Fact]
    public void Build_ClampsGridColumns()
    {
        var builder = new SettingsBuilder();
        var tooSmall = new MenuSettings { GridColumns = 1 };
        var tooLarge = new MenuSettings { GridColumns = 5 };

        var smallSettings = builder.Build(
            new TriggerBinding(),
            new List<MenuGroup>(),
            new List<CommandDefinition>(),
            new SnippingSettings(),
            tooSmall,
            new PinSettings());
        var largeSettings = builder.Build(
            new TriggerBinding(),
            new List<MenuGroup>(),
            new List<CommandDefinition>(),
            new SnippingSettings(),
            tooLarge,
            new PinSettings());

        Assert.Equal(2, smallSettings.Preferences.Menu.GridColumns);
        Assert.Equal(3, largeSettings.Preferences.Menu.GridColumns);
    }
}

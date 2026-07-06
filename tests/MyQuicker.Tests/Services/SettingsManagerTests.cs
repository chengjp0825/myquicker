using System;
using System.Collections.Generic;
using MyQuicker.Domain.DTO;
using MyQuicker.Services;
using Xunit;

namespace MyQuicker.Tests.Services;

public class SettingsManagerTests
{
    [Fact]
    public void Constructor_CreatesIndependentInstances()
    {
        var first = new SettingsManager();
        var second = new SettingsManager();

        first.Save(new Settings
        {
            TriggerBindings = new List<TriggerBinding> { new() { InterceptWakeupKey = false } }
        });

        Assert.Single(first.Settings.TriggerBindings);
        Assert.Empty(second.Settings.TriggerBindings);
    }

    [Fact]
    public void Save_WithNewSettings_ReplacesInMemorySettings()
    {
        var manager = new SettingsManager();
        manager.Load();

        var newSettings = new Settings
        {
            TriggerBindings = new List<TriggerBinding> { new() { InterceptWakeupKey = false } },
            Preferences = new Preferences { Menu = new MenuSettings { Width = 999 } },
        };

        manager.Save(newSettings);

        Assert.Same(newSettings, manager.Settings);
        Assert.False(manager.Settings.TriggerBindings[0].InterceptWakeupKey);
        Assert.Equal(999, manager.Settings.Preferences.Menu.Width);
    }

    [Fact]
    public void Save_NullSettings_ThrowsArgumentNullException()
    {
        var manager = new SettingsManager();
        Assert.Throws<ArgumentNullException>(() => manager.Save(null!));
    }

    [Fact]
    public async Task SaveAsync_WithNewSettings_ReplacesInMemorySettings()
    {
        var manager = new SettingsManager();
        await manager.LoadAsync();

        var newSettings = new Settings
        {
            TriggerBindings = new List<TriggerBinding> { new() { InterceptWakeupKey = false } },
            Preferences = new Preferences { Menu = new MenuSettings { Width = 999 } },
        };

        await manager.SaveAsync(newSettings);

        Assert.Same(newSettings, manager.Settings);
        Assert.False(manager.Settings.TriggerBindings[0].InterceptWakeupKey);
        Assert.Equal(999, manager.Settings.Preferences.Menu.Width);
    }

    [Fact]
    public async Task SaveAsync_NullSettings_ThrowsArgumentNullException()
    {
        var manager = new SettingsManager();
        await Assert.ThrowsAsync<ArgumentNullException>(() => manager.SaveAsync(null!));
    }

    [Fact]
    public async Task LoadAsync_CreatesDefaultsWhenFileMissing()
    {
        var manager = new SettingsManager();

        var settings = await manager.LoadAsync();

        Assert.NotNull(settings);
        Assert.NotNull(manager.Settings);
    }
}

using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using MyQuicker.Domain.DTO;
using MyQuicker.Services;

namespace MyQuicker.UI;

/// <summary>Bindable view-model for SettingsWindow; owns editable copies of settings DTOs.</summary>
public sealed class SettingsViewModel : INotifyPropertyChanged
{
    public TriggerBinding TriggerBinding { get; } = new();
    public SnippingSettings Snipping { get; } = new();
    public MenuSettings Menu { get; } = new();
    public PinSettings Pin { get; } = new();
    public List<MenuGroup> MenuGroups { get; } = new();
    public List<CommandDefinition> Commands { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    public void LoadFrom(Settings settings)
    {
        Copy(settings.TriggerBindings.FirstOrDefault() ?? new TriggerBinding(), TriggerBinding);
        Copy(settings.Preferences.Snipping, Snipping);
        Copy(settings.Preferences.Menu, Menu);
        Copy(settings.Preferences.Pin, Pin);
        MenuGroups.Clear();
        MenuGroups.AddRange(CloneMenuGroups(settings.MenuGroups));
        Commands.Clear();
        Commands.AddRange(CloneCommands(settings.Commands));
        OnPropertyChanged(string.Empty);
    }

    public Settings Build(SettingsBuilder builder)
    {
        return builder.Build(
            TriggerBinding,
            CloneMenuGroups(MenuGroups),
            CloneCommands(Commands),
            Clone(Snipping),
            Clone(Menu),
            Clone(Pin));
    }

    private static void Copy(TriggerBinding src, TriggerBinding dst)
    {
        dst.Type = src.Type;
        dst.WakeupMessage = src.WakeupMessage;
        dst.XButtonData = src.XButtonData;
        dst.InterceptWakeupKey = src.InterceptWakeupKey;
        dst.CircleSensitivity = src.CircleSensitivity;
    }

    private static void Copy(SnippingSettings src, SnippingSettings dst)
    {
        dst.DragThreshold = src.DragThreshold;
        dst.MaskAlpha = src.MaskAlpha;
        dst.BorderColor = src.BorderColor;
        dst.AfterScreenshot = src.AfterScreenshot;
        dst.CaptureScope = src.CaptureScope;
    }

    private static void Copy(MenuSettings src, MenuSettings dst)
    {
        dst.Width = src.Width;
        dst.Height = src.Height;
        dst.Background = src.Background;
        dst.CornerRadius = src.CornerRadius;
        dst.ButtonBackground = src.ButtonBackground;
        dst.ButtonHoverBackground = src.ButtonHoverBackground;
    }

    private static void Copy(PinSettings src, PinSettings dst)
    {
        dst.BorderColor = src.BorderColor;
        dst.DefaultOpacity = src.DefaultOpacity;
        dst.DefaultShowBorder = src.DefaultShowBorder;
        dst.DefaultAnnotationMode = src.DefaultAnnotationMode;
        dst.DefaultTopmost = src.DefaultTopmost;
        dst.DefaultShowShadow = src.DefaultShowShadow;
    }

    private static List<MenuGroup> CloneMenuGroups(List<MenuGroup> source) =>
        source.Select(g => new MenuGroup
        {
            Id = g.Id,
            DisplayName = g.DisplayName,
            Icon = g.Icon,
            Actions = g.Actions.Select(a => new ActionItem
            {
                Name = a.Name,
                CommandId = a.CommandId,
                Arguments = a.Arguments,
                Icon = a.Icon,
            }).ToList(),
        }).ToList();

    private static List<CommandDefinition> CloneCommands(List<CommandDefinition> source) =>
        source.Select(c => new CommandDefinition
        {
            Id = c.Id,
            DisplayName = c.DisplayName,
            Type = c.Type,
            Target = c.Target,
        }).ToList();

    private static T Clone<T>(T source) where T : new()
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new T();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

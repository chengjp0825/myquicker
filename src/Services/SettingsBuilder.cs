using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using MyQuicker.Domain.DTO;
using MyQuicker.Interop;

namespace MyQuicker.Services;

/// <summary>
/// Constructs and validates a <see cref="Settings"/> DTO from raw UI values.
/// Keeps settings-schema policy out of the view code-behind.
/// </summary>
public sealed class SettingsBuilder
{
    private readonly Func<int, int>? _messageResolver;

    public SettingsBuilder(Func<int, int>? messageResolver = null)
    {
        _messageResolver = messageResolver;
    }

    public Settings Build(
        TriggerBinding triggerBinding,
        List<MenuGroup> menuGroups,
        List<CommandDefinition> commands,
        SnippingSettings snipping,
        MenuSettings menu,
        PinSettings pin)
    {
        if (triggerBinding is null)
            throw new ArgumentNullException(nameof(triggerBinding));
        if (menuGroups is null)
            throw new ArgumentNullException(nameof(menuGroups));
        if (commands is null)
            throw new ArgumentNullException(nameof(commands));
        if (snipping is null)
            throw new ArgumentNullException(nameof(snipping));
        if (menu is null)
            throw new ArgumentNullException(nameof(menu));
        if (pin is null)
            throw new ArgumentNullException(nameof(pin));

        var snippingCopy = Clone(snipping);
        var menuCopy = Clone(menu);
        var pinCopy = Clone(pin);

        NormalizeMenuSettings(menuCopy);
        NormalizeSnippingSettings(snippingCopy);
        NormalizePinSettings(pinCopy);

        return new Settings
        {
            TriggerBindings = new List<TriggerBinding> { triggerBinding },
            MenuGroups = menuGroups,
            Commands = commands,
            Preferences = new Preferences
            {
                Snipping = snippingCopy,
                Menu = menuCopy,
                Pin = pinCopy,
            },
        };
    }

    public TriggerBinding BuildTriggerBinding(int wakeupKeyIndex, bool intercept, int circleSensitivityIndex)
    {
        var binding = new TriggerBinding
        {
            InterceptWakeupKey = intercept,
            CircleSensitivity = (CircleSensitivity)circleSensitivityIndex,
        };

        int msg = _messageResolver?.Invoke(wakeupKeyIndex) ?? WakeupKeyIndexToMessage(wakeupKeyIndex);
        if (msg == -1)
        {
            binding.Type = TriggerType.CircleGesture;
        }
        else
        {
            binding.Type = TriggerType.Button;
            binding.WakeupMessage = msg;
            if (msg == NativeMethods.WM_XBUTTONDOWN)
            {
                binding.XButtonData = 1; // X1; extend resolver if X2 needed
            }
        }

        return binding;
    }

    public static int WakeupKeyIndexToMessage(int index)
    {
        return index switch
        {
            0 => NativeMethods.WM_MBUTTONDOWN,
            1 => NativeMethods.WM_XBUTTONDOWN,
            2 => NativeMethods.WM_RBUTTONDOWN,
            3 => -1, // Circle gesture
            _ => NativeMethods.WM_MBUTTONDOWN,
        };
    }

    private static void NormalizeMenuSettings(MenuSettings menu)
    {
        menu.Width = Math.Clamp(menu.Width, 100, 2000);
        menu.Height = Math.Clamp(menu.Height, 100, 2000);
        menu.CornerRadius = Math.Clamp(menu.CornerRadius, 0, 100);
        menu.GridColumns = Math.Clamp(menu.GridColumns, 2, 3);
    }

    private static void NormalizeSnippingSettings(SnippingSettings snipping)
    {
        snipping.DragThreshold = Math.Clamp(snipping.DragThreshold, 1, 50);
        snipping.MaskAlpha = Math.Clamp(snipping.MaskAlpha, 0.0, 1.0);
    }

    private static void NormalizePinSettings(PinSettings pin)
    {
        pin.DefaultOpacity = Math.Clamp(pin.DefaultOpacity, 0.1, 1.0);
    }

    private static T Clone<T>(T source) where T : new()
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<T>(json) ?? new T();
    }
}

using System.Collections.Generic;
using System.Diagnostics;
using MyQuicker.Models;
using MyQuicker.UI;

namespace MyQuicker.Services;

/// <summary>
/// Loads actions from settings.json (via ActionStore) and executes
/// them via System.Diagnostics.Process. Per SPEC.md §4.3 / step 7/8A.
/// </summary>
internal sealed class ActionExecutor
{
    private readonly ActionStore _actionStore = new();
    private readonly ScreenshotService _screenshotService = new();

    /// <summary>
    /// Returns the current action list, freshly loaded from settings.json
    /// so edits made in the settings window are reflected on the next wake-up.
    /// </summary>
    public List<ActionItem> GetActions() => _actionStore.GetActions();

    /// <summary>
    /// Executes the action. The reserved command "sys:snipping" launches the
    /// native screenshot overlay instead of starting an external process.
    /// </summary>
    public void Execute(ActionItem item)
    {
        if (item.Command == "sys:snipping")
        {
            var (source, bounds) = _screenshotService.Capture();
            var window = new ScreenshotWindow(source, bounds);
            window.ShowDialog(); // modal — blocks until the user closes it
            return;
        }

        Process.Start(new ProcessStartInfo
        {
            FileName = item.Command,
            Arguments = item.Arguments ?? string.Empty,
            UseShellExecute = true,
        });
    }
}

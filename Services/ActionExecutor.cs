using System;
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
    private readonly ScreenshotService _screenshotService = new();

    /// <summary>
    /// Returns the current action list, freshly loaded from settings.json
    /// so edits made in the settings window are reflected on the next wake-up.
    /// </summary>
    public List<ActionItem> GetActions() => ActionStore.GetActions();

    /// <summary>
    /// Executes the action. The reserved command "sys:snipping" launches the
    /// native screenshot overlay instead of starting an external process.
    /// </summary>
    public void Execute(ActionItem item)
    {
        if (item.Command == "sys:snipping")
        {
            var (source, bounds, fallback) = _screenshotService.Capture();
            if (fallback)
            {
                // AllMonitors 在主副屏 DPI 不一致时无法跨屏 1:1 渲染，已回退为光标所在屏。
                Toast.Show("主副屏缩放不一致，已截取当前屏", 3000);
            }
            var window = new ScreenshotWindow(source, bounds);
            window.ShowDialog(); // modal — blocks until the user closes it
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Command))
        {
            Debug.WriteLine("ERROR: 动作命令为空，已忽略。");
            Toast.Show(string.IsNullOrWhiteSpace(item.Name) ? "动作未配置命令" : $"动作「{item.Name}」未配置命令", 3000);
            return;
        }

        // sys: 前缀为内部协议指令；非已实现者按未知指令提示，避免误当外部程序启动失败。
        if (item.Command.StartsWith("sys:", StringComparison.Ordinal))
        {
            Toast.Show($"未知指令：{item.Command}", 3000);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = item.Command,
                Arguments = item.Arguments ?? string.Empty,
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            // 错填命令/找不到程序：拦截 Win32Exception，避免常驻进程闪退；弹 toast 告知用户。
            Debug.WriteLine($"ERROR: 启动动作失败 ({item.Command}): {ex.Message}");
            Toast.Show($"无法启动：{item.Command}", 3000);
        }
    }
}

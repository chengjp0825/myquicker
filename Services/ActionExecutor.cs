using System;
using System.Collections.Generic;
using System.Diagnostics;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime.Commands;

namespace MyQuicker.Services;

/// <summary>
/// 动作执行调度中心：根据 <see cref="ActionItem.Command"/> 从 <see cref="CommandRegistry"/> 中检索命令，
/// 将 DTO 字段转换为纯粹参数后执行，并统一捕获异常包装为 <see cref="ActionResult"/>。
/// </summary>
public sealed class ActionExecutor
{
    private readonly CommandRegistry _registry;

    public ActionExecutor(CommandRegistry registry)
    {
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
    }

    /// <summary>执行动作。</summary>
    public ActionResult Execute(CommandContext ctx, ActionItem item)
    {
        return ResolveCommand(ctx, item, (cmd, parameters) => cmd.Execute(ctx, parameters));
    }

    /// <summary>异步执行动作，避免 UI 线程被截图/启动等物理操作阻塞。</summary>
    public async Task<ActionResult> ExecuteAsync(CommandContext ctx, ActionItem item)
    {
        return await ResolveCommandAsync(ctx, item).ConfigureAwait(false);
    }

    private ActionResult ResolveCommand(CommandContext ctx, ActionItem item, Func<ICommand, Dictionary<string, string>, ActionResult> invoke)
    {
        if (string.IsNullOrWhiteSpace(item.Command))
        {
            return new ActionResult(
                ActionOutcomeKind.EmptyCommand,
                string.IsNullOrWhiteSpace(item.Name)
                    ? "动作未配置命令"
                    : $"动作「{item.Name}」未配置命令");
        }

        ICommand? command = _registry.Lookup(item.Command);
        if (command is null)
        {
            // 保留 sys: 前缀的语义：未注册的内建指令视为未知内建指令。
            if (item.Command.StartsWith("sys:", StringComparison.Ordinal))
            {
                return new ActionResult(
                    ActionOutcomeKind.UnknownSystemCommand,
                    $"未知指令：{item.Command}");
            }

            return new ActionResult(
                ActionOutcomeKind.LaunchFailed,
                $"无法启动：{item.Command}",
                ErrorCommand: item.Command);
        }

        try
        {
            Dictionary<string, string> parameters = BuildParameters(item);
            return invoke(command, parameters);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 执行动作失败 ({item.Command}): {ex.Message}");
            return new ActionResult(
                ActionOutcomeKind.LaunchFailed,
                $"无法启动：{item.Command}",
                ErrorCommand: item.Command);
        }
    }

    private async Task<ActionResult> ResolveCommandAsync(CommandContext ctx, ActionItem item)
    {
        if (string.IsNullOrWhiteSpace(item.Command))
        {
            return new ActionResult(
                ActionOutcomeKind.EmptyCommand,
                string.IsNullOrWhiteSpace(item.Name)
                    ? "动作未配置命令"
                    : $"动作「{item.Name}」未配置命令");
        }

        ICommand? command = _registry.Lookup(item.Command);
        if (command is null)
        {
            if (item.Command.StartsWith("sys:", StringComparison.Ordinal))
            {
                return new ActionResult(
                    ActionOutcomeKind.UnknownSystemCommand,
                    $"未知指令：{item.Command}");
            }

            return new ActionResult(
                ActionOutcomeKind.LaunchFailed,
                $"无法启动：{item.Command}",
                ErrorCommand: item.Command);
        }

        try
        {
            Dictionary<string, string> parameters = BuildParameters(item);
            return await command.ExecuteAsync(ctx, parameters).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"ERROR: 执行动作失败 ({item.Command}): {ex.Message}");
            return new ActionResult(
                ActionOutcomeKind.LaunchFailed,
                $"无法启动：{item.Command}",
                ErrorCommand: item.Command);
        }
    }

    private static Dictionary<string, string> BuildParameters(ActionItem item)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["name"] = item.Name ?? string.Empty,
            ["command"] = item.Command,
            ["arguments"] = item.Arguments ?? string.Empty,
            ["icon"] = item.Icon ?? string.Empty,
        };
    }
}

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Aurora.Domain.DTO;
using Aurora.Domain.Runtime.Commands;

namespace Aurora.Services;

/// <summary>
/// Loads user-defined commands from the stable <see cref="Settings.Commands"/> catalog
/// and registers them in the <see cref="CommandRegistry"/>.
/// </summary>
public static class UserCommandStore
{
    public static void Register(CommandRegistry registry, IEnumerable<CommandDefinition> commands)
    {
        if (registry is null)
            throw new ArgumentNullException(nameof(registry));
        if (commands is null)
            throw new ArgumentNullException(nameof(commands));

        foreach (CommandDefinition command in commands)
        {
            if (string.IsNullOrWhiteSpace(command.Id))
                continue;

            // KI-5：拒绝用户配置覆盖内建 sys:* 命令 ID。sys:* 由 BuiltInCommandProvider 注册，
            // 用户配置里的同名项跳过（不注册、不覆盖）。ActionItem.CommandId 仍可指向 sys:*（查找走内建）。
            if (command.Id.StartsWith("sys:", StringComparison.Ordinal))
            {
                Debug.WriteLine($"[UserCommandStore] 跳过用户命令 '{command.Id}'：sys:* 前缀保留给内建命令，禁止覆盖。");
                continue;
            }

            ICommand runtimeCommand = command.Type switch
            {
                CommandType.OpenUrl => new OpenUrlCommand(),
                _ => new LaunchApplicationCommand(),
            };

            registry.Register(command.Id, runtimeCommand);
        }
    }
}

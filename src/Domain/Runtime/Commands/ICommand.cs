using System.Collections.Generic;
using MyQuicker.Services;

namespace MyQuicker.Domain.Runtime.Commands;

/// <summary>
/// 无状态可执行命令。命令只接收纯粹的键值对参数，不依赖具体 DTO（如 <see cref="MyQuicker.Domain.DTO.ActionItem"/>）。
/// 返回值统一为 <see cref="ActionResult"/>，由调用方（<see cref="MyQuicker.Services.ActionExecutor"/>）统一捕获异常并包装。
/// </summary>
public interface ICommand
{
    /// <summary>
    /// 执行命令。
    /// </summary>
    /// <param name="context">运行时服务上下文。</param>
    /// <param name="parameters">由 ActionItem 字段转换而来的纯键值对参数。</param>
    /// <returns>命令执行结果。</returns>
    ActionResult Execute(CommandContext context, Dictionary<string, string> parameters);

    /// <summary>
    /// 异步执行命令。默认实现同步调用 <see cref="Execute"/> 后返回已完成任务；
    /// 需要离屏执行的命令（如截图）可重写此默认实现。
    /// </summary>
    Task<ActionResult> ExecuteAsync(CommandContext context, Dictionary<string, string> parameters)
    {
        return Task.FromResult(Execute(context, parameters));
    }
}


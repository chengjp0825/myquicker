namespace MyQuicker.Services;

/// <summary>ActionExecutor 执行命令后的结果分类。</summary>
public enum ActionOutcomeKind
{
    /// <summary>外部进程已成功启动。</summary>
    StartedProcess,

    /// <summary>动作未配置命令。</summary>
    EmptyCommand,

    /// <summary>未知的 sys: 内部指令。</summary>
    UnknownSystemCommand,

    /// <summary>启动外部进程失败（找不到程序或权限不足）。</summary>
    LaunchFailed,
}

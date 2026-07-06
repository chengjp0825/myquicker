using MyQuicker.Domain.Runtime;

namespace MyQuicker.Services;

/// <summary>
/// 命令执行时的运行时依赖上下文。
/// 纯数据记录；所有服务均为接口或可由工厂创建的工作流，便于测试替换。
/// </summary>
public sealed record CommandContext(
    IProcessLauncher ProcessLauncher,
    IScreenshotCaptureService ScreenshotCaptureService,
    ScreenshotWorkflow? ScreenshotWorkflow = null,
    IToastService? ToastService = null);

using System;
using System.Collections.Generic;
using MyQuicker.Services;

namespace MyQuicker.Domain.Runtime.Commands;

/// <summary>发起屏幕截图的命令。</summary>
public sealed class SnippingCommand : ICommand
{
    public ActionResult Execute(CommandContext context, Dictionary<string, string> parameters)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        CapturedImage captured = context.ScreenshotCaptureService.Capture();
        return BuildResult(captured);
    }

    /// <summary>异步执行截图命令，避免 UI 线程阻塞。</summary>
    public async Task<ActionResult> ExecuteAsync(CommandContext context, Dictionary<string, string> parameters)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));

        CapturedImage captured = await context.ScreenshotCaptureService.CaptureAsync().ConfigureAwait(false);
        return BuildResult(captured);
    }

    private static ActionResult BuildResult(CapturedImage captured)
    {
        string? toast = captured.FallbackToCurrent ? "主副屏缩放不一致，已截取当前屏" : null;
        return new ActionResult(ActionOutcomeKind.ScreenshotRequested, toast, captured);
    }
}

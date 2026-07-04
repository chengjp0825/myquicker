namespace MyQuicker.UI;

/// <summary>
/// Toast 通知入口。在 UI 线程调用 <see cref="Show"/> 弹出瞬时通知（剪贴板失败、启动成功等）。
/// 必须在 UI 线程调用（WPF 窗口创建约束）。窗口生命周期由 <see cref="ToastWindow"/> 自管。
/// </summary>
public static class Toast
{
    /// <summary>弹出 toast，默认 2.5s 后自动淡出关闭。</summary>
    public static void Show(string message, int durationMs = 2500)
    {
        new ToastWindow(message, durationMs).Show();
    }
}

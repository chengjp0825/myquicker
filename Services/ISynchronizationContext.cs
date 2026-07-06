namespace MyQuicker.Services;

/// <summary>
/// 线程调度抽象：让核心服务（如 RawInputSource）无需引用 System.Windows。
/// 实现方负责把回调派发到正确的线程（WPF Dispatcher、测试同步上下文等）。
/// </summary>
public interface ISynchronizationContext
{
    /// <summary>异步投递一个操作到目标线程。</summary>
    void Post(Action action);
}

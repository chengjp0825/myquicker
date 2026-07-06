using System.Threading;
using System.Windows.Threading;
using MyQuicker.UI;
using Xunit;

namespace MyQuicker.Tests.UI;

public class WpfSynchronizationContextTests
{
    [StaFact]
    public void Post_OnDispatcherThread_InvokesActionOnSameThread()
    {
        var sync = new WpfSynchronizationContext();
        int invokedThreadId = -1;
        var reset = new ManualResetEventSlim();

        sync.Post(() =>
        {
            invokedThreadId = Thread.CurrentThread.ManagedThreadId;
            reset.Set();
        });

        // Pump the dispatcher so the posted action runs.
        PumpDispatcher(reset);

        Assert.Equal(Thread.CurrentThread.ManagedThreadId, invokedThreadId);
    }

    [StaFact]
    public void Post_CapturesDispatcherAtConstruction()
    {
        var sync = new WpfSynchronizationContext();
        bool invoked = false;
        var reset = new ManualResetEventSlim();

        sync.Post(() =>
        {
            invoked = true;
            reset.Set();
        });

        PumpDispatcher(reset);

        Assert.True(invoked);
    }

    [StaFact]
    public void Post_MultipleActions_AllExecute()
    {
        var sync = new WpfSynchronizationContext();
        int count = 0;
        var reset = new ManualResetEventSlim();

        sync.Post(() => Interlocked.Increment(ref count));
        sync.Post(() => Interlocked.Increment(ref count));
        sync.Post(() =>
        {
            Interlocked.Increment(ref count);
            reset.Set();
        });

        PumpDispatcher(reset);

        Assert.Equal(3, count);
    }

    /// <summary>简单 pump dispatcher 直到 reset 被触发或超时。</summary>
    private static void PumpDispatcher(ManualResetEventSlim reset, int timeoutMs = 2000)
    {
        var frame = new DispatcherFrame();
        RegisteredWaitHandle? wait = ThreadPool.RegisterWaitForSingleObject(
            reset.WaitHandle,
            (_, _) => frame.Continue = false,
            null,
            timeoutMs,
            executeOnlyOnce: true);

        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new DispatcherOperationCallback((_) =>
            {
                frame.Continue = !reset.IsSet;
                return null;
            }),
            null);

        Dispatcher.PushFrame(frame);
        wait?.Unregister(null);
        Assert.True(reset.IsSet, "Dispatcher pump timed out before posted action ran.");
    }
}

using System.Diagnostics;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>ITimeProvider 的手写 Mock，使用 Stopwatch ticks 作为单调时间单位。</summary>
internal sealed class FakeTimeProvider : ITimeProvider
{
    private long _timestamp = Stopwatch.GetTimestamp();

    public long MonotonicTimestamp => _timestamp;

    /// <summary>前进指定时间（毫秒）。</summary>
    public void AdvanceMilliseconds(double milliseconds)
    {
        _timestamp += (long)(milliseconds * Stopwatch.Frequency / 1000.0);
    }

    /// <summary>直接设置单调时间戳。</summary>
    public void SetTimestamp(long timestamp)
    {
        _timestamp = timestamp;
    }
}

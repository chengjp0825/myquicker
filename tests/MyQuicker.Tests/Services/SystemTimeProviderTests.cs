using System.Threading;
using MyQuicker.Services;
using Xunit;

namespace MyQuicker.Tests.Services;

public class SystemTimeProviderTests
{
    [Fact]
    public void MonotonicTimestamp_IsMonotonicallyIncreasing()
    {
        var provider = new SystemTimeProvider();

        long first = provider.MonotonicTimestamp;
        Thread.Sleep(1);
        long second = provider.MonotonicTimestamp;

        Assert.True(second > first, "单调物理时钟必须严格递增。");
    }
}

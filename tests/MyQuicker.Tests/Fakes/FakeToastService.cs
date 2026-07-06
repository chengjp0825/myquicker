using System.Collections.Generic;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>IToastService 的手写假实现，用于断言消息是否被弹出。</summary>
internal sealed class FakeToastService : IToastService
{
    public List<(string Message, int DurationMs)> Messages { get; } = new();

    public void Show(string message, int durationMs)
    {
        Messages.Add((message, durationMs));
    }
}

using System;
using System.Collections.Generic;
using MyQuicker.Services;

namespace MyQuicker.Tests.Fakes;

/// <summary>
/// <see cref="ISynchronizationContext"/ > 的测试替身：记录所有 Post 的动作，
/// 并可选择立即同步执行（便于断言副作用）。
/// </summary>
internal sealed class FakeSynchronizationContext : ISynchronizationContext
{
    private readonly bool _executeImmediately;
    private readonly List<Action> _posted = new();

    public FakeSynchronizationContext(bool executeImmediately = false)
    {
        _executeImmediately = executeImmediately;
    }

    public IReadOnlyList<Action> Posted => _posted;

    public void Post(Action action)
    {
        _posted.Add(action);
        if (_executeImmediately)
            action();
    }
}

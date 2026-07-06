using System;
using System.Collections.Generic;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>IMenuPresenter 的手写 Mock。</summary>
internal sealed class FakeMenuPresenter : IMenuPresenter
{
    public List<Point> ShowAtCalls { get; } = new();
    public int DismissCallCount { get; private set; }

    public bool IsVisible { get; set; }

    public event EventHandler? Opened;
    public event EventHandler? Closed;
    public event EventHandler? DismissRequested;

    public void ShowAt(Point location) => ShowAtCalls.Add(location);

    public void Dismiss() => DismissCallCount++;

    public void RaiseOpened() => Opened?.Invoke(this, EventArgs.Empty);

    public void RaiseClosed() => Closed?.Invoke(this, EventArgs.Empty);

    public void RaiseDismissRequested() => DismissRequested?.Invoke(this, EventArgs.Empty);
}

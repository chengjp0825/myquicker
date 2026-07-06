using System;
using MyQuicker.Domain.Runtime;

namespace MyQuicker.Tests.Fakes;

/// <summary>WakeOrchestrator 测试用手动外部点击源。</summary>
internal sealed class FakeOutsideClickSource : IOutsideClickSource
{
    public event EventHandler? OutsideClick;

    public void RaiseOutsideClick() => OutsideClick?.Invoke(this, EventArgs.Empty);
}

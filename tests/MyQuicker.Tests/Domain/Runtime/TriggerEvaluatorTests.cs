using System.Collections.Generic;
using MyQuicker.Domain.Runtime;
using MyQuicker.Interop;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime;

public class TriggerEvaluatorTests
{
    [Fact]
    public void Evaluate_ButtonTrigger_ReturnsMatch()
    {
        var evaluator = new TriggerEvaluator();
        var trigger = new ButtonTrigger("MiddleButton", NativeMethods.WM_MBUTTONDOWN, null);
        evaluator.UpdateTriggers(new List<ITrigger> { trigger });

        var result = evaluator.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(10, 20),
            12345,
            NativeMethods.WM_MBUTTONDOWN,
            null));

        Assert.True(result.IsMatch);
        Assert.Equal("MiddleButton", result.Context?.TriggerSource);
    }

    [Fact]
    public void Evaluate_NoTriggers_ReturnsNoMatch()
    {
        var evaluator = new TriggerEvaluator();
        var result = evaluator.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(0, 0),
            0,
            NativeMethods.WM_MBUTTONDOWN,
            null));

        Assert.False(result.IsMatch);
    }

    [Fact]
    public void UpdateTriggers_ReplacesPreviousTriggers()
    {
        var evaluator = new TriggerEvaluator();
        evaluator.UpdateTriggers(new List<ITrigger>
        {
            new ButtonTrigger("Old", NativeMethods.WM_MBUTTONDOWN, null),
        });

        evaluator.UpdateTriggers(new List<ITrigger>
        {
            new ButtonTrigger("New", NativeMethods.WM_RBUTTONDOWN, null),
        });

        var result = evaluator.Evaluate(new TriggerEvent(
            TriggerEventType.MouseDown,
            new Point(0, 0),
            0,
            NativeMethods.WM_RBUTTONDOWN,
            null));

        Assert.True(result.IsMatch);
        Assert.Equal("New", result.Context?.TriggerSource);
    }

    [Fact]
    public void Evaluate_NullEvent_ThrowsArgumentNullException()
    {
        var evaluator = new TriggerEvaluator();
        Assert.Throws<System.ArgumentNullException>(() => evaluator.Evaluate(null!));
    }
}

using System;
using System.Collections.Generic;
using MyQuicker.Domain.DTO;
using MyQuicker.Domain.Runtime;
using MyQuicker.Interop;
using MyQuicker.Services;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime;

/// <summary>
/// RawInputSource 唤醒派发链路的回归测试。
/// 重点覆盖 KI-2：WM_MOUSEMOVE 必须喂给 TriggerEvaluator，画圈匹配后投递 WakeContextReceived。
/// 通过 internal ProcessMouseMessage 直接驱动，无需安装 Win32 钩子。
/// </summary>
public class RawInputSourceTests
{
    [Fact]
    public void ProcessMouseMessage_MouseMoveCircle_RaisesWakeContextReceived()
    {
        // KI-2 回归：画圈事件流必须触发唤醒，而非死在无人订阅的 EventReceived 上。
        var sync = new FakeSynchronizationContext(executeImmediately: true);
        var time = new FakeTimeProvider();
        var evaluator = new TriggerEvaluator();
        evaluator.UpdateTriggers(new ITrigger[]
        {
            new CircleGestureTrigger("CircleGesture", CircleSensitivity.High, time)
        });
        var source = new RawInputSource(sync, time, evaluator);

        WakeContext? raised = null;
        source.WakeContextReceived += (_, ctx) => raised = ctx;

        var center = new NativeMethods.POINT { X = 200, Y = 200 };
        const int radius = 50;
        const int count = 32;
        for (int i = 0; i < count; i++)
        {
            time.AdvanceMilliseconds(20);
            double angle = 2 * Math.PI * i / count;
            var pt = new NativeMethods.POINT
            {
                X = center.X + (int)(radius * Math.Cos(angle)),
                Y = center.Y + (int)(radius * Math.Sin(angle))
            };
            bool swallow = source.ProcessMouseMessage(NativeMethods.WM_MOUSEMOVE, pt, time.MonotonicTimestamp, null);
            Assert.False(swallow); // MouseMove 永不吞键
            if (raised is not null)
                break;
        }

        Assert.NotNull(raised);
        Assert.Equal("CircleGesture", raised!.TriggerSource);
    }

    [Fact]
    public void ProcessMouseMessage_MouseMoveStraightLine_DoesNotRaiseWakeContext()
    {
        var sync = new FakeSynchronizationContext(executeImmediately: true);
        var time = new FakeTimeProvider();
        var evaluator = new TriggerEvaluator();
        evaluator.UpdateTriggers(new ITrigger[]
        {
            new CircleGestureTrigger("CircleGesture", CircleSensitivity.Medium, time)
        });
        var source = new RawInputSource(sync, time, evaluator);

        WakeContext? raised = null;
        source.WakeContextReceived += (_, ctx) => raised = ctx;

        for (int i = 0; i < 50; i++)
        {
            time.AdvanceMilliseconds(10);
            var pt = new NativeMethods.POINT { X = i * 10, Y = i * 10 };
            source.ProcessMouseMessage(NativeMethods.WM_MOUSEMOVE, pt, time.MonotonicTimestamp, null);
        }

        Assert.Null(raised);
    }

    [Fact]
    public void ProcessMouseMessage_MouseMove_RaisesEventReceived()
    {
        // EventReceived 作为原始输入流观察 seam 必须仍然触发（KI-2 修复保留该 seam）。
        var sync = new FakeSynchronizationContext(executeImmediately: true);
        var time = new FakeTimeProvider();
        var evaluator = new TriggerEvaluator();
        var source = new RawInputSource(sync, time, evaluator);

        TriggerEvent? received = null;
        source.EventReceived += (_, ev) => received = ev;

        var pt = new NativeMethods.POINT { X = 100, Y = 100 };
        source.ProcessMouseMessage(NativeMethods.WM_MOUSEMOVE, pt, time.MonotonicTimestamp, null);

        Assert.NotNull(received);
        Assert.Equal(TriggerEventType.MouseMove, received!.EventType);
        Assert.Equal(100, received.Location.X);
        Assert.Equal(100, received.Location.Y);
    }

    [Fact]
    public void ProcessMouseMessage_MouseMove_WithoutEventReceivedSubscriber_DoesNotQueueClosure()
    {
        // 无 EventReceived 订阅者时，PostEvent 应短路，不向同步上下文投递（性能保护）。
        var sync = new FakeSynchronizationContext(executeImmediately: false);
        var time = new FakeTimeProvider();
        var evaluator = new TriggerEvaluator();
        var source = new RawInputSource(sync, time, evaluator);

        var pt = new NativeMethods.POINT { X = 10, Y = 10 };
        source.ProcessMouseMessage(NativeMethods.WM_MOUSEMOVE, pt, time.MonotonicTimestamp, null);

        // 无订阅者 → PostEvent 短路；EvaluateAndPostWake 未匹配 → 也不 Post。 Posted 应为空。
        Assert.Empty(sync.Posted);
    }

    [Fact]
    public void ProcessMouseMessage_MiddleButtonDown_RaisesWakeContextAndAnyMouseDown()
    {
        // 防重构破坏 MouseDown 路径：中键匹配后投递 WakeContext，且总是触发 AnyMouseDown。
        var sync = new FakeSynchronizationContext(executeImmediately: true);
        var time = new FakeTimeProvider();
        var evaluator = new TriggerEvaluator();
        evaluator.UpdateTriggers(new ITrigger[]
        {
            new ButtonTrigger("MiddleButton", NativeMethods.WM_MBUTTONDOWN, null)
        });
        var source = new RawInputSource(sync, time, evaluator); // 无拦截策略

        WakeContext? raised = null;
        source.WakeContextReceived += (_, ctx) => raised = ctx;
        bool anyMouseDown = false;
        source.AnyMouseDown += (_, _) => anyMouseDown = true;

        var pt = new NativeMethods.POINT { X = 50, Y = 50 };
        bool swallow = source.ProcessMouseMessage(NativeMethods.WM_MBUTTONDOWN, pt, time.MonotonicTimestamp, null);

        Assert.True(anyMouseDown);   // AnyMouseDown 总触发（供外部点击检测）
        Assert.NotNull(raised);      // 匹配则投递 WakeContext
        Assert.Equal("MiddleButton", raised!.TriggerSource);
        Assert.False(swallow);       // 无拦截策略，不吞键
    }

    [Fact]
    public void ProcessMouseMessage_MiddleButtonDown_WithInterception_Swallows()
    {
        var sync = new FakeSynchronizationContext(executeImmediately: true);
        var time = new FakeTimeProvider();
        var evaluator = new TriggerEvaluator();
        evaluator.UpdateTriggers(new ITrigger[]
        {
            new ButtonTrigger("MiddleButton", NativeMethods.WM_MBUTTONDOWN, null)
        });
        var source = new RawInputSource(sync, time, evaluator, new InputInterceptionPolicy(interceptWakeupKey: true));

        var pt = new NativeMethods.POINT { X = 50, Y = 50 };
        bool swallow = source.ProcessMouseMessage(NativeMethods.WM_MBUTTONDOWN, pt, time.MonotonicTimestamp, null);

        Assert.True(swallow);
    }
}

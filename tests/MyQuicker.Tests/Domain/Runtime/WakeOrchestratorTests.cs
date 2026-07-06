using System;
using System.Diagnostics;
using MyQuicker.Domain.Runtime;
using MyQuicker.Tests.Fakes;
using Xunit;

namespace MyQuicker.Tests.Domain.Runtime;

public class WakeOrchestratorTests
{
    private const double DefaultMenuWidth = 250;
    private const double DefaultMenuHeight = 250;

    private static WakeOrchestrator CreateOrchestrator(
        out FakeMenuPresenter presenter,
        out FakeScreenGeometry screens,
        out FakeTimeProvider time,
        out FakeWakeBlockPolicy blockPolicy,
        out FakeOutsideClickSource outsideClickSource,
        TimeSpan? debounce = null,
        TimeSpan? staleThreshold = null,
        double menuWidth = DefaultMenuWidth,
        double menuHeight = DefaultMenuHeight)
    {
        presenter = new FakeMenuPresenter();
        screens = new FakeScreenGeometry();
        time = new FakeTimeProvider();
        blockPolicy = new FakeWakeBlockPolicy();
        outsideClickSource = new FakeOutsideClickSource();
        var settings = new WakeOrchestratorSettings(
            DebounceInterval: debounce ?? TimeSpan.FromMilliseconds(200),
            StaleEventThreshold: staleThreshold ?? TimeSpan.FromSeconds(1),
            MenuWidth: menuWidth,
            MenuHeight: menuHeight);
        return new WakeOrchestrator(presenter, screens, time, blockPolicy, outsideClickSource, settings);
    }

    [Fact]
    public void OnWakeContext_WhenHidden_TransitionsToOpeningAndShowsMenuCentered()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Equal(MenuState.Opening, orch.State);
        Assert.Single(presenter.ShowAtCalls);
        // 光标居中：左上角 = 光标 - 菜单半宽高；因 (100,100) 靠近左上角，夹取后贴边。
        Assert.Equal(new Point(0, 0), presenter.ShowAtCalls[0]);
    }

    [Fact]
    public void OnWakeContext_AfterPresenterOpened_TransitionsToVisible()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));
        presenter.RaiseOpened();

        Assert.Equal(MenuState.Visible, orch.State);
    }

    [Fact]
    public void OnWakeContext_WhenVisible_IgnoresReAwake()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));
        presenter.RaiseOpened();

        orch.OnWakeContext(new WakeContext(new Point(500, 500), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Single(presenter.ShowAtCalls);
        Assert.Equal(MenuState.Visible, orch.State);
    }

    [Fact]
    public void OnWakeContext_WithinDebounceInterval_IgnoresSecondWake()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        var firstTime = time.MonotonicTimestamp;
        orch.OnWakeContext(new WakeContext(new Point(100, 100), firstTime, "MiddleButton"));
        presenter.RaiseOpened();
        orch.RequestDismiss();
        presenter.RaiseClosed();
        Assert.Equal(MenuState.Hidden, orch.State);

        time.AdvanceMilliseconds(100); // 小于 200ms 防抖
        var secondTime = time.MonotonicTimestamp;
        orch.OnWakeContext(new WakeContext(new Point(200, 200), secondTime, "MiddleButton"));

        Assert.Single(presenter.ShowAtCalls);
        Assert.Equal(MenuState.Hidden, orch.State);
    }

    [Fact]
    public void OnWakeContext_AfterDebounceInterval_AllowsSecondWake()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        var firstTime = time.MonotonicTimestamp;
        orch.OnWakeContext(new WakeContext(new Point(100, 100), firstTime, "MiddleButton"));
        presenter.RaiseOpened();
        orch.RequestDismiss();
        presenter.RaiseClosed();
        Assert.Equal(MenuState.Hidden, orch.State);

        time.AdvanceMilliseconds(250); // 超过 200ms 防抖
        var secondTime = time.MonotonicTimestamp;
        orch.OnWakeContext(new WakeContext(new Point(200, 200), secondTime, "MiddleButton"));

        Assert.Equal(2, presenter.ShowAtCalls.Count);
        Assert.Equal(MenuState.Opening, orch.State);
    }

    [Fact]
    public void OnWakeContext_StaleEvent_IsIgnored()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        time.AdvanceMilliseconds(2000); // 让 now 远离 0
        var staleTime = time.MonotonicTimestamp - (long)(1.5 * Stopwatch.Frequency); // 超过 1s 过期阈值
        orch.OnWakeContext(new WakeContext(new Point(100, 100), staleTime, "MiddleButton"));

        Assert.Empty(presenter.ShowAtCalls);
        Assert.Equal(MenuState.Hidden, orch.State);
    }

    [Fact]
    public void OnWakeContext_NearRightEdge_ClampsLocationToFitScreen()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        // 物理坐标 (1900,100)，菜单 250x250，不缩放
        // 原始左上角 (1775,-25)，夹取后应为 (1670,0)
        orch.OnWakeContext(new WakeContext(new Point(1900, 100), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Single(presenter.ShowAtCalls);
        Assert.Equal(new Point(1920 - 250, 0), presenter.ShowAtCalls[0]);
    }

    [Fact]
    public void OnWakeContext_OnHighDpiScreen_ConvertsAndClampsInDip()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        // 200% 缩放：物理 3840x2160 对应 DIP 1920x1080
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 3840, 2160), 2.0, 2.0));

        // 物理 (3800,100) => DIP (1900,50)
        // 原始左上角 (1775,-75)，夹取后应为 (1670,0)
        orch.OnWakeContext(new WakeContext(new Point(3800, 100), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Single(presenter.ShowAtCalls);
        Assert.Equal(new Point(1920 - 250, 0), presenter.ShowAtCalls[0]);
    }

    [Fact]
    public void OnWakeContext_WhenBlocked_IgnoresWake()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out var blockPolicy, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));
        blockPolicy.Blocked = true;

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Empty(presenter.ShowAtCalls);
        Assert.Equal(MenuState.Hidden, orch.State);
    }

    [Fact]
    public void OnWakeContext_WhenNotBlocked_AllowsWake()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out var blockPolicy, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));
        blockPolicy.Blocked = false;

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Single(presenter.ShowAtCalls);
        Assert.Equal(MenuState.Opening, orch.State);
    }

    [Fact]
    public void OutsideClick_WhenVisible_RequestsDismiss()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out var outsideClickSource);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));
        presenter.RaiseOpened();
        Assert.Equal(MenuState.Visible, orch.State);

        outsideClickSource.RaiseOutsideClick();

        Assert.Equal(MenuState.Closing, orch.State);
        Assert.Equal(1, presenter.DismissCallCount);
    }

    [Fact]
    public void OutsideClick_WhenHidden_IsIgnored()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out var outsideClickSource);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        outsideClickSource.RaiseOutsideClick();

        Assert.Equal(MenuState.Hidden, orch.State);
        Assert.Equal(0, presenter.DismissCallCount);
    }

    [Fact]
    public void RequestDismiss_WhenVisible_TransitionsToHidden()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));
        presenter.RaiseOpened();
        Assert.Equal(MenuState.Visible, orch.State);

        orch.RequestDismiss();
        Assert.Equal(MenuState.Closing, orch.State);
        Assert.Equal(1, presenter.DismissCallCount);

        presenter.RaiseClosed();
        Assert.Equal(MenuState.Hidden, orch.State);
    }

    [Fact]
    public void DismissRequested_WhenVisible_OrchestratorCommandsDismissAndReturnsToHidden()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));
        presenter.RaiseOpened();
        Assert.Equal(MenuState.Visible, orch.State);

        // Presenter 仅发出关闭请求，不直接隐藏；Orchestrator 接管完整生命周期。
        presenter.RaiseDismissRequested();

        Assert.Equal(MenuState.Closing, orch.State);
        Assert.Equal(1, presenter.DismissCallCount);

        presenter.RaiseClosed();
        Assert.Equal(MenuState.Hidden, orch.State);
    }

    [Fact]
    public void OnPresenterClosed_WhenNotClosing_DefensivelyRecoversToHidden()
    {
        var orch = CreateOrchestrator(out var presenter, out var screens, out var time, out _, out _);
        screens.AddScreen(new ScreenInfo(new ScreenBounds(0, 0, 1920, 1080), 1.0, 1.0));

        orch.OnWakeContext(new WakeContext(new Point(100, 100), time.MonotonicTimestamp, "MiddleButton"));
        presenter.RaiseOpened();
        Assert.Equal(MenuState.Visible, orch.State);

        // Simulate a lifecycle violation: the presenter dismisses itself without
        // going through WakeOrchestrator.RequestDismiss.
        presenter.Dismiss();
        presenter.RaiseClosed();

        // The orchestrator must force-recover to Hidden so the state machine doesn't stall.
        Assert.Equal(MenuState.Hidden, orch.State);

        // Advance past the debounce window so the second wake is not rejected by design.
        time.AdvanceMilliseconds(250);
        orch.OnWakeContext(new WakeContext(new Point(500, 500), time.MonotonicTimestamp, "MiddleButton"));

        Assert.Equal(2, presenter.ShowAtCalls.Count);
        Assert.Equal(MenuState.Opening, orch.State);
    }
}

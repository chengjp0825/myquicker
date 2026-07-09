using System;
using System.Diagnostics;

namespace Aurora.Domain.Runtime;

/// <summary>
/// 唤醒策略中枢：拥有菜单生命周期状态机、防抖、过期事件过滤、多显示器边界计算、
/// 唤醒阻塞策略与外部点击关闭策略。
/// 通过 <see cref="IMenuPresenter"/> 向表现层输出 <see cref="IMenuPresenter.ShowAt"/> /
/// <see cref="IMenuPresenter.Dismiss"/> 命令。
/// 时间计算全部基于单调递增的物理时钟（<see cref="Stopwatch"/>），避免 NTP 同步导致的时间跳变。
/// </summary>
public sealed class WakeOrchestrator
{
    private readonly IMenuPresenter _presenter;
    private readonly IScreenGeometry _screenGeometry;
    private readonly ITimeProvider _timeProvider;
    private readonly IWakeBlockPolicy _blockPolicy;
    private readonly IOutsideClickSource? _outsideClickSource;
    private WakeOrchestratorSettings _settings;

    private long _lastWakeTime;

    /// <summary>当前菜单生命周期状态。</summary>
    public MenuState State { get; private set; } = MenuState.Hidden;

    public WakeOrchestrator(
        IMenuPresenter presenter,
        IScreenGeometry screenGeometry,
        ITimeProvider timeProvider,
        IWakeBlockPolicy blockPolicy,
        IOutsideClickSource? outsideClickSource,
        WakeOrchestratorSettings settings)
    {
        _presenter = presenter ?? throw new ArgumentNullException(nameof(presenter));
        _screenGeometry = screenGeometry ?? throw new ArgumentNullException(nameof(screenGeometry));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _blockPolicy = blockPolicy ?? throw new ArgumentNullException(nameof(blockPolicy));
        _outsideClickSource = outsideClickSource;
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));

        _presenter.Opened += OnPresenterOpened;
        _presenter.Closed += OnPresenterClosed;
        _presenter.DismissRequested += OnPresenterDismissRequested;

        if (_outsideClickSource is not null)
            _outsideClickSource.OutsideClick += OnOutsideClick;
    }

    /// <summary>更新唤醒策略设置（如菜单尺寸变化后重新构建）。</summary>
    public void UpdateSettings(WakeOrchestratorSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <summary>处理触发器匹配成功后的唤醒上下文。</summary>
    public void OnWakeContext(WakeContext context)
    {
        if (_blockPolicy.IsBlocked())
            return;

        long now = _timeProvider.MonotonicTimestamp;

        if (IsStale(context.Timestamp, now))
            return;

        var dipLocation = ClampToScreen(context.Location);

        // KI-16：Visible 态二次唤醒直接 ShowAt 重锚到新位置（跳过防抖，用户意图明确换位置）。
        // Opening/Closing 态仍忽略（动画进行中，重锚会冲突）。
        if (State == MenuState.Visible)
        {
            _presenter.ShowAt(dipLocation);
            _lastWakeTime = context.Timestamp;
            return;
        }

        if (IsDebounced(context.Timestamp))
            return;

        if (State != MenuState.Hidden)
            return;

        _presenter.ShowAt(dipLocation);
        State = MenuState.Opening;
        _lastWakeTime = context.Timestamp;
    }

    /// <summary>请求关闭菜单（如点击菜单外部）。</summary>
    public void RequestDismiss()
    {
        if (State != MenuState.Visible && State != MenuState.Opening)
            return;

        State = MenuState.Closing;
        _presenter.Dismiss();
    }

    private void OnPresenterDismissRequested(object? sender, EventArgs e)
    {
        RequestDismiss();
    }

    private void OnOutsideClick(object? sender, EventArgs e)
    {
        RequestDismiss();
    }

    private bool IsStale(long contextTimestamp, long now)
    {
        return Stopwatch.GetElapsedTime(contextTimestamp, now) > _settings.StaleEventThreshold;
    }

    private bool IsDebounced(long contextTimestamp)
    {
        if (_lastWakeTime == default)
            return false;

        return Stopwatch.GetElapsedTime(_lastWakeTime, contextTimestamp) <= _settings.DebounceInterval;
    }

    private Point ClampToScreen(Point physicalLocation)
    {
        var screen = _screenGeometry.GetScreenContaining(physicalLocation);

        double dipX = physicalLocation.X / screen.ScaleX;
        double dipY = physicalLocation.Y / screen.ScaleY;

        double screenLeft = screen.Bounds.X / screen.ScaleX;
        double screenTop = screen.Bounds.Y / screen.ScaleY;
        double screenRight = screen.Bounds.Right / screen.ScaleX;
        double screenBottom = screen.Bounds.Bottom / screen.ScaleY;

        double halfW = _settings.MenuWidth / 2.0;
        double halfH = _settings.MenuHeight / 2.0;

        double left = dipX - halfW;
        double top = dipY - halfH;

        // 夹取：菜单矩形必须完全落在屏幕 DIP 范围内。
        if (left < screenLeft)
            left = screenLeft;
        else if (left + _settings.MenuWidth > screenRight)
            left = screenRight - _settings.MenuWidth;

        if (top < screenTop)
            top = screenTop;
        else if (top + _settings.MenuHeight > screenBottom)
            top = screenBottom - _settings.MenuHeight;

        return new Point((int)left, (int)top);
    }

    private void OnPresenterOpened(object? sender, EventArgs e)
    {
        if (State == MenuState.Opening)
            State = MenuState.Visible;
    }

    private void OnPresenterClosed(object? sender, EventArgs e)
    {
        // 正常关闭流：RequestDismiss 先把 State 设为 Closing，再驱动 Presenter.Dismiss()，
        // Presenter 隐藏完成后触发 Closed，状态机归位 Hidden。
        if (State == MenuState.Closing)
        {
            State = MenuState.Hidden;
            return;
        }

        // 防御性处理：如果 Closed 在非 Closing 状态触发，说明 Presenter 越权自行关闭。
        // 强制回到 Hidden 避免状态机死锁，并记录警告供排查。
        Debug.WriteLine($"[WARN] Presenter.Closed fired unexpectedly in state {State}; forcing Hidden.");
        State = MenuState.Hidden;
    }
}

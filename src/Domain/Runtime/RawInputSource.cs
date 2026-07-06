using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MyQuicker.Interop;
using MyQuicker.Services;
using static MyQuicker.Interop.NativeMethods;

namespace MyQuicker.Domain.Runtime;

/// <summary>
/// Thin adapter around the WH_MOUSE_LL global hook. Translates Win32 messages into
/// domain <see cref="TriggerEvent"/> instances and posts them to a synchronization context.
/// Evaluation is delegated to the supplied <see cref="TriggerEvaluator"/> so that matches can
/// be applied synchronously in the native callback, and interception to an optional
/// <see cref="IInputInterceptionPolicy"/> that can be updated without reinstalling the hook.
/// </summary>
public sealed class RawInputSource : IDisposable
{
    private NativeMethods.LowLevelMouseProc? _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private readonly ISynchronizationContext _sync;
    private readonly ITimeProvider _timeProvider;
    private readonly TriggerEvaluator _triggerEvaluator;
    private IInputInterceptionPolicy? _interceptionPolicy;

    /// <summary>
    /// Raised on the supplied synchronization context for every low-level mouse event
    /// that this source tracks (mouse moves and tracked button-downs).
    /// </summary>
    public event EventHandler<TriggerEvent>? EventReceived;

    /// <summary>
    /// Raised on the supplied synchronization context for any tracked mouse button down
    /// (left / right / non-client / middle / side), regardless of whether it is swallowed.
    /// Used by the UI to detect clicks outside the menu.
    /// </summary>
    public event EventHandler<Point>? AnyMouseDown;

    /// <summary>
    /// Raised on the supplied synchronization context when a trigger matches.
    /// Carries the <see cref="WakeContext"/> for the orchestrator.
    /// </summary>
    public event EventHandler<WakeContext>? WakeContextReceived;

    /// <summary>
    /// Initializes a new instance of the <see cref="RawInputSource"/> class.
    /// </summary>
    /// <param name="sync">Synchronization context used to marshal events to the UI thread.</param>
    /// <param name="timeProvider">Provider for monotonic timestamps used in trigger events.</param>
    /// <param name="triggerEvaluator">Evaluator that matches trigger events against configured triggers.</param>
    /// <param name="interceptionPolicy">Optional policy that decides whether a matched input is swallowed.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sync"/>, <paramref name="timeProvider"/>, or <paramref name="triggerEvaluator"/> is null.</exception>
    public RawInputSource(
        ISynchronizationContext sync,
        ITimeProvider timeProvider,
        TriggerEvaluator triggerEvaluator,
        IInputInterceptionPolicy? interceptionPolicy = null)
    {
        _sync = sync ?? throw new ArgumentNullException(nameof(sync));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _triggerEvaluator = triggerEvaluator ?? throw new ArgumentNullException(nameof(triggerEvaluator));
        _interceptionPolicy = interceptionPolicy;
    }

    /// <summary>
    /// Replaces the current interception policy without reinstalling the hook.
    /// Safe to call while the hook is running.
    /// </summary>
    /// <param name="policy">The new interception policy, or null to disable interception.</param>
    public void UpdateInterceptionPolicy(IInputInterceptionPolicy? policy)
    {
        _interceptionPolicy = policy;
    }

    /// <summary>
    /// Installs the global low-level mouse hook. Must be called on a
    /// message-pumping thread (the main WPF UI thread).
    /// </summary>
    public void Start()
    {
        if (_hookId != IntPtr.Zero)
            return;

        _hookProc = HookCallback;
        using var process = Process.GetCurrentProcess();
        var mainModule = process.MainModule
            ?? throw new InvalidOperationException("Could not obtain the process main module.");
        IntPtr hMod = NativeMethods.GetModuleHandle(mainModule.ModuleName);

        _hookId = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL, _hookProc, hMod, 0);
        if (_hookId == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    /// <summary>
    /// Unregisters the hook. Safe to call multiple times. Must be called on
    /// application exit to prevent leaks.
    /// </summary>
    public void Stop()
    {
        if (_hookId == IntPtr.Zero)
            return;

        NativeMethods.UnhookWindowsHookEx(_hookId);
        _hookId = IntPtr.Zero;
        _hookProc = null;
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0)
        {
            int msg = wParam.ToInt32();

            // 纯轨迹画圈：旁观 WM_MOUSEMOVE，永不拦截（始终 CallNextHookEx），保证鼠标移动绝对流畅。
            if (msg == NativeMethods.WM_MOUSEMOVE)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                PostEvent(new TriggerEvent(
                    TriggerEventType.MouseMove,
                    new Point(info.pt.X, info.pt.Y),
                    _timeProvider.MonotonicTimestamp));
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            bool isTrackedDown = msg is NativeMethods.WM_LBUTTONDOWN or NativeMethods.WM_RBUTTONDOWN
                or NativeMethods.WM_NCLBUTTONDOWN or NativeMethods.WM_MBUTTONDOWN
                or NativeMethods.WM_XBUTTONDOWN;

            if (isTrackedDown)
            {
                var info = Marshal.PtrToStructure<NativeMethods.MSLLHOOKSTRUCT>(lParam);
                POINT pt = info.pt;

                // 原生回调必须极速返回（<100ms），否则 Windows 静默摘钩。
                // UI 变化一律抛到同步上下文异步执行；仅“吞键”同步返回。
                var domainPoint = new Point(pt.X, pt.Y);
                _sync.Post(() => AnyMouseDown?.Invoke(this, domainPoint));

                int? xButton = msg == NativeMethods.WM_XBUTTONDOWN ? (int?)(info.mouseData >> 16) : null;
                var ev = new TriggerEvent(
                    TriggerEventType.MouseDown,
                    new Point(pt.X, pt.Y),
                    _timeProvider.MonotonicTimestamp,
                    msg,
                    xButton);

                bool matched = EvaluateAndMaybeSwallow(ev);

                // 是否吞掉唤醒键（不传递给前台应用）由拦截策略决定。
                if (matched)
                    return (IntPtr)1; // 同步吞掉唤醒键，立即返回
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    private bool EvaluateAndMaybeSwallow(TriggerEvent ev)
    {
        var result = _triggerEvaluator.Evaluate(ev);
        if (!result.IsMatch || result.Context is null)
            return false;

        _sync.Post(() => WakeContextReceived?.Invoke(this, result.Context));

        if (_interceptionPolicy?.ShouldIntercept(result.Context) == true)
            return true;

        return false;
    }

    private void PostEvent(TriggerEvent ev)
    {
        _sync.Post(() => EventReceived?.Invoke(this, ev));
    }

    /// <summary>
    /// Unregisters the low-level mouse hook.
    /// </summary>
    public void Dispose() => Stop();
}

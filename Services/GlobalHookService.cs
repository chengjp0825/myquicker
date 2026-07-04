using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using MyQuicker.Interop;
using MyQuicker.Models;
using static MyQuicker.Interop.NativeMethods;

namespace MyQuicker.Services;

/// <summary>
/// Installs a global low-level mouse hook (WH_MOUSE_LL) on the calling
/// (UI) thread. Raises <see cref="OnWakeupClick"/> when the configured
/// wake-up button is pressed (and swallows it) and
/// <see cref="OnAnyMouseDown"/> for any tracked button-down so the UI can
/// detect outside clicks. Per SPEC §4.1 / step 6.
/// </summary>
internal sealed class GlobalHookService : IDisposable
{
    // The hook delegate MUST be kept alive for the lifetime of the hook;
    // otherwise the GC may collect it and user32 will crash the process
    // when it tries to invoke the callback through a stale pointer.
    private NativeMethods.LowLevelMouseProc? _hookProc;
    private IntPtr _hookId = IntPtr.Zero;
    private ActionSettings _settings = new();

    /// <summary>纯轨迹画圈的滑动时间窗：最近 GestureWindowMs 内的鼠标移动点。</summary>
    private readonly Queue<(POINT Position, long Timestamp)> _moveHistory = new();

    /// <summary>复用缓冲，避免每次 mousemove 给 IsCircle 传参时分配 List。</summary>
    private readonly List<POINT> _pointsBuffer = new();

    /// <summary>IsCircle 节流时间戳：检测每 CircleCheckIntervalMs ms 一次，避免每个 mousemove 跑几何判定拖慢钩子线程。</summary>
    private long _lastCircleCheckTick;

    private const int GestureWindowMs = 800;
    private const int CircleCheckIntervalMs = 30;

    /// <summary>
    /// Raised on the UI thread when the configured wake-up button is
    /// pressed. The <see cref="POINT"/> carries the physical screen
    /// coordinates of the click. The event is swallowed (not forwarded).
    /// </summary>
    public event EventHandler<POINT>? OnWakeupClick;

    /// <summary>
    /// Raised on the UI thread for any tracked mouse button down
    /// (left / right / non-client / middle / side), regardless of whether
    /// it is swallowed. Used by the UI to detect clicks outside the menu.
    /// </summary>
    public event EventHandler<POINT>? OnAnyMouseDown;

    /// <summary>
    /// Swaps in new hotkey settings. Takes effect on the next hook callback.
    /// </summary>
    public void UpdateSettings(ActionSettings settings)
    {
        _settings = settings ?? new ActionSettings();
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

        using Process process = Process.GetCurrentProcess();
        ProcessModule mainModule = process.MainModule
            ?? throw new InvalidOperationException("Could not obtain the process main module.");
        IntPtr hMod = NativeMethods.GetModuleHandle(mainModule.ModuleName);

        _hookId = NativeMethods.SetWindowsHookEx(
            NativeMethods.WH_MOUSE_LL,
            _hookProc,
            hMod,
            0);

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
            if (msg == WM_MOUSEMOVE)
            {
                if (_settings.WakeupMessage == ActionSettings.WAKEUP_CIRCLE_GESTURE)
                {
                    var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    long now = Environment.TickCount64;
                    _moveHistory.Enqueue((info.pt, now));
                    PruneMoveHistory(now);

                    // 节流：入队/剪枝每次都做（极轻），IsCircle 每 30ms 才跑一次。
                    // 否则高轮询鼠标下每个 mousemove 都复制历史 + Atan2 计算会阻塞钩子线程致鼠标卡顿。
                    if (_moveHistory.Count >= 8 && now - _lastCircleCheckTick >= CircleCheckIntervalMs)
                    {
                        _lastCircleCheckTick = now;
                        _pointsBuffer.Clear();
                        foreach (var item in _moveHistory)
                            _pointsBuffer.Add(item.Position);

                        if (GestureHelper.IsCircle(_pointsBuffer))
                        {
                            _moveHistory.Clear(); // 防止一段轨迹连续触发
                            var dispatcher = System.Windows.Application.Current?.Dispatcher;
                            dispatcher?.BeginInvoke(new Action(() => OnWakeupClick?.Invoke(this, info.pt)));
                        }
                    }
                }
                return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
            }

            bool isTrackedDown = msg is WM_LBUTTONDOWN or WM_RBUTTONDOWN or WM_NCLBUTTONDOWN
                                          or WM_MBUTTONDOWN or WM_XBUTTONDOWN;

            if (isTrackedDown)
            {
                var info = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                POINT pt = info.pt;

                // 原生回调必须极速返回（<100ms），否则 Windows 静默摘钩。
                // UI 变化与磁盘 IO 一律抛到 UI 线程异步执行；仅“吞键”同步返回。
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is not null)
                {
                    dispatcher.BeginInvoke(new Action(() => OnAnyMouseDown?.Invoke(this, pt)));
                }

                // Wake-up interception, driven by the current settings.
                if (msg == _settings.WakeupMessage)
                {
                    // Side buttons encode their identity in the high word of mouseData.
                    if (msg == WM_XBUTTONDOWN)
                    {
                        int xButton = (int)(info.mouseData >> 16);
                        if (xButton != _settings.XButtonData)
                            return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
                    }

                    if (dispatcher is not null)
                    {
                        dispatcher.BeginInvoke(new Action(() => OnWakeupClick?.Invoke(this, pt)));
                    }
                    return (IntPtr)1; // 同步吞掉唤醒键，立即返回
                }
            }
        }

        return NativeMethods.CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>剔除滑动窗口中超过 GestureWindowMs 的老点，保持时间窗简短。</summary>
    private void PruneMoveHistory(long now)
    {
        while (_moveHistory.Count > 0 && now - _moveHistory.Peek().Timestamp > GestureWindowMs)
            _moveHistory.Dequeue();
    }

    public void Dispose() => Stop();
}

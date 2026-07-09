using System.Runtime.InteropServices;

namespace Aurora.Interop;

/// <summary>
/// Contains all P/Invoke signatures for user32.dll / kernel32.dll.
/// Native OS calls are strictly isolated here and must never be placed
/// inside UI code-behind files (per SPEC.md section 5).
/// </summary>
internal static class NativeMethods
{
    // -----------------------------------------------------------------------
    // 3.3 Constants
    // -----------------------------------------------------------------------

    /// <summary>Low-level mouse hook identifier passed to SetWindowsHookEx.</summary>
    public const int WH_MOUSE_LL = 14;

    /// <summary>Middle mouse button down message.</summary>
    public const int WM_MBUTTONDOWN = 0x0207;

    /// <summary>Left mouse button down message.</summary>
    public const int WM_LBUTTONDOWN = 0x0201;

    /// <summary>Right mouse button down message.</summary>
    public const int WM_RBUTTONDOWN = 0x0204;

    /// <summary>Non-client (title bar / border) left button down message.</summary>
    public const int WM_NCLBUTTONDOWN = 0x00A1;

    /// <summary>Side mouse button (XBUTTON1 / XBUTTON2) down message.</summary>
    public const int WM_XBUTTONDOWN = 0x020B;

    /// <summary>Mouse move message; observed passively for the circle wake-up gesture (never intercepted).</summary>
    public const int WM_MOUSEMOVE = 0x0200;

    /// <summary>DPI changed message. Sent to per-monitor DPI aware windows when moved to a display with different scaling.</summary>
    public const int WM_DPICHANGED = 0x02E0;

    // -----------------------------------------------------------------------
    // 3.1 Hook callback delegate
    // -----------------------------------------------------------------------

    /// <summary>
    /// Delegate type for the low-level mouse hook procedure passed to
    /// SetWindowsHookEx. Kept alive for the lifetime of the hook to avoid
    /// a GC callback-collection crash.
    /// </summary>
    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    // -----------------------------------------------------------------------
    // 3.3 Structs — Sequential layout maps memory exactly to the native types.
    // -----------------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public uint mouseData;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    // -----------------------------------------------------------------------
    // 3.1 Hook Definitions
    // -----------------------------------------------------------------------

    /// <summary>Registers a hook procedure. Target WH_MOUSE_LL (id: 14).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

    /// <summary>
    /// Unregisters the hook. Must be called explicitly on application exit
    /// to prevent memory leaks.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);

    /// <summary>
    /// Passes hook information to the next hook procedure in the current
    /// hook chain.
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>
    /// Retrieves a module handle for the specified loaded module. Used to
    /// obtain the correct native module handle (HINSTANCE) to pass to
    /// SetWindowsHookEx for the low-level mouse hook.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr GetModuleHandle(string lpModuleName);

    // -----------------------------------------------------------------------
    // Low-level Keyboard Hook (KI-12: menu keyboard navigation)
    // -----------------------------------------------------------------------

    /// <summary>Low-level keyboard hook identifier passed to SetWindowsHookEx.</summary>
    public const int WH_KEYBOARD_LL = 13;

    /// <summary>Key down message.</summary>
    public const int WM_KEYDOWN = 0x0100;

    /// <summary>System key down (Alt+key).</summary>
    public const int WM_SYSKEYDOWN = 0x0104;

    /// <summary>Virtual key codes for menu navigation.</summary>
    public const int VK_ESCAPE = 0x1B;
    public const int VK_RETURN = 0x0D;
    public const int VK_LEFT = 0x25;
    public const int VK_UP = 0x26;
    public const int VK_RIGHT = 0x27;
    public const int VK_DOWN = 0x28;

    /// <summary>
    /// Delegate for the low-level keyboard hook. Kept alive for the hook's lifetime
    /// to avoid GC callback-collection crash (same pattern as <see cref="LowLevelMouseProc"/>).
    /// </summary>
    public delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT
    {
        public int vkCode;
        public int scanCode;
        public uint flags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    /// <summary>Registers a low-level keyboard hook (overload for LowLevelKeyboardProc).</summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    // -----------------------------------------------------------------------
    // WinEvent Hook (KI-13: foreground change detection for menu dismiss)
    // -----------------------------------------------------------------------

    /// <summary>Event: foreground window changed (Alt-Tab / Win+D / taskbar click).</summary>
    public const uint EVENT_SYSTEM_FOREGROUND = 3;

    /// <summary>Out-of-context: callback is posted to the installer thread's message loop.</summary>
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;

    /// <summary>Skip events generated by the caller's own process.</summary>
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    /// <summary>WinEvent callback delegate. Kept alive for the hook's lifetime.</summary>
    public delegate void WinEventProc(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

    /// <summary>Sets a WinEvent hook for the specified event range.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventProc lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

    /// <summary>Removes a WinEvent hook.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnhookWinEvent(IntPtr hWinEventHook);

    // -----------------------------------------------------------------------
    // 3.2 Coordinate Control
    // -----------------------------------------------------------------------

    /// <summary>Retrieves the cursor's position in physical screen coordinates.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    // -----------------------------------------------------------------------
    // Window Styles (No-Activate window)
    // -----------------------------------------------------------------------

    /// <summary>GetWindowLong index: the extended window styles.</summary>
    public const int GWL_EXSTYLE = -20;

    /// <summary>Extended style: the window does not activate on click or when shown.</summary>
    public const int WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>Extended style: the window is transparent to mouse hit-testing (WindowFromPoint skips it).</summary>
    public const int WS_EX_TRANSPARENT = 0x00000020;

    /// <summary>Retrieves the 32-bit value at the specified window offset.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hwnd, int nIndex);

    /// <summary>
    /// Sets the 32-bit value at the specified window offset. Dispatches to
    /// SetWindowLongPtr on 64-bit (where SetWindowLong is insufficient for
    /// pointer-sized values) and falls back to SetWindowLong on 32-bit.
    /// </summary>
    public static int SetWindowLong(IntPtr hwnd, int nIndex, int dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetWindowLongPtr64(hwnd, nIndex, new IntPtr(dwNewLong)).ToInt32();
        return SetWindowLong32(hwnd, nIndex, dwNewLong);
    }

    [DllImport("user32.dll", EntryPoint = "SetWindowLong", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hwnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hwnd, int nIndex, IntPtr dwNewLong);

    // -----------------------------------------------------------------------
    // GDI Memory Management
    // -----------------------------------------------------------------------

    /// <summary>
    /// Deletes a logical pen, brush, font, bitmap, region, or palette,
    /// freeing all system resources associated with the object. Used to
    /// release the HBITMAP created during screenshot conversion so GDI
    /// handles do not leak.
    /// </summary>
    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool DeleteObject(IntPtr hObject);

    // -----------------------------------------------------------------------
    // Window Edge Detection (smart snipping)
    // -----------------------------------------------------------------------

    /// <summary>
    /// Native RECT (left/top/right/bottom). Used by GetWindowRect and
    /// DwmGetWindowAttribute.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>DwmGetWindowAttribute attribute: the extended frame bounds
    /// (true physical window rect, excluding Win11 invisible shadow).</summary>
    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    /// <summary>Retrieves a handle to the window that contains the specified point.</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT Point);

    /// <summary>Retrieves the specified attribute of a window (DWM).</summary>
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    /// <summary>Retrieves the bounding rectangle of the specified window.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    /// <summary>
    /// Retrieves the name of the class to which the specified window belongs.
    /// Used to filter out the desktop background (Progman/WorkerW) during edge
    /// detection, so empty desktop clicks don't snapshot the whole screen.
    /// </summary>
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    // -----------------------------------------------------------------------
    // Window Position (topmost without activation — 极速唤醒, docs/03 §7)
    // -----------------------------------------------------------------------

    /// <summary>SetWindowPos hWndInsertAfter: place above all non-topmost windows.</summary>
    public static readonly IntPtr HWND_TOPMOST = new(-1);

    /// <summary>Retains current size (cx/cy ignored).</summary>
    public const uint SWP_NOSIZE = 0x0001;

    /// <summary>Retains current position (X/Y ignored).</summary>
    public const uint SWP_NOMOVE = 0x0002;

    /// <summary>Does not activate the window (pairs with WS_EX_NOACTIVATE).</summary>
    public const uint SWP_NOACTIVATE = 0x0010;

    /// <summary>Retains current z-order (hWndInsertAfter ignored).</summary>
    public const uint SWP_NOZORDER = 0x0004;

    /// <summary>
    /// Changes a window's size, position, and z order. Used at wake-up to
    /// re-assert topmost without stealing focus (SWP_NOACTIVATE).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    /// <summary>
    /// Confines the cursor to a rectangular area on the screen. Passing an
    /// empty/zeroed RECT (or IntPtr.Zero) releases the cursor. Used by the
    /// screenshot overlay to keep the mouse inside the capture monitor.
    /// </summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(ref RECT lpRect);

    /// <summary>Releases the cursor clip restriction when passed IntPtr.Zero.</summary>
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClipCursor(IntPtr lpRect);

    // -----------------------------------------------------------------------
    // Per-monitor DPI (主副屏缩放不一致时按目标显示器取真实 DPI, docs/02 §5)
    // -----------------------------------------------------------------------

    /// <summary>MonitorFromPoint / MonitorFromRect: 返回最近显示器（光标/矩形不在任何屏上时不返回 NULL）。</summary>
    public const uint MONITOR_DEFAULTTONEAREST = 0x00000002;

    /// <summary>检索包含指定点的显示器句柄。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    /// <summary>检索与指定矩形相交面积最大的显示器句柄。</summary>
    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromRect(ref RECT lprc, uint dwFlags);

    /// <summary>GetDpiForMonitor 的 DPI 类型：取有效 DPI（含用户缩放设置）。</summary>
    public const int MDT_EFFECTIVE_DPI = 0;

    /// <summary>
    /// 取指定显示器的有效 DPI（像素/英寸）。100% 缩放=96，150%=144。Win10 1607+。
    /// 失败返回非零 HRESULT，调用方按主屏 DPI 兜底。
    /// </summary>
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

    /// <summary>
    /// 取指定窗口所在显示器的有效 DPI（每英寸像素数）。Win10 1607+。
    /// 返回 0 表示 API 不可用，调用方按主屏 DPI 兜底。
    /// 相比 <see cref="GetDpiForMonitor"/ >，此方法直接按 HWND 取值，避免透明窗被误判为主屏 DPI。
    /// </summary>
    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hwnd);
}

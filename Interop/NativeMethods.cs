using System.Runtime.InteropServices;

namespace MyQuicker.Interop;

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

    /// <summary>
    /// Changes a window's size, position, and z order. Used at wake-up to
    /// re-assert topmost without stealing focus (SWP_NOACTIVATE).
    /// </summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
}

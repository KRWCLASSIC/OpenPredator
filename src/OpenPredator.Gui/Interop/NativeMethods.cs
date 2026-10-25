using System;
using System.IO;
using System.Runtime.InteropServices;

namespace OpenPredator.Gui;

internal static class NativeMethods
{
    // ==========================================
    // WIN32 CONSTANTS
    // ==========================================
    public const uint WM_TRAYICON = 0x8000 + 100;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WM_RBUTTONUP = 0x0205;
    public const int WM_CONTEXTMENU = 0x007B;

    public const int CMD_OPEN = 1001;
    public const int CMD_EXIT = 1002;
    public const int CMD_MODE_QUIET = 2001;
    public const int CMD_MODE_BALANCED = 2002;
    public const int CMD_MODE_PERF = 2003;
    public const int CMD_MODE_TURBO = 2004;

    public static readonly IntPtr HKEY_CURRENT_USER = new((long)(unchecked((int)0x80000001)));
    public static readonly IntPtr HKEY_LOCAL_MACHINE = new((long)(unchecked((int)0x80000002)));
    public const int KEY_QUERY_VALUE = 0x0001;
    public const int KEY_SET_VALUE = 0x0002;
    public const uint REG_SZ = 1;
    public const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string RunValueName = "OpenSense";

    public const uint WM_SETICON = 0x0080;
    public const int ICON_SMALL = 0;
    public const int ICON_BIG = 1;
    public const int GCLP_HICON = -14;
    public const int GCLP_HICONSM = -34;

    // ==========================================
    // WIN32 STRUCTS
    // ==========================================
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WNDCLASSEXW
    {
        public uint cbSize;
        public uint style;
        public unsafe delegate* unmanaged<IntPtr, uint, IntPtr, IntPtr, IntPtr> lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public IntPtr hInstance;
        public IntPtr hIcon;
        public IntPtr hCursor;
        public IntPtr hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    // ==========================================
    // USER32 / SHELL32 / KERNEL32 PINVOKES
    // ==========================================
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern ushort RegisterClassExW(ref WNDCLASSEXW lpwcx);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowExW(
        uint dwExStyle,
        string lpClassName,
        string lpWindowName,
        uint dwStyle,
        int X,
        int Y,
        int nWidth,
        int nHeight,
        IntPtr hWndParent,
        IntPtr hMenu,
        IntPtr hInstance,
        IntPtr lpParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool DestroyWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr DefWindowProcW(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool BringWindowToTop(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool AppendMenuW(IntPtr hMenu, uint uFlags, UIntPtr uIDNewItem, string lpNewItem);

    [DllImport("user32.dll")]
    public static extern int TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr lptpm);

    [DllImport("user32.dll")]
    public static extern bool DestroyMenu(IntPtr hMenu);

    [DllImport("user32.dll")]
    public static extern bool SetMenuDefaultItem(IntPtr hMenu, uint uItem, uint fByPos);

    [DllImport("user32.dll")]
    public static extern bool PostMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowW(string? lpClassName, string? lpWindowName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandleW(string? lpModuleName);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr ExtractIconW(IntPtr hInst, string pszExeFileName, int nIconIndex);

    [DllImport("user32.dll", EntryPoint = "SetClassLongPtrW")]
    private static extern IntPtr SetClassLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetClassLongW")]
    private static extern int SetClassLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    public static IntPtr SetClassLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        if (IntPtr.Size == 8)
            return SetClassLongPtr64(hWnd, nIndex, dwNewLong);
        return new IntPtr(SetClassLong32(hWnd, nIndex, dwNewLong.ToInt32()));
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr SendMessageW(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint ExtractIconExW(string szFileName, int nIconIndex, out IntPtr phiconLarge, out IntPtr phiconSmall, uint nIcons);

    // ==========================================
    // REGISTRY PINVOKES
    // ==========================================
    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegOpenKeyExW(IntPtr hKey, string lpSubKey, uint ulOptions, int samDesired, out IntPtr phkResult);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegSetValueExW(IntPtr hKey, string lpValueName, int Reserved, uint dwType, byte[] lpData, int cbData);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegDeleteValueW(IntPtr hKey, string lpValueName);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern int RegQueryValueExW(IntPtr hKey, string lpValueName, IntPtr lpReserved, out uint lpType, [Out] byte[]? lpData, ref uint lpcbData);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern int RegCloseKey(IntPtr hKey);

    // ==========================================
    // KERNEL32 TELEMETRY PINVOKE
    // ==========================================
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out ulong lpIdleTime, out ulong lpKernelTime, out ulong lpUserTime);

    // ==========================================
    // WINDOW ICON HELPERS
    // ==========================================
    private static IntPtr _windowIconSmall = IntPtr.Zero;
    private static IntPtr _windowIconBig = IntPtr.Zero;
    private static IntPtr _lastIconHwnd = IntPtr.Zero;

    public static void EnsureWindowIcons()
    {
        if (_windowIconSmall != IntPtr.Zero && _windowIconBig != IntPtr.Zero) return;

        string localIco = Path.Combine(AppContext.BaseDirectory, "app.ico");
        if (!File.Exists(localIco))
        {
            localIco = Path.Combine(Directory.GetCurrentDirectory(), "app.ico");
        }
        if (File.Exists(localIco))
        {
            _windowIconSmall = LoadImageW(IntPtr.Zero, localIco, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
            _windowIconBig = LoadImageW(IntPtr.Zero, localIco, 1 /* IMAGE_ICON */, 32, 32, 0x00000010 /* LR_LOADFROMFILE */);
        }

        if (_windowIconSmall == IntPtr.Zero || _windowIconBig == IntPtr.Zero)
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                ExtractIconExW(exe, 0, out IntPtr hLarge, out IntPtr hSmall, 1);
                if (_windowIconSmall == IntPtr.Zero && hSmall != IntPtr.Zero) _windowIconSmall = hSmall;
                if (_windowIconBig == IntPtr.Zero && hLarge != IntPtr.Zero) _windowIconBig = hLarge;
            }
        }

        if (_windowIconSmall == IntPtr.Zero)
        {
            _windowIconSmall = LoadIconW(IntPtr.Zero, (IntPtr)32512 /* IDI_APPLICATION */);
        }
        if (_windowIconBig == IntPtr.Zero)
        {
            _windowIconBig = _windowIconSmall;
        }
    }

    public static void ApplyWindowIcon(IntPtr hWnd)
    {
        if (!OperatingSystem.IsWindows() || hWnd == IntPtr.Zero) return;
        EnsureWindowIcons();

        if (_lastIconHwnd != hWnd)
        {
            if (_windowIconSmall != IntPtr.Zero)
            {
                SendMessageW(hWnd, WM_SETICON, (IntPtr)ICON_SMALL, _windowIconSmall);
                try { SetClassLongPtr(hWnd, GCLP_HICONSM, _windowIconSmall); } catch { }
            }
            if (_windowIconBig != IntPtr.Zero)
            {
                SendMessageW(hWnd, WM_SETICON, (IntPtr)ICON_BIG, _windowIconBig);
                try { SetClassLongPtr(hWnd, GCLP_HICON, _windowIconBig); } catch { }
            }
            _lastIconHwnd = hWnd;
        }
    }
}

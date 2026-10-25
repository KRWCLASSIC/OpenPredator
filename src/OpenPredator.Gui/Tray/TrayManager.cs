using System;
using System.IO;
using System.Runtime.InteropServices;
using OpenPredator.Core.Enums;

namespace OpenPredator.Gui;

internal static class TrayManager
{
    public static unsafe void InitializeTray()
    {
        if (!OperatingSystem.IsWindows() || AppState.TrayInitialized) return;

        try
        {
            AppState.TaskbarCreatedMsg = NativeMethods.RegisterWindowMessageW("TaskbarCreated");

            string className = "OpenSenseTrayWindowClass";
            IntPtr hInstance = NativeMethods.GetModuleHandleW(null);

            var wc = new NativeMethods.WNDCLASSEXW
            {
                cbSize = (uint)Marshal.SizeOf<NativeMethods.WNDCLASSEXW>(),
                style = 0,
                lpfnWndProc = &TrayWndProc,
                cbClsExtra = 0,
                cbWndExtra = 0,
                hInstance = hInstance,
                hIcon = IntPtr.Zero,
                hCursor = IntPtr.Zero,
                hbrBackground = IntPtr.Zero,
                lpszMenuName = null,
                lpszClassName = className,
                hIconSm = IntPtr.Zero
            };

            NativeMethods.RegisterClassExW(ref wc);

            AppState.TrayHwnd = NativeMethods.CreateWindowExW(
                0,
                className,
                "OpenSenseTrayWindow",
                0x80000000 /* WS_POPUP */,
                0, 0, 0, 0,
                IntPtr.Zero,
                IntPtr.Zero,
                hInstance,
                IntPtr.Zero);

            if (AppState.TrayHwnd != IntPtr.Zero)
            {
                CreateTrayIcon();
                AppState.TrayInitialized = true;
            }
        }
        catch { }
    }

    public static void CreateTrayIcon()
    {
        if (AppState.TrayHwnd == IntPtr.Zero) return;

        if (AppState.TrayIcon == IntPtr.Zero)
        {
            string? exe = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(exe))
            {
                AppState.TrayIcon = NativeMethods.ExtractIconW(NativeMethods.GetModuleHandleW(null), exe, 0);
            }
            if (AppState.TrayIcon == IntPtr.Zero || AppState.TrayIcon == (IntPtr)1)
            {
                string localIco = Path.Combine(AppContext.BaseDirectory, "app.ico");
                if (File.Exists(localIco))
                {
                    AppState.TrayIcon = NativeMethods.LoadImageW(IntPtr.Zero, localIco, 1 /* IMAGE_ICON */, 16, 16, 0x00000010 /* LR_LOADFROMFILE */);
                }
            }
            if (AppState.TrayIcon == IntPtr.Zero || AppState.TrayIcon == (IntPtr)1)
            {
                AppState.TrayIcon = NativeMethods.LoadIconW(IntPtr.Zero, (IntPtr)32512 /* IDI_APPLICATION */);
            }
        }

        var nid = new NativeMethods.NOTIFYICONDATAW
        {
            cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
            hWnd = AppState.TrayHwnd,
            uID = 1,
            uFlags = 0x00000001 /* NIF_MESSAGE */ | 0x00000002 /* NIF_ICON */ | 0x00000004 /* NIF_TIP */,
            uCallbackMessage = NativeMethods.WM_TRAYICON,
            hIcon = AppState.TrayIcon,
            szTip = "OpenSense - Hardware Control"
        };

        NativeMethods.Shell_NotifyIconW(0 /* NIM_ADD */, ref nid);
    }

    public static void DestroyTray()
    {
        try
        {
            if (AppState.TrayHwnd != IntPtr.Zero)
            {
                var nid = new NativeMethods.NOTIFYICONDATAW
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMethods.NOTIFYICONDATAW>(),
                    hWnd = AppState.TrayHwnd,
                    uID = 1
                };
                NativeMethods.Shell_NotifyIconW(2 /* NIM_DELETE */, ref nid);

                NativeMethods.DestroyWindow(AppState.TrayHwnd);
                AppState.TrayHwnd = IntPtr.Zero;
            }
        }
        catch { }
    }

    [UnmanagedCallersOnly]
    private static IntPtr TrayWndProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam)
    {
        if (uMsg == NativeMethods.WM_TRAYICON)
        {
            int eventId = (int)lParam;
            if (eventId == NativeMethods.WM_LBUTTONUP || eventId == NativeMethods.WM_LBUTTONDBLCLK)
            {
                Program.ShowMainWindow();
                return IntPtr.Zero;
            }
            else if (eventId == NativeMethods.WM_RBUTTONUP || eventId == NativeMethods.WM_CONTEXTMENU)
            {
                ShowTrayContextMenu(hWnd);
                return IntPtr.Zero;
            }
        }
        else if (AppState.TaskbarCreatedMsg != 0 && uMsg == AppState.TaskbarCreatedMsg)
        {
            CreateTrayIcon();
            return IntPtr.Zero;
        }

        return NativeMethods.DefWindowProcW(hWnd, uMsg, wParam, lParam);
    }

    public static void ShowTrayContextMenu(IntPtr hWnd)
    {
        if (!OperatingSystem.IsWindows()) return;
        if (!NativeMethods.GetCursorPos(out NativeMethods.POINT pt)) return;

        IntPtr hMenu = NativeMethods.CreatePopupMenu();
        if (hMenu == IntPtr.Zero) return;

        try
        {
            // 1. Open OpenSense (Bold / Default)
            NativeMethods.AppendMenuW(hMenu, 0 /* MF_STRING */, (UIntPtr)NativeMethods.CMD_OPEN, "Open OpenSense");
            NativeMethods.SetMenuDefaultItem(hMenu, NativeMethods.CMD_OPEN, 0);

            NativeMethods.AppendMenuW(hMenu, 0x00000800 /* MF_SEPARATOR */, UIntPtr.Zero, "");

            // 2. Power Profiles
            string currentModeStr = AppState.CurrentModeText.Value;
            uint quietCheck = currentModeStr.Contains("Quiet", StringComparison.OrdinalIgnoreCase) ? 0x00000008u : 0u;
            uint balCheck = currentModeStr.Contains("Balanced", StringComparison.OrdinalIgnoreCase) ? 0x00000008u : 0u;
            uint perfCheck = currentModeStr.Contains("Performance", StringComparison.OrdinalIgnoreCase) ? 0x00000008u : 0u;
            uint turboCheck = currentModeStr.Contains("Turbo", StringComparison.OrdinalIgnoreCase) ? 0x00000008u : 0u;

            NativeMethods.AppendMenuW(hMenu, 0 | quietCheck, (UIntPtr)NativeMethods.CMD_MODE_QUIET, "Mode: Quiet");
            NativeMethods.AppendMenuW(hMenu, 0 | balCheck, (UIntPtr)NativeMethods.CMD_MODE_BALANCED, "Mode: Balanced");
            NativeMethods.AppendMenuW(hMenu, 0 | perfCheck, (UIntPtr)NativeMethods.CMD_MODE_PERF, "Mode: Performance");
            if (AppState.Caps.IsPredator)
            {
                NativeMethods.AppendMenuW(hMenu, 0 | turboCheck, (UIntPtr)NativeMethods.CMD_MODE_TURBO, "Mode: Turbo");
            }

            NativeMethods.AppendMenuW(hMenu, 0x00000800 /* MF_SEPARATOR */, UIntPtr.Zero, "");

            // 3. Exit
            NativeMethods.AppendMenuW(hMenu, 0 /* MF_STRING */, (UIntPtr)NativeMethods.CMD_EXIT, "Exit OpenSense");

            NativeMethods.SetForegroundWindow(hWnd);
            int cmd = NativeMethods.TrackPopupMenuEx(hMenu, 0x0100 /* TPM_RETURNCMD */ | 0x0002 /* TPM_RIGHTBUTTON */, pt.X, pt.Y, hWnd, IntPtr.Zero);
            NativeMethods.PostMessageW(hWnd, 0 /* WM_NULL */, IntPtr.Zero, IntPtr.Zero);

            switch (cmd)
            {
                case NativeMethods.CMD_OPEN:
                    Program.ShowMainWindow();
                    break;
                case NativeMethods.CMD_MODE_QUIET:
                    HardwareService.SetPowerMode(PowerMode.Quiet);
                    break;
                case NativeMethods.CMD_MODE_BALANCED:
                    HardwareService.SetPowerMode(PowerMode.Default);
                    break;
                case NativeMethods.CMD_MODE_PERF:
                    HardwareService.SetPowerMode(PowerMode.Performance);
                    break;
                case NativeMethods.CMD_MODE_TURBO:
                    HardwareService.SetPowerMode(PowerMode.Turbo);
                    break;
                case NativeMethods.CMD_EXIT:
                    Program.ExitApplication();
                    break;
            }
        }
        finally
        {
            NativeMethods.DestroyMenu(hMenu);
        }
    }
}

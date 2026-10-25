using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OpenPredator.Core.Services;

namespace OpenPredator.Gui;

public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        AppContext.SetSwitch("Aprillz.MewUI.DevTools.Enabled", true);
        bool startMinimizedArg = HasMinimizedArg(args);

        // Single instance check via named Mutex
        if (OperatingSystem.IsWindows())
        {
            AppState.SingleInstanceMutex = new Mutex(true, "OpenPredator_OpenSense_SingleInstanceMutex", out bool createdNew);
            if (!createdNew)
            {
                if (!startMinimizedArg)
                {
                    IntPtr existingHwnd = NativeMethods.FindWindowW("OpenSenseTrayWindowClass", null);
                    if (existingHwnd != IntPtr.Zero)
                    {
                        NativeMethods.PostMessageW(existingHwnd, NativeMethods.WM_TRAYICON, IntPtr.Zero, (IntPtr)NativeMethods.WM_LBUTTONUP);
                    }
                }
                return;
            }
        }

        var app = Application.Create();

        // Register platform and rendering backend
        if (OperatingSystem.IsWindows())
        {
            app.UseWin32().UseDirect2D();
        }
        else if (OperatingSystem.IsLinux())
        {
            app.UseX11().UseMewVGX11();
        }

        // Initialize capabilities and load combined GUI settings (preferences & LDS overrides)
        AppState.Caps = HardwareManager.DetectCapabilities();
        GuiSettingsManager.LoadGuiSettings();
        GuiSettingsManager.SyncLdsObservablesFromCaps();

        TelemetryService.StartTelemetryPolling();
        HardwareService.ApplyLoadedHardwareSettings();

        bool startMinimized = startMinimizedArg;

        app.WithShutdownMode(ShutdownMode.OnExplicitShutdown)
           .OnStartup(() =>
           {
               if (Application.Current != null)
               {
                   Application.Current.RenderLoopSettings.Continuous = !startMinimized;
                   Application.Current.RenderLoopSettings.VSyncEnabled = true;
                   Application.Current.RenderLoopSettings.TargetFps = startMinimized ? 0 : 15;
               }

               if (OperatingSystem.IsWindows())
               {
                   TrayManager.InitializeTray();
               }

               if (!startMinimized)
               {
                   ShowMainWindow();
               }
           })
           .Run();

        TelemetryService.StopTelemetryPolling();
        if (OperatingSystem.IsWindows())
        {
            TrayManager.DestroyTray();
        }
    }

    public static Window EnsureMainWindow()
    {
        if (AppState.MainWindow != null) return AppState.MainWindow;

        AppState.MainWindow = new Window()
            .Title(MainNavigation.GetWindowTitle(AppState.Client.IsConnected))
            .Resizable(940, 680, minWidth: 840, minHeight: 560)
            .Content(MainNavigation.BuildNavigation(0))
            .OnPreviewKeyDown(MainNavigation.HandleKeyDown)
            .OnActivated(HandleWindowActivated)
            .OnDeactivated(HandleWindowDeactivated)
            .OnClosing(HandleWindowClosing)
            .OnClosed(HandleWindowClosed)
            .OnWindowStateChanged(HandleWindowStateChanged);

        if (Application.Current != null)
        {
            Application.Current.MainWindow = AppState.MainWindow;
        }

        // Initialize development & diagnostics tools (Inspector, Visual Tree, Performance Monitor)
        DevToolsService.EnsureDevTools(AppState.MainWindow);

        // Keep title bar live and synchronized on the UI thread when visible
        AppState.UiTimer?.Stop();
        AppState.UiTimer = new DispatcherTimer()
            .Interval(TimeSpan.FromSeconds(1))
            .OnTick(() =>
            {
                if (IsUiVisible())
                {
                    MainNavigation.UpdateWindowTitle(AppState.Client.IsConnected);
                }
            });
        if (IsUiVisible())
        {
            AppState.UiTimer.Start();
        }

        MainNavigation.UpdateWindowTitle(AppState.Client.IsConnected);

        return AppState.MainWindow;
    }

    public static bool IsUiVisible()
    {
        if (!AppState.IsWindowVisible || AppState.MainWindow == null) return false;
        if (AppState.MainWindow.WindowState == WindowState.Minimized) return false;
        if (OperatingSystem.IsWindows() && AppState.MainWindow.Handle != IntPtr.Zero)
        {
            if (NativeMethods.IsIconic(AppState.MainWindow.Handle)) return false;
            return NativeMethods.IsWindowVisible(AppState.MainWindow.Handle);
        }
        return AppState.MainWindow.IsVisible;
    }

    public static void ShowMainWindow()
    {
        AppState.IsWindowVisible = true;
        if (Application.Current != null)
        {
            Application.Current.RenderLoopSettings.Continuous = true;
            Application.Current.RenderLoopSettings.VSyncEnabled = true;
            Application.Current.RenderLoopSettings.TargetFps = 15;
        }
        AppState.UiTimer?.Start();

        var win = EnsureMainWindow();
        win.Show();
        win.WindowState = WindowState.Normal;
        if (OperatingSystem.IsWindows() && win.Handle != IntPtr.Zero)
        {
            NativeMethods.ApplyWindowIcon(win.Handle);
            NativeMethods.ShowWindow(win.Handle, 9 /* SW_RESTORE */);
            NativeMethods.BringWindowToTop(win.Handle);
            NativeMethods.SetForegroundWindow(win.Handle);
        }
        MainNavigation.UpdateWindowTitle(AppState.Client.IsConnected);
        _ = Task.Run(TelemetryService.SampleTelemetryOnceAsync);
    }

    public static void HandleWindowClosing(ClosingEventArgs e)
    {
        if (AppState.MinimizeToTrayState.Value && !AppState.IsExiting)
        {
            AppState.IsWindowVisible = false;
            AppState.UiTimer?.Stop();
            if (Application.Current != null)
            {
                Application.Current.RenderLoopSettings.Continuous = false;
                Application.Current.RenderLoopSettings.TargetFps = 0;
            }
            // Allow close: destroys the Direct2D surface and removes window from render loop (0% GPU)
        }
        else if (!AppState.IsExiting)
        {
            ExitApplication();
        }
    }

    public static void HandleWindowActivated()
    {
        if (Application.Current != null && AppState.IsWindowVisible)
        {
            Application.Current.RenderLoopSettings.Continuous = true;
            Application.Current.RenderLoopSettings.VSyncEnabled = true;
            Application.Current.RenderLoopSettings.TargetFps = 15;
        }
    }

    public static void HandleWindowDeactivated()
    {
        // Do not throttle to 15 FPS on deactivation: prevents unwanted frame rate fluctuations
        // when the mouse leaves the window or another app gains focus.
    }

    public static void HandleWindowStateChanged(WindowState state)
    {
        if (state == WindowState.Minimized)
        {
            AppState.IsWindowVisible = false;
            AppState.UiTimer?.Stop();
            if (Application.Current != null)
            {
                Application.Current.RenderLoopSettings.Continuous = false;
                Application.Current.RenderLoopSettings.TargetFps = 0;
            }
        }
        else if (state == WindowState.Normal || state == WindowState.Maximized)
        {
            AppState.IsWindowVisible = true;
            if (Application.Current != null)
            {
                Application.Current.RenderLoopSettings.Continuous = true;
                Application.Current.RenderLoopSettings.VSyncEnabled = true;
                Application.Current.RenderLoopSettings.TargetFps = 15;
            }
            AppState.UiTimer?.Start();
            MainNavigation.UpdateWindowTitle(AppState.Client.IsConnected);
            _ = Task.Run(TelemetryService.SampleTelemetryOnceAsync);
        }
    }

    public static void HandleWindowClosed()
    {
        AppState.MainWindow = null;
        AppState.IsWindowVisible = false;
        AppState.UiTimer?.Stop();
        if (Application.Current != null)
        {
            Application.Current.MainWindow = null;
            Application.Current.RenderLoopSettings.Continuous = false;
            Application.Current.RenderLoopSettings.TargetFps = 0;
        }
        if (!AppState.LdsPersistState.Value)
        {
            AppState.LdsModeEnabled.Value = false;
        }
        GuiSettingsManager.SaveGuiSettings();
    }

    public static void ExitApplication()
    {
        if (AppState.IsExiting) return;
        AppState.IsExiting = true;

        if (OperatingSystem.IsWindows())
        {
            TrayManager.DestroyTray();
        }

        TelemetryService.StopTelemetryPolling();

        try
        {
            AppState.MainWindow?.Close();
        }
        catch { }

        Application.Shutdown();
    }

    private static bool HasMinimizedArg(string[] args)
    {
        if (args == null || args.Length == 0) return false;
        foreach (var arg in args)
        {
            if (string.Equals(arg, "--minimized", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-m", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/minimized", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }
}

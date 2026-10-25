using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace OpenPredator.Gui;

internal static class MainNavigation
{
    public static NavigationView BuildNavigation(int selectedIndex = 0)
    {
        var pageList = new List<NavPage>
        {
            new NavPage("Fans", FansView.BuildFansPage),
            new NavPage("Misc", MiscView.BuildMiscPage),
            new NavPage("Settings", SettingsView.BuildSettingsPage),
        };

        if (AppState.LdsModeEnabled.Value)
        {
            pageList.Add(new NavPage("LDS Overrides", LdsView.BuildLdsPage));
        }

        var nav = new NavigationView
        {
            PaneWidth = 150,
            PaneDisplayMode = PaneDisplayMode.Inline,
        };

        nav.Items(
            pageList,
            page => page.Title,
            icon: (Func<NavPage, Aprillz.MewUI.Rendering.PathGeometry>?)null,
            content: page => new ScrollViewer()
                .Content(new Border()
                    .Padding(16)
                    .Child(page.Build())));

        if (selectedIndex >= pageList.Count) selectedIndex = pageList.Count - 1;
        nav.SelectedIndex = selectedIndex;
        AppState.CurrentNavPage = pageList[selectedIndex].Title;

        nav.OnSelectionChanged(item =>
        {
            if (item is NavPage page)
            {
                AppState.CurrentNavPage = page.Title;
                if (page.Title == "Fans")
                {
                    _ = Task.Run(TelemetryService.SampleTelemetryOnceAsync);
                }
            }
        });

        return nav;
    }

    public static void HandleKeyDown(KeyEventArgs e)
    {
        // DevTools shortcuts
        if (e.Key == Key.F12)
        {
            DevToolsService.ToggleInspector();
            return;
        }
        if (e.Key == Key.F11)
        {
            DevToolsService.ToggleVisualTree();
            return;
        }
        if (e.Key == Key.F10)
        {
            DevToolsService.TogglePerformanceMonitor();
            return;
        }
        if (e.Key == Key.F9)
        {
            DevToolsService.ToggleRepaintOverlay();
            return;
        }

        if ((DateTime.UtcNow - AppState.LastKeyTime).TotalSeconds > 2.0)
        {
            AppState.KeyBuffer.Clear();
        }
        AppState.LastKeyTime = DateTime.UtcNow;

        AppState.KeyBuffer.Add(e.Key);
        if (AppState.KeyBuffer.Count > 3)
        {
            AppState.KeyBuffer.RemoveAt(0);
        }

        if (AppState.KeyBuffer.Count == 3 &&
            AppState.KeyBuffer[0] == Key.L &&
            AppState.KeyBuffer[1] == Key.D &&
            AppState.KeyBuffer[2] == Key.S)
        {
            AppState.KeyBuffer.Clear();
            ToggleLdsMode();
        }
    }

    public static void ToggleLdsMode()
    {
        AppState.LdsModeEnabled.Value = !AppState.LdsModeEnabled.Value;
        UpdateWindowTitle(AppState.Client.IsConnected);

        int targetIndex = AppState.LdsModeEnabled.Value ? 3 : 0;
        if (AppState.MainWindow != null)
        {
            AppState.MainWindow.Content(BuildNavigation(targetIndex));
        }
    }

    public static string DetectDaemonName()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var opProcesses = Process.GetProcessesByName("openpredator-service");
                if (opProcesses.Length == 0) opProcesses = Process.GetProcessesByName("OpenPredator.Service");
                if (opProcesses.Length == 0) opProcesses = Process.GetProcessesByName("openpredator-daemon");
                if (opProcesses.Length == 0) opProcesses = Process.GetProcessesByName("OpenPredator.Daemon");
                if (opProcesses.Length > 0) return "Service Pipe";

                var psProcesses = Process.GetProcessesByName("PSSvc");
                if (psProcesses.Length == 0) psProcesses = Process.GetProcessesByName("PredatorSenseService");
                if (psProcesses.Length > 0) return "PSSvc";
            }
            catch { }
        }
        return "Service Pipe";
    }

    public static string GetWindowTitle(bool connected)
    {
        string ldsBadge = AppState.LdsModeEnabled.Value ? "[LDS MODE] " : "";
        string adminBadge = GuiSettingsManager.IsAdministrator() ? "[ADMIN] " : "";
        string status;
        if (connected)
        {
            status = DetectDaemonName();
        }
        else
        {
            bool serviceRunning = Process.GetProcessesByName("openpredator-service").Length > 0
                || Process.GetProcessesByName("OpenPredator.Service").Length > 0;

            if (serviceRunning)
            {
                status = "Service Pipe";
            }
            else if (AppState.ConnectionAttempts > 2)
            {
                status = GuiSettingsManager.IsAdministrator() ? "Direct WMI (Standalone)" : "Service Offline";
            }
            else
            {
                status = "Connecting...";
            }
        }
        return $"OpenSense {ldsBadge}{adminBadge}- {status}";
    }

    public static void UpdateWindowTitle(bool connected)
    {
        if (AppState.MainWindow != null)
        {
            if (OperatingSystem.IsWindows() && AppState.MainWindow.Handle != IntPtr.Zero)
            {
                NativeMethods.ApplyWindowIcon(AppState.MainWindow.Handle);
            }
            string newTitle = GetWindowTitle(connected);
            if (AppState.MainWindow.Title != newTitle)
            {
                AppState.MainWindow.Title = newTitle;
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using OpenPredator.Client;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Services;

namespace OpenPredator.Gui;

internal static class AppState
{
    public static readonly OpenPredatorClient Client = new();
    public static DeviceCapabilities Caps = HardwareManager.DetectCapabilities();

    // LDS (Load Different Settings) Mode State
    public static readonly ObservableValue<bool> LdsModeEnabled = new(false);
    public static readonly ObservableValue<bool> LdsPersistState = new(false);
    public static readonly ObservableValue<bool> LdsIsPredator = new(false);
    public static readonly ObservableValue<bool> LdsIsRgbKeyboard = new(false);
    public static readonly ObservableValue<bool> LdsSupportsLcdOverdrive = new(false);
    public static readonly ObservableValue<bool> LdsSupportsDiscreteGpuSwitch = new(false);
    public static readonly ObservableValue<bool> LdsSupportsCpuOverclock = new(false);
    public static readonly ObservableValue<bool> LdsSupportsGpuOverclock = new(false);
    public static readonly ObservableValue<string> LdsStatusMessage = new("LDS Mode active. Manually toggle features or force power modes below if detection fails.");
    public static readonly List<Key> KeyBuffer = new();
    public static DateTime LastKeyTime = DateTime.MinValue;

    // Fan State
    public static FanMode CurrentCpuFanMode = FanMode.Auto;
    public static FanMode CurrentGpuFanMode = FanMode.Auto;
    public static readonly ObservableValue<bool> FansLocked = new(false);
    public static TabControl? CpuTabControl;
    public static TabControl? GpuTabControl;
    public static Slider? CpuSlider;
    public static Slider? GpuSlider;
    public static bool IsSyncingDuty = false;

    // Live Telemetry Observables
    public static readonly ObservableValue<string> CpuTempText = new("-- °C");
    public static readonly ObservableValue<string> CpuLoadText = new("-- %");
    public static readonly ObservableValue<string> GpuTempText = new("-- °C");
    public static readonly ObservableValue<string> GpuLoadText = new("-- %");
    public static readonly ObservableValue<string> CpuFanRpmText = new("-- RPM");
    public static readonly ObservableValue<string> GpuFanRpmText = new("-- RPM");
    public static readonly ObservableValue<double> CpuTempProgress = new(0);
    public static readonly ObservableValue<double> CpuLoadProgress = new(0);
    public static readonly ObservableValue<double> GpuTempProgress = new(0);
    public static readonly ObservableValue<double> GpuLoadProgress = new(0);
    public static readonly ObservableValue<string> CpuTempStatus = new("Cool");
    public static readonly ObservableValue<string> CpuLoadStatus = new("Idle");
    public static readonly ObservableValue<string> GpuTempStatus = new("Cool");
    public static readonly ObservableValue<string> GpuLoadStatus = new("Idle");
    public static readonly ObservableValue<int> CpuDutyValue = new(50);
    public static readonly ObservableValue<int> GpuDutyValue = new(50);
    public static readonly ObservableValue<string> CpuDutyText = new("50%");
    public static readonly ObservableValue<string> GpuDutyText = new("50%");
    public static readonly ObservableValue<bool> CoolBoostState = new(false);
    public static bool AutoCoolBoostPreferred = false;
    public static readonly ObservableValue<bool> ExperimentalCoolBoostUnlocked = new(false);
    public static readonly ObservableValue<bool> CoolBoostCanToggle = new(true);
    public static readonly ObservableValue<bool> ShowCoolBoostUnlockButton = new(false);
    public static readonly ObservableValue<string> CoolBoostCardSubtitle = new("Boosts maximum fan speeds for extra cooling during heavy gaming or demanding workloads.");
    public static ToggleSwitch? CoolBoostToggle;
    public static PowerMode CurrentPowerMode = PowerMode.Default;
    public static readonly ObservableValue<string> CurrentModeText = new("Balanced / Default");
    public static readonly ObservableValue<int> BrightnessValue = new(100);
    public static readonly ObservableValue<string> BrightnessText = new("100%");
    public static readonly ObservableValue<bool> KbTimeoutState = new(true);
    public static readonly ObservableValue<bool> LcdOverdriveState = new(false);
    public static readonly ObservableValue<bool> WinKeyLockState = new(false);

    // Telemetry Refresh Rate / Update Frequency State
    public static readonly int[] TelemetryIntervalsMs = [500, 1000, 2000, 3000, 5000];
    public static readonly string[] TelemetryIntervalLabels = ["0.5 sec (Fast)", "1 sec (Normal)", "2 sec (Balanced)", "3 sec (Eco)", "5 sec (Low Overhead)"];
    public static readonly string[] TelemetryIntervalShortLabels = ["0.5s", "1s", "2s", "3s", "5s"];
    public static readonly ObservableValue<int> TelemetryIntervalIndex = new(1); // Default: 1 sec
    public static int TelemetryIntervalMs => TelemetryIntervalsMs[Math.Clamp(TelemetryIntervalIndex.Value, 0, TelemetryIntervalsMs.Length - 1)];
    public static ComboBox? SettingsTelemetryComboBox;
    public static string CurrentNavPage = "Fans";

    // Settings State
    public static readonly ObservableValue<bool> AutostartState = new(false);
    public static readonly ObservableValue<bool> AutostartSystemWideState = new(false);
    public static readonly ObservableValue<int> AutostartModeIndex = new(0);
    public static ComboBox? AutostartComboBox;
    public static readonly ObservableValue<bool> MinimizeToTrayState = new(true);
    public static readonly ObservableValue<bool> StartMinimizedState = new(false);
    public static bool SuppressAutostartCallback = false;

    // Window & App Lifecycle State
    public static Window? MainWindow;
    public static bool IsWindowVisible = false;
    public static DispatcherTimer? UiTimer;
    public static Mutex? SingleInstanceMutex;
    public static IntPtr TrayHwnd = IntPtr.Zero;
    public static IntPtr TrayIcon = IntPtr.Zero;
    public static uint TaskbarCreatedMsg = 0;
    public static bool TrayInitialized = false;
    public static bool IsExiting = false;

    // Telemetry Threading State
    public static CancellationTokenSource? TelemetryCts;
    public static int ConnectionAttempts = 0;
}

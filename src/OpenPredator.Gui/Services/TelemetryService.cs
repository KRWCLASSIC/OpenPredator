using System;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Services;

namespace OpenPredator.Gui;

internal static class TelemetryService
{
    private static ulong _prevIdleTime;
    private static ulong _prevKernelTime;
    private static ulong _prevUserTime;
    private static bool _firstTimesSample = true;

    // Deadband tracking to prevent redundant UI layout recalculations and Direct2D GPU frame presents
    private static int _lastDisplayedCpuTemp = -1;
    private static int _lastDisplayedGpuTemp = -1;
    private static int _lastDisplayedCpuLoad = -1;
    private static int _lastDisplayedGpuLoad = -1;

    public static void ResetDeadband()
    {
        _lastDisplayedCpuTemp = -1;
        _lastDisplayedGpuTemp = -1;
        _lastDisplayedCpuLoad = -1;
        _lastDisplayedGpuLoad = -1;
    }

    public static int SampleCpuLoad()
    {
        if (!OperatingSystem.IsWindows()) return 0;
        if (!NativeMethods.GetSystemTimes(out ulong idle, out ulong kernel, out ulong user)) return 0;

        if (_firstTimesSample)
        {
            _prevIdleTime = idle;
            _prevKernelTime = kernel;
            _prevUserTime = user;
            _firstTimesSample = false;
            return 0;
        }

        ulong usrDiff = user - _prevUserTime;
        ulong kerDiff = kernel - _prevKernelTime;
        ulong idlDiff = idle - _prevIdleTime;

        _prevIdleTime = idle;
        _prevKernelTime = kernel;
        _prevUserTime = user;

        ulong totalSys = kerDiff + usrDiff;
        if (totalSys == 0) return 0;

        long busy = (long)totalSys - (long)idlDiff;
        if (busy < 0) busy = 0;
        int percent = (int)(busy * 100 / (long)totalSys);
        return Math.Clamp(percent, 0, 100);
    }

    public static async Task SampleTelemetryOnceAsync()
    {
        try
        {
            if (AppState.TelemetryCts == null) return;
            var token = AppState.TelemetryCts.Token;

            // Unconditionally update all observables on initial / resumed sample
            ResetDeadband();

            int cpuL = SampleCpuLoad();
            _lastDisplayedCpuLoad = cpuL;
            AppState.CpuLoadText.Value = $"{cpuL} %";
            AppState.CpuLoadProgress.Value = Math.Clamp(cpuL, 0, 100);
            AppState.CpuLoadStatus.Value = cpuL switch
            {
                < 20 => "Idle",
                < 70 => "Active",
                _ => "High Load"
            };

            bool isConnected = await AppState.Client.ConnectAsync(500, token);
            if (isConnected)
            {
                var sensors = await AppState.Client.GetSensorsAsync(token);
                int cpuT = sensors.CpuTemperature;
                int gpuT = sensors.GpuTemperature;
                int gpuL = sensors.GpuUsagePercent;
                int cpuRpm = sensors.CpuFanRpm;
                int gpuRpm = sensors.GpuFanRpm;

                _lastDisplayedCpuTemp = cpuT;
                _lastDisplayedGpuTemp = gpuT;
                _lastDisplayedGpuLoad = gpuL;

                AppState.CpuTempText.Value = cpuT > 0 ? $"{cpuT} °C" : "-- °C";
                AppState.GpuTempText.Value = gpuT > 0 ? $"{gpuT} °C" : "-- °C";
                AppState.CpuFanRpmText.Value = $"{cpuRpm} RPM";
                AppState.GpuFanRpmText.Value = $"{gpuRpm} RPM";
                AppState.GpuLoadText.Value = $"{gpuL} %";

                AppState.CpuTempProgress.Value = Math.Clamp(cpuT, 0, 100);
                AppState.GpuTempProgress.Value = Math.Clamp(gpuT, 0, 100);
                AppState.GpuLoadProgress.Value = Math.Clamp(gpuL, 0, 100);

                AppState.CpuTempStatus.Value = cpuT switch
                {
                    < 55 => "Cool",
                    < 75 => "Normal",
                    < 88 => "Warm",
                    _ => "Hot"
                };

                AppState.GpuTempStatus.Value = gpuT switch
                {
                    < 55 => "Cool",
                    < 72 => "Normal",
                    < 85 => "Warm",
                    _ => "Hot"
                };

                AppState.GpuLoadStatus.Value = gpuL switch
                {
                    < 20 => "Idle",
                    < 70 => "Active",
                    _ => "High Load"
                };
            }
        }
        catch { }
    }

    public static void StartTelemetryPolling()
    {
        AppState.TelemetryCts = new CancellationTokenSource();
        var token = AppState.TelemetryCts.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                if (!Program.IsUiVisible())
                {
                    // While hidden in tray or minimized, do not update UI observables or trigger Direct2D repaints!
                    await Task.Delay(2000, token);
                    continue;
                }

                // Optimization: When user is viewing "Misc" or "Settings" page, telemetry gauges and fan dials are not visible.
                // Do not thrash sensor polling or trigger Direct2D invalidations!
                if (AppState.CurrentNavPage != "Fans")
                {
                    try
                    {
                        bool isConnected = await AppState.Client.ConnectAsync(1000, token);
                        MainNavigation.UpdateWindowTitle(isConnected);
                    }
                    catch { }

                    await Task.Delay(Math.Max(AppState.TelemetryIntervalMs, 2000), token);
                    continue;
                }

                try
                {
                    AppState.ConnectionAttempts++;
                    bool isConnected = await AppState.Client.ConnectAsync(1000, token);
                    if (isConnected) AppState.ConnectionAttempts = 0;

                    MainNavigation.UpdateWindowTitle(isConnected);

                    // Sample live CPU load with small 2% deadband to avoid repainting on idle jitter
                    int cpuL = SampleCpuLoad();
                    if (Math.Abs(cpuL - _lastDisplayedCpuLoad) >= 2 || _lastDisplayedCpuLoad < 0)
                    {
                        _lastDisplayedCpuLoad = cpuL;
                        AppState.CpuLoadText.Value = $"{cpuL} %";
                        AppState.CpuLoadProgress.Value = Math.Clamp(cpuL, 0, 100);
                        AppState.CpuLoadStatus.Value = cpuL switch
                        {
                            < 20 => "Idle",
                            < 70 => "Active",
                            _ => "High Load"
                        };
                    }

                    if (isConnected)
                    {
                        var sensors = await AppState.Client.GetSensorsAsync(token);
                        int cpuT = sensors.CpuTemperature;
                        int gpuT = sensors.GpuTemperature;
                        int gpuL = sensors.GpuUsagePercent;
                        int cpuRpm = sensors.CpuFanRpm;
                        int gpuRpm = sensors.GpuFanRpm;

                        // Temperature deadband (integer degree changes only)
                        if (cpuT != _lastDisplayedCpuTemp || _lastDisplayedCpuTemp < 0)
                        {
                            _lastDisplayedCpuTemp = cpuT;
                            AppState.CpuTempText.Value = cpuT > 0 ? $"{cpuT} °C" : "-- °C";
                            AppState.CpuTempProgress.Value = Math.Clamp(cpuT, 0, 100);
                            AppState.CpuTempStatus.Value = cpuT switch
                            {
                                < 55 => "Cool",
                                < 75 => "Normal",
                                < 88 => "Warm",
                                _ => "Hot"
                            };
                        }

                        if (gpuT != _lastDisplayedGpuTemp || _lastDisplayedGpuTemp < 0)
                        {
                            _lastDisplayedGpuTemp = gpuT;
                            AppState.GpuTempText.Value = gpuT > 0 ? $"{gpuT} °C" : "-- °C";
                            AppState.GpuTempProgress.Value = Math.Clamp(gpuT, 0, 100);
                            AppState.GpuTempStatus.Value = gpuT switch
                            {
                                < 55 => "Cool",
                                < 72 => "Normal",
                                < 85 => "Warm",
                                _ => "Hot"
                            };
                        }

                        // GPU load deadband
                        if (Math.Abs(gpuL - _lastDisplayedGpuLoad) >= 2 || _lastDisplayedGpuLoad < 0)
                        {
                            _lastDisplayedGpuLoad = gpuL;
                            AppState.GpuLoadText.Value = $"{gpuL} %";
                            AppState.GpuLoadProgress.Value = Math.Clamp(gpuL, 0, 100);
                            AppState.GpuLoadStatus.Value = gpuL switch
                            {
                                < 20 => "Idle",
                                < 70 => "Active",
                                _ => "High Load"
                            };
                        }

                        // Fan RPM: update directly without deadband
                        AppState.CpuFanRpmText.Value = $"{cpuRpm} RPM";
                        AppState.GpuFanRpmText.Value = $"{gpuRpm} RPM";

                        bool isAuto = (AppState.CurrentCpuFanMode == FanMode.Auto && AppState.CurrentGpuFanMode == FanMode.Auto);
                        bool isExp = AppState.ExperimentalCoolBoostUnlocked.Value;
                        if (isAuto || isExp)
                        {
                            bool cb = await AppState.Client.GetCoolBoostAsync(token);
                            if (AppState.CoolBoostState.Value != cb)
                            {
                                HardwareService.IsSyncingCoolBoostUi = true;
                                try
                                {
                                    AppState.CoolBoostState.Value = cb;
                                }
                                finally
                                {
                                    HardwareService.IsSyncingCoolBoostUi = false;
                                }
                            }
                        }

                        bool exp = await AppState.Client.GetExperimentalCoolBoostAsync(token);
                        if (AppState.ExperimentalCoolBoostUnlocked.Value != exp)
                        {
                            AppState.ExperimentalCoolBoostUnlocked.Value = exp;
                            HardwareService.UpdateCoolBoostCompatibility();
                        }

                        var mode = await AppState.Client.GetPowerModeAsync(token);
                        AppState.CurrentModeText.Value = mode switch
                        {
                            PowerMode.Quiet => "Quiet",
                            PowerMode.Default => "Balanced / Default",
                            PowerMode.Performance => "Performance",
                            PowerMode.Turbo => "Turbo",
                            _ => mode.ToString()
                        };
                    }
                    else
                    {
                        // Direct WMI telemetry fallback if service is unreachable
                        try
                        {
                            var sensors = await HardwareManager.Instance.GetSensorsAsync();
                            int cpuT = sensors.CpuTemperature;
                            int gpuT = sensors.GpuTemperature;
                            int gpuL = sensors.GpuUsagePercent;
                            int cpuRpm = sensors.CpuFanRpm;
                            int gpuRpm = sensors.GpuFanRpm;

                            if (cpuT > 0 || gpuT > 0)
                            {
                                if (cpuT != _lastDisplayedCpuTemp || _lastDisplayedCpuTemp < 0)
                                {
                                    _lastDisplayedCpuTemp = cpuT;
                                    AppState.CpuTempText.Value = cpuT > 0 ? $"{cpuT} °C" : "-- °C";
                                    AppState.CpuTempProgress.Value = Math.Clamp(cpuT, 0, 100);
                                }

                                if (gpuT != _lastDisplayedGpuTemp || _lastDisplayedGpuTemp < 0)
                                {
                                    _lastDisplayedGpuTemp = gpuT;
                                    AppState.GpuTempText.Value = gpuT > 0 ? $"{gpuT} °C" : "-- °C";
                                    AppState.GpuTempProgress.Value = Math.Clamp(gpuT, 0, 100);
                                }

                                AppState.CpuFanRpmText.Value = $"{cpuRpm} RPM";
                                AppState.GpuFanRpmText.Value = $"{gpuRpm} RPM";

                                if (Math.Abs(gpuL - _lastDisplayedGpuLoad) >= 2 || _lastDisplayedGpuLoad < 0)
                                {
                                    _lastDisplayedGpuLoad = gpuL;
                                    AppState.GpuLoadText.Value = $"{gpuL} %";
                                    AppState.GpuLoadProgress.Value = Math.Clamp(gpuL, 0, 100);
                                }
                            }
                        }
                        catch { }
                    }
                }
                catch
                {
                    MainNavigation.UpdateWindowTitle(false);
                }

                // Dynamic sleep based on user-selected update frequency
                await Task.Delay(AppState.TelemetryIntervalMs, token);
            }
        }, token);
    }

    public static void StopTelemetryPolling()
    {
        AppState.TelemetryCts?.Cancel();
    }
}

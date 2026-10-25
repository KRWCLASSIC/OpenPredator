using System;
using System.Threading;
using System.Threading.Tasks;
using Aprillz.MewUI;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Services;

namespace OpenPredator.Gui;

internal static class HardwareService
{
    public static bool IsSyncingCoolBoostUi = false;
    private static DispatcherTimer? _fanApplyTimer;
    private static int _isApplyingFans = 0;

    public static void ScheduleApplyFanSpeeds(int delayMs = 150)
    {
        if (_fanApplyTimer == null)
        {
            var timer = new DispatcherTimer()
                .Interval(TimeSpan.FromMilliseconds(delayMs));
            timer.OnTick(() =>
            {
                timer.Stop();
                ApplyFanSpeeds();
            });
            _fanApplyTimer = timer;
        }
        _fanApplyTimer.Stop();
        _fanApplyTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
        _fanApplyTimer.Start();
    }

    public static void UnlockExperimentalCoolBoost()
    {
        AppState.ExperimentalCoolBoostUnlocked.Value = true;
        HardwareManager.SetExperimentalCoolBoostUnlocked(true);
        _ = Task.Run(async () =>
        {
            if (AppState.Client.IsConnected)
            {
                await AppState.Client.SetExperimentalCoolBoostAsync(true);
            }
        });
        UpdateCoolBoostCompatibility();
    }

    public static void UpdateCoolBoostCompatibility()
    {
        bool isAuto = (AppState.CurrentCpuFanMode == FanMode.Auto && AppState.CurrentGpuFanMode == FanMode.Auto);
        bool isExp = AppState.ExperimentalCoolBoostUnlocked.Value;
        bool canToggle = isAuto || isExp;

        AppState.CoolBoostCanToggle.Value = canToggle;
        AppState.ShowCoolBoostUnlockButton.Value = !isAuto && !isExp;

        if (AppState.CoolBoostToggle != null)
        {
            AppState.CoolBoostToggle.IsEnabled = canToggle;
        }

        if (!canToggle)
        {
            if (AppState.CoolBoostState.Value)
            {
                IsSyncingCoolBoostUi = true;
                try
                {
                    AppState.CoolBoostState.Value = false;
                }
                finally
                {
                    IsSyncingCoolBoostUi = false;
                }
            }
        }
        else if (isAuto)
        {
            if (AppState.CoolBoostState.Value != AppState.AutoCoolBoostPreferred)
            {
                IsSyncingCoolBoostUi = true;
                try
                {
                    AppState.CoolBoostState.Value = AppState.AutoCoolBoostPreferred;
                }
                finally
                {
                    IsSyncingCoolBoostUi = false;
                }
            }
        }

        if (!isAuto && !isExp)
        {
            AppState.CoolBoostCardSubtitle.Value = "Restricted to Auto mode by default. Click 'Unlock' to enable experimental mode.";
        }
        else if (!isAuto && isExp)
        {
            AppState.CoolBoostCardSubtitle.Value = "Experimental mode active: CoolBoost is enabled for Custom / Max modes.";
        }
        else
        {
            AppState.CoolBoostCardSubtitle.Value = "Boosts maximum fan speeds for extra cooling during heavy gaming or demanding workloads.";
        }
    }

    public static void ApplyFanSpeeds()
    {
        UpdateCoolBoostCompatibility();
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            if (Interlocked.Exchange(ref _isApplyingFans, 1) == 1)
            {
                ScheduleApplyFanSpeeds(100);
                return;
            }

            try
            {
                bool isAuto = (AppState.CurrentCpuFanMode == FanMode.Auto && AppState.CurrentGpuFanMode == FanMode.Auto);
                bool effectiveCb = isAuto
                    ? AppState.AutoCoolBoostPreferred
                    : (AppState.ExperimentalCoolBoostUnlocked.Value ? AppState.CoolBoostState.Value : false);

                FanMode masterMode = (AppState.CurrentCpuFanMode == AppState.CurrentGpuFanMode) ? AppState.CurrentCpuFanMode : FanMode.Custom;

                await AppState.Client.SetFanModeAsync(
                    mode: masterMode,
                    coolBoost: effectiveCb,
                    cpuPercentage: AppState.CpuDutyValue.Value,
                    gpuPercentage: AppState.GpuDutyValue.Value,
                    cpuMode: AppState.CurrentCpuFanMode,
                    gpuMode: AppState.CurrentGpuFanMode);
            }
            finally
            {
                Interlocked.Exchange(ref _isApplyingFans, 0);
            }
        });
    }

    public static void SetCoolBoost(bool enabled)
    {
        if (IsSyncingCoolBoostUi) return;

        bool isAuto = (AppState.CurrentCpuFanMode == FanMode.Auto && AppState.CurrentGpuFanMode == FanMode.Auto);
        bool isExp = AppState.ExperimentalCoolBoostUnlocked.Value;
        if (enabled && !isAuto && !isExp)
        {
            AppState.CoolBoostState.Value = false;
            return;
        }

        if (isAuto)
        {
            AppState.AutoCoolBoostPreferred = enabled;
        }

        AppState.CoolBoostState.Value = enabled;
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            await AppState.Client.SetCoolBoostAsync(enabled);
        });
    }

    public static void SetPowerMode(PowerMode mode)
    {
        AppState.CurrentPowerMode = mode;
        AppState.CurrentModeText.Value = mode switch
        {
            PowerMode.Quiet => "Quiet",
            PowerMode.Default => "Balanced / Default",
            PowerMode.Performance => "Performance",
            PowerMode.Turbo => "Turbo",
            _ => mode.ToString()
        };
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            await AppState.Client.SetPowerModeAsync(mode);
        });
    }

    public static void SetBrightness(int brightness)
    {
        AppState.BrightnessValue.Value = brightness;
        AppState.BrightnessText.Value = brightness == 0 ? "0% (Off)" : $"{brightness}%";
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            await AppState.Client.SetKbBacklightAsync(brightness);
        });
    }

    public static void SetTimeout(bool enabled)
    {
        AppState.KbTimeoutState.Value = enabled;
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            await AppState.Client.SetKbTimeoutAsync(enabled);
        });
    }

    public static void SetRgbPreset(RgbZoneColor color)
    {
        _ = Task.Run(async () =>
        {
            var config = new RgbConfig
            {
                Brightness = (int)Math.Clamp(AppState.BrightnessValue.Value / 20, 1, 5),
                Effect = RgbEffectType.Static,
                Speed = 5,
                Zone1 = color,
                Zone2 = color,
                Zone3 = color,
                Zone4 = color
            };
            await AppState.Client.SetRgbKeyboardAsync(config);
        });
    }

    public static void SetRgbEffect(RgbEffectType effect)
    {
        _ = Task.Run(async () =>
        {
            var config = new RgbConfig
            {
                Brightness = (int)Math.Clamp(AppState.BrightnessValue.Value / 20, 1, 5),
                Effect = effect,
                Speed = 5
            };
            await AppState.Client.SetRgbKeyboardAsync(config);
        });
    }

    public static void SetWinKeyLock(bool locked)
    {
        AppState.WinKeyLockState.Value = locked;
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            await AppState.Client.SetWinKeyLockAsync(locked);
        });
    }

    public static void SetLcdOverdrive(bool enabled)
    {
        AppState.LcdOverdriveState.Value = enabled;
        GuiSettingsManager.SaveGuiSettings();
        _ = Task.Run(async () =>
        {
            await AppState.Client.SetLcdOverdriveAsync(enabled);
        });
    }

    public static void ApplyLoadedHardwareSettings()
    {
        _ = Task.Run(async () =>
        {
            for (int i = 0; i < 15; i++)
            {
                if (AppState.Client.IsConnected) break;
                await Task.Delay(200);
            }

            if (AppState.Client.IsConnected)
            {
                bool isExp = await AppState.Client.GetExperimentalCoolBoostAsync();
                AppState.ExperimentalCoolBoostUnlocked.Value = isExp;
            }
            UpdateCoolBoostCompatibility();
        });
    }
}

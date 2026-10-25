using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Principal;
using System.Text.Json;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Services;

namespace OpenPredator.Gui;

internal static class GuiSettingsManager
{
    private static bool _suppressTelemetryIntervalCallback = false;

    public static string GetGuiSettingsPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenPredator");
        return Path.Combine(dir, "gui_settings.json");
    }

    public static string GetLdsConfigPath()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenPredator");
        return Path.Combine(dir, "gui_lds.json");
    }

    public static void SyncLdsObservablesFromCaps()
    {
        AppState.LdsIsPredator.Value = AppState.Caps.IsPredator;
        AppState.LdsIsRgbKeyboard.Value = AppState.Caps.IsRgbKeyboard;
        AppState.LdsSupportsLcdOverdrive.Value = AppState.Caps.SupportsLcdOverdrive;
        AppState.LdsSupportsDiscreteGpuSwitch.Value = AppState.Caps.SupportsDiscreteGpuSwitch;
        AppState.LdsSupportsCpuOverclock.Value = AppState.Caps.SupportsCpuOverclock;
        AppState.LdsSupportsGpuOverclock.Value = AppState.Caps.SupportsGpuOverclock;
    }

    public static void UpdateCaps(
        bool? isPredator = null,
        bool? isRgbKeyboard = null,
        bool? supportsLcdOverdrive = null,
        bool? supportsDiscreteGpuSwitch = null,
        bool? supportsCpuOverclock = null,
        bool? supportsGpuOverclock = null)
    {
        AppState.Caps = AppState.Caps with
        {
            IsPredator = isPredator ?? AppState.Caps.IsPredator,
            IsRgbKeyboard = isRgbKeyboard ?? AppState.Caps.IsRgbKeyboard,
            SupportsLcdOverdrive = supportsLcdOverdrive ?? AppState.Caps.SupportsLcdOverdrive,
            SupportsDiscreteGpuSwitch = supportsDiscreteGpuSwitch ?? AppState.Caps.SupportsDiscreteGpuSwitch,
            SupportsCpuOverclock = supportsCpuOverclock ?? AppState.Caps.SupportsCpuOverclock,
            SupportsGpuOverclock = supportsGpuOverclock ?? AppState.Caps.SupportsGpuOverclock
        };

        SyncLdsObservablesFromCaps();

        if (AppState.LdsPersistState.Value)
        {
            SaveGuiSettings();
        }
    }

    public static void SetLdsPersist(bool persist)
    {
        AppState.LdsPersistState.Value = persist;
        SaveGuiSettings();
    }

    public static void LoadGuiSettings()
    {
        // 1. Defaults
        AppState.MinimizeToTrayState.Value = true;
        AppState.StartMinimizedState.Value = false;
        AppState.AutostartState.Value = false;
        AppState.AutostartSystemWideState.Value = false;
        AppState.AutostartModeIndex.Value = 0;
        AppState.TelemetryIntervalIndex.Value = 1; // Default 1 sec
        AppState.LdsModeEnabled.Value = false;
        AppState.LdsPersistState.Value = false;

        AppState.CurrentCpuFanMode = FanMode.Auto;
        AppState.CurrentGpuFanMode = FanMode.Auto;
        AppState.FansLocked.Value = false;
        AppState.CpuDutyValue.Value = 50;
        AppState.CpuDutyText.Value = "50%";
        AppState.GpuDutyValue.Value = 50;
        AppState.GpuDutyText.Value = "50%";
        AppState.CoolBoostState.Value = false;
        AppState.CurrentPowerMode = PowerMode.Default;
        AppState.CurrentModeText.Value = "Balanced / Default";
        AppState.BrightnessValue.Value = 100;
        AppState.BrightnessText.Value = "100%";
        AppState.KbTimeoutState.Value = true;
        AppState.WinKeyLockState.Value = false;
        AppState.LcdOverdriveState.Value = false;

        // 2. Load from gui_settings.json
        try
        {
            string settingsPath = GetGuiSettingsPath();
            if (File.Exists(settingsPath))
            {
                string json = File.ReadAllText(settingsPath);
                var loaded = JsonSerializer.Deserialize(json, GuiJsonContext.Default.GuiAppSettings);
                if (loaded != null)
                {
                    AppState.MinimizeToTrayState.Value = loaded.MinimizeToTray;
                    AppState.StartMinimizedState.Value = loaded.StartMinimized;
                    AppState.AutostartState.Value = loaded.AutostartWithWindows;
                    AppState.AutostartSystemWideState.Value = loaded.AutostartSystemWide;
                    AppState.AutostartModeIndex.Value = !loaded.AutostartWithWindows ? 0 : (loaded.AutostartSystemWide ? 2 : 1);
                    AppState.TelemetryIntervalIndex.Value = Math.Clamp(loaded.TelemetryIntervalIndex, 0, AppState.TelemetryIntervalsMs.Length - 1);

                    // Load active hardware state from unified ConfigManager (C:\ProgramData\OpenPredator\config.json)
                    var config = ConfigManager.Instance.Load();
                    AppState.CurrentCpuFanMode = (config.Fan.Mode == FanMode.Custom && config.Fan.CpuCustomAuto) ? FanMode.Auto : config.Fan.Mode;
                    AppState.CurrentGpuFanMode = (config.Fan.Mode == FanMode.Custom && config.Fan.GpuCustomAuto) ? FanMode.Auto : config.Fan.Mode;
                    AppState.FansLocked.Value = loaded.FansLocked;

                    int cpuDuty = Math.Clamp(config.Fan.CpuPercentage, 0, 100);
                    int gpuDuty = Math.Clamp(config.Fan.GpuPercentage, 0, 100);
                    AppState.CpuDutyValue.Value = cpuDuty;
                    AppState.CpuDutyText.Value = $"{cpuDuty}%";
                    AppState.GpuDutyValue.Value = gpuDuty;
                    AppState.GpuDutyText.Value = $"{gpuDuty}%";

                    AppState.CoolBoostState.Value = config.Fan.CoolBoost;
                    AppState.AutoCoolBoostPreferred = config.Fan.CoolBoost;
                    AppState.CurrentPowerMode = config.PowerMode;
                    AppState.CurrentModeText.Value = config.PowerMode switch
                    {
                        PowerMode.Quiet => "Quiet",
                        PowerMode.Default => "Balanced / Default",
                        PowerMode.Performance => "Performance",
                        PowerMode.Turbo => "Turbo",
                        _ => config.PowerMode.ToString()
                    };

                    AppState.BrightnessValue.Value = config.KeyboardBrightness;
                    AppState.BrightnessText.Value = config.KeyboardBrightness == 0 ? "0% (Off)" : $"{config.KeyboardBrightness}%";
                    AppState.KbTimeoutState.Value = config.KeyboardTimeout;
                    AppState.WinKeyLockState.Value = config.WinKeyLock;
                    AppState.LcdOverdriveState.Value = config.LcdOverdrive;

                    if (loaded.PersistLdsMode)
                    {
                        AppState.LdsModeEnabled.Value = true;
                        AppState.LdsPersistState.Value = true;
                        AppState.Caps = new DeviceCapabilities
                        {
                            ModelName = loaded.ModelName,
                            IsPredator = loaded.IsPredator,
                            IsRgbKeyboard = loaded.IsRgbKeyboard,
                            SupportsLcdOverdrive = loaded.SupportsLcdOverdrive,
                            SupportsDiscreteGpuSwitch = loaded.SupportsDiscreteGpuSwitch,
                            SupportsCpuOverclock = loaded.SupportsCpuOverclock,
                            SupportsGpuOverclock = loaded.SupportsGpuOverclock
                        };
                    }
                }
            }
            else
            {
                // Fallback / migration from legacy gui_lds.json
                string ldsPath = GetLdsConfigPath();
                if (File.Exists(ldsPath))
                {
                    string json = File.ReadAllText(ldsPath);
                    var loaded = JsonSerializer.Deserialize(json, GuiJsonContext.Default.GuiLdsConfig);
                    if (loaded != null && loaded.PersistLdsMode)
                    {
                        AppState.LdsModeEnabled.Value = true;
                        AppState.LdsPersistState.Value = true;
                        AppState.Caps = new DeviceCapabilities
                        {
                            ModelName = loaded.ModelName,
                            IsPredator = loaded.IsPredator,
                            IsRgbKeyboard = loaded.IsRgbKeyboard,
                            SupportsLcdOverdrive = loaded.SupportsLcdOverdrive,
                            SupportsDiscreteGpuSwitch = loaded.SupportsDiscreteGpuSwitch,
                            SupportsCpuOverclock = loaded.SupportsCpuOverclock,
                            SupportsGpuOverclock = loaded.SupportsGpuOverclock
                        };
                    }
                }
            }
        }
        catch { }

        // 3. Keep Autostart in sync with actual Windows Registry state
        if (OperatingSystem.IsWindows())
        {
            if (IsAutostartInRegistry(out bool isSystemWide, out bool regMinimized))
            {
                AppState.AutostartState.Value = true;
                AppState.AutostartSystemWideState.Value = isSystemWide;
                AppState.AutostartModeIndex.Value = isSystemWide ? 2 : 1;
                if (regMinimized)
                {
                    AppState.StartMinimizedState.Value = true;
                }
            }
            else
            {
                AppState.AutostartState.Value = false;
                AppState.AutostartSystemWideState.Value = false;
                AppState.AutostartModeIndex.Value = 0;
            }
        }
    }

    public static void SaveGuiSettings()
    {
        try
        {
            string path = GetGuiSettingsPath();
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var data = new GuiAppSettings
            {
                AutostartWithWindows = AppState.AutostartState.Value,
                AutostartSystemWide = AppState.AutostartSystemWideState.Value,
                MinimizeToTray = AppState.MinimizeToTrayState.Value,
                StartMinimized = AppState.StartMinimizedState.Value,
                TelemetryIntervalIndex = AppState.TelemetryIntervalIndex.Value,

                CpuFanMode = AppState.CurrentCpuFanMode,
                GpuFanMode = AppState.CurrentGpuFanMode,
                FansLocked = AppState.FansLocked.Value,
                CpuDuty = AppState.CpuDutyValue.Value,
                GpuDuty = AppState.GpuDutyValue.Value,
                CoolBoost = AppState.AutoCoolBoostPreferred,
                PowerMode = AppState.CurrentPowerMode,
                KeyboardBrightness = AppState.BrightnessValue.Value,
                KeyboardTimeout = AppState.KbTimeoutState.Value,
                WinKeyLock = AppState.WinKeyLockState.Value,
                LcdOverdrive = AppState.LcdOverdriveState.Value,

                PersistLdsMode = AppState.LdsPersistState.Value,
                ModelName = AppState.Caps.ModelName,
                IsPredator = AppState.Caps.IsPredator,
                IsRgbKeyboard = AppState.Caps.IsRgbKeyboard,
                SupportsLcdOverdrive = AppState.Caps.SupportsLcdOverdrive,
                SupportsDiscreteGpuSwitch = AppState.Caps.SupportsDiscreteGpuSwitch,
                SupportsCpuOverclock = AppState.Caps.SupportsCpuOverclock,
                SupportsGpuOverclock = AppState.Caps.SupportsGpuOverclock
            };

            string json = JsonSerializer.Serialize(data, GuiJsonContext.Default.GuiAppSettings);
            File.WriteAllText(path, json);
        }
        catch { }
    }

    public static void HandleTelemetryIntervalChanged(object? selected)
    {
        if (_suppressTelemetryIntervalCallback) return;

        int newIndex = -1;
        if (selected is string str)
        {
            for (int i = 0; i < AppState.TelemetryIntervalLabels.Length; i++)
            {
                if (AppState.TelemetryIntervalLabels[i].StartsWith(str, StringComparison.OrdinalIgnoreCase) ||
                    AppState.TelemetryIntervalShortLabels[i].Equals(str, StringComparison.OrdinalIgnoreCase) ||
                    AppState.TelemetryIntervalLabels[i].Equals(str, StringComparison.OrdinalIgnoreCase))
                {
                    newIndex = i;
                    break;
                }
            }
        }
        else if (selected is int idx)
        {
            newIndex = idx;
        }

        if (newIndex < 0 || newIndex >= AppState.TelemetryIntervalsMs.Length)
        {
            if (AppState.SettingsTelemetryComboBox != null) newIndex = AppState.SettingsTelemetryComboBox.SelectedIndex;
        }

        if (newIndex < 0 || newIndex == AppState.TelemetryIntervalIndex.Value) return;

        AppState.TelemetryIntervalIndex.Value = newIndex;

        _suppressTelemetryIntervalCallback = true;
        try
        {
            if (AppState.SettingsTelemetryComboBox != null && AppState.SettingsTelemetryComboBox.SelectedIndex != newIndex)
            {
                AppState.SettingsTelemetryComboBox.SelectedIndex = newIndex;
            }
        }
        finally
        {
            _suppressTelemetryIntervalCallback = false;
        }

        SaveGuiSettings();
    }

    public static void ResetLdsToDefaults()
    {
        AppState.Caps = HardwareManager.DetectCapabilities();
        SyncLdsObservablesFromCaps();
        SetLdsPersist(false);
        AppState.LdsStatusMessage.Value = "Settings restored to auto-detected hardware defaults.";
    }

    public static void HandleAutostartSelectionChanged(object? selected)
    {
        if (AppState.SuppressAutostartCallback) return;

        int newIndex = AppState.AutostartComboBox?.SelectedIndex ?? 0;
        if (selected is string str)
        {
            newIndex = str switch
            {
                "Current User" => 1,
                "System-Wide" => 2,
                _ => 0
            };
        }

        int previousIndex = AppState.AutostartModeIndex.Value;
        if (newIndex == previousIndex) return;

        bool success = true;
        if (newIndex == 0) // Off
        {
            success = SetAutostartRegistry(false, false, AppState.StartMinimizedState.Value);
            if (success)
            {
                AppState.AutostartState.Value = false;
                AppState.AutostartSystemWideState.Value = false;
            }
        }
        else if (newIndex == 1) // Current User
        {
            success = SetAutostartRegistry(true, false, AppState.StartMinimizedState.Value);
            if (success)
            {
                AppState.AutostartState.Value = true;
                AppState.AutostartSystemWideState.Value = false;
            }
        }
        else if (newIndex == 2) // System-Wide
        {
            success = SetAutostartRegistry(true, true, AppState.StartMinimizedState.Value);
            if (success)
            {
                AppState.AutostartState.Value = true;
                AppState.AutostartSystemWideState.Value = true;
            }
        }

        if (!success)
        {
            AppState.SuppressAutostartCallback = true;
            try
            {
                if (AppState.AutostartComboBox != null)
                {
                    AppState.AutostartComboBox.SelectedIndex = previousIndex;
                }
                AppState.AutostartModeIndex.Value = previousIndex;
            }
            finally
            {
                AppState.SuppressAutostartCallback = false;
            }
            return;
        }

        AppState.AutostartModeIndex.Value = newIndex;
        SaveGuiSettings();
    }

    public static void SetMinimizeToTrayPreference(bool enabled)
    {
        AppState.MinimizeToTrayState.Value = enabled;
        SaveGuiSettings();
    }

    public static void SetStartMinimizedPreference(bool enabled)
    {
        AppState.StartMinimizedState.Value = enabled;
        if (AppState.AutostartState.Value)
        {
            SetAutostartRegistry(true, AppState.AutostartSystemWideState.Value, enabled);
        }
        SaveGuiSettings();
    }

    public static bool CheckRunKey(IntPtr rootKey, out bool isMinimized)
    {
        isMinimized = false;
        if (!OperatingSystem.IsWindows()) return false;

        int res = NativeMethods.RegOpenKeyExW(rootKey, NativeMethods.RunSubKey, 0, NativeMethods.KEY_QUERY_VALUE, out IntPtr hKey);
        if (res != 0) return false;

        try
        {
            uint type = 0;
            uint size = 0;
            int queryRes = NativeMethods.RegQueryValueExW(hKey, NativeMethods.RunValueName, IntPtr.Zero, out type, null, ref size);
            if (queryRes != 0 || size == 0) return false;

            byte[] buffer = new byte[size];
            queryRes = NativeMethods.RegQueryValueExW(hKey, NativeMethods.RunValueName, IntPtr.Zero, out type, buffer, ref size);
            if (queryRes == 0)
            {
                string val = System.Text.Encoding.Unicode.GetString(buffer).TrimEnd('\0');
                isMinimized = val.Contains("--minimized", StringComparison.OrdinalIgnoreCase) ||
                              val.Contains("-m", StringComparison.OrdinalIgnoreCase);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
        finally
        {
            NativeMethods.RegCloseKey(hKey);
        }
    }

    public static bool IsAutostartInRegistry(out bool isSystemWide, out bool isMinimized)
    {
        isSystemWide = false;
        isMinimized = false;
        if (!OperatingSystem.IsWindows()) return false;

        // Check HKLM first (System-Wide)
        if (CheckRunKey(NativeMethods.HKEY_LOCAL_MACHINE, out isMinimized))
        {
            isSystemWide = true;
            return true;
        }

        // Check HKCU (Current User)
        if (CheckRunKey(NativeMethods.HKEY_CURRENT_USER, out isMinimized))
        {
            isSystemWide = false;
            return true;
        }

        return false;
    }

    public static void SetHkcuRunValue(bool enable, bool startMinimized)
    {
        if (!OperatingSystem.IsWindows()) return;

        int res = NativeMethods.RegOpenKeyExW(NativeMethods.HKEY_CURRENT_USER, NativeMethods.RunSubKey, 0, NativeMethods.KEY_SET_VALUE, out IntPtr hKey);
        if (res != 0) return;

        try
        {
            if (enable)
            {
                string? exe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exe)) return;

                string command = startMinimized ? $"\"{exe}\" --minimized" : $"\"{exe}\"";
                byte[] bytes = System.Text.Encoding.Unicode.GetBytes(command + "\0");
                NativeMethods.RegSetValueExW(hKey, NativeMethods.RunValueName, 0, NativeMethods.REG_SZ, bytes, bytes.Length);
            }
            else
            {
                NativeMethods.RegDeleteValueW(hKey, NativeMethods.RunValueName);
            }
        }
        catch { }
        finally
        {
            NativeMethods.RegCloseKey(hKey);
        }
    }

    public static bool SetHklmRunValue(bool enable, bool startMinimized)
    {
        if (!OperatingSystem.IsWindows()) return false;

        if (IsAdministrator())
        {
            int res = NativeMethods.RegOpenKeyExW(NativeMethods.HKEY_LOCAL_MACHINE, NativeMethods.RunSubKey, 0, NativeMethods.KEY_SET_VALUE, out IntPtr hKey);
            if (res != 0) return false;

            try
            {
                if (enable)
                {
                    string? exe = Environment.ProcessPath;
                    if (string.IsNullOrEmpty(exe)) return false;

                    string command = startMinimized ? $"\"{exe}\" --minimized" : $"\"{exe}\"";
                    byte[] bytes = System.Text.Encoding.Unicode.GetBytes(command + "\0");
                    return NativeMethods.RegSetValueExW(hKey, NativeMethods.RunValueName, 0, NativeMethods.REG_SZ, bytes, bytes.Length) == 0;
                }
                else
                {
                    NativeMethods.RegDeleteValueW(hKey, NativeMethods.RunValueName);
                    return true;
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                NativeMethods.RegCloseKey(hKey);
            }
        }
        else
        {
            string? exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) return false;

            string regArgs = enable
                ? $"/c reg add \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"{NativeMethods.RunValueName}\" /d \"\\\"{exe}\\\"{(startMinimized ? " --minimized" : "")}\" /f"
                : $"/c reg delete \"HKLM\\Software\\Microsoft\\Windows\\CurrentVersion\\Run\" /v \"{NativeMethods.RunValueName}\" /f & exit 0";
            return ExecuteElevatedReg(regArgs);
        }
    }

    public static bool SetAutostartRegistry(bool enable, bool systemWide, bool startMinimized)
    {
        if (!OperatingSystem.IsWindows()) return false;

        if (enable)
        {
            if (systemWide)
            {
                bool success = SetHklmRunValue(true, startMinimized);
                if (success)
                {
                    // Clean up any HKCU duplicate
                    SetHkcuRunValue(false, false);
                    return true;
                }
                return false;
            }
            else
            {
                SetHkcuRunValue(true, startMinimized);
                // Clean up HKLM if present
                if (CheckRunKey(NativeMethods.HKEY_LOCAL_MACHINE, out _))
                {
                    SetHklmRunValue(false, false);
                }
                return true;
            }
        }
        else
        {
            SetHkcuRunValue(false, false);
            if (CheckRunKey(NativeMethods.HKEY_LOCAL_MACHINE, out _))
            {
                return SetHklmRunValue(false, false);
            }
            return true;
        }
    }

    public static bool ExecuteElevatedReg(string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using var proc = Process.Start(psi);
            if (proc == null) return false;
            proc.WaitForExit();
            return proc.ExitCode == 0;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return false;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAdministrator()
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                using var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }
        return false;
    }
}

using System;
using System.IO;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Backends;

public class LinuxAcpiBackend : IHardwareBackend
{
    private const string AcerWmiPlatformPath = "/sys/devices/platform/acer-wmi";
    private const string AcerWmiSysfsPath = "/sys/bus/wmi/devices/7A4D91EE-9E47-41E7-9E90-F338E0822AB4";
    private const string HwmonBasePath = "/sys/class/hwmon";

    public string BackendName => "Linux ACPI / Sysfs (/sys/devices/platform/acer-wmi)";
    public bool IsSupported => OperatingSystem.IsLinux() && (Directory.Exists(AcerWmiPlatformPath) || Directory.Exists(AcerWmiSysfsPath));

    public async Task<SensorData> GetSensorDataAsync()
    {
        return await Task.Run(() =>
        {
            int cpuTemp = ReadSysfsInt($"{AcerWmiPlatformPath}/cpu_temp", ReadHwmonTemp("coretemp") ?? 45);
            int gpuTemp = ReadSysfsInt($"{AcerWmiPlatformPath}/gpu_temp", 40);
            int cpuRpm = ReadSysfsInt($"{AcerWmiPlatformPath}/cpu_fan_rpm", ReadHwmonFan(1) ?? 2500);
            int gpuRpm = ReadSysfsInt($"{AcerWmiPlatformPath}/gpu_fan_rpm", ReadHwmonFan(2) ?? 2700);

            return new SensorData
            {
                CpuTemperature = cpuTemp,
                GpuTemperature = gpuTemp,
                SystemTemperature = 0,
                CpuFanRpm = cpuRpm,
                GpuFanRpm = gpuRpm,
                SystemFanRpm = 0,
                GpuCount = 1,
                GpuUsagePercent = 0,
                GpuFrequencyMhz = 0
            };
        });
    }

    public async Task<bool> SetFanModeAsync(
        FanMode mode,
        bool coolBoost,
        int cpuPercentage = 50,
        int gpuPercentage = 50,
        bool cpuAuto = true,
        bool gpuAuto = true,
        FanMode? cpuMode = null,
        FanMode? gpuMode = null)
    {
        return await Task.Run(() =>
        {
            try
            {
                WriteSysfs($"{AcerWmiPlatformPath}/coolboost", coolBoost ? "1" : "0");
                WriteSysfs($"{AcerWmiPlatformPath}/fan_mode", ((int)mode).ToString());

                if (mode == FanMode.Custom || (cpuMode == FanMode.Custom || gpuMode == FanMode.Custom))
                {
                    if (!cpuAuto || cpuMode == FanMode.Custom) WriteSysfs($"{AcerWmiPlatformPath}/cpu_fan_speed", cpuPercentage.ToString());
                    if (!gpuAuto || gpuMode == FanMode.Custom) WriteSysfs($"{AcerWmiPlatformPath}/gpu_fan_speed", gpuPercentage.ToString());
                }
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetCoolBoostAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            return WriteSysfs($"{AcerWmiPlatformPath}/coolboost", enabled ? "1" : "0");
        });
    }

    public async Task<bool> SetFanSpeedAsync(int fanIndex, int percentage)
    {
        return await Task.Run(() =>
        {
            string target = fanIndex == 1 ? "cpu_fan_speed" : "gpu_fan_speed";
            return WriteSysfs($"{AcerWmiPlatformPath}/{target}", percentage.ToString());
        });
    }

    public async Task<bool> SetPowerModeAsync(PowerMode mode)
    {
        return await Task.Run(() =>
        {
            return WriteSysfs($"{AcerWmiPlatformPath}/gaming_mode", ((int)mode).ToString());
        });
    }

    public async Task<bool> SetRgbKeyboardAsync(RgbConfig config)
    {
        return await Task.Run(() =>
        {
            try
            {
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_brightness", config.Brightness.ToString());
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_mode", ((int)config.Effect).ToString());
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_speed", config.Speed.ToString());
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_zone1", $"{config.Zone1.R:X2}{config.Zone1.G:X2}{config.Zone1.B:X2}");
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_zone2", $"{config.Zone2.R:X2}{config.Zone2.G:X2}{config.Zone2.B:X2}");
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_zone3", $"{config.Zone3.R:X2}{config.Zone3.G:X2}{config.Zone3.B:X2}");
                WriteSysfs($"{AcerWmiPlatformPath}/rgb_zone4", $"{config.Zone4.R:X2}{config.Zone4.G:X2}{config.Zone4.B:X2}");
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetKbBacklightAsync(int brightnessPercentage)
    {
        return await Task.Run(() =>
        {
            return WriteSysfs($"{AcerWmiPlatformPath}/kbd_backlight", brightnessPercentage.ToString());
        });
    }

    public async Task<int> GetKbBacklightAsync()
    {
        return await Task.Run(() =>
        {
            return ReadSysfsInt($"{AcerWmiPlatformPath}/kbd_backlight", 100);
        });
    }

    public async Task<bool> SetKbTimeoutAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            return WriteSysfs($"{AcerWmiPlatformPath}/kbd_timeout", enabled ? "1" : "0");
        });
    }

    public async Task<bool> GetKbTimeoutAsync()
    {
        return await Task.Run(() =>
        {
            return ReadSysfsInt($"{AcerWmiPlatformPath}/kbd_timeout", 0) == 1;
        });
    }

    public async Task<PowerMode> GetPowerModeAsync()
    {
        return await Task.Run(() =>
        {
            int val = ReadSysfsInt($"{AcerWmiPlatformPath}/gaming_mode", 1);
            return (PowerMode)Math.Clamp(val, 0, 3);
        });
    }

    public async Task<bool> GetCoolBoostAsync()
    {
        return await Task.Run(() =>
        {
            return ReadSysfsInt($"{AcerWmiPlatformPath}/coolboost", 0) == 1;
        });
    }

    public async Task<bool> SetLcdOverdriveAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            return WriteSysfs($"{AcerWmiPlatformPath}/lcd_overdrive", enabled ? "1" : "0");
        });
    }

    public async Task<bool> GetLcdOverdriveAsync()
    {
        return await Task.Run(() =>
        {
            return ReadSysfsInt($"{AcerWmiPlatformPath}/lcd_overdrive", 0) == 1;
        });
    }

    public async Task<bool> SetWinKeyLockAsync(bool locked)
    {
        return await Task.Run(() =>
        {
            return WriteSysfs($"{AcerWmiPlatformPath}/winkey_lock", locked ? "1" : "0");
        });
    }

    public Task<bool> SetMiscSettingAsync(uint settingId, uint value) => Task.FromResult(true);
    public Task<ulong> GetMiscSettingAsync(uint settingId) => Task.FromResult(0UL);
    public Task<ulong> InvokeGamingFunctionAsync(string methodName, ulong input) => Task.FromResult(0UL);
    public Task<ulong> InvokeGenericMethodAsync(string methodName, ulong input) => Task.FromResult(0UL);

    private static int ReadSysfsInt(string path, int fallback = 0)
    {
        try
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path).Trim();
                if (int.TryParse(text, out int val)) return val;
            }
        }
        catch { }
        return fallback;
    }

    private static bool WriteSysfs(string path, string value)
    {
        try
        {
            if (File.Exists(path))
            {
                File.WriteAllText(path, value);
                return true;
            }
        }
        catch { }
        return false;
    }

    private static int? ReadHwmonTemp(string chipName)
    {
        try
        {
            if (!Directory.Exists(HwmonBasePath)) return null;
            foreach (var dir in Directory.GetDirectories(HwmonBasePath))
            {
                string nameFile = Path.Combine(dir, "name");
                if (File.Exists(nameFile) && File.ReadAllText(nameFile).Trim() == chipName)
                {
                    string tempInput = Path.Combine(dir, "temp1_input");
                    if (File.Exists(tempInput) && int.TryParse(File.ReadAllText(tempInput).Trim(), out int milliC))
                        return milliC / 1000;
                }
            }
        }
        catch { }
        return null;
    }

    private static int? ReadHwmonFan(int fanNum)
    {
        try
        {
            if (!Directory.Exists(HwmonBasePath)) return null;
            foreach (var dir in Directory.GetDirectories(HwmonBasePath))
            {
                string fanFile = Path.Combine(dir, $"fan{fanNum}_input");
                if (File.Exists(fanFile) && int.TryParse(File.ReadAllText(fanFile).Trim(), out int rpm))
                    return rpm;
            }
        }
        catch { }
        return null;
    }
}

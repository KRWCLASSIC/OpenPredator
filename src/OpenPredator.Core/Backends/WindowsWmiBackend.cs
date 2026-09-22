using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Backends;

[SupportedOSPlatform("windows")]
public class WindowsWmiBackend : IHardwareBackend
{
    private const string WmiNamespace = @"\\.\root\wmi";
    private const string GamingClassName = "AcerGamingFunction";
    private const string GenericClassName = "APGeAction";

    public string BackendName => "Windows WMI (root\\wmi -> AcerGamingFunction)";
    public bool IsSupported => OperatingSystem.IsWindows();

    public async Task<SensorData> GetSensorDataAsync()
    {
        return await Task.Run(() =>
        {
            int cpuTemp = 0, gpuTemp = 0, sysTemp = 0;
            int cpuRpm = 0, gpuRpm = 0, sysRpm = 0;

            try
            {
                // CPU Temp (Category 1, Index 1 -> 0x0101)
                ulong rCpuTemp = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0101);
                cpuTemp = (int)((rCpuTemp >> 8) & 0xFF);

                // CPU Fan RPM (Category 2, Index 1 -> 0x0201)
                ulong rCpuRpm = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0201);
                cpuRpm = (int)((rCpuRpm >> 8) & 0xFFFF);

                // GPU Temp (Category 10, Index 1 -> 0x0A01 on modern Acer gaming laptops)
                ulong rGpuTemp = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0A01);
                int parsedGpuTemp = (int)((rGpuTemp >> 8) & 0xFF);

                // System / Motherboard Temp (Category 3, Index 1 -> 0x0301)
                ulong rSysTemp = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0301);
                sysTemp = (int)((rSysTemp >> 8) & 0xFF);

                // If 0x0A01 returned valid temperature, use it; otherwise fallback to 0x0301
                gpuTemp = parsedGpuTemp > 0 ? parsedGpuTemp : sysTemp;

                // GPU Fan RPM (Category 6, Index 1 -> 0x0601)
                ulong rGpuRpm = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0601);
                gpuRpm = (int)((rGpuRpm >> 8) & 0xFFFF);

                // System Fan RPM (Category 5, Index 1 -> 0x0501)
                ulong rSysRpm = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0501);
                sysRpm = (int)((rSysRpm >> 8) & 0xFFFF);
            }
            catch { }

            return new SensorData
            {
                CpuTemperature = cpuTemp,
                GpuTemperature = gpuTemp,
                SystemTemperature = sysTemp,
                CpuFanRpm = cpuRpm,
                GpuFanRpm = gpuRpm,
                SystemFanRpm = sysRpm,
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
                FanMode effectiveCpuMode = cpuMode ?? (mode == FanMode.Custom ? (cpuAuto ? FanMode.Auto : FanMode.Custom) : mode);
                FanMode effectiveGpuMode = gpuMode ?? (mode == FanMode.Custom ? (gpuAuto ? FanMode.Auto : FanMode.Custom) : mode);

                // 1. Calculate and set Acer EC Behavior bitmask first:
                // CPU mode (bits 16-17): 1 = Auto, 2 = Max, 3 = Custom
                // GPU mode (bits 22-23): 1 = Auto, 2 = Max, 3 = Custom
                // Base flags: 9 (bits 0 and 3 enabled)
                uint cpuModeBits = effectiveCpuMode switch
                {
                    FanMode.Auto => 1u,
                    FanMode.Max => 2u,
                    FanMode.Custom => 3u,
                    _ => 1u
                };

                uint gpuModeBits = effectiveGpuMode switch
                {
                    FanMode.Auto => 1u,
                    FanMode.Max => 2u,
                    FanMode.Custom => 3u,
                    _ => 1u
                };

                ulong behaviorFlags = 9UL | ((ulong)cpuModeBits << 16) | ((ulong)gpuModeBits << 22);
                InvokeGamingFunctionSync("SetGamingFanBehavior", behaviorFlags);

                // 2. Apply Speeds for CPU (Fan index 1)
                if (effectiveCpuMode == FanMode.Custom)
                {
                    ulong cpuSpeedInput = ((ulong)Math.Clamp(cpuPercentage, 0, 100) << 8) | 1;
                    InvokeGamingFunctionSync("SetGamingFanSpeed", cpuSpeedInput);
                }
                else if (effectiveCpuMode == FanMode.Max)
                {
                    InvokeGamingFunctionSync("SetGamingFanSpeed", (100UL << 8) | 1);
                }

                // 3. Apply Speeds for GPU (Fan index 4 and index 2 for dual GPU compatibility)
                if (effectiveGpuMode == FanMode.Custom)
                {
                    ulong gpuSpeedInput4 = ((ulong)Math.Clamp(gpuPercentage, 0, 100) << 8) | 4;
                    InvokeGamingFunctionSync("SetGamingFanSpeed", gpuSpeedInput4);
                    ulong gpuSpeedInput2 = ((ulong)Math.Clamp(gpuPercentage, 0, 100) << 8) | 2;
                    InvokeGamingFunctionSync("SetGamingFanSpeed", gpuSpeedInput2);
                }
                else if (effectiveGpuMode == FanMode.Max)
                {
                    InvokeGamingFunctionSync("SetGamingFanSpeed", (100UL << 8) | 4);
                    InvokeGamingFunctionSync("SetGamingFanSpeed", (100UL << 8) | 2);
                }

                // Table selection (3 for CoolBoost or Max, 1 for normal/custom, 0 for Auto)
                ulong targetTable = (coolBoost || (effectiveCpuMode == FanMode.Max && effectiveGpuMode == FanMode.Max)) ? 3UL : 1UL;
                InvokeGamingFunctionSync("SetGamingFanTable", targetTable);

                SetCoolBoostAsync(coolBoost).GetAwaiter().GetResult();

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
            try
            {
                // 1. APGeAction.SetFunction(0x00010007 for ON, 0x00000007 for OFF)
                ulong input = ((enabled ? 1UL : 0UL) << 16) | 7UL;
                InvokeGenericMethodSync("SetFunction", input);

                // 2. Table selection (3 for CoolBoost / Extreme, 1 for normal)
                ulong targetTable = enabled ? 3UL : 1UL;
                InvokeGamingFunctionSync("SetGamingFanTable", targetTable);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> GetCoolBoostAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. APGeAction.GetFunction(0x0207) -> status in byte 0 (0 = success), enabled state in byte 1 (1 = ON)
                ulong res = InvokeGenericMethodSync("GetFunction", 0x0207UL);
                if ((res & 0xFF) == 0 && res > 0)
                {
                    return ((res >> 8) & 0xFF) == 1;
                }

                // 2. Check Fan Table from AcerGamingFunction
                ulong tableRes = InvokeGamingFunctionSync("GetGamingFanTable", 0UL);
                if ((tableRes & 0xFF) == 0 && ((tableRes >> 8) & 0xFF) == 3)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetFanSpeedAsync(int fanIndex, int percentage)
    {
        return await Task.Run(() =>
        {
            try
            {
                ulong input = (ulong)((Math.Clamp(percentage, 0, 100) << 8) | (fanIndex & 0xFF));
                InvokeGamingFunctionSync("SetGamingFanSpeed", input);
                InvokeGamingFunctionSync("SetGamingFanSpeed", input | 0x00080000UL);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetPowerModeAsync(PowerMode mode)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Setting ID 0x0B (11) is Operation Mode in AcerGamingFunction.SetGamingMiscSetting
                // Mode 0: Quiet, Mode 1: Default/Balanced, Mode 2: Performance, Mode 3: Turbo
                ulong input = ((ulong)mode << 8) | 0x0BUL;
                ulong res = InvokeGamingFunctionSync("SetGamingMiscSetting", input);
                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetRgbKeyboardAsync(RgbConfig config)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Brightness setting (0-100)
                SetKbBacklightAsync(config.Brightness * 25).GetAwaiter().GetResult();

                // Set 4-zone colors: wire format (B << 24) | (G << 16) | (R << 8) | zoneId
                // Zone 1: ID 1
                ulong z1Input = (ulong)((uint)(config.Zone1.B << 24) | (uint)(config.Zone1.G << 16) | (uint)(config.Zone1.R << 8) | 1u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z1Input);

                // Zone 2: ID 2
                ulong z2Input = (ulong)((uint)(config.Zone2.B << 24) | (uint)(config.Zone2.G << 16) | (uint)(config.Zone2.R << 8) | 2u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z2Input);

                // Zone 3: ID 3
                ulong z3Input = (ulong)((uint)(config.Zone3.B << 24) | (uint)(config.Zone3.G << 16) | (uint)(config.Zone3.R << 8) | 3u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z3Input);

                // Zone 4: ID 4
                ulong z4Input = (ulong)((uint)(config.Zone4.B << 24) | (uint)(config.Zone4.G << 16) | (uint)(config.Zone4.R << 8) | 4u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z4Input);

                // Effect pattern & speed: (effect << 16) | (speed << 8) | (direction & 0xFF)
                ulong effectInput = (ulong)(((uint)config.Effect << 16) | ((uint)config.Speed << 8) | (uint)(config.Direction & 0xFF));
                InvokeGamingFunctionSync("SetGamingLEDBehavior", effectInput);

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
            try
            {
                int clamped = Math.Clamp(brightnessPercentage, 0, 100);
                const int bkHotkey = 132;

                // Check current timeout setting directly from ACPI EC
                bool timeoutEnabled = false;
                try
                {
                    ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                    ulong qRes = InvokeGenericMethodSync("GetFunction", q);
                    if ((qRes & 0xFF) == 0 && qRes > 0)
                    {
                        timeoutEnabled = ((qRes >> 40) & 0xFF) == 30;
                    }
                }
                catch { }

                ulong timeoutByte = timeoutEnabled ? 0x1EUL : 0UL;

                // 1. APGeAction SetFunction: 2 | (bkHotkey << 8) | 0x80000 | (clamped << 32) | (timeoutByte << 40)
                ulong genericInput = 2UL | ((ulong)bkHotkey << 8) | 0x80000UL | ((ulong)clamped << 32) | (timeoutByte << 40);
                InvokeGenericMethodSync("SetFunction", genericInput);

                // 2. AcerGamingFunction.SetGamingKBBacklight: (clamped << 16)
                ulong gamingInput = (ulong)clamped << 16;
                InvokeGamingFunctionSync("SetGamingKBBacklight", gamingInput);

                // 3. SetGamingRgbKb for zone compatibility
                InvokeGamingFunctionSync("SetGamingRgbKb", 1UL | ((ulong)clamped << 8));

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<int> GetKbBacklightAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                const int bkHotkey = 132;
                ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                ulong res = InvokeGenericMethodSync("GetFunction", q);
                if ((res & 0xFF) == 0 && res > 0)
                {
                    return (int)((res >> 32) & 0xFF);
                }
            }
            catch { }
            return 100;
        });
    }

    public async Task<bool> SetKbTimeoutAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            try
            {
                const int bkHotkey = 132;
                int currentBrightness = 100;
                try
                {
                    ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                    ulong qRes = InvokeGenericMethodSync("GetFunction", q);
                    if ((qRes & 0xFF) == 0 && qRes > 0)
                    {
                        currentBrightness = (int)((qRes >> 32) & 0xFF);
                    }
                }
                catch { }

                ulong timeoutByte = enabled ? 0x1EUL : 0UL;
                ulong genericInput = 2UL | ((ulong)bkHotkey << 8) | 0x80000UL | ((ulong)currentBrightness << 32) | (timeoutByte << 40);
                ulong res = InvokeGenericMethodSync("SetFunction", genericInput);

                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> GetKbTimeoutAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                const int bkHotkey = 132;
                ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                ulong res = InvokeGenericMethodSync("GetFunction", q);
                if ((res & 0xFF) == 0 && res > 0)
                {
                    return ((res >> 40) & 0xFF) == 30;
                }
            }
            catch { }
            return false;
        });
    }

    public async Task<PowerMode> GetPowerModeAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                ulong res = InvokeGamingFunctionSync("GetGamingMiscSetting", 0x0BUL);
                int modeVal = (int)((res >> 8) & 0x3);
                return (PowerMode)modeVal;
            }
            catch
            {
                return PowerMode.Default;
            }
        });
    }

    public async Task<bool> SetLcdOverdriveAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense / PSSvc: AcerGamingFunction.SetGamingProfile(0x1000000000000 | 16 for ON, 16 for OFF)
                ulong input = enabled ? (0x1000000000000UL | 16UL) : 16UL;
                ulong res = InvokeGamingFunctionSync("SetGamingProfile", input);
                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> GetLcdOverdriveAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense: AcerGamingFunction.GetGamingProfile(0) -> bits 48..55 indicate LCD overdrive state
                ulong res = InvokeGamingFunctionSync("GetGamingProfile", 0UL);
                return (res & 0xFF) == 0 && (((res >> 48) & 0xFF) == 1);
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetWinKeyLockAsync(bool locked)
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense: AcerGamingFunction.SetGamingProfile((locked ? 1 : 0) << 24 | 2)
                ulong input = ((locked ? 1UL : 0UL) << 24) | 2UL;
                ulong res = InvokeGamingFunctionSync("SetGamingProfile", input);
                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<bool> SetMiscSettingAsync(uint settingId, uint value)
    {
        return await Task.Run(() =>
        {
            try
            {
                ulong input = ((ulong)value << 8) | (settingId & 0xFF);
                InvokeGamingFunctionSync("SetGamingMiscSetting", input);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public async Task<ulong> GetMiscSettingAsync(uint settingId)
    {
        return await Task.Run(() =>
        {
            return InvokeGamingFunctionSync("GetGamingMiscSetting", settingId);
        });
    }

    public Task<ulong> InvokeGamingFunctionAsync(string methodName, ulong input)
    {
        return Task.Run(() => InvokeGamingFunctionSync(methodName, input));
    }

    public Task<ulong> InvokeGenericMethodAsync(string methodName, ulong input)
    {
        return Task.Run(() => InvokeGenericMethodSync(methodName, input));
    }

    private ulong InvokeGamingFunctionSync(string methodName, ulong input)
    {
        return NativeWmi.ExecuteGamingFunction(methodName, input);
    }

    private ulong InvokeGenericMethodSync(string methodName, ulong input)
    {
        return NativeWmi.ExecuteGenericMethod(methodName, input);
    }
}

using System;
using System.Threading.Tasks;
using OpenPredator.Core.Backends;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Services;

public sealed class HardwareManager
{
    private static readonly Lazy<HardwareManager> _instance = new(() => new HardwareManager());
    public static HardwareManager Instance => _instance.Value;

    public IHardwareBackend Backend { get; private set; }

    public FanConfig CurrentFanConfig { get; set; } = ConfigManager.Instance.Current.Fan;
    public RgbConfig CurrentRgbConfig { get; set; } = ConfigManager.Instance.Current.Rgb;
    public PowerMode CurrentPowerMode { get; set; } = ConfigManager.Instance.Current.PowerMode;
    public DeviceCapabilities Capabilities { get; private set; } = DetectCapabilities();

    public HardwareManager()
    {
        Backend = CreateBestBackend();
    }

    public static DeviceCapabilities DetectCapabilities()
    {
        string model = "Acer Gaming Laptop";
        bool isPredator = false;
        bool isRgb = false;
        bool lcdOd = false;
        bool mux = false;
        bool cpuOc = false;
        bool gpuOc = false;

        if (OperatingSystem.IsWindows())
        {
            try
            {
                // 1. Read System Model & OEM Hardware Table via Direct Kernel SMBIOS
                var smbios = SmbiosReader.GetSystemInfo();
                if (!string.IsNullOrWhiteSpace(smbios.ProductName) && smbios.ProductName != "Acer Gaming Laptop")
                {
                    model = smbios.ProductName;
                }
                isPredator = model.Contains("predator", StringComparison.OrdinalIgnoreCase)
                    || smbios.Family.Contains("predator", StringComparison.OrdinalIgnoreCase);

                // 2. Hardware Profile Probe (AcerGamingFunction.GetGamingProfile)
                ulong profRaw = NativeWmi.ExecuteGamingFunction("GetGamingProfile", 0UL);

                // LCD Overdrive (Bits 48-55 of hardware profile bitmask)
                if (profRaw > 0)
                {
                    int lcdVal = (int)((profRaw >> 48) & 0xFF);
                    lcdOd = lcdVal < 2;
                }

                // Discrete GPU / MUX Switchable Graphics (Misc Setting ID 9)
                ulong dGpuRaw = NativeWmi.ExecuteGamingFunction("GetGamingMiscSetting", 9UL);
                mux = (dGpuRaw & 0xFF00) == 0x0300;

                // 3. Overclocking & Boost Capabilities (Native Acer Predator Overclock)
                if (isPredator)
                {
                    ulong gpuMisc = NativeWmi.ExecuteGamingFunction("GetGamingMiscSetting", 0x09UL);
                    int gpuVal = (int)((gpuMisc >> 8) & 0xFF);
                    gpuOc = (gpuMisc & 0xFF) == 0 && gpuVal > 0 && gpuVal != 0xFF;

                    ulong cpuMisc = NativeWmi.ExecuteGamingFunction("GetGamingMiscSetting", 0x0AUL);
                    int cpuVal = (int)((cpuMisc >> 8) & 0xFF);
                    cpuOc = (cpuMisc & 0xFF) == 0 && cpuVal > 0 && cpuVal != 0xFF;
                }

                // 4. Keyboard Lighting Architecture Probe
                // 4-Zone RGB vs Monochrome: Parsed strictly from Acer OEM SMBIOS Type 171 Feature 19
                isRgb = smbios.HasRgbKeyboard;
            }
            catch { }
        }

        return new DeviceCapabilities
        {
            ModelName = model,
            IsPredator = isPredator,
            IsRgbKeyboard = isRgb,
            SupportsLcdOverdrive = lcdOd,
            SupportsDiscreteGpuSwitch = mux,
            SupportsCpuOverclock = cpuOc,
            SupportsGpuOverclock = gpuOc
        };
    }

    public void SetCustomBackend(IHardwareBackend backend)
    {
        Backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public static IHardwareBackend CreateBestBackend()
    {
        if (OperatingSystem.IsWindows())
        {
            return new WindowsWmiBackend();
        }
        if (OperatingSystem.IsLinux())
        {
            return new LinuxAcpiBackend();
        }

        throw new PlatformNotSupportedException("OpenPredator requires a supported Windows or Linux environment with ACPI/WMI hardware.");
    }

    public async Task<SensorData> GetSensorsAsync()
    {
        var data = await Backend.GetSensorDataAsync();

        // Overlay direct NVML hardware telemetry if available
        if (NvmlTelemetry.TryGetGpuMetrics(out int gpuTemp, out int gpuLoad, out int gpuFreq, out int gpuCount))
        {
            data = data with
            {
                GpuTemperature = gpuTemp > 0 ? gpuTemp : data.GpuTemperature,
                GpuUsagePercent = gpuLoad,
                GpuFrequencyMhz = gpuFreq,
                GpuCount = gpuCount
            };
        }

        return data;
    }

    public async Task<bool> ApplyFanConfigAsync(FanConfig config, bool persist = true)
    {
        CurrentFanConfig = config;
        bool ok = await Backend.SetFanModeAsync(
            config.Mode,
            config.CoolBoost,
            config.CpuPercentage,
            config.GpuPercentage,
            config.CpuCustomAuto,
            config.GpuCustomAuto);

        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { Fan = config });
        }

        return ok;
    }

    public async Task<bool> SetFanModeAsync(
        FanMode mode,
        bool coolBoost = false,
        int cpuPercentage = 50,
        int gpuPercentage = 50,
        bool cpuAuto = true,
        bool gpuAuto = true,
        FanMode? cpuMode = null,
        FanMode? gpuMode = null,
        bool persist = true)
    {
        CurrentFanConfig = CurrentFanConfig with
        {
            Mode = mode,
            CoolBoost = coolBoost,
            CpuPercentage = cpuPercentage,
            GpuPercentage = gpuPercentage,
            CpuCustomAuto = cpuAuto,
            GpuCustomAuto = gpuAuto
        };
        bool ok = await Backend.SetFanModeAsync(mode, coolBoost, cpuPercentage, gpuPercentage, cpuAuto, gpuAuto, cpuMode, gpuMode);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { Fan = CurrentFanConfig });
        }
        return ok;
    }

    public async Task<bool> SetCoolBoostAsync(bool enabled, bool persist = true)
    {
        CurrentFanConfig = CurrentFanConfig with { CoolBoost = enabled };
        bool ok = await Backend.SetCoolBoostAsync(enabled);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { Fan = CurrentFanConfig });
        }
        return ok;
    }

    public async Task<bool> GetCoolBoostAsync()
    {
        bool cb = await Backend.GetCoolBoostAsync();
        CurrentFanConfig = CurrentFanConfig with { CoolBoost = cb };
        return cb;
    }

    public async Task<bool> SetPowerModeAsync(PowerMode mode, bool persist = true)
    {
        CurrentPowerMode = mode;
        bool ok = await Backend.SetPowerModeAsync(mode);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { PowerMode = mode });
        }
        return ok;
    }

    public async Task<PowerMode> GetPowerModeAsync()
    {
        var mode = await Backend.GetPowerModeAsync();
        CurrentPowerMode = mode;
        return mode;
    }

    public async Task<bool> SetKbBacklightAsync(int brightnessPercentage, bool persist = true)
    {
        CurrentRgbConfig = CurrentRgbConfig with { Brightness = Math.Clamp(brightnessPercentage / 25, 0, 4) };
        bool ok = await Backend.SetKbBacklightAsync(brightnessPercentage);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { KeyboardBrightness = brightnessPercentage });
        }
        return ok;
    }

    public async Task<int> GetKbBacklightAsync()
    {
        return await Backend.GetKbBacklightAsync();
    }

    public async Task<bool> SetKbTimeoutAsync(bool enabled, bool persist = true)
    {
        bool ok = await Backend.SetKbTimeoutAsync(enabled);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { KeyboardTimeout = enabled });
        }
        return ok;
    }

    public async Task<bool> GetKbTimeoutAsync()
    {
        return await Backend.GetKbTimeoutAsync();
    }

    public async Task<bool> ApplyRgbConfigAsync(RgbConfig config, bool persist = true)
    {
        CurrentRgbConfig = config;
        bool ok = await Backend.SetRgbKeyboardAsync(config);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { Rgb = config });
        }
        return ok;
    }

    public async Task<bool> SetLcdOverdriveAsync(bool enabled, bool persist = true)
    {
        bool ok = await Backend.SetLcdOverdriveAsync(enabled);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { LcdOverdrive = enabled });
        }
        return ok;
    }

    public async Task<bool> GetLcdOverdriveAsync()
    {
        return await Backend.GetLcdOverdriveAsync();
    }

    public async Task<bool> SetWinKeyLockAsync(bool locked, bool persist = true)
    {
        bool ok = await Backend.SetWinKeyLockAsync(locked);
        if (ok && persist)
        {
            var cfg = ConfigManager.Instance.Current;
            ConfigManager.Instance.Save(cfg with { WinKeyLock = locked });
        }
        return ok;
    }
}

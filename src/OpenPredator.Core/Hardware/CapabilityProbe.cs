using System;
using System.Collections.Generic;
using System.Text;
using OpenPredator.Core.Backends;
using OpenPredator.Core.Backends.Linux;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Hardware;

/// <summary>
/// Hardware capability prober.
/// Discovers supported features directly from motherboard ACPI/WMI methods and OEM SMBIOS tables.
/// Zero hardcoded model catalogs; 100% firmware-driven.
/// </summary>
public static class CapabilityProbe
{
    public static DeviceCapabilities Probe(IHardwareBackend backend, SmbiosReader.SmbiosInfo? smbios = null)
    {
        var diag = new StringBuilder();
        diag.AppendLine("=== OpenPredator Dynamic Hardware Capability Probe ===");

        if (smbios == null)
        {
            if (OperatingSystem.IsWindows())
            {
                smbios = SmbiosReader.GetSystemInfo();
            }
            else if (OperatingSystem.IsLinux())
            {
                smbios = LinuxDmiReader.GetSystemInfo();
            }
            else
            {
                smbios = new SmbiosReader.SmbiosInfo("Acer", "Acer Gaming Laptop", "", "", "", false);
            }
        }

        string model = string.IsNullOrWhiteSpace(smbios.ProductName) || smbios.ProductName == "Acer Gaming Laptop"
            ? "Acer Gaming Laptop"
            : smbios.ProductName;

        string family = smbios.Family ?? "";
        string serial = smbios.SerialNumber?.Trim() ?? "";
        string partNumber = "";
        if (serial.Length >= 10 && serial.StartsWith("NH", StringComparison.OrdinalIgnoreCase))
        {
            partNumber = $"{serial[..2]}.{serial[2..7]}.{serial[7..10]}";
        }

        bool isPredator = model.Contains("predator", StringComparison.OrdinalIgnoreCase)
            || family.Contains("predator", StringComparison.OrdinalIgnoreCase);

        diag.AppendLine($"[Hardware Identity] Model: '{model}' | Family: '{family}' | Serial: '{serial}' | Part: '{partNumber}' | Predator Brand: {isPredator}");
        diag.AppendLine($"[OEM SMBIOS] Type 171 RGB Keyboard Hardware Flag: {smbios.HasRgbKeyboard}");

        bool isRgb = smbios.HasRgbKeyboard;

        // 1. Dynamic Fan & Sensor Channel Discovery via GetGamingSysInfo(0x00)
        bool hasCpuFan = true;
        bool hasGpuFan = true;
        bool hasSysFan = false;
        try
        {
            ulong maskRaw = backend.InvokeGamingFunctionSync("GetGamingSysInfo", 0x00);
            diag.AppendLine($"[SensorMask] GetGamingSysInfo(0x00) -> 0x{maskRaw:X16}");
            if ((maskRaw & 0xFF) == 0 && maskRaw > 0)
            {
                // Acer System_Health_Information_Index bitmask (1u << (Index - 1)):
                // Index 2 (sCPU_Fan_Speed)  -> Bit 1
                // Index 4 (sSystem_Fan_Speed)-> Bit 3
                // Index 6 (sGPU_Fan_Speed)  -> Bit 5
                uint mask = (uint)((maskRaw >> 8) & 0xFFFF);
                hasCpuFan = (mask & (1u << 1)) != 0;
                hasGpuFan = (mask & (1u << 5)) != 0;
                hasSysFan = (mask & (1u << 3)) != 0;
            }
            else
            {
                // Direct probe on auxiliary/system fan RPM (Category 5, Index 1 -> 0x0501)
                ulong sysFanRpm = backend.InvokeGamingFunctionSync("GetGamingSysInfo", 0x0501);
                hasSysFan = (sysFanRpm & 0xFF) == 0 && ((sysFanRpm >> 8) & 0xFFFF) > 0;
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"[SensorMask] Probing failed: {ex.Message}");
        }

        int fanCount = (hasCpuFan ? 1 : 0) + (hasGpuFan ? 1 : 0) + (hasSysFan ? 1 : 0);
        if (fanCount < 2) fanCount = 2;
        diag.AppendLine($"[Fans] Total: {fanCount} (CPU: {hasCpuFan}, GPU: {hasGpuFan}, Aux/System: {hasSysFan})");

        // 2. Hardware Operating Modes vs CoolBoost
        bool supportsModes = false;
        var supportedModes = new List<PowerMode>();
        try
        {
            // Probe supported modes bitmask from GetGamingMiscSetting(0x0A)
            // Bit 0: Quiet (0), Bit 1: Default (1), Bit 2/4: Extreme/Performance (2/4), Bit 3/5: Turbo (3/5)
            ulong modesRaw = backend.InvokeGamingFunctionSync("GetGamingMiscSetting", 0x0AUL);
            diag.AppendLine($"[OperatingModes] GetGamingMiscSetting(0x0A) -> 0x{modesRaw:X16}");
            if ((modesRaw & 0xFF) == 0 && modesRaw > 0)
            {
                uint mask = (uint)((modesRaw >> 8) & 0xFF);
                if ((mask & (1u << 0)) != 0) supportedModes.Add(PowerMode.Quiet);
                if ((mask & (1u << 1)) != 0) supportedModes.Add(PowerMode.Default);
                if ((mask & ((1u << 2) | (1u << 4))) != 0) supportedModes.Add(PowerMode.Performance);
                if (isPredator && (mask & ((1u << 3) | (1u << 5))) != 0) supportedModes.Add(PowerMode.Turbo);
            }

            if (supportedModes.Count == 0)
            {
                // Check if current operating mode query GetGamingMiscSetting(0x0B) is valid
                ulong curModeRaw = backend.InvokeGamingFunctionSync("GetGamingMiscSetting", 0x0BUL);
                diag.AppendLine($"[OperatingModes] GetGamingMiscSetting(0x0B) -> 0x{curModeRaw:X16}");
                if ((curModeRaw & 0xFF) == 0)
                {
                    int modeVal = (int)((curModeRaw >> 8) & 0xFF);
                    if (modeVal >= 0 && modeVal <= 5)
                    {
                        supportedModes.AddRange([PowerMode.Quiet, PowerMode.Default, PowerMode.Performance]);
                        if (isPredator)
                        {
                            supportedModes.Add(PowerMode.Turbo);
                        }
                    }
                }
            }

            supportsModes = supportedModes.Count > 0;
        }
        catch (Exception ex)
        {
            diag.AppendLine($"[OperatingModes] Probing failed: {ex.Message}");
        }
        diag.AppendLine($"[OperatingModes] Supported: {supportsModes} ({string.Join(", ", supportedModes)})");

        // 3. CoolBoost Capability Probe
        bool supportsCoolBoost = false;
        try
        {
            ulong cbRaw = backend.InvokeGenericMethodSync("GetFunction", 0x0207UL);
            diag.AppendLine($"[CoolBoost] GetFunction(0x0207) -> 0x{cbRaw:X16}");
            if ((cbRaw & 0xFF) == 0)
            {
                supportsCoolBoost = true;
            }
            else
            {
                ulong tableRaw = backend.InvokeGamingFunctionSync("GetGamingFanTable", 0UL);
                diag.AppendLine($"[CoolBoost] GetGamingFanTable(0) -> 0x{tableRaw:X16}");
                if ((tableRaw & 0xFF) == 0)
                {
                    supportsCoolBoost = true;
                }
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"[CoolBoost] Probing failed: {ex.Message}");
        }
        diag.AppendLine($"[CoolBoost] Supported: {supportsCoolBoost}");

        // 4. Discrete GPU / Hardware MUX Switch
        bool supportsMux = false;
        try
        {
            ulong dGpuRaw = backend.InvokeGamingFunctionSync("GetGamingMiscSetting", 9UL);
            diag.AppendLine($"[MUX] GetGamingMiscSetting(0x09) -> 0x{dGpuRaw:X16}");
            if ((dGpuRaw & 0xFF) == 0)
            {
                uint gpuModes = (uint)((dGpuRaw >> 8) & 0xFF);
                supportsMux = (gpuModes & 0x03) == 0x03;
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"[MUX] Probing failed: {ex.Message}");
        }
        diag.AppendLine($"[MUX] Hardware MUX Switch Supported: {supportsMux}");

        // 5. LCD Overdrive & Windows Key Lock via GetGamingProfile(0)
        bool supportsLcdOd = false;
        bool supportsWinKeyLock = false;
        try
        {
            ulong profRaw = backend.InvokeGamingFunctionSync("GetGamingProfile", 0UL);
            diag.AppendLine($"[Profile] GetGamingProfile(0) -> 0x{profRaw:X16}");
            if ((profRaw & 0xFF) == 0 && profRaw > 0)
            {
                // Status 0 indicates GetGamingProfile is supported by EC firmware.
                supportsWinKeyLock = true;

                int lcdVal = (int)((profRaw >> 48) & 0xFF);
                supportsLcdOd = lcdVal == 0 || lcdVal == 1;
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"[Profile] Probing failed: {ex.Message}");
        }
        diag.AppendLine($"[Profile] LCD Overdrive: {supportsLcdOd} | WinKeyLock: {supportsWinKeyLock}");

        // 6. Keyboard Timeout via APGeAction hotkey 132
        bool supportsKbTimeout = false;
        try
        {
            const int bkHotkey = 132;
            ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
            ulong qRes = backend.InvokeGenericMethodSync("GetFunction", q);
            diag.AppendLine($"[KeyboardTimeout] GetFunction(0x80000 | (132 << 8) | 1) -> 0x{qRes:X16}");
            if ((qRes & 0xFF) == 0)
            {
                supportsKbTimeout = true;
            }
        }
        catch (Exception ex)
        {
            diag.AppendLine($"[KeyboardTimeout] Probing failed: {ex.Message}");
        }
        diag.AppendLine($"[KeyboardTimeout] Supported: {supportsKbTimeout}");

        return new DeviceCapabilities
        {
            ModelName = model,
            SerialNumber = serial,
            PartNumber = partNumber,
            IsPredator = isPredator,
            IsRgbKeyboard = isRgb,
            SupportsLcdOverdrive = supportsLcdOd,
            SupportsDiscreteGpuSwitch = supportsMux,
            SupportsCpuOverclock = isPredator && supportsModes,
            SupportsGpuOverclock = isPredator && supportsModes,
            FanCount = fanCount,
            HasCpuFan = hasCpuFan,
            HasGpuFan = hasGpuFan,
            HasSystemFan = hasSysFan,
            SupportsCoolBoost = supportsCoolBoost,
            SupportsOperatingModes = supportsModes,
            SupportedOperatingModes = supportedModes,
            SupportsWinKeyLock = supportsWinKeyLock,
            SupportsBacklightTimeout = supportsKbTimeout,
            DiagnosticsLog = diag.ToString()
        };
    }
}

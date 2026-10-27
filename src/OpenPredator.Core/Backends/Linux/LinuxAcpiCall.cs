using System;
using System.Globalization;
using System.IO;
using OpenPredator.Core.Services;

namespace OpenPredator.Core.Backends.Linux;

/// <summary>
/// Direct Linux ACPI transport for Acer laptops.
/// Evaluates WMI ACPI methods directly via /proc/acpi/call using method IDs dynamically resolved by BmofReader.
/// Discovers ACPI PNP0C14 device path dynamically from sysfs with zero hardcoded fallbacks.
/// </summary>
public static class LinuxAcpiCall
{
    private const string AcpiCallPath = "/proc/acpi/call";
    private const string GamingFunctionGuid = "7A4DDFE7-5B5D-40B4-8595-4408E0CC7F56";
    private const string GenericActionGuid = "BD6FED80-9DA7-47CE-827D-5A46C495B422";

    private static string? _acpiBasePath = null;
    private static string? _gamingMethodName = null;
    private static string? _genericMethodName = null;
    private static bool _discoveryAttempted = false;
    private static readonly object _syncLock = new();
    public static bool IsAvailable
    {
        get
        {
            if (!File.Exists(AcpiCallPath)) return false;
            if (ProcessElevation.IsElevated) return true;
            try
            {
                using var fs = File.Open(AcpiCallPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public static ulong ExecuteGamingFunction(string methodName, ulong input)
    {
        uint? methodId = BmofReader.GetGamingMethodId(methodName);
        if (methodId == null)
        {
            AppLogger.Warn($"[LinuxAcpiCall] Unknown or undiscovered AcerGamingFunction method '{methodName}'");
            return 0;
        }

        string? basePath = GetAcpiDeviceBasePath();
        if (basePath == null)
        {
            AppLogger.Error("[LinuxAcpiCall] ACPI device path undiscovered in sysfs. Cannot execute AcerGamingFunction.");
            return 0;
        }

        if (_gamingMethodName == null)
        {
            _gamingMethodName = GetWmiMethodName(GamingFunctionGuid);
            if (_gamingMethodName == null)
            {
                AppLogger.Error($"[LinuxAcpiCall] Could not discover WMI object_id for AcerGamingFunction ({GamingFunctionGuid}) in sysfs.");
                return 0;
            }
            AppLogger.Info($"[LinuxAcpiCall] Discovered AcerGamingFunction WMI method: {_gamingMethodName}");
        }

        string fullPath = $"{basePath}.{_gamingMethodName}";
        ulong result = Evaluate(fullPath, 0, methodId.Value, input);
        AppLogger.Debug($"[LinuxAcpiCall] ExecuteGamingFunction({methodName}, 0x{input:X}) -> 0x{result:X}");
        return result;
    }

    public static ulong ExecuteGenericMethod(string methodName, ulong input)
    {
        uint? methodId = BmofReader.GetGenericMethodId(methodName);
        if (methodId == null)
        {
            AppLogger.Warn($"[LinuxAcpiCall] Unknown or undiscovered APGeAction method '{methodName}'");
            return 0;
        }

        string? basePath = GetAcpiDeviceBasePath();
        if (basePath == null)
        {
            AppLogger.Error("[LinuxAcpiCall] ACPI device path undiscovered in sysfs. Cannot execute APGeAction.");
            return 0;
        }

        if (_genericMethodName == null)
        {
            _genericMethodName = GetWmiMethodName(GenericActionGuid);
            if (_genericMethodName == null)
            {
                AppLogger.Error($"[LinuxAcpiCall] Could not discover WMI object_id for APGeAction ({GenericActionGuid}) in sysfs.");
                return 0;
            }
            AppLogger.Info($"[LinuxAcpiCall] Discovered APGeAction WMI method: {_genericMethodName}");
        }

        string fullPath = $"{basePath}.{_genericMethodName}";
        ulong result = Evaluate(fullPath, 0, methodId.Value, input);
        AppLogger.Debug($"[LinuxAcpiCall] ExecuteGenericMethod({methodName}, 0x{input:X}) -> 0x{result:X}");
        return result;
    }

    private static string? GetWmiMethodName(string guid)
    {
        const string wmiDevicesPath = "/sys/bus/wmi/devices";
        if (!Directory.Exists(wmiDevicesPath)) return null;

        foreach (var dir in Directory.GetDirectories(wmiDevicesPath))
        {
            string dirName = Path.GetFileName(dir);
            if (dirName.StartsWith(guid, StringComparison.OrdinalIgnoreCase))
            {
                string objIdFile = Path.Combine(dir, "object_id");
                if (File.Exists(objIdFile))
                {
                    try
                    {
                        string id = File.ReadAllText(objIdFile).Trim();
                        if (!string.IsNullOrEmpty(id))
                        {
                            return $"WM{id}";
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.Warn($"[LinuxAcpiCall] Failed to read {objIdFile}: {ex.Message}");
                    }
                }
            }
        }
        return null;
    }

    private static string? GetAcpiDeviceBasePath()
    {
        if (_discoveryAttempted) return _acpiBasePath;

        lock (_syncLock)
        {
            if (_discoveryAttempted) return _acpiBasePath;
            _discoveryAttempted = true;

            // 0. Use ACPI device path directly associated with the Acer BMOF table
            if (!string.IsNullOrEmpty(BmofReader.DiscoveredAcpiBasePath))
            {
                _acpiBasePath = BmofReader.DiscoveredAcpiBasePath;
                AppLogger.Info($"[LinuxAcpiCall] Using ACPI device path from BMOF reader: {_acpiBasePath}");
                return _acpiBasePath;
            }

            // 1. Discover via /sys/bus/wmi/devices/*/firmware_node/path
            const string wmiPath = "/sys/bus/wmi/devices";
            if (Directory.Exists(wmiPath))
            {
                foreach (var dir in Directory.GetDirectories(wmiPath))
                {
                    string fnPath = Path.Combine(dir, "firmware_node", "path");
                    if (File.Exists(fnPath))
                    {
                        try
                        {
                            string path = File.ReadAllText(fnPath).Trim();
                            if (!string.IsNullOrEmpty(path))
                            {
                                _acpiBasePath = path;
                                AppLogger.Info($"[LinuxAcpiCall] Discovered ACPI device path from WMI sysfs: {path}");
                                return _acpiBasePath;
                            }
                        }
                        catch { }
                    }
                }
            }

            // 2. Discover via /sys/bus/acpi/devices/PNP0C14:*/path
            const string acpiPath = "/sys/bus/acpi/devices";
            if (Directory.Exists(acpiPath))
            {
                foreach (var dir in Directory.GetDirectories(acpiPath, "PNP0C14*"))
                {
                    string pFile = Path.Combine(dir, "path");
                    if (File.Exists(pFile))
                    {
                        try
                        {
                            string path = File.ReadAllText(pFile).Trim();
                            if (!string.IsNullOrEmpty(path))
                            {
                                _acpiBasePath = path;
                                AppLogger.Info($"[LinuxAcpiCall] Discovered ACPI device path from ACPI sysfs: {path}");
                                return _acpiBasePath;
                            }
                        }
                        catch { }
                    }
                }
            }

            AppLogger.Error("[LinuxAcpiCall] Failed to locate PNP0C14 ACPI device path in sysfs (/sys/bus/wmi or /sys/bus/acpi).");
            return null;
        }
    }

    public static ulong Evaluate(string acpiPath, uint instance, uint methodId, ulong input)
    {
        lock (_syncLock)
        {
            return EvaluateDirect(acpiPath, instance, methodId, input);
        }
    }

    private static ulong EvaluateDirect(string acpiPath, uint instance, uint methodId, ulong input)
    {
        if (!File.Exists(AcpiCallPath))
        {
            return 0;
        }

        try
        {
            byte[] inputBytes = BitConverter.GetBytes(input);
            string bufferArg = "b" + Convert.ToHexString(inputBytes).ToLowerInvariant();
            string cmd = $"{acpiPath} {instance} {methodId} {bufferArg}";
            File.WriteAllText(AcpiCallPath, cmd);

            string raw = File.ReadAllText(AcpiCallPath).Trim().Trim('\0');
            if (string.IsNullOrEmpty(raw))
            {
                return 0;
            }

            if (raw.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            {
                AppLogger.Warn($"[LinuxAcpiCall] ACPI error for '{cmd}': {raw}");
                return 0;
            }

            // Case 1: ACPI buffer format: "{0x01, 0x28, 0x00, 0x00, ...}"
            if (raw.StartsWith("{") && raw.EndsWith("}"))
            {
                string inner = raw[1..^1].Trim();
                if (string.IsNullOrEmpty(inner)) return 0;

                string[] parts = inner.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                ulong val = 0;
                for (int i = 0; i < Math.Min(parts.Length, 8); i++)
                {
                    string p = parts[i];
                    if (p.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) p = p[2..];
                    if (byte.TryParse(p, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                    {
                        val |= ((ulong)b) << (i * 8);
                    }
                }
                return val;
            }

            // Case 2: Hex integer: "0x1234"
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hexVal))
                {
                    return hexVal;
                }
            }

            // Case 3: Decimal integer: "1234"
            if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong decVal))
            {
                return decVal;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn($"[LinuxAcpiCall] Evaluation exception ({acpiPath}): {ex.Message}");
        }

        return 0;
    }
}

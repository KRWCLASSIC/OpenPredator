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

    private static string? _acpiBasePath = null;
    private static bool _discoveryAttempted = false;
    private static readonly object _syncLock = new();

    public static bool IsAvailable => File.Exists(AcpiCallPath);

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

        string fullPath = $"{basePath}.WMBC";
        return Evaluate(fullPath, 0, methodId.Value, input);
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

        string fullPath = $"{basePath}.WMBA";
        return Evaluate(fullPath, 0, methodId.Value, input);
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
            string cmd = $"{acpiPath} {instance} {methodId} 0x{input:X}";
            File.WriteAllText(AcpiCallPath, cmd);

            string raw = File.ReadAllText(AcpiCallPath).Trim().Trim('\0');
            if (string.IsNullOrEmpty(raw) || raw.StartsWith("Error", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (raw.StartsWith("{") && raw.EndsWith("}"))
            {
                raw = raw[1..^1].Trim();
            }

            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                if (ulong.TryParse(raw[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong hexVal))
                {
                    return hexVal;
                }
            }

            if (ulong.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong decVal))
            {
                return decVal;
            }
        }
        catch (Exception ex)
        {
            AppLogger.Debug($"[LinuxAcpiCall] Evaluation error ({acpiPath}): {ex.Message}");
        }

        return 0;
    }
}

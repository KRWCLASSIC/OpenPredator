using System;
using System.Globalization;
using System.IO;
using OpenPredator.Core.Services;

namespace OpenPredator.Core.Backends.Linux;

/// <summary>
/// Direct Linux ACPI transport for Acer laptops.
/// Evaluates WMI ACPI methods directly via /proc/acpi/call using method IDs dynamically resolved by BmofReader.
/// </summary>
public static class LinuxAcpiCall
{
    private const string AcpiCallPath = "/proc/acpi/call";

    private static readonly string[] GamingCandidates =
    {
        @"\_SB.WMID.WMBC",
        @"\_SB.AMW0.WMBC",
        @"\_SB.PCI0.LPCB.EC0.WMBC",
        @"\WMBC"
    };

    private static readonly string[] GenericCandidates =
    {
        @"\_SB.WMID.WMBA",
        @"\_SB.AMW0.WMBA",
        @"\_SB.PCI0.LPCB.EC0.WMBA",
        @"\WMBA"
    };

    private static string? _resolvedGamingPath = null;
    private static string? _resolvedGenericPath = null;
    private static readonly object _syncLock = new();

    public static bool IsAvailable => File.Exists(AcpiCallPath);

    public static ulong ExecuteGamingFunction(string methodName, ulong input)
    {
        uint? methodId = BmofReader.GetGamingMethodId(methodName);
        if (methodId == null)
        {
            AppLogger.Warning($"[LinuxAcpiCall] Unknown or undiscovered AcerGamingFunction method '{methodName}'");
            return 0;
        }

        string path = ResolvePath(ref _resolvedGamingPath, GamingCandidates, 1, 0x0101);
        return Evaluate(path, 0, methodId.Value, input);
    }

    public static ulong ExecuteGenericMethod(string methodName, ulong input)
    {
        uint? methodId = BmofReader.GetGenericMethodId(methodName);
        if (methodId == null)
        {
            AppLogger.Warning($"[LinuxAcpiCall] Unknown or undiscovered APGeAction method '{methodName}'");
            return 0;
        }

        string path = ResolvePath(ref _resolvedGenericPath, GenericCandidates, 1, 0x0207);
        return Evaluate(path, 0, methodId.Value, input);
    }

    private static string ResolvePath(ref string? cachedPath, string[] candidates, uint probeMethodId, ulong probeInput)
    {
        if (cachedPath != null) return cachedPath;

        lock (_syncLock)
        {
            if (cachedPath != null) return cachedPath;

            if (IsAvailable)
            {
                foreach (var candidate in candidates)
                {
                    try
                    {
                        ulong testVal = EvaluateDirect(candidate, 0, probeMethodId, probeInput);
                        if (testVal > 0)
                        {
                            cachedPath = candidate;
                            AppLogger.Info($"[LinuxAcpiCall] Detected active ACPI path: {candidate}");
                            return candidate;
                        }
                    }
                    catch { }
                }
            }

            // Fallback to default Acer System Bus path
            cachedPath = candidates[0];
            return cachedPath;
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

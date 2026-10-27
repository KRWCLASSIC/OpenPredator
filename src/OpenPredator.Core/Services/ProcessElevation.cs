using System;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace OpenPredator.Core.Services;

/// <summary>
/// Cross-platform process privilege and elevation detector.
/// Identifies Administrator role on Windows and root/euid 0 on Linux.
/// </summary>
public static class ProcessElevation
{
    private static bool? _isElevatedCached = null;

    public static bool IsElevated
    {
        get
        {
            if (_isElevatedCached.HasValue) return _isElevatedCached.Value;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    using var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);
                    _isElevatedCached = principal.IsInRole(WindowsBuiltInRole.Administrator);
                    return _isElevatedCached.Value;
                }
                catch
                {
                    _isElevatedCached = false;
                    return false;
                }
            }

            if (OperatingSystem.IsLinux())
            {
                try
                {
                    _isElevatedCached = geteuid() == 0;
                    return _isElevatedCached.Value;
                }
                catch
                {
                    _isElevatedCached = Environment.UserName == "root";
                    return _isElevatedCached.Value;
                }
            }

            _isElevatedCached = false;
            return false;
        }
    }

    [DllImport("libc", EntryPoint = "geteuid")]
    private static extern uint geteuid();
}

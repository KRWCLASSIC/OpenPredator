using System;
using System.Runtime.Versioning;
using System.Threading.Tasks;

namespace OpenPredator.Core.Backends.Windows;

/// <summary>
/// Windows hardware backend.
/// Inherits full Acer gaming control logic from AcerHardwareBackend, dispatching directly
/// to COM WMI (root\wmi) via NativeWmi.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsHardwareBackend : AcerHardwareBackend
{
    public override string BackendName => "Windows WMI (root\\wmi -> AcerGamingFunction)";
    public override bool IsSupported => OperatingSystem.IsWindows();

    public override ulong InvokeGamingFunctionSync(string methodName, ulong input) =>
        NativeWmi.ExecuteGamingFunction(methodName, input);

    public override ulong InvokeGenericMethodSync(string methodName, ulong input) =>
        NativeWmi.ExecuteGenericMethod(methodName, input);
}

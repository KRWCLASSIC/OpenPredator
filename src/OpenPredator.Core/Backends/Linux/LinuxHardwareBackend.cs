using System;

namespace OpenPredator.Core.Backends.Linux;

/// <summary>
/// Linux hardware backend.
/// Inherits full Acer gaming control logic from AcerHardwareBackend, dispatching directly
/// to firmware via /proc/acpi/call (LinuxAcpiCall).
/// </summary>
public class LinuxHardwareBackend : AcerHardwareBackend
{
    public override string BackendName => "Linux ACPI / Direct WMI (/proc/acpi/call)";
    public override bool IsSupported => OperatingSystem.IsLinux() && LinuxAcpiCall.IsAvailable;

    public override ulong InvokeGamingFunctionSync(string methodName, ulong input) =>
        LinuxAcpiCall.ExecuteGamingFunction(methodName, input);

    public override ulong InvokeGenericMethodSync(string methodName, ulong input) =>
        LinuxAcpiCall.ExecuteGenericMethod(methodName, input);
}

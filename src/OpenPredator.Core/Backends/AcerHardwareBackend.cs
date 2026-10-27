using System;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;

namespace OpenPredator.Core.Backends;

/// <summary>
/// Universal shared hardware control backend for Acer Nitro and Predator laptops.
/// Implements full feature parity across Windows and Linux using clear WMI/ACPI method names.
/// Platform-specific subclasses (WindowsHardwareBackend, LinuxHardwareBackend) only need to implement raw method dispatch.
/// Domain-specific implementations are modularized under OpenPredator.Core.Hardware:
/// - Telemetry.cs: Sensor monitoring (temperatures, fan RPMs)
/// - FanControl.cs: Fan speed, fan modes, and CoolBoost
/// - PowerModes.cs: Performance profiles and power modes
/// - KeyboardLighting.cs: 4-zone RGB, backlight brightness, and timeout
/// - DisplayControl.cs: LCD overdrive
/// - SystemControl.cs: Win-key lock and generic misc settings
/// </summary>
public abstract partial class AcerHardwareBackend : IHardwareBackend
{
    protected FanMode _lastMode = FanMode.Auto;

    public abstract string BackendName { get; }
    public abstract bool IsSupported { get; }

    // Platform-specific WMI/ACPI method dispatchers
    public abstract ulong InvokeGamingFunctionSync(string methodName, ulong input);
    public abstract ulong InvokeGenericMethodSync(string methodName, ulong input);

    public virtual Task<ulong> InvokeGamingFunctionAsync(string methodName, ulong input) =>
        Task.Run(() => InvokeGamingFunctionSync(methodName, input));

    public virtual Task<ulong> InvokeGenericMethodAsync(string methodName, ulong input) =>
        Task.Run(() => InvokeGenericMethodSync(methodName, input));
}

using OpenPredator.Core.Enums;

namespace OpenPredator.Gui;

internal sealed record GuiAppSettings
{
    public bool AutostartWithWindows { get; init; } = false;
    public bool AutostartSystemWide { get; init; } = false;
    public bool MinimizeToTray { get; init; } = true;
    public bool StartMinimized { get; init; } = false;
    public int TelemetryIntervalIndex { get; init; } = 1; // 0=0.5s, 1=1s, 2=2s, 3=3s, 4=5s

    // Fan & Hardware Settings (OpenSense State)
    public FanMode CpuFanMode { get; init; } = FanMode.Auto;
    public FanMode GpuFanMode { get; init; } = FanMode.Auto;
    public bool FansLocked { get; init; } = false;
    public int CpuDuty { get; init; } = 50;
    public int GpuDuty { get; init; } = 50;
    public bool CoolBoost { get; init; } = false;
    public PowerMode PowerMode { get; init; } = PowerMode.Default;
    public int KeyboardBrightness { get; init; } = 100;
    public bool KeyboardTimeout { get; init; } = true;
    public bool WinKeyLock { get; init; } = false;
    public bool LcdOverdrive { get; init; } = false;

    // LDS Settings
    public bool PersistLdsMode { get; init; } = false;
    public string ModelName { get; init; } = "Acer Gaming Laptop";
    public bool IsPredator { get; init; } = false;
    public bool IsRgbKeyboard { get; init; } = false;
    public bool SupportsLcdOverdrive { get; init; } = false;
    public bool SupportsDiscreteGpuSwitch { get; init; } = false;
    public bool SupportsCpuOverclock { get; init; } = false;
    public bool SupportsGpuOverclock { get; init; } = false;
}

internal sealed record GuiLdsConfig
{
    public bool PersistLdsMode { get; init; } = false;
    public string ModelName { get; init; } = "Acer Gaming Laptop";
    public bool IsPredator { get; init; } = false;
    public bool IsRgbKeyboard { get; init; } = false;
    public bool SupportsLcdOverdrive { get; init; } = false;
    public bool SupportsDiscreteGpuSwitch { get; init; } = false;
    public bool SupportsCpuOverclock { get; init; } = false;
    public bool SupportsGpuOverclock { get; init; } = false;
}

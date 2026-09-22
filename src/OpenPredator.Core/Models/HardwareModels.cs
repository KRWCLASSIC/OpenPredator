namespace OpenPredator.Core.Models;

public sealed record SensorData
{
    public int CpuTemperature { get; init; }
    public int GpuTemperature { get; init; }
    public int SystemTemperature { get; init; }
    
    public int CpuFanRpm { get; init; }
    public int GpuFanRpm { get; init; }
    public int SystemFanRpm { get; init; }

    public int GpuCount { get; init; }
    public int GpuUsagePercent { get; init; }
    public int GpuFrequencyMhz { get; init; }

    public override string ToString() =>
        $"CPU: {CpuTemperature}°C ({CpuFanRpm} RPM) | GPU: {GpuTemperature}°C ({GpuFanRpm} RPM, {GpuUsagePercent}% load, {GpuFrequencyMhz} MHz)";
}

public sealed record DeviceCapabilities
{
    public string ModelName { get; init; } = "Acer Gaming Laptop";
    public bool IsPredator { get; init; } = false;
    public bool IsRgbKeyboard { get; init; } = false;
    public bool SupportsLcdOverdrive { get; init; } = false;
    public bool SupportsDiscreteGpuSwitch { get; init; } = false;
    public bool SupportsCpuOverclock { get; init; } = false;
    public bool SupportsGpuOverclock { get; init; } = false;
}

public sealed record FanConfig
{
    public Enums.FanMode Mode { get; init; } = Enums.FanMode.Auto;
    public int CpuPercentage { get; init; } = 50;
    public int GpuPercentage { get; init; } = 50;
    public bool CpuCustomAuto { get; init; } = true;
    public bool GpuCustomAuto { get; init; } = true;
    public bool CoolBoost { get; init; } = false;
}

public sealed record RgbZoneColor
{
    public byte R { get; init; } = 255;
    public byte G { get; init; } = 0;
    public byte B { get; init; } = 0;

    public RgbZoneColor() { }
    public RgbZoneColor(byte r, byte g, byte b) { R = r; G = g; B = b; }

    public static RgbZoneColor FromHex(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return new RgbZoneColor(255, 0, 0);
        hex = hex.TrimStart('#');
        if (hex.Length == 6)
        {
            byte r = Convert.ToByte(hex.Substring(0, 2), 16);
            byte g = Convert.ToByte(hex.Substring(2, 2), 16);
            byte b = Convert.ToByte(hex.Substring(4, 2), 16);
            return new RgbZoneColor(r, g, b);
        }
        return new RgbZoneColor(255, 0, 0);
    }

    public string ToHex() => $"#{R:X2}{G:X2}{B:X2}";
}

public sealed record RgbConfig
{
    public Enums.RgbEffectType Effect { get; init; } = Enums.RgbEffectType.Static;
    public int Brightness { get; init; } = 5; // 0 - 5
    public int Speed { get; init; } = 5;      // 1 - 5
    public int Direction { get; init; } = 0;
    
    public RgbZoneColor Zone1 { get; init; } = new(255, 0, 0);
    public RgbZoneColor Zone2 { get; init; } = new(255, 0, 0);
    public RgbZoneColor Zone3 { get; init; } = new(255, 0, 0);
    public RgbZoneColor Zone4 { get; init; } = new(255, 0, 0);
}

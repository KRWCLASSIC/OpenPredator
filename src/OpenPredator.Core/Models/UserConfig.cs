using System;
using System.Text.Json.Serialization;
using OpenPredator.Core.Enums;

namespace OpenPredator.Core.Models;

public sealed record UserConfig
{
    public FanConfig Fan { get; init; } = new();
    public PowerMode PowerMode { get; init; } = PowerMode.Default;
    public int KeyboardBrightness { get; init; } = 100;
    public bool KeyboardTimeout { get; init; } = true;
    public bool LcdOverdrive { get; init; } = false;
    public bool WinKeyLock { get; init; } = false;
    public RgbConfig Rgb { get; init; } = new();
}

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(UserConfig))]
[JsonSerializable(typeof(FanConfig))]
[JsonSerializable(typeof(RgbConfig))]
[JsonSerializable(typeof(RgbZoneColor))]
[JsonSerializable(typeof(FanMode))]
[JsonSerializable(typeof(PowerMode))]
[JsonSerializable(typeof(RgbEffectType))]
public partial class OpenPredatorJsonContext : JsonSerializerContext
{
}

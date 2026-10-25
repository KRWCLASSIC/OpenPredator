using System.Text.Json.Serialization;
using OpenPredator.Core.Enums;

namespace OpenPredator.Gui;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(GuiAppSettings))]
[JsonSerializable(typeof(GuiLdsConfig))]
[JsonSerializable(typeof(FanMode))]
[JsonSerializable(typeof(PowerMode))]
internal partial class GuiJsonContext : JsonSerializerContext
{
}

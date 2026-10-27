using System;
using System.Buffers.Binary;
using OpenPredator.Core.Backends;
using OpenPredator.Core.Enums;

namespace OpenPredator.Client;

/// <summary>
/// Hardware backend proxy that routes all ACPI/WMI method queries through the elevated named pipe service.
/// Provides unelevated client tools (CLI, GUI) with unprivileged access to verified motherboard telemetry.
/// 100% wire-compatible with OpenPredator.Service and OEM Acer PSSvc.
/// </summary>
public sealed class ClientHardwareBackend : AcerHardwareBackend
{
    private readonly IOpenPredatorClient _client;

    public override string BackendName => "OpenPredator Named Pipe (Elevated IPC Proxy)";
    public override bool IsSupported => _client.IsConnected;

    public ClientHardwareBackend(IOpenPredatorClient client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
    }

    public override ulong InvokeGamingFunctionSync(string methodName, ulong input)
    {
        ServiceCommand cmd = methodName switch
        {
            "GetGamingSysInfo" => ServiceCommand.GetGamingSysInfoFunction,
            "GetGamingMiscSetting" => ServiceCommand.WmiGetGamingMiscSetting,
            "SetGamingMiscSetting" => ServiceCommand.WmiSetGamingMiscSetting,
            "GetGamingProfile" => ServiceCommand.GamingProfileWmiGetFunction,
            "SetGamingProfile" => ServiceCommand.GamingProfileWmiSetFunction,
            "SetGamingKBBacklight" => ServiceCommand.WmiSetGamingKbBacklight,
            "SetGamingRgbKb" => ServiceCommand.WmiSetGamingRgbKbSetting,
            "SetGamingLEDColor" => ServiceCommand.GamingLedGroupColorSetFunction,
            "SetGamingLEDBehavior" => ServiceCommand.WmiSetGamingLedBehavior,
            _ => (ServiceCommand)(-1)
        };

        if ((short)cmd == -1) return 0UL;

        if (_client is OpenPredatorClient opClient)
        {
            var res = opClient.SendCommandAsync(cmd, new object[] { input > uint.MaxValue ? input : (uint)input }).GetAwaiter().GetResult();
            if (res.Count > 0 && res[0].Length >= 4)
            {
                return res[0].Length >= 8
                    ? BinaryPrimitives.ReadUInt64LittleEndian(res[0])
                    : BinaryPrimitives.ReadUInt32LittleEndian(res[0]);
            }
        }

        return 0UL;
    }

    public override ulong InvokeGenericMethodSync(string methodName, ulong input)
    {
        ServiceCommand cmd = methodName switch
        {
            "GetFunction" => ServiceCommand.WmiGetFunction,
            "SetFunction" => ServiceCommand.WmiSetFunction,
            _ => (ServiceCommand)(-1)
        };

        if ((short)cmd == -1) return 0UL;

        if (_client is OpenPredatorClient opClient)
        {
            var res = opClient.SendCommandAsync(cmd, new object[] { input > uint.MaxValue ? input : (uint)input }).GetAwaiter().GetResult();
            if (res.Count > 0 && res[0].Length >= 4)
            {
                return res[0].Length >= 8
                    ? BinaryPrimitives.ReadUInt64LittleEndian(res[0])
                    : BinaryPrimitives.ReadUInt32LittleEndian(res[0]);
            }
        }

        return 0UL;
    }
}

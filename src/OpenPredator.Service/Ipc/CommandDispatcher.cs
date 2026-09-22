using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Protocol;
using OpenPredator.Core.Services;

namespace OpenPredator.Service.Ipc;

public class CommandDispatcher
{
    private readonly HardwareManager _hardware = HardwareManager.Instance;

    public async Task<byte[]> DispatchAsync(ServiceCommand command, List<byte[]> args)
    {
        switch (command)
        {
            case ServiceCommand.EchoArguments:
                return PacketSerializer.SerializeResponse((args.Count > 0 ? args[0] : Array.Empty<byte>()));

            case ServiceCommand.GetGamingSysInfoFunction:
                // Category & Index requested in arg[0]
                uint sysInfoQuery = args.Count > 0 && args[0].Length >= 4
                    ? BinaryPrimitives.ReadUInt32LittleEndian(args[0])
                    : 0;

                ulong sysInfoResult = await HandleSysInfoQueryAsync(sysInfoQuery);
                return PacketSerializer.SerializeResponse(sysInfoResult);

            case ServiceCommand.GetGpuUsageLoading:
                // Returns 2 fields: GPU1 load %, GPU2 load %
                var sensors = await _hardware.GetSensorsAsync();
                return PacketSerializer.SerializeResponse((ulong)sensors.GpuUsagePercent, 0UL);

            case ServiceCommand.GpuGetCount:
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.GpuGetFrequency:
                var gpuSensors = await _hardware.GetSensorsAsync();
                return PacketSerializer.SerializeResponse(gpuSensors.GpuFrequencyMhz > 0 ? gpuSensors.GpuFrequencyMhz : 367);

            case ServiceCommand.GamingFanGroupBehaviorSetFunction:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    ulong fanBehavior = args[0].Length >= 8
                        ? BinaryPrimitives.ReadUInt64LittleEndian(args[0])
                        : BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    await HandleFanBehaviorAsync(fanBehavior);
                }
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.GamingFanGroupSpeedSetFunction:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    ulong fanSpeedData = args[0].Length >= 8
                        ? BinaryPrimitives.ReadUInt64LittleEndian(args[0])
                        : BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    int fanIndex = (int)(fanSpeedData & 0xFF);
                    int percentage = (int)((fanSpeedData >> 8) & 0xFF);
                    await _hardware.Backend.SetFanSpeedAsync(fanIndex, percentage);
                    if (fanIndex == 1)
                    {
                        _ = _hardware.ApplyFanConfigAsync(_hardware.CurrentFanConfig with { CpuPercentage = percentage, CpuCustomAuto = false });
                    }
                    else if (fanIndex == 2 || fanIndex == 4)
                    {
                        _ = _hardware.ApplyFanConfigAsync(_hardware.CurrentFanConfig with { GpuPercentage = percentage, GpuCustomAuto = false });
                    }
                }
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.SetOperationMode:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    uint modeVal = BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    var powerMode = (PowerMode)Math.Clamp(modeVal, 0, 3);
                    await _hardware.SetPowerModeAsync(powerMode);
                }
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.GamingProfileWmiSetFunction:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    ulong profInput = args[0].Length >= 8
                        ? BinaryPrimitives.ReadUInt64LittleEndian(args[0])
                        : BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    ulong res = await _hardware.Backend.InvokeGamingFunctionAsync("SetGamingProfile", profInput);
                    return PacketSerializer.SerializeResponse(res);
                }
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.GamingProfileWmiGetFunction:
                uint profQuery = args.Count > 0 && args[0].Length >= 4
                    ? BinaryPrimitives.ReadUInt32LittleEndian(args[0])
                    : 0;
                ulong profRes = await _hardware.Backend.InvokeGamingFunctionAsync("GetGamingProfile", profQuery);
                return PacketSerializer.SerializeResponse(profRes);

            case ServiceCommand.WmiSetGamingRgbKbSetting:
            case ServiceCommand.GamingLedGroupColorSetFunction:
                // Handle 4-Zone RGB Setting
                if (args.Count > 0)
                {
                    await HandleRgbSettingAsync(args[0]);
                }
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.WmiSetGamingLedBehavior:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    ulong effectInput = args[0].Length >= 8
                        ? BinaryPrimitives.ReadUInt64LittleEndian(args[0])
                        : BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    await _hardware.Backend.InvokeGamingFunctionAsync("SetGamingLEDBehavior", effectInput);
                }
                return PacketSerializer.SerializeResponse(1);

            case ServiceCommand.WmiSetGamingKbBacklight:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    uint brightnessRaw = BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    int brightness = (int)(brightnessRaw > 255 ? ((brightnessRaw >> 16) & 0xFF) : (brightnessRaw & 0xFF));
                    await _hardware.Backend.SetKbBacklightAsync(brightness);
                }
                return PacketSerializer.SerializeResponse(0);

            case ServiceCommand.WmiSetGamingMiscSetting:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    uint settingVal = BinaryPrimitives.ReadUInt32LittleEndian(args[0]);
                    uint settingId = settingVal & 0xFF;
                    uint value = (settingVal >> 8) & 0xFF;
                    await _hardware.Backend.SetMiscSettingAsync(settingId, value);
                }
                return PacketSerializer.SerializeResponse(0);

            case ServiceCommand.WmiGetGamingMiscSetting:
                uint querySettingId = args.Count > 0 && args[0].Length >= 4
                    ? BinaryPrimitives.ReadUInt32LittleEndian(args[0])
                    : 0;
                ulong miscVal = await _hardware.Backend.GetMiscSettingAsync(querySettingId);
                return PacketSerializer.SerializeResponse(miscVal);

            case ServiceCommand.WmiSetFunction:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    ulong wmiInput = args[0].Length >= 8
                        ? BinaryPrimitives.ReadUInt64LittleEndian(args[0])
                        : BinaryPrimitives.ReadUInt32LittleEndian(args[0]);

                    // CoolBoost toggle (Function ID 7)
                    if ((wmiInput & 0xFFFF) == 7)
                    {
                        bool cb = ((wmiInput >> 16) & 1) == 1;
                        await _hardware.SetCoolBoostAsync(cb);
                        return PacketSerializer.SerializeResponse(0);
                    }

                    // Keyboard Backlight (Function ID 2)
                    if ((wmiInput & 0xFF) == 2)
                    {
                        int brightness = (int)((wmiInput >> 32) & 0xFF);
                        await _hardware.SetKbBacklightAsync(brightness);
                    }

                    ulong res = await _hardware.Backend.InvokeGenericMethodAsync("SetFunction", wmiInput);
                    return PacketSerializer.SerializeResponse(res);
                }
                return PacketSerializer.SerializeResponse(0);

            case ServiceCommand.WmiGetFunction:
                if (args.Count > 0 && args[0].Length >= 4)
                {
                    uint wmiInput = BinaryPrimitives.ReadUInt32LittleEndian(args[0]);

                    // CoolBoost query (0x0207)
                    if (wmiInput == 0x0207)
                    {
                        bool cb = await _hardware.GetCoolBoostAsync();
                        ulong cbWire = cb ? 0x0100UL : 0x0000UL;
                        return PacketSerializer.SerializeResponse(cbWire);
                    }

                    // Keyboard Backlight query (Function ID 1)
                    if ((wmiInput & 0xFF) == 1)
                    {
                        int bk = await _hardware.GetKbBacklightAsync();
                        bool timeout = await _hardware.GetKbTimeoutAsync();
                        ulong timeoutByte = timeout ? 30UL : 0UL;
                        ulong bkWire = 0UL | (132UL << 8) | (8UL << 16) | ((ulong)bk << 32) | (timeoutByte << 40);
                        return PacketSerializer.SerializeResponse(bkWire);
                    }

                    ulong res = await _hardware.Backend.InvokeGenericMethodAsync("GetFunction", wmiInput);
                    return PacketSerializer.SerializeResponse(res);
                }
                return PacketSerializer.SerializeResponse(0UL);

            default:
                // Default fallback response
                return PacketSerializer.SerializeResponse(1);
        }
    }

    private async Task<ulong> HandleSysInfoQueryAsync(uint query)
    {
        uint category = (query >> 8) & 0xFF;
        uint index = query & 0xFF;

        var sensors = await _hardware.GetSensorsAsync();

        // Direct cached responses from modern high-speed telemetry
        if (category == 1 && index == 1) return (ulong)(sensors.CpuTemperature << 8);
        if (category == 2 && index == 1) return (ulong)(sensors.CpuFanRpm << 8);
        if (category == 10 && index == 1) return (ulong)(sensors.GpuTemperature << 8);
        if (category == 3 && index == 1) return (ulong)(sensors.SystemTemperature << 8);
        if (category == 6 && index == 1) return (ulong)(sensors.GpuFanRpm << 8);
        if (category == 5 && index == 1) return (ulong)(sensors.SystemFanRpm << 8);

        // Raw WMI Fallback if running on Windows
        return await _hardware.Backend.InvokeGamingFunctionAsync("GetGamingSysInfo", query);
    }

    private async Task HandleFanBehaviorAsync(ulong behavior)
    {
        // Check CoolBoost (bit 4 or (enabled << 8) | 4)
        if ((behavior & 0xFF) == 4)
        {
            bool coolBoost = ((behavior >> 8) & 1) == 1;
            await _hardware.SetCoolBoostAsync(coolBoost);
            return;
        }

        // Direct pass-through to WMI/ACPI backend
        await _hardware.Backend.InvokeGamingFunctionAsync("SetGamingFanBehavior", behavior);

        // Decode per-fan modes:
        // CPU mode (bits 16-17): 1 = Auto, 2 = Max, 3 = Custom
        // GPU mode (bits 22-23): 1 = Auto, 2 = Max, 3 = Custom
        uint cpuModeVal = (uint)((behavior >> 16) & 0x3);
        uint gpuModeVal = (uint)((behavior >> 22) & 0x3);

        FanMode cpuMode = cpuModeVal switch { 1 => FanMode.Auto, 2 => FanMode.Max, 3 => FanMode.Custom, _ => FanMode.Auto };
        FanMode gpuMode = gpuModeVal switch { 1 => FanMode.Auto, 2 => FanMode.Max, 3 => FanMode.Custom, _ => FanMode.Auto };

        FanMode overallMode = (cpuMode == gpuMode) ? cpuMode : FanMode.Custom;

        _hardware.CurrentFanConfig = _hardware.CurrentFanConfig with
        {
            Mode = overallMode,
            CpuCustomAuto = (cpuMode == FanMode.Auto),
            GpuCustomAuto = (gpuMode == FanMode.Auto)
        };
    }

    private async Task HandleRgbSettingAsync(byte[] data)
    {
        // Expected RGB payload: can be raw zone color or effect configuration
        if (data.Length >= 4)
        {
            uint val = BinaryPrimitives.ReadUInt32LittleEndian(data);
            byte zoneId = (byte)(val & 0xFF);
            byte r = (byte)((val >> 8) & 0xFF);
            byte g = (byte)((val >> 16) & 0xFF);
            byte b = (byte)((val >> 24) & 0xFF);

            var currentColor = _hardware.CurrentRgbConfig;
            var newZone = new RgbZoneColor(r, g, b);

            var updatedConfig = zoneId switch
            {
                1 => currentColor with { Zone1 = newZone },
                2 => currentColor with { Zone2 = newZone },
                3 => currentColor with { Zone3 = newZone },
                4 => currentColor with { Zone4 = newZone },
                _ => currentColor
            };

            await _hardware.ApplyRgbConfigAsync(updatedConfig);
        }
    }
}

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Protocol;
using OpenPredator.Core.Services;

namespace OpenPredator.Client;

public interface IOpenPredatorClient : IDisposable
{
    bool IsConnected { get; }
    Task<bool> ConnectAsync(int timeoutMs = 2000, CancellationToken ct = default);
    Task<SensorData> GetSensorsAsync(CancellationToken ct = default);
    Task<bool> SetFanModeAsync(FanMode mode, bool coolBoost = false, int cpuPercentage = 50, int gpuPercentage = 50, bool cpuAuto = true, bool gpuAuto = true, FanMode? cpuMode = null, FanMode? gpuMode = null, CancellationToken ct = default);
    Task<bool> SetCoolBoostAsync(bool enabled, CancellationToken ct = default);
    Task<bool> GetCoolBoostAsync(CancellationToken ct = default);
    Task<bool> SetFanSpeedAsync(int fanIndex, int percentage, CancellationToken ct = default);
    Task<bool> SetPowerModeAsync(PowerMode mode, CancellationToken ct = default);
    Task<bool> SetRgbKeyboardAsync(RgbConfig config, CancellationToken ct = default);
    Task<bool> SetKbBacklightAsync(int brightnessPercentage, CancellationToken ct = default);
    Task<int> GetKbBacklightAsync(CancellationToken ct = default);
    Task<bool> SetKbTimeoutAsync(bool enabled, CancellationToken ct = default);
    Task<bool> GetKbTimeoutAsync(CancellationToken ct = default);
    Task<PowerMode> GetPowerModeAsync(CancellationToken ct = default);
    Task<bool> SetLcdOverdriveAsync(bool enabled, CancellationToken ct = default);
    Task<bool> GetLcdOverdriveAsync(CancellationToken ct = default);
    Task<bool> SetWinKeyLockAsync(bool locked, CancellationToken ct = default);
}

public class OpenPredatorClient : IOpenPredatorClient
{
    private const string PipeName = "predatorsense_service_namedpipe";
    private const string LinuxSocketPath = "/tmp/predatorsense_service_namedpipe";

    private Stream? _stream;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsConnected => _stream != null;

    public async Task<bool> ConnectAsync(int timeoutMs = 2000, CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct);
        try
        {
            if (_stream != null) return true;

            if (OperatingSystem.IsWindows())
            {
                var pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                await pipe.ConnectAsync(timeoutMs, ct);
                _stream = pipe;
                return true;
            }
            else
            {
                var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                var endpoint = new UnixDomainSocketEndPoint(LinuxSocketPath);
                await socket.ConnectAsync(endpoint, ct);
                _stream = new NetworkStream(socket, ownsSocket: true);
                return true;
            }
        }
        catch
        {
            _stream = null;
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<SensorData> GetSensorsAsync(CancellationToken ct = default)
    {
        int cpuTemp = 0, cpuRpm = 0, gpuTemp = 0, gpuRpm = 0, sysTemp = 0;
        int gpuLoad = 0, gpuFreq = 0, gpuCount = 1;

        try
        {
            // Category 1, Index 1 (CPU Temp -> 0x0101)
            var r1 = await SendCommandAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0101u }, ct);
            if (r1.Count > 0 && r1[0].Length >= 8) cpuTemp = (int)((BinaryPrimitives.ReadUInt64LittleEndian(r1[0]) >> 8) & 0xFF);

            // Category 2, Index 1 (CPU RPM -> 0x0201)
            var r2 = await SendCommandAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0201u }, ct);
            if (r2.Count > 0 && r2[0].Length >= 8) cpuRpm = (int)((BinaryPrimitives.ReadUInt64LittleEndian(r2[0]) >> 8) & 0xFFFF);

            // Category 10, Index 1 (GPU Temp -> 0x0A01 on modern Acer laptops)
            int parsedGpuTemp = 0;
            var rGpu = await SendCommandAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0A01u }, ct);
            if (rGpu.Count > 0 && rGpu[0].Length >= 8) parsedGpuTemp = (int)((BinaryPrimitives.ReadUInt64LittleEndian(rGpu[0]) >> 8) & 0xFF);

            // Category 3, Index 1 (System / VRM Temp -> 0x0301)
            var r3 = await SendCommandAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0301u }, ct);
            if (r3.Count > 0 && r3[0].Length >= 8) sysTemp = (int)((BinaryPrimitives.ReadUInt64LittleEndian(r3[0]) >> 8) & 0xFF);

            gpuTemp = parsedGpuTemp > 0 ? parsedGpuTemp : sysTemp;

            // Category 6, Index 1 (GPU RPM -> 0x0601)
            var r4 = await SendCommandAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0601u }, ct);
            if (r4.Count > 0 && r4[0].Length >= 8) gpuRpm = (int)((BinaryPrimitives.ReadUInt64LittleEndian(r4[0]) >> 8) & 0xFFFF);

            // GPU Usage Loading
            var rGpuLoad = await SendCommandAsync(ServiceCommand.GetGpuUsageLoading, new object[] { 0u }, ct);
            if (rGpuLoad.Count > 0 && rGpuLoad[0].Length >= 8) gpuLoad = (int)(BinaryPrimitives.ReadUInt64LittleEndian(rGpuLoad[0]) & 0xFF);

            // GPU Frequency
            var rGpuFreq = await SendCommandAsync(ServiceCommand.GpuGetFrequency, new object[] { 0u }, ct);
            if (rGpuFreq.Count > 0 && rGpuFreq[0].Length >= 4) gpuFreq = BinaryPrimitives.ReadInt32LittleEndian(rGpuFreq[0]);
        }
        catch { }

        // Direct NVML overlay for high-frequency interactive metrics
        if (Core.Backends.NvmlTelemetry.TryGetGpuMetrics(out int nvGpuTemp, out int nvGpuLoad, out int nvGpuFreq, out int nvGpuCount))
        {
            if (nvGpuTemp > 0) gpuTemp = nvGpuTemp;
            gpuLoad = nvGpuLoad;
            gpuFreq = nvGpuFreq;
            gpuCount = nvGpuCount;
        }

        return new SensorData
        {
            CpuTemperature = cpuTemp,
            CpuFanRpm = cpuRpm,
            GpuTemperature = gpuTemp,
            GpuFanRpm = gpuRpm,
            SystemTemperature = sysTemp,
            GpuCount = gpuCount,
            GpuUsagePercent = gpuLoad,
            GpuFrequencyMhz = gpuFreq
        };
    }

    public async Task<bool> SetFanModeAsync(
        FanMode mode,
        bool coolBoost = false,
        int cpuPercentage = 50,
        int gpuPercentage = 50,
        bool cpuAuto = true,
        bool gpuAuto = true,
        FanMode? cpuMode = null,
        FanMode? gpuMode = null,
        CancellationToken ct = default)
    {
        FanMode effectiveCpuMode = cpuMode ?? (mode == FanMode.Custom ? (cpuAuto ? FanMode.Auto : FanMode.Custom) : mode);
        FanMode effectiveGpuMode = gpuMode ?? (mode == FanMode.Custom ? (gpuAuto ? FanMode.Auto : FanMode.Custom) : mode);

        // 1. Set EC Fan Behavior bitmask first (enables manual/auto mode in firmware)
        uint cpuModeBits = effectiveCpuMode switch
        {
            FanMode.Auto => 1u,
            FanMode.Max => 2u,
            FanMode.Custom => 3u,
            _ => 1u
        };

        uint gpuModeBits = effectiveGpuMode switch
        {
            FanMode.Auto => 1u,
            FanMode.Max => 2u,
            FanMode.Custom => 3u,
            _ => 1u
        };

        ulong behavior = 9UL | ((ulong)cpuModeBits << 16) | ((ulong)gpuModeBits << 22);
        await SendCommandAsync(ServiceCommand.GamingFanGroupBehaviorSetFunction, new object[] { behavior }, ct);

        // 2. Apply Speeds for CPU (Fan index 1)
        if (effectiveCpuMode == FanMode.Custom)
        {
            ulong cpuSpeed = (ulong)((Math.Clamp(cpuPercentage, 0, 100) << 8) | 1);
            await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { cpuSpeed }, ct);
        }
        else if (effectiveCpuMode == FanMode.Max)
        {
            ulong cpuSpeed = (100UL << 8) | 1;
            await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { cpuSpeed }, ct);
        }

        // 3. Apply Speeds for GPU (Fan index 4 and index 2 for dual-GPU compatibility)
        if (effectiveGpuMode == FanMode.Custom)
        {
            ulong gpuSpeed4 = (ulong)((Math.Clamp(gpuPercentage, 0, 100) << 8) | 4);
            await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { gpuSpeed4 }, ct);
            ulong gpuSpeed2 = (ulong)((Math.Clamp(gpuPercentage, 0, 100) << 8) | 2);
            await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { gpuSpeed2 }, ct);
        }
        else if (effectiveGpuMode == FanMode.Max)
        {
            ulong gpuSpeed4 = (100UL << 8) | 4;
            await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { gpuSpeed4 }, ct);
            ulong gpuSpeed2 = (100UL << 8) | 2;
            await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { gpuSpeed2 }, ct);
        }

        if (coolBoost)
        {
            await SetCoolBoostAsync(true, ct);
        }

        return true;
    }

    public async Task<bool> SetCoolBoostAsync(bool enabled, CancellationToken ct = default)
    {
        // NitroSense / PSSvc: ServiceCommand.WmiSetFunction (Cmd 17) -> APGeAction.SetFunction(0x00010007 / 0x00000007)
        ulong wmiInput = ((enabled ? 1UL : 0UL) << 16) | 7UL;
        var res = await SendCommandAsync(ServiceCommand.WmiSetFunction, new object[] { wmiInput }, ct);
        return res.Count > 0;
    }

    public async Task<bool> GetCoolBoostAsync(CancellationToken ct = default)
    {
        // NitroSense / PSSvc: ServiceCommand.WmiGetFunction (Cmd 20) with 0x0207
        var res = await SendCommandAsync(ServiceCommand.WmiGetFunction, new object[] { 0x0207u }, ct);
        if (res.Count > 0 && res[0].Length >= 8)
        {
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(res[0]);
            if ((val & 0xFF) == 0) return ((val >> 8) & 0xFF) == 1;
        }
        return false;
    }

    public async Task<bool> SetFanSpeedAsync(int fanIndex, int percentage, CancellationToken ct = default)
    {
        ulong speed = (ulong)((Math.Clamp(percentage, 0, 100) << 8) | (fanIndex & 0xFF));
        var res = await SendCommandAsync(ServiceCommand.GamingFanGroupSpeedSetFunction, new object[] { speed }, ct);
        return res.Count > 0;
    }

    public async Task<bool> SetPowerModeAsync(PowerMode mode, CancellationToken ct = default)
    {
        var res = await SendCommandAsync(ServiceCommand.SetOperationMode, new object[] { (uint)mode }, ct);
        return res.Count > 0;
    }

    public async Task<bool> SetRgbKeyboardAsync(RgbConfig config, CancellationToken ct = default)
    {
        // Set brightness
        await SetKbBacklightAsync(config.Brightness * 25, ct);

        // Zone 1: wire format (B << 24) | (G << 16) | (R << 8) | zoneId
        ulong z1 = (ulong)((config.Zone1.B << 24) | (config.Zone1.G << 16) | (config.Zone1.R << 8) | 1);
        await SendCommandAsync(ServiceCommand.WmiSetGamingRgbKbSetting, new object[] { z1 }, ct);

        // Zone 2
        ulong z2 = (ulong)((config.Zone2.B << 24) | (config.Zone2.G << 16) | (config.Zone2.R << 8) | 2);
        await SendCommandAsync(ServiceCommand.WmiSetGamingRgbKbSetting, new object[] { z2 }, ct);

        // Zone 3
        ulong z3 = (ulong)((config.Zone3.B << 24) | (config.Zone3.G << 16) | (config.Zone3.R << 8) | 3);
        await SendCommandAsync(ServiceCommand.WmiSetGamingRgbKbSetting, new object[] { z3 }, ct);

        // Zone 4
        ulong z4 = (ulong)((config.Zone4.B << 24) | (config.Zone4.G << 16) | (config.Zone4.R << 8) | 4);
        await SendCommandAsync(ServiceCommand.WmiSetGamingRgbKbSetting, new object[] { z4 }, ct);

        // Effect
        ulong effect = (ulong)(((int)config.Effect << 16) | (config.Speed << 8) | (config.Direction & 0xFF));
        await SendCommandAsync(ServiceCommand.WmiSetGamingLedBehavior, new object[] { effect }, ct);

        return true;
    }

    public async Task<bool> SetKbBacklightAsync(int brightnessPercentage, CancellationToken ct = default)
    {
        uint clamped = (uint)Math.Clamp(brightnessPercentage, 0, 100);
        ulong payload = (ulong)clamped << 16;
        await SendCommandAsync(ServiceCommand.WmiSetGamingKbBacklight, new object[] { payload }, ct);

        // Also pass through generic function for full Acer EC compatibility (bkHotkey=132/0x84, sub-command 8 -> 0x80000)
        bool timeout = await GetKbTimeoutAsync(ct);
        ulong timeoutByte = timeout ? 0x1EUL : 0UL;
        ulong genericInput = 2UL | (132UL << 8) | 0x80000UL | ((ulong)clamped << 32) | (timeoutByte << 40);
        var res = await SendCommandAsync(ServiceCommand.WmiSetFunction, new object[] { genericInput }, ct);

        return res.Count > 0;
    }

    public async Task<int> GetKbBacklightAsync(CancellationToken ct = default)
    {
        var res = await SendCommandAsync(ServiceCommand.WmiGetFunction, new object[] { 0x00088401u }, ct);
        if (res.Count > 0 && res[0].Length >= 8)
        {
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(res[0]);
            if ((val & 0xFF) == 0) return (int)((val >> 32) & 0xFF);
        }
        return 100;
    }

    public async Task<bool> SetKbTimeoutAsync(bool enabled, CancellationToken ct = default)
    {
        int currentBrightness = await GetKbBacklightAsync(ct);
        ulong timeoutByte = enabled ? 0x1EUL : 0UL;
        ulong genericInput = 2UL | (132UL << 8) | 0x80000UL | ((ulong)(uint)currentBrightness << 32) | (timeoutByte << 40);
        var res = await SendCommandAsync(ServiceCommand.WmiSetFunction, new object[] { genericInput }, ct);
        return res.Count > 0;
    }

    public async Task<bool> GetKbTimeoutAsync(CancellationToken ct = default)
    {
        var res = await SendCommandAsync(ServiceCommand.WmiGetFunction, new object[] { 0x00088401u }, ct);
        if (res.Count > 0 && res[0].Length >= 8)
        {
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(res[0]);
            if ((val & 0xFF) == 0) return ((val >> 40) & 0xFF) == 30;
        }
        return false;
    }

    public async Task<PowerMode> GetPowerModeAsync(CancellationToken ct = default)
    {
        var res = await SendCommandAsync(ServiceCommand.WmiGetGamingMiscSetting, new object[] { 0x0Bu }, ct);
        if (res.Count > 0 && res[0].Length >= 8)
        {
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(res[0]);
            return (PowerMode)((val >> 8) & 0x3);
        }
        return PowerMode.Default;
    }

    public async Task<bool> SetLcdOverdriveAsync(bool enabled, CancellationToken ct = default)
    {
        ulong input = enabled ? (0x1000000000000UL | 16UL) : 16UL;
        var res = await SendCommandAsync(ServiceCommand.GamingProfileWmiSetFunction, new object[] { input }, ct);
        return res.Count > 0;
    }

    public async Task<bool> GetLcdOverdriveAsync(CancellationToken ct = default)
    {
        var res = await SendCommandAsync(ServiceCommand.GamingProfileWmiGetFunction, new object[] { 0u }, ct);
        if (res.Count > 0 && res[0].Length >= 8)
        {
            ulong val = BinaryPrimitives.ReadUInt64LittleEndian(res[0]);
            return (val & 0xFF) == 0 && (((val >> 48) & 0xFF) == 1);
        }
        return false;
    }

    public async Task<bool> SetWinKeyLockAsync(bool locked, CancellationToken ct = default)
    {
        ulong input = ((locked ? 1UL : 0UL) << 24) | 2UL;
        var res = await SendCommandAsync(ServiceCommand.GamingProfileWmiSetFunction, new object[] { input }, ct);
        return res.Count > 0;
    }

    public async Task<List<byte[]>> SendCommandAsync(ServiceCommand command, object[] args, CancellationToken ct = default)
    {
        if (_stream == null)
        {
            bool connected = await ConnectAsync(1000, ct);
            if (!connected || _stream == null) return new List<byte[]>();
        }

        await _lock.WaitAsync(ct);
        try
        {
            byte[] req = PacketSerializer.SerializeRequest(command, args);
            await _stream.WriteAsync(req, 0, req.Length, ct);
            await _stream.FlushAsync(ct);

            byte[] buffer = new byte[4096];
            int read = await _stream.ReadAsync(buffer, 0, buffer.Length, ct);
            if (read > 0 && PacketSerializer.TryDeserializeResponse(buffer.AsSpan(0, read), out var fields))
            {
                return fields;
            }
            return new List<byte[]>();
        }
        catch
        {
            _stream = null;
            return new List<byte[]>();
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Dispose()
    {
        _stream?.Dispose();
        _stream = null;
        _lock.Dispose();
    }
}

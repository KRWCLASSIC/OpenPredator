using System;
using System.Runtime.InteropServices;

namespace OpenPredator.Core.Backends;

/// <summary>
/// Direct Native NVIDIA Management Library (NVML) bindings for real GPU telemetry.
/// Uses nvml.dll on Windows and libnvidia-ml.so on Linux.
/// </summary>
public static unsafe class NvmlTelemetry
{
    private static bool _initialized = false;
    private static bool _available = false;
    private static IntPtr _deviceHandle = IntPtr.Zero;
    private static readonly object _lock = new();

    private const string WindowsLibrary = "nvml.dll";
    private const string LinuxLibrary = "libnvidia-ml.so";

    [DllImport("nvml", EntryPoint = "nvmlInit_v2")]
    private static extern int nvmlInit_v2();

    [DllImport("nvml", EntryPoint = "nvmlDeviceGetCount_v2")]
    private static extern int nvmlDeviceGetCount_v2(uint* deviceCount);

    [DllImport("nvml", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, IntPtr* device);

    [DllImport("nvml", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, uint* temp);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint Gpu;
        public uint Memory;
    }

    [DllImport("nvml", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, NvmlUtilization* utilization);

    [DllImport("nvml", EntryPoint = "nvmlDeviceGetClockInfo")]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, uint* clockMHz);

    public static bool TryGetGpuMetrics(out int temperature, out int loadPercent, out int clockMhz, out int gpuCount)
    {
        temperature = 0;
        loadPercent = 0;
        clockMhz = 0;
        gpuCount = 0;

        lock (_lock)
        {
            if (!_initialized)
            {
                _initialized = true;
                try
                {
                    int initResult = nvmlInit_v2();
                    if (initResult == 0)
                    {
                        uint count = 0;
                        nvmlDeviceGetCount_v2(&count);
                        if (count > 0)
                        {
                            IntPtr dev;
                            if (nvmlDeviceGetHandleByIndex_v2(0, &dev) == 0)
                            {
                                _deviceHandle = dev;
                                _available = true;
                            }
                        }
                    }
                }
                catch
                {
                    _available = false;
                }
            }

            if (!_available || _deviceHandle == IntPtr.Zero)
                return false;

            try
            {
                gpuCount = 1;
                uint temp = 0;
                if (nvmlDeviceGetTemperature(_deviceHandle, 0, &temp) == 0)
                    temperature = (int)temp;

                NvmlUtilization util;
                if (nvmlDeviceGetUtilizationRates(_deviceHandle, &util) == 0)
                    loadPercent = (int)util.Gpu;

                uint clock = 0;
                if (nvmlDeviceGetClockInfo(_deviceHandle, 0, &clock) == 0)
                    clockMhz = (int)clock;

                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}

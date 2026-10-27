using System.Threading.Tasks;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Backends;

public abstract partial class AcerHardwareBackend
{
    public virtual async Task<SensorData> GetSensorDataAsync()
    {
        return await Task.Run(() =>
        {
            int cpuTemp = 0, gpuTemp = 0, sysTemp = 0;
            int cpuRpm = 0, gpuRpm = 0, sysRpm = 0;

            try
            {
                // CPU Temp (Category 1, Index 1 -> 0x0101)
                ulong rCpuTemp = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0101);
                cpuTemp = (int)((rCpuTemp >> 8) & 0xFF);

                // CPU Fan RPM (Category 2, Index 1 -> 0x0201)
                ulong rCpuRpm = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0201);
                cpuRpm = (int)((rCpuRpm >> 8) & 0xFFFF);

                // GPU Temp (Category 10, Index 1 -> 0x0A01 on modern Acer gaming laptops)
                ulong rGpuTemp = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0A01);
                int parsedGpuTemp = (int)((rGpuTemp >> 8) & 0xFF);

                // System / Motherboard Temp (Category 3, Index 1 -> 0x0301)
                ulong rSysTemp = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0301);
                sysTemp = (int)((rSysTemp >> 8) & 0xFF);

                // If 0x0A01 returned valid temperature, use it; otherwise fallback to 0x0301
                gpuTemp = parsedGpuTemp > 0 ? parsedGpuTemp : sysTemp;

                // GPU Fan RPM (Category 6, Index 1 -> 0x0601)
                ulong rGpuRpm = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0601);
                gpuRpm = (int)((rGpuRpm >> 8) & 0xFFFF);

                // System Fan RPM (Category 5, Index 1 -> 0x0501)
                ulong rSysRpm = InvokeGamingFunctionSync("GetGamingSysInfo", 0x0501);
                sysRpm = (int)((rSysRpm >> 8) & 0xFFFF);
            }
            catch { }

            return new SensorData
            {
                CpuTemperature = cpuTemp,
                GpuTemperature = gpuTemp,
                SystemTemperature = sysTemp,
                CpuFanRpm = cpuRpm,
                GpuFanRpm = gpuRpm,
                SystemFanRpm = sysRpm,
                GpuCount = 1,
                GpuUsagePercent = 0,
                GpuFrequencyMhz = 0
            };
        });
    }
}

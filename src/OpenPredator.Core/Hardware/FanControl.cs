using System;
using System.Threading.Tasks;
using OpenPredator.Core.Enums;

namespace OpenPredator.Core.Backends;

public abstract partial class AcerHardwareBackend
{
    public virtual async Task<bool> SetFanModeAsync(
        FanMode mode,
        bool coolBoost,
        int cpuPercentage = 50,
        int gpuPercentage = 50,
        bool cpuAuto = true,
        bool gpuAuto = true,
        FanMode? cpuMode = null,
        FanMode? gpuMode = null)
    {
        return await Task.Run(() =>
        {
            if (!IsSupported) return false;
            try
            {
                FanMode effectiveCpuMode = cpuMode ?? (mode == FanMode.Custom ? (cpuAuto ? FanMode.Auto : FanMode.Custom) : mode);
                FanMode effectiveGpuMode = gpuMode ?? (mode == FanMode.Custom ? (gpuAuto ? FanMode.Auto : FanMode.Custom) : mode);

                // 1. Calculate and set Acer EC Behavior bitmask first:
                // CPU mode (bits 16-17): 1 = Auto, 2 = Max, 3 = Custom
                // GPU mode (bits 22-23): 1 = Auto, 2 = Max, 3 = Custom
                // Base flags: 9 (bits 0 and 3 enabled)
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

                ulong behaviorFlags = 9UL | ((ulong)cpuModeBits << 16) | ((ulong)gpuModeBits << 22);
                InvokeGamingFunctionSync("SetGamingFanBehavior", behaviorFlags);

                // 2. Apply Speeds for CPU (Fan index 1)
                if (effectiveCpuMode == FanMode.Custom)
                {
                    ulong cpuSpeedInput = ((ulong)Math.Clamp(cpuPercentage, 0, 100) << 8) | 1;
                    InvokeGamingFunctionSync("SetGamingFanSpeed", cpuSpeedInput);
                }
                else if (effectiveCpuMode == FanMode.Max)
                {
                    InvokeGamingFunctionSync("SetGamingFanSpeed", (100UL << 8) | 1);
                }

                // 3. Apply Speeds for GPU (Fan index 4 and index 2 for dual GPU compatibility)
                if (effectiveGpuMode == FanMode.Custom)
                {
                    ulong gpuSpeedInput4 = ((ulong)Math.Clamp(gpuPercentage, 0, 100) << 8) | 4;
                    InvokeGamingFunctionSync("SetGamingFanSpeed", gpuSpeedInput4);
                    ulong gpuSpeedInput2 = ((ulong)Math.Clamp(gpuPercentage, 0, 100) << 8) | 2;
                    InvokeGamingFunctionSync("SetGamingFanSpeed", gpuSpeedInput2);
                }
                else if (effectiveGpuMode == FanMode.Max)
                {
                    InvokeGamingFunctionSync("SetGamingFanSpeed", (100UL << 8) | 4);
                    InvokeGamingFunctionSync("SetGamingFanSpeed", (100UL << 8) | 2);
                }

                _lastMode = mode;

                // Table selection (3 for CoolBoost or Max, 1 for normal/custom, 0 for Auto)
                ulong targetTable = (coolBoost || (effectiveCpuMode == FanMode.Max && effectiveGpuMode == FanMode.Max)) ? 3UL : 1UL;
                InvokeGamingFunctionSync("SetGamingFanTable", targetTable);

                // Apply CoolBoost bit to APGeAction directly without clobbering SetGamingFanTable
                ulong cbInput = ((coolBoost ? 1UL : 0UL) << 16) | 7UL;
                InvokeGenericMethodSync("SetFunction", cbInput);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> SetCoolBoostAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            if (!IsSupported) return false;
            try
            {
                // 1. APGeAction.SetFunction(0x00010007 for ON, 0x00000007 for OFF)
                ulong input = ((enabled ? 1UL : 0UL) << 16) | 7UL;
                InvokeGenericMethodSync("SetFunction", input);

                // 2. Table selection: Table 3 is high-performance/CoolBoost table. If disabling CoolBoost, preserve Table 3 if in Max mode.
                ulong targetTable = (enabled || _lastMode == FanMode.Max) ? 3UL : 1UL;
                InvokeGamingFunctionSync("SetGamingFanTable", targetTable);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> GetCoolBoostAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // 1. APGeAction.GetFunction(0x0207) -> status in byte 0 (0 = success), enabled state in byte 1 (1 = ON)
                ulong res = InvokeGenericMethodSync("GetFunction", 0x0207UL);
                if ((res & 0xFF) == 0)
                {
                    return ((res >> 8) & 0xFF) == 1;
                }

                // 2. Check Fan Table from AcerGamingFunction
                ulong tableRes = InvokeGamingFunctionSync("GetGamingFanTable", 0UL);
                if ((tableRes & 0xFF) == 0 && ((tableRes >> 8) & 0xFF) == 3)
                {
                    return true;
                }

                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> SetFanSpeedAsync(int fanIndex, int percentage)
    {
        return await Task.Run(() =>
        {
            if (!IsSupported) return false;
            try
            {
                ulong input = (ulong)((Math.Clamp(percentage, 0, 100) << 8) | (fanIndex & 0xFF));
                InvokeGamingFunctionSync("SetGamingFanSpeed", input);
                InvokeGamingFunctionSync("SetGamingFanSpeed", input | 0x00080000UL);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }
}

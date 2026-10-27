using System.Threading.Tasks;
using OpenPredator.Core.Enums;

namespace OpenPredator.Core.Backends;

public abstract partial class AcerHardwareBackend
{
    public virtual async Task<bool> SetPowerModeAsync(PowerMode mode)
    {
        return await Task.Run(() =>
        {
            if (!IsSupported) return false;
            try
            {
                // Setting ID 0x0B (11) is Operation Mode in AcerGamingFunction.SetGamingMiscSetting
                // Mode 0: Quiet, Mode 1: Default/Balanced, Mode 2: Performance, Mode 3: Turbo
                ulong input = ((ulong)mode << 8) | 0x0BUL;
                ulong res = InvokeGamingFunctionSync("SetGamingMiscSetting", input);
                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<PowerMode> GetPowerModeAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                ulong res = InvokeGamingFunctionSync("GetGamingMiscSetting", 0x0BUL);
                int modeVal = (int)((res >> 8) & 0x3);
                return (PowerMode)modeVal;
            }
            catch
            {
                return PowerMode.Default;
            }
        });
    }
}

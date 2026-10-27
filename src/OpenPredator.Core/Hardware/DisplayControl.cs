using System.Threading.Tasks;

namespace OpenPredator.Core.Backends;

public abstract partial class AcerHardwareBackend
{
    public virtual async Task<bool> SetLcdOverdriveAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense / PSSvc: AcerGamingFunction.SetGamingProfile(0x1000000000000 | 16 for ON, 16 for OFF)
                ulong input = enabled ? (0x1000000000000UL | 16UL) : 16UL;
                ulong res = InvokeGamingFunctionSync("SetGamingProfile", input);
                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> GetLcdOverdriveAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense: AcerGamingFunction.GetGamingProfile(0) -> bits 48..55 indicate LCD overdrive state
                ulong res = InvokeGamingFunctionSync("GetGamingProfile", 0UL);
                return (res & 0xFF) == 0 && (((res >> 48) & 0xFF) == 1);
            }
            catch
            {
                return false;
            }
        });
    }
}

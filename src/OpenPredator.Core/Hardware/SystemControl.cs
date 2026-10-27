using System.Threading.Tasks;

namespace OpenPredator.Core.Backends;

public abstract partial class AcerHardwareBackend
{
    public virtual async Task<bool> SetWinKeyLockAsync(bool locked)
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense: AcerGamingFunction.SetGamingProfile((locked ? 1 : 0) << 24 | 2)
                ulong input = ((locked ? 1UL : 0UL) << 24) | 2UL;
                ulong res = InvokeGamingFunctionSync("SetGamingProfile", input);
                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> GetWinKeyLockAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                // NitroSense: AcerGamingFunction.GetGamingProfile(0) -> Byte 3 is WinKeyLock (1 = locked, 0 = unlocked)
                ulong res = InvokeGamingFunctionSync("GetGamingProfile", 0UL);
                if ((res & 0xFF) == 0)
                {
                    return ((res >> 24) & 0xFF) == 1;
                }
                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> SetMiscSettingAsync(uint settingId, uint value)
    {
        return await Task.Run(() =>
        {
            try
            {
                ulong input = ((ulong)value << 8) | (settingId & 0xFF);
                InvokeGamingFunctionSync("SetGamingMiscSetting", input);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<ulong> GetMiscSettingAsync(uint settingId)
    {
        return await Task.Run(() =>
        {
            return InvokeGamingFunctionSync("GetGamingMiscSetting", settingId);
        });
    }
}

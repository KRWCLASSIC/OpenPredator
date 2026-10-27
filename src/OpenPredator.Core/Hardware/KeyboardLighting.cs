using System;
using System.Threading.Tasks;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Backends;

public abstract partial class AcerHardwareBackend
{
    public virtual async Task<bool> SetRgbKeyboardAsync(RgbConfig config)
    {
        return await Task.Run(() =>
        {
            if (!IsSupported) return false;
            try
            {
                // Brightness setting (0-100)
                SetKbBacklightAsync(config.Brightness * 25).GetAwaiter().GetResult();

                // Set 4-zone colors: wire format (B << 24) | (G << 16) | (R << 8) | zoneId
                ulong z1Input = (ulong)((uint)(config.Zone1.B << 24) | (uint)(config.Zone1.G << 16) | (uint)(config.Zone1.R << 8) | 1u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z1Input);

                ulong z2Input = (ulong)((uint)(config.Zone2.B << 24) | (uint)(config.Zone2.G << 16) | (uint)(config.Zone2.R << 8) | 2u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z2Input);

                ulong z3Input = (ulong)((uint)(config.Zone3.B << 24) | (uint)(config.Zone3.G << 16) | (uint)(config.Zone3.R << 8) | 3u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z3Input);

                ulong z4Input = (ulong)((uint)(config.Zone4.B << 24) | (uint)(config.Zone4.G << 16) | (uint)(config.Zone4.R << 8) | 4u);
                InvokeGamingFunctionSync("SetGamingLEDColor", z4Input);

                // Effect pattern & speed: (effect << 16) | (speed << 8) | (direction & 0xFF)
                ulong effectInput = (ulong)(((uint)config.Effect << 16) | ((uint)config.Speed << 8) | (uint)(config.Direction & 0xFF));
                InvokeGamingFunctionSync("SetGamingLEDBehavior", effectInput);

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> SetKbBacklightAsync(int brightnessPercentage)
    {
        return await Task.Run(() =>
        {
            if (!IsSupported) return false;
            try
            {
                int clamped = Math.Clamp(brightnessPercentage, 0, 100);
                const int bkHotkey = 132;

                // Check current timeout setting directly from ACPI EC
                bool timeoutEnabled = false;
                try
                {
                    ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                    ulong qRes = InvokeGenericMethodSync("GetFunction", q);
                    if ((qRes & 0xFF) == 0)
                    {
                        timeoutEnabled = ((qRes >> 40) & 0xFF) == 30;
                    }
                }
                catch { }

                ulong timeoutByte = timeoutEnabled ? 0x1EUL : 0UL;

                // 1. APGeAction SetFunction: 2 | (bkHotkey << 8) | 0x80000 | (clamped << 32) | (timeoutByte << 40)
                ulong genericInput = 2UL | ((ulong)bkHotkey << 8) | 0x80000UL | ((ulong)clamped << 32) | (timeoutByte << 40);
                InvokeGenericMethodSync("SetFunction", genericInput);

                // 2. AcerGamingFunction.SetGamingKBBacklight: (clamped << 16)
                ulong gamingInput = (ulong)clamped << 16;
                InvokeGamingFunctionSync("SetGamingKBBacklight", gamingInput);

                // 3. SetGamingRgbKb for zone compatibility
                InvokeGamingFunctionSync("SetGamingRgbKb", 1UL | ((ulong)clamped << 8));

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<int> GetKbBacklightAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                const int bkHotkey = 132;
                ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                ulong res = InvokeGenericMethodSync("GetFunction", q);
                if ((res & 0xFF) == 0)
                {
                    return (int)((res >> 32) & 0xFF);
                }
            }
            catch { }
            return 100;
        });
    }

    public virtual async Task<bool> SetKbTimeoutAsync(bool enabled)
    {
        return await Task.Run(() =>
        {
            try
            {
                const int bkHotkey = 132;
                int currentBrightness = 100;
                try
                {
                    ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                    ulong qRes = InvokeGenericMethodSync("GetFunction", q);
                    if ((qRes & 0xFF) == 0)
                    {
                        currentBrightness = (int)((qRes >> 32) & 0xFF);
                    }
                }
                catch { }

                ulong timeoutByte = enabled ? 0x1EUL : 0UL;
                ulong genericInput = 2UL | ((ulong)bkHotkey << 8) | 0x80000UL | ((ulong)currentBrightness << 32) | (timeoutByte << 40);
                ulong res = InvokeGenericMethodSync("SetFunction", genericInput);

                return (res & 0xFF) == 0;
            }
            catch
            {
                return false;
            }
        });
    }

    public virtual async Task<bool> GetKbTimeoutAsync()
    {
        return await Task.Run(() =>
        {
            try
            {
                const int bkHotkey = 132;
                ulong q = 1UL | ((ulong)bkHotkey << 8) | 0x80000UL;
                ulong res = InvokeGenericMethodSync("GetFunction", q);
                if ((res & 0xFF) == 0)
                {
                    return ((res >> 40) & 0xFF) == 30;
                }
            }
            catch { }
            return false;
        });
    }
}

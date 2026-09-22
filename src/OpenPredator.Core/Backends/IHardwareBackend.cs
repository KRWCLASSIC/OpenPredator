using System.Threading.Tasks;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Backends;

public interface IHardwareBackend
{
    string BackendName { get; }
    bool IsSupported { get; }

    Task<SensorData> GetSensorDataAsync();
    Task<bool> SetFanModeAsync(FanMode mode, bool coolBoost, int cpuPercentage = 50, int gpuPercentage = 50, bool cpuAuto = true, bool gpuAuto = true, FanMode? cpuMode = null, FanMode? gpuMode = null);
    Task<bool> SetCoolBoostAsync(bool enabled);
    Task<bool> GetCoolBoostAsync();
    Task<bool> SetFanSpeedAsync(int fanIndex, int percentage);
    Task<bool> SetPowerModeAsync(PowerMode mode);
    Task<bool> SetRgbKeyboardAsync(RgbConfig config);
    Task<bool> SetKbBacklightAsync(int brightnessPercentage);
    Task<int> GetKbBacklightAsync();
    Task<bool> SetKbTimeoutAsync(bool enabled);
    Task<bool> GetKbTimeoutAsync();
    Task<PowerMode> GetPowerModeAsync();
    Task<bool> SetMiscSettingAsync(uint settingId, uint value);
    Task<ulong> GetMiscSettingAsync(uint settingId);
    Task<bool> SetLcdOverdriveAsync(bool enabled);
    Task<bool> GetLcdOverdriveAsync();
    Task<bool> SetWinKeyLockAsync(bool locked);

    // Raw WMI / ACPI Pass-Through
    Task<ulong> InvokeGamingFunctionAsync(string methodName, ulong input);
    Task<ulong> InvokeGenericMethodAsync(string methodName, ulong input);
}

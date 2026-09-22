namespace OpenPredator.Core.Enums;

/// <summary>
/// PredatorSense / NitroSense named pipe service command codes (0x00 - 0x22).
/// 100% wire compatible with Acer's PSSvc.exe protocol.
/// </summary>
public enum ServiceCommand : short
{
    EchoArguments = 0,
    RegCreateKey = 1,
    RegDeleteKey = 2,
    RegCreateValue = 3,
    RegDeleteValue = 4,
    RegSetValue = 5,
    RegGetValue = 6,
    CreateProcessAsUser = 7,
    CreateProcessOnLogon = 8,
    GamingProfileWmiSetFunction = 9,
    GamingProfileWmiGetFunction = 10,
    GamingLedGroupColorSetFunction = 11,
    GamingLedGroupColorGetFunction = 12,
    GetGamingSysInfoFunction = 13,
    GetGpuUsageLoading = 14,
    GamingFanGroupBehaviorSetFunction = 15,
    GamingFanGroupSpeedSetFunction = 16,
    WmiSetFunction = 17,
    WmiSetLogoLed = 18,
    WmiSetLogoBehavior = 19,
    WmiGetFunction = 20,
    NvidiaOcGetFunction = 21,
    IntelOcGetFunction = 22,
    NvidiaOcSetFunction = 23,
    IntelOcSetFunction = 24,
    NvidiaSliSetFunction = 25,
    NvidiaGetCoprocStatusFunction = 26,
    WmiSetGamingKbBacklight = 27,
    WmiSetGamingRgbKbSetting = 28,
    WmiSetGamingLedBehavior = 29,
    SetOperationMode = 30,
    GpuGetCount = 31,
    GpuGetFrequency = 32,
    WmiSetGamingMiscSetting = 33,
    WmiGetGamingMiscSetting = 34
}

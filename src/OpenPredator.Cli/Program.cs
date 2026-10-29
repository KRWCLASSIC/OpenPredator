using System;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Client;
using OpenPredator.Core.Backends;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Hardware;
using OpenPredator.Core.Models;
using OpenPredator.Core.Services;

namespace OpenPredator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        string cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "help";
        bool isHelp = args.Length == 0 ||
                      cmd is "help" or "--help" or "-h";

        using var client = new OpenPredatorClient();
        bool connected = await client.ConnectAsync(1000);

        if (isHelp)
        {
            PrintHelp(client);
            return 0;
        }

        if (!connected)
        {
            if (cmd != "config" && !ProcessElevation.IsElevated)
            {
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("[Notice] Background daemon (OpenPredator Service / OEM PSSvc) is not active on pipe.");
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Error] Direct hardware access requires elevated privileges (Administrator on Windows, sudo/root on Linux).");
                Console.ResetColor();
                Console.WriteLine("        Please start the service (e.g. 'sudo openpredator-service' or Windows Service) or run this command with elevation.\n");
                return 1;
            }

            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Notice] No background daemon (OpenPredator Service / OEM PSSvc) active on pipe.");
            Console.WriteLine("         Falling back to direct hardware access (ACPI WMI + SMBIOS + NVML)...\n");
            Console.ResetColor();
        }

        switch (cmd)
        {
            case "status":
                return await ShowStatusAsync(client);

            case "watch":
                return await WatchStatusAsync(client);

            case "capabilities":
                return await HandleCapabilitiesCommandAsync(client, args);

            case "fan":
            case "fans":
                return await HandleFanCommandAsync(client, args);

            case "coolboost":
            case "cb":
                return await HandleCoolBoostCommandAsync(client, args);

            case "mode":
            case "profile":
                return await HandleModeCommandAsync(client, args);

            case "lcd":
                return await HandleLcdCommandAsync(client, args);

            case "winkey":
                return await HandleWinKeyCommandAsync(client, args);

            case "rgb":
                return await HandleRgbCommandAsync(client, args);

            case "brightness":
            case "backlight":
                return await HandleBrightnessCommandAsync(client, args);

            case "timeout":
                return await HandleKbTimeoutCommandAsync(client, args);

            case "config":
                return await HandleConfigCommandAsync(args);

            default:
                PrintError($"Unknown command: {args[0]}");
                Console.WriteLine();
                PrintHelp(client);
                return 1;
        }
    }

    private static void PrintOk(string message)
    {
        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m {message}");
    }

    private static void PrintNotice(string message)
    {
        Console.WriteLine($"\x1b[1;33m[Notice]\x1b[0m {message}");
    }

    private static void PrintError(string message)
    {
        Console.WriteLine($"\x1b[1;31m[Error]\x1b[0m {message}");
    }

    private static void PrintHelp(IOpenPredatorClient? client = null)
    {
        IHardwareBackend backend = (client != null && client.IsConnected)
            ? new ClientHardwareBackend(client)
            : HardwareManager.CreateBestBackend();
        var caps = CapabilityProbe.Probe(backend);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("                OpenPredator CLI                 ");
        Console.WriteLine("     Hardware Control & Telemetry Utility        ");
        Console.WriteLine("=================================================");
        Console.ResetColor();

        string series = caps.IsPredator ? "Predator Series" : "Nitro Series";
        Console.WriteLine($"\x1b[90mDevice: {caps.ModelName} ({series})\x1b[0m\n");

        Console.WriteLine("Usage: openpredator <command> [arguments]\n");

        Console.WriteLine("\x1b[1;36mMonitoring & Hardware Info:\x1b[0m");
        Console.WriteLine("  status                              Display live temperatures, fan speeds & GPU metrics");
        Console.WriteLine("  watch                               Live terminal dashboard updating every second");
        Console.WriteLine("  capabilities \x1b[90m[-v|--verbose]\x1b[0m         Inspect verified motherboard capabilities & diagnostic log\n");

        Console.WriteLine("\x1b[1;36mFan & Cooling \x1b[90m(command: fan | fans)\x1b[1;36m:\x1b[0m");
        Console.WriteLine("  fans auto \x1b[90m[-c|-nc]\x1b[0m                  Set fan mode to Auto \x1b[90m(-c|--coolboost, -nc|--no-coolboost)\x1b[0m");
        Console.WriteLine("  fans max \x1b[33m[-c]\x1b[0m\x1b[90m[-nc]\x1b[0m                   Set fans to Maximum 100% \x1b[33m[-c: experimental]\x1b[0m");
        Console.WriteLine("  fans custom <cpu%> <gpu%> \x1b[33m[-c]\x1b[0m\x1b[90m[-nc]\x1b[0m  Set custom CPU & GPU speeds (0-100%) \x1b[33m[-c: experimental]\x1b[0m");
        Console.WriteLine("  fans custom <both%> \x1b[33m[-c]\x1b[0m\x1b[90m[-nc]\x1b[0m        Set both fans to the same percentage \x1b[33m[-c: experimental]\x1b[0m");
        Console.WriteLine("  fans custom cpu|gpu <%> \x1b[33m[-c]\x1b[0m\x1b[90m[-nc]\x1b[0m    Set only CPU or only GPU speed \x1b[33m[-c: experimental]\x1b[0m");
        Console.WriteLine("  fans custom --cpu <%> --gpu <%>     Explicit target flags (0-100%)");
        Console.WriteLine("  coolboost \x1b[90m(or cb)\x1b[0m on|off            Toggle CoolBoost technology (Auto mode)");
        Console.WriteLine("  coolboost \x1b[90m(or cb)\x1b[0m \x1b[33mexp\x1b[0m on|off        Unlock \x1b[33mexp\x1b[0merimental CoolBoost for Custom/Max \x1b[90m(alias: experimental)\x1b[0m\n");

        Console.WriteLine("\x1b[1;36mPower & Performance \x1b[90m(command: mode | profile)\x1b[1;36m:\x1b[0m");
        if (caps.IsPredator)
        {
            Console.WriteLine("  mode quiet|default|perf|turbo       Switch profile \x1b[90m(aliases: balanced, performance, extreme)\x1b[0m");
        }
        else
        {
            Console.WriteLine("  mode quiet|default|perf             Switch profile \x1b[90m(aliases: balanced, performance)\x1b[0m");
        }
        Console.WriteLine();

        Console.WriteLine("\x1b[1;36mKeyboard Backlight:\x1b[0m");
        if (caps.IsRgbKeyboard)
        {
            Console.WriteLine("  rgb static <z1> <z2> <z3> <z4>      Set 4-zone static hex colors \x1b[90m(e.g. FF0000 00FF00 ...)\x1b[0m");
            Console.WriteLine("  rgb effect <type> [speed] [bright]  Set dynamic effect (breathing, neon, wave, zoom, shifting)");
        }
        Console.WriteLine("  brightness \x1b[90m(or backlight)\x1b[0m <0-100>   Set keyboard backlight brightness percentage");
        Console.WriteLine("  timeout on|off                      Toggle 30-second keyboard backlight sleep timer\n");

        if (caps.SupportsLcdOverdrive || caps.IsPredator)
        {
            Console.WriteLine("\x1b[1;36mDisplay & Controls:\x1b[0m");
            if (caps.SupportsLcdOverdrive)
            {
                Console.WriteLine("  lcd on|off                          Toggle LCD panel 3ms Overdrive");
            }
            Console.WriteLine("  winkey lock|unlock                  Lock/unlock Windows key during gaming \x1b[90m(aliases: on/off)\x1b[0m\n");
        }
        else
        {
            Console.WriteLine("\x1b[1;36mSystem Controls:\x1b[0m");
            Console.WriteLine("  winkey lock|unlock                  Lock/unlock Windows key during gaming \x1b[90m(aliases: on/off)\x1b[0m\n");
        }

        Console.WriteLine("\x1b[1;36mConfiguration:\x1b[0m");
        Console.WriteLine("  config show|path|apply              Display or apply config \x1b[90m(aliases: apply, restore)\x1b[0m");
        Console.WriteLine("  help \x1b[90m[-h|--help]\x1b[0m                    Show this help screen\n");
    }

    private static async Task<int> ShowStatusAsync(IOpenPredatorClient client)
    {
        var sensors = client.IsConnected
            ? await client.GetSensorsAsync()
            : await HardwareManager.Instance.GetSensorsAsync();

        string TempColor(int temp) => temp >= 85 ? "\x1b[1;31m" : (temp >= 70 ? "\x1b[1;33m" : "\x1b[1;32m");

        Console.WriteLine("\x1b[1;36m+--------------------------------------------+\x1b[0m");
        Console.WriteLine("\x1b[1;37m|           SYSTEM LIVE TELEMETRY            |\x1b[0m");
        Console.WriteLine("\x1b[1;36m+--------------------------------------------+\x1b[0m");
        Console.WriteLine($"  CPU Temperature   : {TempColor(sensors.CpuTemperature)}{sensors.CpuTemperature,2} °C\x1b[0m");
        Console.WriteLine($"  CPU Fan Speed     : \x1b[1;36m{sensors.CpuFanRpm,4} RPM\x1b[0m");
        Console.WriteLine($"  GPU Temperature   : {TempColor(sensors.GpuTemperature)}{sensors.GpuTemperature,2} °C\x1b[0m");
        Console.WriteLine($"  GPU Fan Speed     : \x1b[1;36m{sensors.GpuFanRpm,4} RPM\x1b[0m");
        Console.WriteLine($"  GPU Utilization   : \x1b[1;37m{sensors.GpuUsagePercent,2}%\x1b[0m");
        Console.WriteLine($"  GPU Frequency     : \x1b[1;37m{sensors.GpuFrequencyMhz,4} MHz\x1b[0m");
        if (sensors.SystemTemperature > 0)
        {
            Console.WriteLine($"  Motherboard / VRM : {TempColor(sensors.SystemTemperature)}{sensors.SystemTemperature,2} °C\x1b[0m");
        }
        Console.WriteLine("\x1b[1;36m+--------------------------------------------+\x1b[0m");
        return 0;
    }

    private static async Task<int> WatchStatusAsync(IOpenPredatorClient client)
    {
        Console.CursorVisible = false;
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (s, e) => { e.Cancel = true; cts.Cancel(); };

        Console.Clear();
        while (!cts.Token.IsCancellationRequested)
        {
            Console.SetCursorPosition(0, 0);
            await ShowStatusAsync(client);
            Console.WriteLine("\x1b[2K\x1b[90m  Press Ctrl+C to exit dashboard.\x1b[0m");
            try { await Task.Delay(1000, cts.Token); } catch { break; }
        }

        Console.CursorVisible = true;
        return 0;
    }

    private static async Task<int> HandleFanCommandAsync(IOpenPredatorClient client, string[] args)
    {
        if (args.Length < 2)
        {
            FanMode curMode = client.IsConnected
                ? await client.GetFanModeAsync()
                : await HardwareManager.Instance.GetFanModeAsync();
            bool cb = client.IsConnected
                ? await client.GetCoolBoostAsync()
                : await HardwareManager.Instance.GetCoolBoostAsync();
            var sensors = client.IsConnected
                ? await client.GetSensorsAsync()
                : await HardwareManager.Instance.GetSensorsAsync();

            PrintOk($"Fan Mode: \x1b[1;36m{curMode}\x1b[0m | CPU: \x1b[1;37m{sensors.CpuFanRpm} RPM\x1b[0m | GPU: \x1b[1;37m{sensors.GpuFanRpm} RPM\x1b[0m | CoolBoost: {(cb ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
            return 0;
        }

        string sub = args[1].ToLowerInvariant();
        bool? coolBoostFlag = null;
        for (int i = 2; i < args.Length; i++)
        {
            string a = args[i].ToLowerInvariant();
            if (a is "--coolboost" or "-c") coolBoostFlag = true;
            else if (a is "--no-coolboost" or "-nc") coolBoostFlag = false;
        }

        bool success = false;

        // In Custom and Max modes, CoolBoost requires experimental unlock
        if (sub != "auto")
        {
            bool isExp = client.IsConnected
                ? await client.GetExperimentalCoolBoostAsync()
                : HardwareManager.IsExperimentalCoolBoostUnlocked();

            if (coolBoostFlag == true && !isExp)
            {
                PrintError("CoolBoost in Custom/Max modes is experimental (can cause EC thermal desynchronization).");
                Console.WriteLine("\x1b[90mTip: To unlock in-memory experimental mode, run:\x1b[0m");
                Console.WriteLine("  \x1b[1;33mopenpredator coolboost experimental on\x1b[0m\n");
                return 1;
            }
            else if (!isExp)
            {
                // Without experimental unlock, ensure CoolBoost is OFF in non-Auto mode
                coolBoostFlag = false;
            }
        }

        switch (sub)
        {
            case "auto":
                success = client.IsConnected
                    ? await client.SetFanModeAsync(FanMode.Auto, coolBoost: coolBoostFlag)
                    : await HardwareManager.Instance.SetFanModeAsync(FanMode.Auto, coolBoost: coolBoostFlag);
                if (success)
                {
                    FanMode actualMode = client.IsConnected ? await client.GetFanModeAsync() : await HardwareManager.Instance.GetFanModeAsync();
                    bool cb = client.IsConnected ? await client.GetCoolBoostAsync() : await HardwareManager.Instance.GetCoolBoostAsync();
                    var sensors = client.IsConnected ? await client.GetSensorsAsync() : await HardwareManager.Instance.GetSensorsAsync();

                    if (actualMode != FanMode.Auto)
                    {
                        PrintError($"Firmware rejected Auto fan mode (hardware reported: \x1b[1;33m{actualMode}\x1b[0m).");
                        return 1;
                    }

                    PrintOk($"Fan Mode: \x1b[1;36mAuto\x1b[0m (Dynamic curve) | CPU: \x1b[1;37m{sensors.CpuFanRpm} RPM\x1b[0m | GPU: \x1b[1;37m{sensors.GpuFanRpm} RPM\x1b[0m | CoolBoost: {(cb ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
                }
                else PrintError("Failed to set fan mode to Auto.");
                break;

            case "max":
                success = client.IsConnected
                    ? await client.SetFanModeAsync(FanMode.Max, coolBoost: coolBoostFlag)
                    : await HardwareManager.Instance.SetFanModeAsync(FanMode.Max, coolBoost: coolBoostFlag);
                if (success)
                {
                    FanMode actualMode = client.IsConnected ? await client.GetFanModeAsync() : await HardwareManager.Instance.GetFanModeAsync();
                    bool cb = client.IsConnected ? await client.GetCoolBoostAsync() : await HardwareManager.Instance.GetCoolBoostAsync();
                    var sensors = client.IsConnected ? await client.GetSensorsAsync() : await HardwareManager.Instance.GetSensorsAsync();

                    if (actualMode != FanMode.Max)
                    {
                        PrintError($"Firmware rejected Max fan mode (hardware reported: \x1b[1;33m{actualMode}\x1b[0m).");
                        return 1;
                    }

                    PrintOk($"Fan Mode: \x1b[1;31mMax\x1b[0m (Target: 100%) | CPU: \x1b[1;37m{sensors.CpuFanRpm} RPM\x1b[0m | GPU: \x1b[1;37m{sensors.GpuFanRpm} RPM\x1b[0m | CoolBoost: {(cb ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
                }
                else PrintError("Failed to set fan mode to Max.");
                break;

            case "custom":
                int? targetCpu = null;
                int? targetGpu = null;
                bool targetBoth = false;
                var positionalNumbers = new List<int>();

                for (int i = 2; i < args.Length; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a is "--coolboost" or "-c" or "--no-coolboost" or "-nc")
                    {
                        continue;
                    }

                    if (a is "both" or "--both")
                    {
                        targetBoth = true;
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int bVal))
                        {
                            if (bVal < 0 || bVal > 100)
                            {
                                PrintError($"Invalid percentage '{args[i + 1]}'. Expected an integer between 0 and 100.");
                                return 1;
                            }
                            targetCpu = bVal;
                            targetGpu = bVal;
                            i++;
                        }
                        else if (positionalNumbers.Count > 0)
                        {
                            int bPos = positionalNumbers[^1];
                            positionalNumbers.RemoveAt(positionalNumbers.Count - 1);
                            targetCpu = bPos;
                            targetGpu = bPos;
                        }
                        else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            PrintError($"Invalid percentage '{args[i + 1]}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        else
                        {
                            PrintError("Expected a percentage for '--both' (e.g. 'custom both 50' or 'custom 50 --both').");
                            return 1;
                        }
                    }
                    else if (a.StartsWith("--both=") && int.TryParse(a["--both=".Length..], out int bValEq))
                    {
                        if (bValEq < 0 || bValEq > 100)
                        {
                            PrintError($"Invalid percentage '{bValEq}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        targetCpu = bValEq;
                        targetGpu = bValEq;
                        targetBoth = true;
                    }
                    else if (a is "cpu" or "--cpu")
                    {
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int cVal))
                        {
                            if (cVal < 0 || cVal > 100)
                            {
                                PrintError($"Invalid percentage '{args[i + 1]}'. Expected an integer between 0 and 100.");
                                return 1;
                            }
                            targetCpu = cVal;
                            i++;
                        }
                        else if (positionalNumbers.Count > 0)
                        {
                            targetCpu = positionalNumbers[^1];
                            positionalNumbers.RemoveAt(positionalNumbers.Count - 1);
                        }
                        else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            PrintError($"Invalid percentage '{args[i + 1]}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        else
                        {
                            PrintError("Expected a percentage for CPU (e.g. 'custom cpu 50' or 'custom 50 --cpu').");
                            return 1;
                        }
                    }
                    else if (a.StartsWith("--cpu=") && int.TryParse(a["--cpu=".Length..], out int cValEq))
                    {
                        if (cValEq < 0 || cValEq > 100)
                        {
                            PrintError($"Invalid percentage '{cValEq}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        targetCpu = cValEq;
                    }
                    else if (a is "gpu" or "--gpu")
                    {
                        if (i + 1 < args.Length && int.TryParse(args[i + 1], out int gVal))
                        {
                            if (gVal < 0 || gVal > 100)
                            {
                                PrintError($"Invalid percentage '{args[i + 1]}'. Expected an integer between 0 and 100.");
                                return 1;
                            }
                            targetGpu = gVal;
                            i++;
                        }
                        else if (positionalNumbers.Count > 0)
                        {
                            targetGpu = positionalNumbers[^1];
                            positionalNumbers.RemoveAt(positionalNumbers.Count - 1);
                        }
                        else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                        {
                            PrintError($"Invalid percentage '{args[i + 1]}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        else
                        {
                            PrintError("Expected a percentage for GPU (e.g. 'custom gpu 50' or 'custom 50 --gpu').");
                            return 1;
                        }
                    }
                    else if (a.StartsWith("--gpu=") && int.TryParse(a["--gpu=".Length..], out int gValEq))
                    {
                        if (gValEq < 0 || gValEq > 100)
                        {
                            PrintError($"Invalid percentage '{gValEq}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        targetGpu = gValEq;
                    }
                    else if (int.TryParse(a, out int numVal))
                    {
                        if (numVal < 0 || numVal > 100)
                        {
                            PrintError($"Invalid percentage '{a}'. Expected an integer between 0 and 100.");
                            return 1;
                        }
                        positionalNumbers.Add(numVal);
                    }
                    else
                    {
                        PrintError($"Unrecognized custom fan argument '{args[i]}'.");
                        Console.WriteLine("\x1b[90mUsage:\x1b[0m");
                        Console.WriteLine("  openpredator fan custom <both%>");
                        Console.WriteLine("  openpredator fan custom <cpu%> <gpu%>");
                        Console.WriteLine("  openpredator fan custom cpu <%> | gpu <%> | both <%>");
                        Console.WriteLine("  openpredator fan custom --cpu <%> --gpu <%>\n");
                        return 1;
                    }
                }

                // Resolve target percentages
                int finalCpu;
                int finalGpu;
                bool cpuTargeted = false;
                bool gpuTargeted = false;

                if (targetBoth || (targetCpu == null && targetGpu == null && positionalNumbers.Count == 1))
                {
                    int val = targetCpu ?? positionalNumbers[0];
                    finalCpu = val;
                    finalGpu = val;
                    cpuTargeted = true;
                    gpuTargeted = true;
                }
                else if (targetCpu != null && targetGpu != null)
                {
                    finalCpu = targetCpu.Value;
                    finalGpu = targetGpu.Value;
                    cpuTargeted = true;
                    gpuTargeted = true;
                }
                else if (positionalNumbers.Count >= 2)
                {
                    finalCpu = positionalNumbers[0];
                    finalGpu = positionalNumbers[1];
                    cpuTargeted = true;
                    gpuTargeted = true;
                }
                else if (targetCpu != null)
                {
                    finalCpu = targetCpu.Value;
                    cpuTargeted = true;
                    finalGpu = ConfigManager.Instance.Current.Fan.GpuPercentage >= 0
                        ? ConfigManager.Instance.Current.Fan.GpuPercentage
                        : 50;
                }
                else if (targetGpu != null)
                {
                    finalGpu = targetGpu.Value;
                    gpuTargeted = true;
                    finalCpu = ConfigManager.Instance.Current.Fan.CpuPercentage >= 0
                        ? ConfigManager.Instance.Current.Fan.CpuPercentage
                        : 50;
                }
                else
                {
                    PrintError("Missing custom fan percentage.");
                    Console.WriteLine("\x1b[90mUsage:\x1b[0m");
                    Console.WriteLine("  openpredator fan custom <both%>");
                    Console.WriteLine("  openpredator fan custom <cpu%> <gpu%>");
                    Console.WriteLine("  openpredator fan custom cpu <%> | gpu <%> | both <%>");
                    Console.WriteLine("  openpredator fan custom --cpu <%> --gpu <%>\n");
                    return 1;
                }

                bool currentCpuAuto = !cpuTargeted && ConfigManager.Instance.Current.Fan.CpuCustomAuto;
                bool currentGpuAuto = !gpuTargeted && ConfigManager.Instance.Current.Fan.GpuCustomAuto;

                FanMode cpuModeReq = currentCpuAuto ? FanMode.Auto : FanMode.Custom;
                FanMode gpuModeReq = currentGpuAuto ? FanMode.Auto : FanMode.Custom;

                success = client.IsConnected
                    ? await client.SetFanModeAsync(FanMode.Custom, coolBoost: coolBoostFlag, cpuPercentage: finalCpu, gpuPercentage: finalGpu, cpuAuto: currentCpuAuto, gpuAuto: currentGpuAuto, cpuMode: cpuModeReq, gpuMode: gpuModeReq)
                    : await HardwareManager.Instance.SetFanModeAsync(FanMode.Custom, coolBoost: coolBoostFlag, cpuPercentage: finalCpu, gpuPercentage: finalGpu, cpuAuto: currentCpuAuto, gpuAuto: currentGpuAuto, cpuMode: cpuModeReq, gpuMode: gpuModeReq);
                if (success)
                {
                    FanMode actualMode = client.IsConnected ? await client.GetFanModeAsync() : await HardwareManager.Instance.GetFanModeAsync();
                    bool cb = client.IsConnected ? await client.GetCoolBoostAsync() : await HardwareManager.Instance.GetCoolBoostAsync();
                    var sensors = client.IsConnected ? await client.GetSensorsAsync() : await HardwareManager.Instance.GetSensorsAsync();

                    if (actualMode != FanMode.Custom)
                    {
                        PrintError($"Firmware rejected Custom fan mode (hardware reported: \x1b[1;33m{actualMode}\x1b[0m).");
                        return 1;
                    }

                    if (cpuTargeted && gpuTargeted)
                    {
                        PrintOk($"Custom Fans: Target CPU \x1b[1;36m{finalCpu}%\x1b[0m | GPU \x1b[1;36m{finalGpu}%\x1b[0m | CPU: \x1b[1;37m{sensors.CpuFanRpm} RPM\x1b[0m | GPU: \x1b[1;37m{sensors.GpuFanRpm} RPM\x1b[0m | CoolBoost: {(cb ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
                    }
                    else if (cpuTargeted)
                    {
                        PrintOk($"Custom Fan: Target CPU \x1b[1;36m{finalCpu}%\x1b[0m (GPU unchanged) | CPU: \x1b[1;37m{sensors.CpuFanRpm} RPM\x1b[0m | GPU: \x1b[1;37m{sensors.GpuFanRpm} RPM\x1b[0m | CoolBoost: {(cb ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
                    }
                    else
                    {
                        PrintOk($"Custom Fan: Target GPU \x1b[1;36m{finalGpu}%\x1b[0m (CPU unchanged) | CPU: \x1b[1;37m{sensors.CpuFanRpm} RPM\x1b[0m | GPU: \x1b[1;37m{sensors.GpuFanRpm} RPM\x1b[0m | CoolBoost: {(cb ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
                    }
                }
                else PrintError("Failed to set custom fan speeds.");
                break;

            default:
                PrintError($"Unknown fan subcommand: {sub}");
                Console.WriteLine("\x1b[90mUsage:\x1b[0m");
                Console.WriteLine("  openpredator fan auto | max | custom <cpu%> <gpu%> [-c|--coolboost] [-nc|--no-coolboost]");
                Console.WriteLine("  openpredator fan custom <both%> | cpu <%> | gpu <%> [-c]");
                return 1;
        }

        return success ? 0 : 1;
    }

    private static async Task<int> HandleCoolBoostCommandAsync(IOpenPredatorClient client, string[] args)
    {
        if (args.Length >= 2 && (args[1].Equals("experimental", StringComparison.OrdinalIgnoreCase) || args[1].Equals("exp", StringComparison.OrdinalIgnoreCase)))
        {
            if (args.Length < 3)
            {
                bool isUnlocked = client.IsConnected
                    ? await client.GetExperimentalCoolBoostAsync()
                    : HardwareManager.IsExperimentalCoolBoostUnlocked();
                PrintOk($"Experimental CoolBoost: {(isUnlocked ? "\x1b[1;33mUnlocked (In-Memory)\x1b[0m" : "\x1b[90mLocked\x1b[0m")}");
                return 0;
            }

            bool enableExp = args[2].Equals("on", StringComparison.OrdinalIgnoreCase) ||
                             args[2].Equals("true", StringComparison.OrdinalIgnoreCase) ||
                             args[2] == "1";

            if (client.IsConnected)
            {
                await client.SetExperimentalCoolBoostAsync(enableExp);
            }
            HardwareManager.SetExperimentalCoolBoostUnlocked(enableExp);

            if (enableExp)
            {
                PrintOk("Experimental CoolBoost: \x1b[1;33mUnlocked (In-Memory)\x1b[0m. CoolBoost is now permitted in Custom and Max modes.");
            }
            else
            {
                FanMode currentMode = client.IsConnected ? await client.GetFanModeAsync() : await HardwareManager.Instance.GetFanModeAsync();
                if (currentMode != FanMode.Auto)
                {
                    if (client.IsConnected) await client.SetCoolBoostAsync(false);
                    else await HardwareManager.Instance.SetCoolBoostAsync(false);
                }
                PrintOk("Experimental CoolBoost: \x1b[90mLocked\x1b[0m. CoolBoost is restricted to Auto mode.");
            }
            return 0;
        }

        if (args.Length < 2)
        {
            bool cur = client.IsConnected
                ? await client.GetCoolBoostAsync()
                : await HardwareManager.Instance.GetCoolBoostAsync();
            PrintOk($"CoolBoost: {(cur ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
            return 0;
        }

        bool enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1] == "1";

        if (enable)
        {
            FanMode currentMode = client.IsConnected
                ? await client.GetFanModeAsync()
                : await HardwareManager.Instance.GetFanModeAsync();

            bool isExp = client.IsConnected
                ? await client.GetExperimentalCoolBoostAsync()
                : HardwareManager.IsExperimentalCoolBoostUnlocked();

            if (currentMode != FanMode.Auto && !isExp)
            {
                PrintError($"CoolBoost is officially restricted to Auto mode (current mode: {currentMode}).");
                Console.WriteLine("\x1b[90mTip: To unlock in-memory experimental mode for Custom/Max, run:\x1b[0m");
                Console.WriteLine("  \x1b[1;33mopenpredator coolboost experimental on\x1b[0m\n");
                return 1;
            }
        }

        bool success = client.IsConnected
            ? await client.SetCoolBoostAsync(enable)
            : await HardwareManager.Instance.SetCoolBoostAsync(enable);

        if (success)
        {
            bool actual = client.IsConnected
                ? await client.GetCoolBoostAsync()
                : await HardwareManager.Instance.GetCoolBoostAsync();

            if (actual != enable)
            {
                PrintError($"Firmware did not apply CoolBoost state (requested: {(enable ? "Enabled" : "Disabled")}, hardware reported: {(actual ? "Enabled" : "Disabled")}).");
                return 1;
            }

            PrintOk($"CoolBoost: {(actual ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
        }
        else
        {
            PrintError("Failed to update CoolBoost state (mode not supported or rejected).");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleModeCommandAsync(IOpenPredatorClient client, string[] args)
    {
        IHardwareBackend backend = client.IsConnected
            ? new ClientHardwareBackend(client)
            : HardwareManager.CreateBestBackend();
        var caps = CapabilityProbe.Probe(backend);

        if (args.Length < 2)
        {
            var curMode = client.IsConnected
                ? await client.GetPowerModeAsync()
                : await HardwareManager.Instance.GetPowerModeAsync();
            PrintOk($"Power Mode: \x1b[1;36m{curMode}\x1b[0m");
            return 0;
        }

        string rawMode = args[1].ToLowerInvariant();
        if ((rawMode == "turbo" || rawMode == "extreme") && !caps.IsPredator)
        {
            PrintNotice("Turbo mode is exclusive to Predator series laptops.");
            Console.WriteLine("         Available modes on this device: \x1b[1;37mquiet\x1b[0m, \x1b[1;37mdefault\x1b[0m (balanced), \x1b[1;37mperf\x1b[0m.\n");
            return 1;
        }

        var mode = rawMode switch
        {
            "quiet" => PowerMode.Quiet,
            "default" or "balanced" => PowerMode.Default,
            "perf" or "performance" => PowerMode.Performance,
            "turbo" or "extreme" => PowerMode.Turbo,
            _ => PowerMode.Default
        };

        bool success = client.IsConnected
            ? await client.SetPowerModeAsync(mode)
            : await HardwareManager.Instance.SetPowerModeAsync(mode);

        if (success)
        {
            var actual = client.IsConnected
                ? await client.GetPowerModeAsync()
                : await HardwareManager.Instance.GetPowerModeAsync();

            if (actual != mode)
            {
                PrintNotice($"Requested power mode '{mode}', but firmware reported: \x1b[1;33m{actual}\x1b[0m.");
            }
            else
            {
                PrintOk($"Power Mode: \x1b[1;36m{actual}\x1b[0m");
            }
        }
        else
        {
            PrintError("Failed to switch power mode.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleLcdCommandAsync(IOpenPredatorClient client, string[] args)
    {
        IHardwareBackend backend = client.IsConnected
            ? new ClientHardwareBackend(client)
            : HardwareManager.CreateBestBackend();
        var caps = CapabilityProbe.Probe(backend);

        if (!caps.SupportsLcdOverdrive)
        {
            PrintNotice($"LCD Overdrive is not supported on this display panel / device ({caps.ModelName}).");
            return 1;
        }

        if (args.Length < 2)
        {
            bool cur = client.IsConnected
                ? await client.GetLcdOverdriveAsync()
                : await HardwareManager.Instance.GetLcdOverdriveAsync();
            PrintOk($"LCD Overdrive: {(cur ? "\x1b[1;32mEnabled (3ms)\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
            return 0;
        }

        bool enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1] == "1";
        bool success = client.IsConnected
            ? await client.SetLcdOverdriveAsync(enable)
            : await HardwareManager.Instance.SetLcdOverdriveAsync(enable);

        if (success)
        {
            bool actual = client.IsConnected
                ? await client.GetLcdOverdriveAsync()
                : await HardwareManager.Instance.GetLcdOverdriveAsync();

            if (actual != enable)
            {
                PrintNotice($"LCD Overdrive requested {(enable ? "Enabled" : "Disabled")}, but firmware reported: {(actual ? "Enabled (3ms)" : "Disabled")}.");
            }
            else
            {
                PrintOk($"LCD Overdrive: {(actual ? "\x1b[1;32mEnabled (3ms)\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
            }
        }
        else
        {
            PrintError("Failed to update LCD Overdrive.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleWinKeyCommandAsync(IOpenPredatorClient client, string[] args)
    {
        if (args.Length < 2)
        {
            bool isLocked = client.IsConnected
                ? await client.GetWinKeyLockAsync()
                : await HardwareManager.Instance.GetWinKeyLockAsync();
            PrintOk($"Windows Key: {(isLocked ? "\x1b[1;33mLocked (Disabled)\x1b[0m" : "\x1b[1;32mUnlocked (Active)\x1b[0m")}");
            return 0;
        }

        bool lockKey = args[1].Equals("lock", StringComparison.OrdinalIgnoreCase) || args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1] == "1";
        bool success = client.IsConnected
            ? await client.SetWinKeyLockAsync(lockKey)
            : await HardwareManager.Instance.SetWinKeyLockAsync(lockKey);

        if (success)
        {
            bool actual = client.IsConnected
                ? await client.GetWinKeyLockAsync()
                : await HardwareManager.Instance.GetWinKeyLockAsync();

            if (actual != lockKey)
            {
                PrintError($"Firmware rejected Windows key lock state (hardware reported: {(actual ? "Locked" : "Unlocked")}).");
                return 1;
            }

            PrintOk($"Windows Key: {(actual ? "\x1b[1;33mLocked (Disabled)\x1b[0m" : "\x1b[1;32mUnlocked (Active)\x1b[0m")}");
        }
        else
        {
            PrintError("Failed to update Windows key state.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleRgbCommandAsync(IOpenPredatorClient client, string[] args)
    {
        IHardwareBackend backend = client.IsConnected
            ? new ClientHardwareBackend(client)
            : HardwareManager.CreateBestBackend();
        var caps = CapabilityProbe.Probe(backend);
        if (!caps.IsRgbKeyboard)
        {
            PrintNotice($"4-Zone RGB keyboard is not supported on this model ({caps.ModelName}).");
            Console.WriteLine("         This device features a monochrome keyboard backlight.");
            Console.WriteLine("         Adjust backlight brightness using: \x1b[1;36mopenpredator brightness <0-100>\x1b[0m\n");
            return 1;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Usage: openpredator rgb static <z1> <z2> <z3> <z4> | rgb effect <name> [speed] [bright]");
            return 1;
        }

        string sub = args[1].ToLowerInvariant();
        if (sub == "static")
        {
            string z1 = args.Length > 2 ? args[2] : "FF0000";
            string z2 = args.Length > 3 ? args[3] : "FF0000";
            string z3 = args.Length > 4 ? args[4] : "FF0000";
            string z4 = args.Length > 5 ? args[5] : "FF0000";

            var config = new RgbConfig
            {
                Effect = RgbEffectType.Static,
                Zone1 = RgbZoneColor.FromHex(z1),
                Zone2 = RgbZoneColor.FromHex(z2),
                Zone3 = RgbZoneColor.FromHex(z3),
                Zone4 = RgbZoneColor.FromHex(z4),
                Brightness = 5
            };

            bool success = client.IsConnected
                ? await client.SetRgbKeyboardAsync(config)
                : await HardwareManager.Instance.ApplyRgbConfigAsync(config);

            if (success)
            {
                PrintOk($"RGB 4-Zone Colors: \x1b[1;37m#{z1} #{z2} #{z3} #{z4}\x1b[0m");
            }
            else
            {
                PrintError("Failed to apply RGB colors.");
            }
            return success ? 0 : 1;
        }
        else if (sub == "effect")
        {
            string effectName = args.Length > 2 ? args[2].ToLowerInvariant() : "breathing";
            int speed = args.Length > 3 && int.TryParse(args[3], out int s) ? s : 5;
            int brightness = args.Length > 4 && int.TryParse(args[4], out int b) ? b : 5;

            var effect = effectName switch
            {
                "static" => RgbEffectType.Static,
                "breathing" => RgbEffectType.Breathing,
                "neon" => RgbEffectType.Neon,
                "wave" => RgbEffectType.Wave,
                "zoom" => RgbEffectType.Zoom,
                "shifting" => RgbEffectType.Shifting,
                _ => RgbEffectType.Breathing
            };

            var config = new RgbConfig
            {
                Effect = effect,
                Speed = speed,
                Brightness = brightness
            };

            bool success = client.IsConnected
                ? await client.SetRgbKeyboardAsync(config)
                : await HardwareManager.Instance.ApplyRgbConfigAsync(config);

            if (success)
            {
                PrintOk($"RGB Effect: \x1b[1;36m{effect}\x1b[0m (Speed: {speed}, Brightness: {brightness})");
            }
            else
            {
                PrintError("Failed to apply RGB effect.");
            }
            return success ? 0 : 1;
        }

        return 1;
    }

    private static async Task<int> HandleBrightnessCommandAsync(IOpenPredatorClient client, string[] args)
    {
        if (args.Length < 2)
        {
            int cur = client.IsConnected
                ? await client.GetKbBacklightAsync()
                : await HardwareManager.Instance.GetKbBacklightAsync();
            PrintOk($"Keyboard Backlight Brightness: \x1b[1;36m{cur}%\x1b[0m");
            return 0;
        }

        if (!int.TryParse(args[1], out int pct))
        {
            Console.WriteLine("Usage: openpredator brightness <0-100>");
            return 1;
        }

        bool success = client.IsConnected
            ? await client.SetKbBacklightAsync(pct)
            : await HardwareManager.Instance.SetKbBacklightAsync(pct);

        if (success)
        {
            int actual = client.IsConnected
                ? await client.GetKbBacklightAsync()
                : await HardwareManager.Instance.GetKbBacklightAsync();
            if (actual != pct)
            {
                PrintOk($"Keyboard Backlight Brightness: \x1b[1;36m{actual}%\x1b[0m \x1b[90m(hardware quantized to 5-stage step)\x1b[0m");
            }
            else
            {
                PrintOk($"Keyboard Backlight Brightness: \x1b[1;36m{actual}%\x1b[0m");
            }
        }
        else
        {
            PrintError("Failed to set keyboard backlight brightness.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleKbTimeoutCommandAsync(IOpenPredatorClient client, string[] args)
    {
        if (args.Length < 2)
        {
            bool cur = client.IsConnected
                ? await client.GetKbTimeoutAsync()
                : await HardwareManager.Instance.GetKbTimeoutAsync();
            PrintOk($"Keyboard Backlight Sleep Timer: {(cur ? "\x1b[1;32mEnabled (30s auto-off)\x1b[0m" : "\x1b[90mDisabled (Always on)\x1b[0m")}");
            return 0;
        }

        bool enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1] == "1";
        bool success = client.IsConnected
            ? await client.SetKbTimeoutAsync(enable)
            : await HardwareManager.Instance.SetKbTimeoutAsync(enable);

        if (success)
        {
            bool actual = client.IsConnected
                ? await client.GetKbTimeoutAsync()
                : await HardwareManager.Instance.GetKbTimeoutAsync();

            if (actual != enable)
            {
                PrintError($"Firmware did not apply sleep timer setting (hardware reported: {(actual ? "Enabled (30s auto-off)" : "Disabled (Always on)")}).");
                return 1;
            }

            PrintOk($"Keyboard Backlight Sleep Timer: {(actual ? "\x1b[1;32mEnabled (30s auto-off)\x1b[0m" : "\x1b[90mDisabled (Always on)\x1b[0m")}");
        }
        else
        {
            PrintError("Failed to update keyboard sleep timer.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleConfigCommandAsync(string[] args)
    {
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "show";
        var configManager = ConfigManager.Instance;

        switch (sub)
        {
            case "path":
                PrintOk($"Configuration Path: \x1b[1;37m{configManager.ConfigFilePath}\x1b[0m");
                return 0;

            case "apply":
            case "restore":
                Console.WriteLine($"\x1b[90mApplying configuration from {configManager.ConfigFilePath} to hardware...\x1b[0m");
                await configManager.ApplyToHardwareAsync(HardwareManager.Instance);
                PrintOk("Hardware configuration restored successfully.");
                return 0;

            case "show":
            default:
                Console.WriteLine($"\x1b[1;36mConfiguration File:\x1b[0m {configManager.ConfigFilePath}\n");
                var cfg = configManager.Current;
                string json = System.Text.Json.JsonSerializer.Serialize(cfg, OpenPredatorJsonContext.Default.UserConfig);
                Console.WriteLine(json);
                return 0;
        }
    }

    private static Task<int> HandleCapabilitiesCommandAsync(IOpenPredatorClient client, string[] args)
    {
        IHardwareBackend backend = client.IsConnected
            ? new ClientHardwareBackend(client)
            : HardwareManager.CreateBestBackend();

        var caps = CapabilityProbe.Probe(backend);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("        Hardware Capabilities & Discovery        ");
        Console.WriteLine("=================================================");
        Console.ResetColor();

        string platform = OperatingSystem.IsWindows()
            ? "Windows (Kernel ACPI-WMI & SMBIOS)"
            : (OperatingSystem.IsLinux() ? "Linux (wmi-bmof / ec_sys & sysfs)" : "Generic");

        string modelDisplay = !string.IsNullOrWhiteSpace(caps.PartNumber)
            ? $"{caps.ModelName} (Part: {caps.PartNumber})"
            : caps.ModelName;
        Console.WriteLine($"  Device Model          : \x1b[1;37m{modelDisplay}\x1b[0m");
        Console.WriteLine($"  Hardware Series       : {(caps.IsPredator ? "\x1b[1;36mPredator Series\x1b[0m" : "\x1b[1;32mNitro Series\x1b[0m")}");
        Console.WriteLine($"  Platform Driver       : \x1b[1;37m{platform}\x1b[0m");
        Console.WriteLine($"  Detected Fans         : \x1b[1;37m{caps.FanCount} Fans\x1b[0m (CPU, GPU{(caps.HasSystemFan ? ", Aux/System" : "")})");
        Console.WriteLine($"  CoolBoost Technology  : {(caps.SupportsCoolBoost ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");
        Console.WriteLine($"  Hardware Power Modes  : {(caps.SupportsOperatingModes ? $"\x1b[1;32mSupported\x1b[0m ({string.Join(", ", caps.SupportedOperatingModes)})" : "\x1b[90mNot Supported (CoolBoost Fan Table)\x1b[0m")}");
        Console.WriteLine($"  Keyboard Backlight    : {(caps.IsRgbKeyboard ? "\x1b[1;35m4-Zone RGB Matrix\x1b[0m" : "\x1b[1;31mMonochrome Red/White\x1b[0m")}");
        Console.WriteLine($"  Backlight Timeout     : {(caps.SupportsBacklightTimeout ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");
        Console.WriteLine($"  LCD Overdrive         : {(caps.SupportsLcdOverdrive ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");
        Console.WriteLine($"  Hardware MUX Switch   : {(caps.SupportsDiscreteGpuSwitch ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");
        Console.WriteLine($"  Windows Key Lock      : {(caps.SupportsWinKeyLock ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");
        Console.WriteLine($"  CPU Overclocking      : {(caps.SupportsCpuOverclock ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");
        Console.WriteLine($"  GPU Overclocking      : {(caps.SupportsGpuOverclock ? "\x1b[1;32mSupported\x1b[0m" : "\x1b[90mNot Supported\x1b[0m")}");

        bool verbose = args.Length > 1 && (args[1] is "--verbose" or "-v" or "--diag");
        if (verbose && !string.IsNullOrWhiteSpace(caps.DiagnosticsLog))
        {
            Console.WriteLine();
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("--- Motherboard Diagnostic Log ---");
            Console.WriteLine(caps.DiagnosticsLog.TrimEnd());
            Console.ResetColor();
        }
        else
        {
            Console.WriteLine("\n\x1b[90mPass --verbose to view raw firmware ACPI/WMI query responses.\x1b[0m");
        }

        return Task.FromResult(0);
    }
}


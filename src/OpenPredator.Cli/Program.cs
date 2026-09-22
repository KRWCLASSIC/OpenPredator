using System;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Client;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Services;

namespace OpenPredator.Cli;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args[0].Equals("help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("--help", StringComparison.OrdinalIgnoreCase) || args[0].Equals("-h", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        string cmd = args[0].ToLowerInvariant();
        using var client = new OpenPredatorClient();

        bool connected = await client.ConnectAsync(1500);
        if (!connected)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Notice] No background daemon (OpenPredator Service / OEM PSSvc) active on pipe.");
            Console.WriteLine("         Falling back to direct hardware access (ACPI WMI + SMBIOS + NVML)...\n");
            Console.ResetColor();
        }

        switch (cmd)
        {
            case "status":
            case "sensors":
            case "sensor":
                return await ShowStatusAsync(client);

            case "watch":
            case "top":
                return await WatchStatusAsync(client);

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
            case "led":
                return await HandleRgbCommandAsync(client, args);

            case "brightness":
            case "backlight":
                return await HandleBrightnessCommandAsync(client, args);

            case "kb-timeout":
            case "timeout":
                return await HandleKbTimeoutCommandAsync(client, args);

            case "config":
                return await HandleConfigCommandAsync(args);

            case "diag":
            case "benchmark":
            case "test":
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("Hardware diagnostic and benchmarking tools are part of OpenPredator Testing Suite.");
                Console.WriteLine("Run: openpredator-testsuite.exe diag\n");
                Console.ResetColor();
                return 0;

            default:
                PrintError($"Unknown command: {args[0]}");
                Console.WriteLine();
                PrintHelp();
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

    private static void PrintHelp()
    {
        var caps = HardwareManager.DetectCapabilities();

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=================================================");
        Console.WriteLine("                OpenPredator CLI                 ");
        Console.WriteLine("     Hardware Control & Telemetry Utility        ");
        Console.WriteLine("=================================================");
        Console.ResetColor();

        string series = caps.IsPredator ? "Predator Series" : "Nitro Series";
        Console.WriteLine($"\x1b[90mDevice: {caps.ModelName} ({series})\x1b[0m\n");

        Console.WriteLine("Usage: openpredator <command> [arguments]\n");

        Console.WriteLine("\x1b[1;36mMonitoring & Telemetry:\x1b[0m");
        Console.WriteLine("  status / sensors                    Display live temperatures, fan speeds & GPU metrics");
        Console.WriteLine("  watch / top                         Live terminal dashboard updating every second\n");

        Console.WriteLine("\x1b[1;36mFan & Cooling:\x1b[0m");
        Console.WriteLine("  fan auto                            Set fan mode to Auto (firmware dynamic curve)");
        Console.WriteLine("  fan max                             Set fans to Maximum (100% speed)");
        Console.WriteLine("  fan custom <cpu%> <gpu%>            Set custom fan percentages (0-100%)");
        Console.WriteLine("  coolboost on|off                    Toggle CoolBoost technology (extra fan headroom)\n");

        Console.WriteLine("\x1b[1;36mPower & Performance:\x1b[0m");
        if (caps.IsPredator)
        {
            Console.WriteLine("  mode quiet|default|perf|turbo       Switch power & performance profile");
        }
        else
        {
            Console.WriteLine("  mode quiet|default|perf             Switch power & performance profile");
        }
        Console.WriteLine();

        Console.WriteLine("\x1b[1;36mKeyboard Backlight:\x1b[0m");
        if (caps.IsRgbKeyboard)
        {
            Console.WriteLine("  rgb static <z1> <z2> <z3> <z4>      Set 4-zone static hex colors (e.g. FF0000 00FF00 ...)");
            Console.WriteLine("  rgb effect <type> [speed] [bright]  Set dynamic effect (breathing, neon, wave, zoom, shifting)");
        }
        Console.WriteLine("  brightness / backlight <0-100>      Set keyboard backlight brightness percentage");
        Console.WriteLine("  kb-timeout on|off                   Toggle 30-second keyboard backlight sleep timer\n");

        if (caps.SupportsLcdOverdrive || caps.IsPredator)
        {
            Console.WriteLine("\x1b[1;36mDisplay & Controls:\x1b[0m");
            if (caps.SupportsLcdOverdrive)
            {
                Console.WriteLine("  lcd on|off                          Toggle LCD panel 3ms Overdrive");
            }
            Console.WriteLine("  winkey lock|unlock                  Lock/unlock Windows key during gaming\n");
        }
        else
        {
            Console.WriteLine("\x1b[1;36mSystem Controls:\x1b[0m");
            Console.WriteLine("  winkey lock|unlock                  Lock/unlock Windows key during gaming\n");
        }

        Console.WriteLine("\x1b[1;36mConfiguration:\x1b[0m");
        Console.WriteLine("  config show|path|apply              Display or apply persistent hardware configuration");
        Console.WriteLine("  help                                Show this help screen\n");
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
            Console.WriteLine("Usage: openpredator fan auto | max | custom <cpu%> <gpu%>");
            return 1;
        }

        string sub = args[1].ToLowerInvariant();
        bool success = false;

        switch (sub)
        {
            case "auto":
                success = client.IsConnected
                    ? await client.SetFanModeAsync(FanMode.Auto)
                    : await HardwareManager.Instance.SetFanModeAsync(FanMode.Auto);
                if (success) PrintOk("Fan Mode: \x1b[1;36mAuto\x1b[0m (Dynamic firmware curve)");
                else PrintError("Failed to set fan mode to Auto.");
                break;

            case "max":
                success = client.IsConnected
                    ? await client.SetFanModeAsync(FanMode.Max)
                    : await HardwareManager.Instance.SetFanModeAsync(FanMode.Max);
                if (success) PrintOk("Fan Mode: \x1b[1;31mMax\x1b[0m (100% Speed)");
                else PrintError("Failed to set fan mode to Max.");
                break;

            case "custom":
                int cpuPct = args.Length > 2 && int.TryParse(args[2], out int c) ? c : 50;
                int gpuPct = args.Length > 3 && int.TryParse(args[3], out int g) ? g : 50;

                success = client.IsConnected
                    ? await client.SetFanModeAsync(FanMode.Custom, cpuPercentage: cpuPct, gpuPercentage: gpuPct, cpuAuto: false, gpuAuto: false)
                    : await HardwareManager.Instance.ApplyFanConfigAsync(new FanConfig
                    {
                        Mode = FanMode.Custom,
                        CpuPercentage = cpuPct,
                        GpuPercentage = gpuPct,
                        CpuCustomAuto = false,
                        GpuCustomAuto = false,
                        CoolBoost = HardwareManager.Instance.CurrentFanConfig.CoolBoost
                    });
                if (success) PrintOk($"Custom Fan Speeds: CPU \x1b[1;36m{cpuPct}%\x1b[0m | GPU \x1b[1;36m{gpuPct}%\x1b[0m");
                else PrintError("Failed to set custom fan speeds.");
                break;

            default:
                PrintError($"Unknown fan subcommand: {sub}");
                Console.WriteLine("Usage: openpredator fan auto | max | custom <cpu%> <gpu%>");
                return 1;
        }

        return success ? 0 : 1;
    }

    private static async Task<int> HandleCoolBoostCommandAsync(IOpenPredatorClient client, string[] args)
    {
        if (args.Length < 2)
        {
            bool cur = client.IsConnected
                ? await client.GetCoolBoostAsync()
                : await HardwareManager.Instance.GetCoolBoostAsync();
            PrintOk($"CoolBoost: {(cur ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
            return 0;
        }

        bool enable = args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1] == "1";
        bool success = client.IsConnected
            ? await client.SetCoolBoostAsync(enable)
            : await HardwareManager.Instance.SetCoolBoostAsync(enable);

        if (success)
        {
            PrintOk($"CoolBoost: {(enable ? "\x1b[1;32mEnabled\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
        }
        else
        {
            PrintError("Failed to update CoolBoost state.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleModeCommandAsync(IOpenPredatorClient client, string[] args)
    {
        var caps = HardwareManager.DetectCapabilities();

        if (args.Length < 2)
        {
            string avail = caps.IsPredator ? "quiet | default | perf | turbo" : "quiet | default | perf";
            Console.WriteLine($"Usage: openpredator mode {avail}");
            return 1;
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
            PrintOk($"Power Mode: \x1b[1;36m{mode}\x1b[0m");
        }
        else
        {
            PrintError("Failed to switch power mode.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleLcdCommandAsync(IOpenPredatorClient client, string[] args)
    {
        var caps = HardwareManager.DetectCapabilities();

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
            PrintOk($"LCD Overdrive: {(enable ? "\x1b[1;32mEnabled (3ms)\x1b[0m" : "\x1b[90mDisabled\x1b[0m")}");
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
            Console.WriteLine("Usage: openpredator winkey lock | unlock");
            return 1;
        }

        bool lockKey = args[1].Equals("lock", StringComparison.OrdinalIgnoreCase) || args[1].Equals("on", StringComparison.OrdinalIgnoreCase) || args[1] == "1";
        bool success = client.IsConnected
            ? await client.SetWinKeyLockAsync(lockKey)
            : await HardwareManager.Instance.SetWinKeyLockAsync(lockKey);

        if (success)
        {
            PrintOk($"Windows Key: {(lockKey ? "\x1b[1;33mLocked (Disabled)\x1b[0m" : "\x1b[1;32mUnlocked (Active)\x1b[0m")}");
        }
        else
        {
            PrintError("Failed to update Windows key state.");
        }
        return success ? 0 : 1;
    }

    private static async Task<int> HandleRgbCommandAsync(IOpenPredatorClient client, string[] args)
    {
        var caps = HardwareManager.DetectCapabilities();
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
        if (args.Length < 2 || !int.TryParse(args[1], out int pct))
        {
            Console.WriteLine("Usage: openpredator brightness <0-100>");
            return 1;
        }

        bool success = client.IsConnected
            ? await client.SetKbBacklightAsync(pct)
            : await HardwareManager.Instance.SetKbBacklightAsync(pct);

        if (success)
        {
            PrintOk($"Keyboard Backlight Brightness: \x1b[1;36m{pct}%\x1b[0m");
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
            PrintOk($"Keyboard Backlight Sleep Timer: {(enable ? "\x1b[1;32mEnabled (30s auto-off)\x1b[0m" : "\x1b[90mDisabled (Always on)\x1b[0m")}");
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
}


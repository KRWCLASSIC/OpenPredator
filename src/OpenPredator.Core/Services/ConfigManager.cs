using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using OpenPredator.Core.Models;

namespace OpenPredator.Core.Services;

public sealed class ConfigManager
{
    private static readonly Lazy<ConfigManager> _instance = new(() => new ConfigManager());
    public static ConfigManager Instance => _instance.Value;

    private readonly object _lock = new();
    private UserConfig _current;
    private readonly string _configFilePath;

    public UserConfig Current
    {
        get
        {
            lock (_lock)
            {
                return _current;
            }
        }
        set
        {
            lock (_lock)
            {
                _current = value;
            }
        }
    }

    public string ConfigFilePath => _configFilePath;

    public ConfigManager()
    {
        _configFilePath = ResolveConfigPath();
        _current = LoadInternal();
    }

    public static string ResolveConfigPath()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "OpenPredator", "config.json");
        }
        
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openpredator", "config.json");
    }

    private UserConfig LoadInternal()
    {
        try
        {
            if (File.Exists(_configFilePath))
            {
                string json = File.ReadAllText(_configFilePath);
                var loaded = JsonSerializer.Deserialize(json, OpenPredatorJsonContext.Default.UserConfig);
                if (loaded != null) return loaded;
            }
        }
        catch { }

        return new UserConfig();
    }

    public UserConfig Load()
    {
        lock (_lock)
        {
            _current = LoadInternal();
            return _current;
        }
    }

    public void Save(UserConfig config)
    {
        lock (_lock)
        {
            _current = config;
            try
            {
                string? dir = Path.GetDirectoryName(_configFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonSerializer.Serialize(config, OpenPredatorJsonContext.Default.UserConfig);
                File.WriteAllText(_configFilePath, json);
            }
            catch { }
        }
    }

    public async Task ApplyToHardwareAsync(HardwareManager hardware, UserConfig? config = null)
    {
        var cfg = config ?? Current;
        
        try
        {
            // 1. Fan mode & Speeds & CoolBoost
            await hardware.ApplyFanConfigAsync(cfg.Fan);

            // 2. Power / Performance Mode
            await hardware.SetPowerModeAsync(cfg.PowerMode);

            // 3. Keyboard Backlight Brightness
            await hardware.SetKbBacklightAsync(cfg.KeyboardBrightness);

            // 4. Keyboard Timeout
            await hardware.SetKbTimeoutAsync(cfg.KeyboardTimeout);

            // 5. LCD Overdrive (if supported)
            if (hardware.Capabilities.SupportsLcdOverdrive)
            {
                await hardware.SetLcdOverdriveAsync(cfg.LcdOverdrive);
            }

            // 6. Windows Key Lock
            await hardware.SetWinKeyLockAsync(cfg.WinKeyLock);

            // 7. RGB Lighting (if RGB keyboard)
            if (hardware.Capabilities.IsRgbKeyboard)
            {
                await hardware.ApplyRgbConfigAsync(cfg.Rgb);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[ConfigManager] Note: Could not apply all settings to hardware: {ex.Message}");
        }
    }
}

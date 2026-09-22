using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Client;
using OpenPredator.Core.Backends;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Services;
using OpenPredator.TestingSuite.Diagnostics;
using OpenPredator.TestingSuite.Services;

namespace OpenPredator.TestingSuite;

public enum TreeNodeType
{
    Branch,
    Radio,
    Toggle,
    Slider,
    Action
}

public class TreeNode
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public TreeNodeType Type { get; set; }
    public bool IsExpanded { get; set; } = true;
    public List<TreeNode> Children { get; } = new();

    public Func<string>? GetValueText { get; set; }
    public Func<bool>? GetIsActive { get; set; }
    public Func<Task>? OnActivateAsync { get; set; }
    public Func<int, Task>? OnAdjustAsync { get; set; }
}

public class FlatTreeItem
{
    public TreeNode Node { get; }
    public TreeNode? ParentBranch { get; }
    public int Depth { get; }
    public bool IsLastSibling { get; }
    public bool ParentIsLast { get; }

    public FlatTreeItem(TreeNode node, TreeNode? parentBranch, int depth, bool isLastSibling, bool parentIsLast)
    {
        Node = node;
        ParentBranch = parentBranch;
        Depth = depth;
        IsLastSibling = isLastSibling;
        ParentIsLast = parentIsLast;
    }
}

public class InteractiveTui
{
    private readonly IOpenPredatorClient _client;
    private readonly TerminalEngine _term;

    private SensorData _sensors = new();
    private string _statusMessage = "Ready. Use Arrow keys + Enter to navigate, or direct hotkeys.";
    private string _acerServiceStatus = "UNKNOWN";
    private string _openPredatorStatus = "UNKNOWN";
    private bool _coolBoost = false;
    private bool _lcdOverdrive = false;
    private bool _winKeyLock = false;
    private bool _kbTimeout = true;
    private int _cpuCustomPct = 75;
    private int _gpuCustomPct = 85;
    private FanMode _cpuFanMode = FanMode.Auto;
    private FanMode _gpuFanMode = FanMode.Auto;
    private PowerMode _powerMode = PowerMode.Default;
    private string _rgbMode = "Static Red";
    private int _brightness = 4;

    private readonly DeviceCapabilities _caps;
    private readonly List<TreeNode> _rootBranches = new();
    private int _selectedIndex = 0;
    private int _scrollOffset = 0;

    public InteractiveTui(IOpenPredatorClient client, TerminalEngine term)
    {
        _client = client;
        _term = term;
        _caps = HardwareManager.Instance.Capabilities;

        var cfg = ConfigManager.Instance.Current;
        _coolBoost = cfg.Fan.CoolBoost;
        _cpuCustomPct = cfg.Fan.CpuPercentage;
        _gpuCustomPct = cfg.Fan.GpuPercentage;
        _cpuFanMode = cfg.Fan.CpuCustomAuto ? FanMode.Auto : (cfg.Fan.Mode == FanMode.Max ? FanMode.Max : FanMode.Custom);
        _gpuFanMode = cfg.Fan.GpuCustomAuto ? FanMode.Auto : (cfg.Fan.Mode == FanMode.Max ? FanMode.Max : FanMode.Custom);
        _powerMode = cfg.PowerMode;
        _brightness = Math.Clamp((int)Math.Round(cfg.KeyboardBrightness / 25.0), 0, 4);
        _kbTimeout = cfg.KeyboardTimeout;
        _lcdOverdrive = cfg.LcdOverdrive;
        _winKeyLock = cfg.WinKeyLock;

        BuildTreeModel();
    }

    private void BuildTreeModel()
    {
        _rootBranches.Clear();

        // -------------------------------------------------------------
        // BRANCH 1: Fan & Thermal Management (Independent per-fan + presets)
        // -------------------------------------------------------------
        var fanBranch = new TreeNode
        {
            Id = "fans_branch",
            Title = "Fan & Thermal Management",
            Type = TreeNodeType.Branch,
            IsExpanded = true
        };

        // Presets: Both Fans
        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_both_auto",
            Title = "All Fans: Auto (Dynamic Curves)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _cpuFanMode == FanMode.Auto && _gpuFanMode == FanMode.Auto,
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Auto;
                _gpuFanMode = FanMode.Auto;
                await ApplyFanStateAsync();
                _statusMessage = "Both Fans set to AUTO (firmware curves).";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_both_max",
            Title = "All Fans: Maximum (100% Full Speed)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _cpuFanMode == FanMode.Max && _gpuFanMode == FanMode.Max,
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Max;
                _gpuFanMode = FanMode.Max;
                await ApplyFanStateAsync();
                _statusMessage = "Both Fans set to MAX (100% Full Speed).";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_both_custom",
            Title = "All Fans: Custom (Manual Target %)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _cpuFanMode == FanMode.Custom && _gpuFanMode == FanMode.Custom,
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Custom;
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"Both Fans set to CUSTOM (CPU: {_cpuCustomPct}%, GPU: {_gpuCustomPct}%).";
            }
        });

        // CPU Fan Controls
        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_cpu_auto",
            Title = "CPU Fan: Auto Mode",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _cpuFanMode == FanMode.Auto,
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Auto;
                await ApplyFanStateAsync();
                _statusMessage = "CPU Fan set to AUTO.";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_cpu_max",
            Title = "CPU Fan: Maximum Mode (100%)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _cpuFanMode == FanMode.Max,
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Max;
                await ApplyFanStateAsync();
                _statusMessage = "CPU Fan set to MAXIMUM.";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_cpu_custom",
            Title = "CPU Fan: Custom Target Mode",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _cpuFanMode == FanMode.Custom,
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"CPU Fan set to CUSTOM ({_cpuCustomPct}%).";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_cpu_slider",
            Title = "CPU Fan Target Duty Cycle",
            Type = TreeNodeType.Slider,
            GetValueText = () => $"◄ [ {_cpuCustomPct,3}% ] ►  ({_sensors.CpuFanRpm,4} RPM)",
            OnAdjustAsync = async (delta) =>
            {
                _cpuCustomPct = Math.Clamp(_cpuCustomPct + delta * 5, 0, 100);
                _cpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"CPU Fan Target: {_cpuCustomPct}%.";
            },
            OnActivateAsync = async () =>
            {
                _cpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"CPU Fan Target applied: {_cpuCustomPct}%.";
            }
        });

        // GPU Fan Controls
        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_gpu_auto",
            Title = "GPU Fan: Auto Mode",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _gpuFanMode == FanMode.Auto,
            OnActivateAsync = async () =>
            {
                _gpuFanMode = FanMode.Auto;
                await ApplyFanStateAsync();
                _statusMessage = "GPU Fan set to AUTO.";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_gpu_max",
            Title = "GPU Fan: Maximum Mode (100%)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _gpuFanMode == FanMode.Max,
            OnActivateAsync = async () =>
            {
                _gpuFanMode = FanMode.Max;
                await ApplyFanStateAsync();
                _statusMessage = "GPU Fan set to MAXIMUM.";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_gpu_custom",
            Title = "GPU Fan: Custom Target Mode",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _gpuFanMode == FanMode.Custom,
            OnActivateAsync = async () =>
            {
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"GPU Fan set to CUSTOM ({_gpuCustomPct}%).";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_gpu_slider",
            Title = "GPU Fan Target Duty Cycle",
            Type = TreeNodeType.Slider,
            GetValueText = () => $"◄ [ {_gpuCustomPct,3}% ] ►  ({_sensors.GpuFanRpm,4} RPM)",
            OnAdjustAsync = async (delta) =>
            {
                _gpuCustomPct = Math.Clamp(_gpuCustomPct + delta * 5, 0, 100);
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"GPU Fan Target: {_gpuCustomPct}%.";
            },
            OnActivateAsync = async () =>
            {
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"GPU Fan Target applied: {_gpuCustomPct}%.";
            }
        });

        fanBranch.Children.Add(new TreeNode
        {
            Id = "fan_coolboost",
            Title = "CoolBoost Fan RPM Offset",
            Type = TreeNodeType.Toggle,
            GetIsActive = () => _coolBoost,
            GetValueText = () => _coolBoost ? "[ENABLED ]" : "[DISABLED]",
            OnActivateAsync = async () =>
            {
                _coolBoost = !_coolBoost;
                await ApplyCoolBoostAsync(_coolBoost);
                bool verified = _client.IsConnected
                    ? await _client.GetCoolBoostAsync()
                    : await HardwareManager.Instance.GetCoolBoostAsync();
                _coolBoost = verified;
                _statusMessage = $"CoolBoost toggled {(_coolBoost ? "ON" : "OFF")} | Hardware Verified: {(verified ? "[ENABLED]" : "[DISABLED]")}";
            }
        });

        _rootBranches.Add(fanBranch);

        // -------------------------------------------------------------
        // BRANCH 2: Power & Performance Profiles
        // -------------------------------------------------------------
        var powerBranch = new TreeNode
        {
            Id = "power_branch",
            Title = "Power & Performance Profiles",
            Type = TreeNodeType.Branch,
            IsExpanded = true
        };

        powerBranch.Children.Add(new TreeNode
        {
            Id = "power_quiet",
            Title = "Quiet Profile (Silent Operation / Low TDP)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _powerMode == PowerMode.Quiet,
            OnActivateAsync = async () =>
            {
                _powerMode = PowerMode.Quiet;
                await ApplyPowerModeAsync(PowerMode.Quiet);
                var verified = _client.IsConnected
                    ? await _client.GetPowerModeAsync()
                    : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode set to QUIET | Hardware Verified: [{verified}]";
            }
        });

        powerBranch.Children.Add(new TreeNode
        {
            Id = "power_balanced",
            Title = "Balanced Profile (Default Factory TDP)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _powerMode == PowerMode.Default,
            OnActivateAsync = async () =>
            {
                _powerMode = PowerMode.Default;
                await ApplyPowerModeAsync(PowerMode.Default);
                var verified = _client.IsConnected
                    ? await _client.GetPowerModeAsync()
                    : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode set to BALANCED | Hardware Verified: [{verified}]";
            }
        });

        powerBranch.Children.Add(new TreeNode
        {
            Id = "power_perf",
            Title = "Performance Profile (High Performance / Elevated TDP & Fan Ramp)",
            Type = TreeNodeType.Radio,
            GetIsActive = () => _powerMode == PowerMode.Performance,
            OnActivateAsync = async () =>
            {
                _powerMode = PowerMode.Performance;
                await ApplyPowerModeAsync(PowerMode.Performance);
                var verified = _client.IsConnected
                    ? await _client.GetPowerModeAsync()
                    : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode set to PERFORMANCE | Hardware Verified: [{verified}]";
            }
        });

        if (_caps.IsPredator)
        {
            powerBranch.Children.Add(new TreeNode
            {
                Id = "power_turbo",
                Title = "Turbo Profile (Extreme Overclock & Max TDP)",
                Type = TreeNodeType.Radio,
                GetIsActive = () => _powerMode == PowerMode.Turbo,
                OnActivateAsync = async () =>
                {
                    _powerMode = PowerMode.Turbo;
                    await ApplyPowerModeAsync(PowerMode.Turbo);
                    var verified = _client.IsConnected
                        ? await _client.GetPowerModeAsync()
                        : await HardwareManager.Instance.GetPowerModeAsync();
                    _statusMessage = $"Power Mode set to TURBO | Hardware Verified: [{verified}]";
                }
            });
        }

        _rootBranches.Add(powerBranch);

        // -------------------------------------------------------------
        // BRANCH 3: Keyboard Backlight & Lighting
        // -------------------------------------------------------------
        var rgbBranch = new TreeNode
        {
            Id = "rgb_branch",
            Title = _caps.IsRgbKeyboard ? "RGB Lighting & Key Backlight" : "Keyboard Backlight (Red)",
            Type = TreeNodeType.Branch,
            IsExpanded = true
        };

        if (_caps.IsRgbKeyboard)
        {
            rgbBranch.Children.Add(new TreeNode
            {
                Id = "rgb_red",
                Title = "Static Predator Red Profile",
                Type = TreeNodeType.Radio,
                GetIsActive = () => _rgbMode == "Static Red",
                OnActivateAsync = async () =>
                {
                    _rgbMode = "Static Red";
                    await ApplyRgbConfigAsync(new RgbConfig
                    {
                        Effect = RgbEffectType.Static,
                        Zone1 = new RgbZoneColor(255, 0, 0),
                        Zone2 = new RgbZoneColor(255, 0, 0),
                        Zone3 = new RgbZoneColor(255, 0, 0),
                        Zone4 = new RgbZoneColor(255, 0, 0),
                        Brightness = _brightness
                    });
                    _statusMessage = "RGB Profile set to STATIC RED.";
                }
            });

            rgbBranch.Children.Add(new TreeNode
            {
                Id = "rgb_neon",
                Title = "Neon Color Flow Effect",
                Type = TreeNodeType.Radio,
                GetIsActive = () => _rgbMode == "Neon",
                OnActivateAsync = async () =>
                {
                    _rgbMode = "Neon";
                    await ApplyRgbConfigAsync(new RgbConfig
                    {
                        Effect = RgbEffectType.Neon,
                        Speed = 5,
                        Brightness = _brightness
                    });
                    _statusMessage = "RGB Profile set to NEON.";
                }
            });

            rgbBranch.Children.Add(new TreeNode
            {
                Id = "rgb_wave",
                Title = "Rainbow Wave Effect",
                Type = TreeNodeType.Radio,
                GetIsActive = () => _rgbMode == "Wave",
                OnActivateAsync = async () =>
                {
                    _rgbMode = "Wave";
                    await ApplyRgbConfigAsync(new RgbConfig
                    {
                        Effect = RgbEffectType.Wave,
                        Speed = 5,
                        Brightness = _brightness
                    });
                    _statusMessage = "RGB Profile set to WAVE.";
                }
            });
        }

        rgbBranch.Children.Add(new TreeNode
        {
            Id = "rgb_brightness_slider",
            Title = "Keyboard Backlight Brightness",
            Type = TreeNodeType.Slider,
            GetValueText = () => $"◄ [ {_brightness * 25,3}% ] ►",
            OnAdjustAsync = async (delta) =>
            {
                _brightness = Math.Clamp(_brightness + delta, 0, 4);
                int target = _brightness * 25;
                await ApplyKbBacklightAsync(target);
                int verified = _client.IsConnected
                    ? await _client.GetKbBacklightAsync()
                    : await HardwareManager.Instance.GetKbBacklightAsync();
                _statusMessage = $"Keyboard Backlight: {target}% | Hardware Verified: [{verified}%]";
            },
            OnActivateAsync = async () =>
            {
                _brightness = (_brightness + 1) % 5;
                int target = _brightness * 25;
                await ApplyKbBacklightAsync(target);
                int verified = _client.IsConnected
                    ? await _client.GetKbBacklightAsync()
                    : await HardwareManager.Instance.GetKbBacklightAsync();
                _statusMessage = $"Keyboard Backlight: {target}% | Hardware Verified: [{verified}%]";
            }
        });

        if (!_caps.IsRgbKeyboard)
        {
            rgbBranch.Children.Add(new TreeNode
            {
                Id = "kb_timeout",
                Title = "Backlight 30s Auto-Off Timeout",
                Type = TreeNodeType.Toggle,
                GetIsActive = () => _kbTimeout,
                GetValueText = () => _kbTimeout ? "[ENABLED ]" : "[DISABLED]",
                OnActivateAsync = async () =>
                {
                    _kbTimeout = !_kbTimeout;
                    if (_client.IsConnected)
                    {
                        await _client.SetKbTimeoutAsync(_kbTimeout);
                        _kbTimeout = await _client.GetKbTimeoutAsync();
                    }
                    else
                    {
                        await HardwareManager.Instance.SetKbTimeoutAsync(_kbTimeout);
                        _kbTimeout = await HardwareManager.Instance.GetKbTimeoutAsync();
                    }
                    _statusMessage = $"Backlight Auto-Off {(_kbTimeout ? "ENABLED (30s)" : "DISABLED")}.";
                }
            });
        }

        _rootBranches.Add(rgbBranch);

        // -------------------------------------------------------------
        // BRANCH 4: Display & Hardware Extras
        // -------------------------------------------------------------
        var extrasBranch = new TreeNode
        {
            Id = "extras_branch",
            Title = "Display & Hardware Extras",
            Type = TreeNodeType.Branch,
            IsExpanded = true
        };

        if (_caps.SupportsLcdOverdrive)
        {
            extrasBranch.Children.Add(new TreeNode
            {
                Id = "lcd_overdrive",
                Title = "LCD 3ms Overdrive Boost",
                Type = TreeNodeType.Toggle,
                GetIsActive = () => _lcdOverdrive,
                GetValueText = () => _lcdOverdrive ? "[ENABLED ]" : "[DISABLED]",
                OnActivateAsync = async () =>
                {
                    _lcdOverdrive = !_lcdOverdrive;
                    await ApplyLcdOverdriveAsync(_lcdOverdrive);
                    _statusMessage = $"LCD Overdrive {(_lcdOverdrive ? "ENABLED" : "DISABLED")}.";
                }
            });
        }

        extrasBranch.Children.Add(new TreeNode
        {
            Id = "winkey_lock",
            Title = "Windows & Menu Key Lock (Gaming Mode)",
            Type = TreeNodeType.Toggle,
            GetIsActive = () => _winKeyLock,
            GetValueText = () => _winKeyLock ? "[LOCKED  ]" : "[UNLOCKED]",
            OnActivateAsync = async () =>
            {
                _winKeyLock = !_winKeyLock;
                await ApplyWinKeyLockAsync(_winKeyLock);
                _statusMessage = $"Windows & Menu Key {(_winKeyLock ? "LOCKED" : "UNLOCKED")}.";
            }
        });

        _rootBranches.Add(extrasBranch);

        // -------------------------------------------------------------
        // BRANCH 5: Windows Services Management
        // -------------------------------------------------------------
        var servicesBranch = new TreeNode
        {
            Id = "services_branch",
            Title = "Windows Services & Daemon Management",
            Type = TreeNodeType.Branch,
            IsExpanded = true
        };

        servicesBranch.Children.Add(new TreeNode
        {
            Id = "svc_acer",
            Title = "Acer Predator Service (PSSvc)",
            Type = TreeNodeType.Action,
            GetValueText = () => $"[{_acerServiceStatus,-10}] -> [Enter: {(_acerServiceStatus == "RUNNING" ? "STOP" : "START")}]",
            OnActivateAsync = async () =>
            {
                var state = WindowsServiceManager.GetServiceState("PSSvc");
                if (state == ServiceState.Running || state == ServiceState.StartPending)
                {
                    _statusMessage = "Stopping Acer PSSvc service...";
                    bool stopOk = await WindowsServiceManager.StopServiceAsync("PSSvc");
                    _statusMessage = stopOk ? "Acer PSSvc service STOPPED." : "Stop failed (Admin required).";
                }
                else
                {
                    _statusMessage = "Starting Acer PSSvc service...";
                    bool startOk = await WindowsServiceManager.StartServiceAsync("PSSvc");
                    _statusMessage = startOk ? "Acer PSSvc service STARTED." : "Start failed (Admin required).";
                    if (startOk) await _client.ConnectAsync(1500);
                }
            }
        });

        servicesBranch.Children.Add(new TreeNode
        {
            Id = "svc_op",
            Title = "OpenPredator Daemon Service",
            Type = TreeNodeType.Action,
            GetValueText = () => $"[{_openPredatorStatus,-10}] -> [Enter: {(_openPredatorStatus == "RUNNING" ? "STOP" : "START")}]",
            OnActivateAsync = async () =>
            {
                var state = WindowsServiceManager.GetServiceState("OpenPredator");
                if (state == ServiceState.Running || state == ServiceState.StartPending)
                {
                    _statusMessage = "Stopping OpenPredator Service...";
                    bool stopOk = await WindowsServiceManager.StopServiceAsync("OpenPredator");
                    _statusMessage = stopOk ? "OpenPredator Service STOPPED." : "Stop failed (Admin required).";
                }
                else
                {
                    _statusMessage = "Starting OpenPredator Service...";
                    bool startOk = await WindowsServiceManager.StartServiceAsync("OpenPredator");
                    _statusMessage = startOk ? "OpenPredator Service STARTED." : "Start failed (Admin required).";
                    if (startOk) await _client.ConnectAsync(1500);
                }
            }
        });

        servicesBranch.Children.Add(new TreeNode
        {
            Id = "wmi_auto_diag",
            Title = "Automated Tri-Service Hardware Parity Benchmark",
            Type = TreeNodeType.Action,
            GetValueText = () => $"[Enter: RUN FULL AUTO BENCHMARK]",
            OnActivateAsync = async () =>
            {
                _statusMessage = "Executing Automated Tri-Service Benchmark (Direct WMI vs PSSvc vs OpenPredator)...";
                var diagEngine = new HardwareDiagnosticEngine();
                var results = await diagEngine.RunAutoComparisonAsync();
                int passed = results.FindAll(r => r.Verdict.Contains("PARITY") || r.Verdict.Contains("MATCH") || r.Verdict.Equals("OK")).Count;
                _statusMessage = $"[AUTO BENCHMARK COMPLETED] Verified: {passed}/{results.Count} ({((double)passed/results.Count)*100:F0}% Parity) (Report saved to disk)";
            }
        });

        servicesBranch.Children.Add(new TreeNode
        {
            Id = "wmi_diag",
            Title = "Single-Snapshot Hardware Diagnostic",
            Type = TreeNodeType.Action,
            GetValueText = () => $"[Enter: RUN SNAPSHOT DIAG]",
            OnActivateAsync = async () =>
            {
                _statusMessage = "Running Single-Snapshot Hardware & Pipe Diagnostic...";
                var diagEngine = new HardwareDiagnosticEngine();
                var results = await diagEngine.RunComparisonAsync();
                int passed = results.FindAll(r => r.Verdict.Contains("MATCH") || r.Verdict.Equals("OK") || r.Verdict.Contains("WMI OK")).Count;
                _statusMessage = $"[DIAG FINISHED] Passed: {passed}/{results.Count} | Gaming: {NativeWmi.GamingInstancePath} | APGe: {NativeWmi.GenericInstancePath} (Report saved to disk)";
            }
        });

        servicesBranch.Children.Add(new TreeNode
        {
            Id = "wmi_reset",
            Title = "Reset WMI Cache (Force Re-Init)",
            Type = TreeNodeType.Action,
            GetValueText = () => $"[Enter: RESET CACHE]",
            OnActivateAsync = async () =>
            {
                NativeWmi.ResetCache();
                _statusMessage = $"WMI cache cleared. Next hardware call will re-initialize.";
                await Task.CompletedTask;
            }
        });

        _rootBranches.Add(servicesBranch);
    }

    private List<FlatTreeItem> GetFlattenedTree()
    {
        var list = new List<FlatTreeItem>();
        for (int i = 0; i < _rootBranches.Count; i++)
        {
            var branch = _rootBranches[i];
            bool isLastBranch = (i == _rootBranches.Count - 1);
            list.Add(new FlatTreeItem(branch, null, 0, isLastBranch, false));

            if (branch.IsExpanded)
            {
                for (int j = 0; j < branch.Children.Count; j++)
                {
                    var child = branch.Children[j];
                    bool isLastChild = (j == branch.Children.Count - 1);
                    list.Add(new FlatTreeItem(child, branch, 1, isLastChild, isLastBranch));
                }
            }
        }
        return list;
    }

    public async Task RunAsync()
    {
        _term.ClearScreen();
        var cts = new CancellationTokenSource();

        // Background polling task for sensors & service statuses
        _ = Task.Run(async () =>
        {
            while (!cts.Token.IsCancellationRequested)
            {
                try
                {
                    _sensors = _client.IsConnected
                        ? await _client.GetSensorsAsync(cts.Token)
                        : await HardwareManager.Instance.GetSensorsAsync();

                    _coolBoost = _client.IsConnected
                        ? await _client.GetCoolBoostAsync(cts.Token)
                        : await HardwareManager.Instance.GetCoolBoostAsync();

                    int liveKb = _client.IsConnected
                        ? await _client.GetKbBacklightAsync(cts.Token)
                        : await HardwareManager.Instance.GetKbBacklightAsync();
                    if (liveKb >= 0)
                    {
                        _brightness = Math.Clamp((int)Math.Round(liveKb / 25.0), 0, 4);
                    }

                    var livePm = _client.IsConnected
                        ? await _client.GetPowerModeAsync(cts.Token)
                        : await HardwareManager.Instance.GetPowerModeAsync();
                    _powerMode = livePm;

                    if (!_caps.IsRgbKeyboard)
                    {
                        _kbTimeout = _client.IsConnected
                            ? await _client.GetKbTimeoutAsync(cts.Token)
                            : await HardwareManager.Instance.GetKbTimeoutAsync();
                    }

                    _acerServiceStatus = WindowsServiceManager.GetServiceState("PSSvc") switch
                    {
                        ServiceState.Running => "RUNNING",
                        ServiceState.Stopped => "STOPPED",
                        ServiceState.NotFound => "NOT INSTALLED",
                        var s => s.ToString().ToUpperInvariant()
                    };
                    _openPredatorStatus = WindowsServiceManager.GetServiceState("OpenPredator") switch
                    {
                        ServiceState.Running => "RUNNING",
                        ServiceState.Stopped => "STOPPED",
                        ServiceState.NotFound => "NOT INSTALLED",
                        var s => s.ToString().ToUpperInvariant()
                    };

                    // If disconnected and a service is running, attempt background auto-reconnect
                    if (!_client.IsConnected && (_acerServiceStatus == "RUNNING" || _openPredatorStatus == "RUNNING"))
                    {
                        await _client.ConnectAsync(1000, cts.Token);
                    }

                    await Task.Delay(1000, cts.Token);
                }
                catch { }
            }
        });

        // Main render & input loop
        while (!cts.Token.IsCancellationRequested)
        {
            var flatList = GetFlattenedTree();
            if (_selectedIndex >= flatList.Count)
            {
                _selectedIndex = Math.Max(0, flatList.Count - 1);
            }

            Render(flatList);

            // Input handling
            while (_term.PollInput(out var evt))
            {
                if (evt.Key == ConsoleKey.Escape || evt.Key == ConsoleKey.Q)
                {
                    cts.Cancel();
                    return;
                }

                await HandleInputAsync(evt.Key, evt.Char, flatList);
            }

            await Task.Delay(50);
        }
    }

    private async Task HandleInputAsync(ConsoleKey key, char ch, List<FlatTreeItem> flatList)
    {
        // 1. Navigation Keys
        switch (key)
        {
            case ConsoleKey.UpArrow:
                if (_selectedIndex > 0) _selectedIndex--;
                return;

            case ConsoleKey.DownArrow:
                if (_selectedIndex < flatList.Count - 1) _selectedIndex++;
                return;

            case ConsoleKey.Home:
                _selectedIndex = 0;
                return;

            case ConsoleKey.End:
                _selectedIndex = Math.Max(0, flatList.Count - 1);
                return;

            case ConsoleKey.PageUp:
                _selectedIndex = Math.Max(0, _selectedIndex - 5);
                return;

            case ConsoleKey.PageDown:
                _selectedIndex = Math.Min(flatList.Count - 1, _selectedIndex + 5);
                return;

            case ConsoleKey.LeftArrow:
                if (_selectedIndex >= 0 && _selectedIndex < flatList.Count)
                {
                    var item = flatList[_selectedIndex];
                    if (item.Node.Type == TreeNodeType.Branch && item.Node.IsExpanded)
                    {
                        item.Node.IsExpanded = false;
                        _statusMessage = $"Collapsed [{item.Node.Title}].";
                        return;
                    }
                    else if (item.Node.Type == TreeNodeType.Slider && item.Node.OnAdjustAsync != null)
                    {
                        await item.Node.OnAdjustAsync(-1);
                        return;
                    }
                    else if (item.ParentBranch != null)
                    {
                        // Jump cursor to parent branch
                        int parentIdx = flatList.FindIndex(f => f.Node == item.ParentBranch);
                        if (parentIdx >= 0) _selectedIndex = parentIdx;
                        return;
                    }
                }
                break;

            case ConsoleKey.RightArrow:
                if (_selectedIndex >= 0 && _selectedIndex < flatList.Count)
                {
                    var item = flatList[_selectedIndex];
                    if (item.Node.Type == TreeNodeType.Branch && !item.Node.IsExpanded)
                    {
                        item.Node.IsExpanded = true;
                        _statusMessage = $"Expanded [{item.Node.Title}].";
                        return;
                    }
                    else if (item.Node.Type == TreeNodeType.Slider && item.Node.OnAdjustAsync != null)
                    {
                        await item.Node.OnAdjustAsync(1);
                        return;
                    }
                }
                break;

            case ConsoleKey.Enter:
            case ConsoleKey.Spacebar:
                if (_selectedIndex >= 0 && _selectedIndex < flatList.Count)
                {
                    var item = flatList[_selectedIndex];
                    if (item.Node.Type == TreeNodeType.Branch)
                    {
                        item.Node.IsExpanded = !item.Node.IsExpanded;
                        _statusMessage = item.Node.IsExpanded ? $"Expanded [{item.Node.Title}]." : $"Collapsed [{item.Node.Title}].";
                    }
                    else if (item.Node.OnActivateAsync != null)
                    {
                        await item.Node.OnActivateAsync();
                    }
                }
                return;
        }

        // 2. Direct Hotkey Shortcuts
        char c = char.ToUpperInvariant(ch);
        switch (c)
        {
            case '1':
                _cpuFanMode = FanMode.Auto;
                _gpuFanMode = FanMode.Auto;
                await ApplyFanStateAsync();
                _statusMessage = "All Fans set to AUTO (firmware curves).";
                break;

            case '2':
                _cpuFanMode = FanMode.Max;
                _gpuFanMode = FanMode.Max;
                await ApplyFanStateAsync();
                _statusMessage = "All Fans set to MAX (100%).";
                break;

            case '3':
                _cpuFanMode = FanMode.Custom;
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"All Fans set to CUSTOM (CPU: {_cpuCustomPct}%, GPU: {_gpuCustomPct}%).";
                break;

            case '[':
                _cpuCustomPct = Math.Clamp(_cpuCustomPct - 5, 0, 100);
                _cpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"CPU Fan target: {_cpuCustomPct}% [GPU: {_gpuCustomPct}%].";
                break;

            case ']':
                _cpuCustomPct = Math.Clamp(_cpuCustomPct + 5, 0, 100);
                _cpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"CPU Fan target: {_cpuCustomPct}% [GPU: {_gpuCustomPct}%].";
                break;

            case ';':
            case '{':
                _gpuCustomPct = Math.Clamp(_gpuCustomPct - 5, 0, 100);
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"GPU Fan target: {_gpuCustomPct}% [CPU: {_cpuCustomPct}%].";
                break;

            case '\'':
            case '}':
                _gpuCustomPct = Math.Clamp(_gpuCustomPct + 5, 0, 100);
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"GPU Fan target: {_gpuCustomPct}% [CPU: {_cpuCustomPct}%].";
                break;

            case ',':
            case '<':
                _cpuCustomPct = Math.Clamp(_cpuCustomPct - 5, 0, 100);
                _gpuCustomPct = Math.Clamp(_gpuCustomPct - 5, 0, 100);
                _cpuFanMode = FanMode.Custom;
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"Both Fans decreased: CPU={_cpuCustomPct}%, GPU={_gpuCustomPct}%.";
                break;

            case '.':
            case '>':
                _cpuCustomPct = Math.Clamp(_cpuCustomPct + 5, 0, 100);
                _gpuCustomPct = Math.Clamp(_gpuCustomPct + 5, 0, 100);
                _cpuFanMode = FanMode.Custom;
                _gpuFanMode = FanMode.Custom;
                await ApplyFanStateAsync();
                _statusMessage = $"Both Fans increased: CPU={_cpuCustomPct}%, GPU={_gpuCustomPct}%.";
                break;

            case 'C':
                _coolBoost = !_coolBoost;
                await ApplyCoolBoostAsync(_coolBoost);
                bool cbVer = _client.IsConnected ? await _client.GetCoolBoostAsync() : await HardwareManager.Instance.GetCoolBoostAsync();
                _coolBoost = cbVer;
                _statusMessage = $"CoolBoost: {(_coolBoost ? "ON" : "OFF")} | Hardware Verified: {(cbVer ? "[ENABLED]" : "[DISABLED]")}";
                break;

            case 'U':
                _powerMode = PowerMode.Quiet;
                await ApplyPowerModeAsync(PowerMode.Quiet);
                var pmU = _client.IsConnected ? await _client.GetPowerModeAsync() : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode: QUIET | Hardware Verified: [{pmU}]";
                break;

            case 'B':
                _powerMode = PowerMode.Default;
                await ApplyPowerModeAsync(PowerMode.Default);
                var pmB = _client.IsConnected ? await _client.GetPowerModeAsync() : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode: BALANCED | Hardware Verified: [{pmB}]";
                break;

            case 'P':
                _powerMode = PowerMode.Performance;
                await ApplyPowerModeAsync(PowerMode.Performance);
                var pmP = _client.IsConnected ? await _client.GetPowerModeAsync() : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode: PERFORMANCE | Hardware Verified: [{pmP}]";
                break;

            case 'T':
                _powerMode = PowerMode.Turbo;
                await ApplyPowerModeAsync(PowerMode.Turbo);
                var pmT = _client.IsConnected ? await _client.GetPowerModeAsync() : await HardwareManager.Instance.GetPowerModeAsync();
                _statusMessage = $"Power Mode: TURBO | Hardware Verified: [{pmT}]";
                break;

            case 'G':
                _rgbMode = "Static Red";
                await ApplyRgbConfigAsync(new RgbConfig
                {
                    Effect = RgbEffectType.Static,
                    Zone1 = new RgbZoneColor(255, 0, 0),
                    Zone2 = new RgbZoneColor(255, 0, 0),
                    Zone3 = new RgbZoneColor(255, 0, 0),
                    Zone4 = new RgbZoneColor(255, 0, 0),
                    Brightness = _brightness
                });
                _statusMessage = "RGB Profile set to STATIC RED.";
                break;

            case 'N':
                _rgbMode = "Neon";
                await ApplyRgbConfigAsync(new RgbConfig
                {
                    Effect = RgbEffectType.Neon,
                    Speed = 5,
                    Brightness = _brightness
                });
                _statusMessage = "RGB Profile set to NEON effect.";
                break;

            case 'W':
                _rgbMode = "Wave";
                await ApplyRgbConfigAsync(new RgbConfig
                {
                    Effect = RgbEffectType.Wave,
                    Speed = 5,
                    Brightness = _brightness
                });
                _statusMessage = "RGB Profile set to WAVE effect.";
                break;

            case '+':
            case '=':
                _brightness = Math.Min(4, _brightness + 1);
                int bUp = _brightness * 25;
                await ApplyKbBacklightAsync(bUp);
                int vUp = _client.IsConnected ? await _client.GetKbBacklightAsync() : await HardwareManager.Instance.GetKbBacklightAsync();
                _statusMessage = $"Keyboard Backlight: {bUp}% | Hardware Verified: [{vUp}%]";
                break;

            case '-':
            case '_':
                _brightness = Math.Max(0, _brightness - 1);
                int bDn = _brightness * 25;
                await ApplyKbBacklightAsync(bDn);
                int vDn = _client.IsConnected ? await _client.GetKbBacklightAsync() : await HardwareManager.Instance.GetKbBacklightAsync();
                _statusMessage = $"Keyboard Backlight: {bDn}% | Hardware Verified: [{vDn}%]";
                break;

            case 'D':
                bool auditCb = _client.IsConnected ? await _client.GetCoolBoostAsync() : await HardwareManager.Instance.GetCoolBoostAsync();
                int auditKb = _client.IsConnected ? await _client.GetKbBacklightAsync() : await HardwareManager.Instance.GetKbBacklightAsync();
                bool auditTo = _client.IsConnected ? await _client.GetKbTimeoutAsync() : await HardwareManager.Instance.GetKbTimeoutAsync();
                var auditPm = _client.IsConnected ? await _client.GetPowerModeAsync() : await HardwareManager.Instance.GetPowerModeAsync();
                string pipeAudit = _client.IsConnected ? "PIPE" : "DIRECT";
                _statusMessage = $"[HARDWARE AUDIT] CB: {(auditCb ? "ON" : "OFF")} | KB: {auditKb}% (30s: {(auditTo ? "ON" : "OFF")}) | Mode: {auditPm} | Backend: {pipeAudit} | WMI: {Core.Backends.NativeWmi.LastError}";
                break;
        }
    }

    private async Task ApplyFanStateAsync()
    {
        if (_client.IsConnected)
        {
            await _client.SetFanModeAsync(
                FanMode.Custom,
                coolBoost: _coolBoost,
                cpuPercentage: _cpuCustomPct,
                gpuPercentage: _gpuCustomPct,
                cpuAuto: _cpuFanMode == FanMode.Auto,
                gpuAuto: _gpuFanMode == FanMode.Auto,
                cpuMode: _cpuFanMode,
                gpuMode: _gpuFanMode);
        }
        else
        {
            await HardwareManager.Instance.SetFanModeAsync(
                FanMode.Custom,
                coolBoost: _coolBoost,
                cpuPercentage: _cpuCustomPct,
                gpuPercentage: _gpuCustomPct,
                cpuAuto: _cpuFanMode == FanMode.Auto,
                gpuAuto: _gpuFanMode == FanMode.Auto,
                cpuMode: _cpuFanMode,
                gpuMode: _gpuFanMode);
        }
    }

    private async Task ApplyCoolBoostAsync(bool enabled)
    {
        if (_client.IsConnected)
        {
            await _client.SetCoolBoostAsync(enabled);
        }
        else
        {
            await HardwareManager.Instance.SetCoolBoostAsync(enabled);
        }
    }

    private async Task ApplyPowerModeAsync(PowerMode mode)
    {
        if (_client.IsConnected)
        {
            await _client.SetPowerModeAsync(mode);
        }
        else
        {
            await HardwareManager.Instance.SetPowerModeAsync(mode);
        }
    }

    private async Task ApplyRgbConfigAsync(RgbConfig config)
    {
        if (_client.IsConnected)
        {
            await _client.SetRgbKeyboardAsync(config);
        }
        else
        {
            await HardwareManager.Instance.ApplyRgbConfigAsync(config);
        }
    }

    private async Task ApplyKbBacklightAsync(int brightness)
    {
        if (_client.IsConnected)
        {
            await _client.SetKbBacklightAsync(brightness);
        }
        else
        {
            await HardwareManager.Instance.SetKbBacklightAsync(brightness);
        }
    }

    private async Task ApplyLcdOverdriveAsync(bool enabled)
    {
        if (_client.IsConnected)
        {
            await _client.SetLcdOverdriveAsync(enabled);
        }
        else
        {
            await HardwareManager.Instance.SetLcdOverdriveAsync(enabled);
        }
    }

    private async Task ApplyWinKeyLockAsync(bool locked)
    {
        if (_client.IsConnected)
        {
            await _client.SetWinKeyLockAsync(locked);
        }
        else
        {
            await HardwareManager.Instance.SetWinKeyLockAsync(locked);
        }
    }

    private void Render(List<FlatTreeItem> flatList)
    {
        try
        {
            Console.SetCursorPosition(0, 0);
        }
        catch
        {
            _term.MoveCursor(0, 0);
        }

        var sb = new StringBuilder();

        // Top Banner
        sb.AppendLine("\x1b[1;36m====================================================================================================\x1b[0m\x1b[K");
        sb.AppendLine($"\x1b[1;37m        OPENPREDATOR HARDWARE CONTROL & TESTING SUITE  |  MODEL: {_caps.ModelName.ToUpperInvariant(),-24}\x1b[0m\x1b[K");
        sb.AppendLine("\x1b[1;36m====================================================================================================\x1b[0m\x1b[K");

        // Pipe Status & Admin Status
        string connStr = _client.IsConnected
            ? "\x1b[1;32m[CONNECTED: \\\\.\\pipe\\predatorsense_service_namedpipe]\x1b[0m"
            : "\x1b[1;33m[DIRECT HARDWARE ACCESS MODE]\x1b[0m";

        string adminStr = WindowsServiceManager.IsAdministrator()
            ? "\x1b[1;32m[ADMIN: YES]\x1b[0m"
            : "\x1b[1;31m[⚠ NO ADMIN - WMI NEEDS ADMIN! START SERVICE OR RUN AS ADMIN]\x1b[0m";

        string wmiErr = NativeWmi.LastError.Length > 0 && NativeWmi.LastError != "None"
            ? $" | WMI: \x1b[1;33m{NativeWmi.LastError.Substring(0, Math.Min(40, NativeWmi.LastError.Length))}\x1b[0m"
            : "";

        sb.AppendLine($" Status      : {connStr}   {adminStr}{wmiErr}\x1b[K");

        // Live Telemetry Bar
        string cpuBar = MakeProgressBar(_sensors.CpuTemperature, 100, 8);
        string gpuBar = MakeProgressBar(_sensors.GpuTemperature, 100, 8);
        string vrmText = _sensors.SystemTemperature > 0 ? $"{_sensors.SystemTemperature}°C" : "N/A";

        sb.AppendLine($" Telemetry   : CPU: {cpuBar} {_sensors.CpuTemperature,2}°C ({_sensors.CpuFanRpm,4} RPM) | GPU: {gpuBar} {_sensors.GpuTemperature,2}°C ({_sensors.GpuFanRpm,4} RPM, {_sensors.GpuUsagePercent,2}%, {_sensors.GpuFrequencyMhz}MHz) | VRM: {vrmText}\x1b[K");
        sb.AppendLine("----------------------------------------------------------------------------------------------------\x1b[K");

        // Viewport Calculation to prevent terminal scrolling & flicker
        int winH = Math.Max(20, Console.WindowHeight);
        int headerLines = 8;
        int footerLines = 5;
        int maxVisible = Math.Max(4, winH - headerLines - footerLines);

        if (_selectedIndex < _scrollOffset) _scrollOffset = _selectedIndex;
        if (_selectedIndex >= _scrollOffset + maxVisible) _scrollOffset = _selectedIndex - maxVisible + 1;
        _scrollOffset = Math.Clamp(_scrollOffset, 0, Math.Max(0, flatList.Count - maxVisible));

        int renderCount = Math.Min(maxVisible, flatList.Count - _scrollOffset);
        string scrollInfo = flatList.Count > maxVisible
            ? $" \x1b[1;30m(Showing {_scrollOffset + 1}-{_scrollOffset + renderCount} of {flatList.Count})\x1b[0m"
            : "";

        // Render Tree Header
        sb.AppendLine($"\x1b[1;33m  [ OPENPREDATOR HARDWARE CONTROL TREE ]{scrollInfo}\x1b[0m\x1b[K");

        // Render Visible Items
        for (int i = 0; i < renderCount; i++)
        {
            int actualIdx = _scrollOffset + i;
            var item = flatList[actualIdx];
            bool isSelected = (actualIdx == _selectedIndex);

            string cursor = isSelected ? "\x1b[1;36m ► \x1b[0m" : "   ";
            string text = FormatTreeNode(item, isSelected);

            sb.AppendLine($"{cursor}{text}\x1b[K");
        }

        // Fill remaining viewport with blank lines to prevent ghost text
        for (int i = renderCount; i < maxVisible; i++)
        {
            sb.AppendLine("\x1b[K");
        }

        sb.AppendLine("----------------------------------------------------------------------------------------------------\x1b[K");
        sb.AppendLine($"\x1b[1;36m STATUS   :\x1b[0m \x1b[1;37m{_statusMessage}\x1b[0m\x1b[K");
        sb.AppendLine("\x1b[1;32m NAVIGATE :\x1b[0m \x1b[1;37m[↑/↓] Select Item   [Enter/Space] Activate/Toggle/Expand   [←/→] Adjust/Collapse   [Esc] Exit\x1b[0m\x1b[K");
        sb.AppendLine("\x1b[1;30m HOTKEYS  : 1-3 (Fans), [ ] (CPU %), ; ' (GPU %), C (CoolBoost), +/- (Brightness), U/B/P/T (Modes), D (Audit)\x1b[0m\x1b[K");
        sb.AppendLine("\x1b[1;36m====================================================================================================\x1b[0m\x1b[K");

        Console.Write(sb.ToString());
    }

    private static string FormatTreeNode(FlatTreeItem item, bool isSelected)
    {
        var node = item.Node;
        var sb = new StringBuilder();

        if (item.Depth == 0)
        {
            // Root Branch Node
            string connector = item.IsLastSibling ? "└─── " : "├─── ";
            string expandIcon = node.IsExpanded ? "\x1b[1;36m[▼]\x1b[0m" : "\x1b[1;33m[►]\x1b[0m";
            string titleColor = isSelected ? "\x1b[1;37;44m" : "\x1b[1;37m";
            string titleEnd = isSelected ? "\x1b[0m" : "\x1b[0m";

            sb.Append($"\x1b[1;30m{connector}\x1b[0m{expandIcon} {titleColor} {node.Title} {titleEnd}");
            if (!node.IsExpanded)
            {
                sb.Append(" \x1b[1;30m(Collapsed - Press Enter or → to expand)\x1b[0m");
            }
        }
        else
        {
            // Child Node (Level 1)
            string parentPrefix = item.ParentIsLast ? "     " : "\x1b[1;30m│    \x1b[0m";
            string connector = item.IsLastSibling ? "└─── " : "├─── ";
            sb.Append($"{parentPrefix}\x1b[1;30m{connector}\x1b[0m");

            string highlightStart = isSelected ? "\x1b[1;37;44m" : "";
            string highlightEnd = isSelected ? "\x1b[0m" : "";

            switch (node.Type)
            {
                case TreeNodeType.Radio:
                    bool isActiveRadio = node.GetIsActive?.Invoke() == true;
                    string radioDot = isActiveRadio ? "\x1b[1;32m(●)\x1b[0m" : "\x1b[1;30m( )\x1b[0m";
                    string radioTitleColor = isActiveRadio ? "\x1b[1;32m" : (isSelected ? "\x1b[1;37m" : "\x1b[0m");
                    sb.Append($"{radioDot} {highlightStart}{radioTitleColor}{node.Title}{highlightEnd}\x1b[0m");
                    break;

                case TreeNodeType.Toggle:
                    bool isActiveToggle = node.GetIsActive?.Invoke() == true;
                    string toggleBadge = isActiveToggle ? "\x1b[1;32m[ENABLED ]\x1b[0m" : "\x1b[1;33m[DISABLED]\x1b[0m";
                    if (node.Id == "winkey_lock")
                    {
                        toggleBadge = isActiveToggle ? "\x1b[1;31m[LOCKED  ]\x1b[0m" : "\x1b[1;32m[UNLOCKED]\x1b[0m";
                    }
                    string toggleIcon = isActiveToggle ? "\x1b[1;32m[X]\x1b[0m" : "\x1b[1;30m[ ]\x1b[0m";
                    sb.Append($"{toggleIcon} {highlightStart}\x1b[1;37m{node.Title,-38}\x1b[0m{highlightEnd} : {toggleBadge}");
                    break;

                case TreeNodeType.Slider:
                    string sliderVal = node.GetValueText?.Invoke() ?? "";
                    sb.Append($"\x1b[1;36m[♦]\x1b[0m {highlightStart}\x1b[1;37m{node.Title,-38}\x1b[0m{highlightEnd} : \x1b[1;33m{sliderVal}\x1b[0m");
                    break;

                case TreeNodeType.Action:
                    string actionVal = node.GetValueText?.Invoke() ?? "";
                    sb.Append($"\x1b[1;35m[►]\x1b[0m {highlightStart}\x1b[1;37m{node.Title,-38}\x1b[0m{highlightEnd} : \x1b[1;36m{actionVal}\x1b[0m");
                    break;
            }
        }

        return sb.ToString();
    }

    private static string MakeProgressBar(int current, int max, int width)
    {
        if (max <= 0) max = 100;
        int filled = (int)Math.Clamp((double)current / max * width, 0, width);
        int empty = width - filled;

        string color = current switch
        {
            > 80 => "\x1b[1;31m", // Red
            > 65 => "\x1b[1;33m", // Yellow
            _ => "\x1b[1;32m"      // Green
        };

        return $"{color}[{new string('█', filled)}{new string('░', empty)}]\x1b[0m";
    }
}

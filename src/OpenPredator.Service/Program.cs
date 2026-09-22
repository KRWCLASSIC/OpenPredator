using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Core.Services;
using OpenPredator.Service.Ipc;

namespace OpenPredator.Service;

public static class Program
{
    private static readonly CancellationTokenSource _cts = new();

    public static async Task<int> Main(string[] args)
    {
        string action = args.Length > 0 ? args[0].ToLowerInvariant() : "--run";

        switch (action)
        {
            case "--install":
            case "install":
                return InstallService();

            case "--uninstall":
            case "uninstall":
                return UninstallService();

            case "--status":
            case "status":
                return CheckStatus();

            case "--console":
            case "console":
                return await RunConsoleDaemonAsync();

            case "--run":
            case "run":
            default:
                if (OperatingSystem.IsWindows())
                {
                    // Try to run as Windows Service first
                    bool ranAsService = WindowsServiceHost.RunAsService(RunDaemonWorkerAsync);
                    if (ranAsService) return 0;
                }
                return await RunConsoleDaemonAsync();
        }
    }

    private static async Task RunDaemonWorkerAsync(CancellationToken ct)
    {
        var hardware = HardwareManager.Instance;

        // Restore saved settings on startup immediately
        await ConfigManager.Instance.ApplyToHardwareAsync(hardware);

        var pipeServer = new NamedPipeServer();
        pipeServer.Start();

        try
        {
            var tcs = new TaskCompletionSource();
            using var reg = ct.Register(() => tcs.TrySetResult());
            await tcs.Task;
        }
        finally
        {
            pipeServer.Stop();
        }
    }

    private static async Task<int> RunConsoleDaemonAsync()
    {
        try
        {
            Console.Title = "OpenPredator Service";
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=================================================");
            Console.WriteLine("              OpenPredator Service               ");
            Console.WriteLine("   Open-Source PredatorSense / NitroSense Daemon ");
            Console.WriteLine("=================================================");
            Console.ResetColor();
        }
        catch { }

        Console.CancelKeyPress += (s, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
        };

        var hardware = HardwareManager.Instance;
        Console.WriteLine($"[Hardware] Active Backend: {hardware.Backend.BackendName}");

        // Restore saved settings on startup immediately
        Console.WriteLine($"[Config] Restoring saved configuration from {ConfigManager.Instance.ConfigFilePath}...");
        await ConfigManager.Instance.ApplyToHardwareAsync(hardware);
        Console.WriteLine($"[Config] Hardware restored to saved state.");

        var pipeServer = new NamedPipeServer();
        pipeServer.Start();

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("[Service] Daemon is running in console mode. Press Ctrl+C to terminate.");
        Console.ResetColor();

        // Background monitor loop (prints telemetry every 10s if in console mode)
        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(10000, _cts.Token);
                var sensors = await hardware.GetSensorsAsync();
                Console.WriteLine($"[Telemetry] {sensors}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] {ex.Message}");
            }
        }

        Console.WriteLine("[Service] Shutting down pipe server...");
        pipeServer.Stop();
        Console.WriteLine("[Service] Exited cleanly.");
        return 0;
    }

    private static int InstallService()
    {
        if (OperatingSystem.IsWindows())
        {
            string exePath = Process.GetCurrentProcess().MainModule?.FileName ?? "OpenPredator.Service.exe";
            Console.WriteLine($"[Service] Registering Windows Service: OpenPredator -> {exePath}");

            // sc.exe create OpenPredator binPath= "..." start= auto
            var psi = new ProcessStartInfo("sc.exe", $"create OpenPredator binPath= \"{exePath} --run\" start= auto DisplayName= \"OpenPredator Service\"")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                Console.WriteLine("[Service] Installation command executed.");
                return proc?.ExitCode ?? 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to install service: {ex.Message}");
                return 1;
            }
        }
        else if (OperatingSystem.IsLinux())
        {
            string serviceUnit = """
            [Unit]
            Description=OpenPredator Service Daemon
            After=network.target

            [Service]
            Type=simple
            ExecStart=/usr/local/bin/openpredator-service --run
            Restart=always
            RestartSec=3

            [Install]
            WantedBy=multi-user.target
            """;
            File.WriteAllText("/etc/systemd/system/openpredator.service", serviceUnit);
            Console.WriteLine("[Service] Wrote /etc/systemd/system/openpredator.service. Run: sudo systemctl enable --now openpredator");
            return 0;
        }

        Console.WriteLine("[Service] Installation not supported on this OS.");
        return 1;
    }

    private static int UninstallService()
    {
        if (OperatingSystem.IsWindows())
        {
            var psi = new ProcessStartInfo("sc.exe", "delete OpenPredator")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            try
            {
                var proc = Process.Start(psi);
                proc?.WaitForExit();
                Console.WriteLine("[Service] Uninstalled Windows Service.");
                return proc?.ExitCode ?? 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error] Failed to delete service: {ex.Message}");
                return 1;
            }
        }
        return 0;
    }

    private static int CheckStatus()
    {
        var hardware = HardwareManager.Instance;
        Console.WriteLine($"Active Backend: {hardware.Backend.BackendName}");
        Console.WriteLine($"IsSupported: {hardware.Backend.IsSupported}");
        return 0;
    }
}

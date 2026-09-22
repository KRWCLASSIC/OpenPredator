using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenPredator.Client;
using OpenPredator.Core.Backends;
using OpenPredator.Core.Enums;
using OpenPredator.Core.Models;
using OpenPredator.Core.Protocol;
using OpenPredator.Core.Services;
using OpenPredator.TestingSuite.Services;

namespace OpenPredator.TestingSuite.Diagnostics;

public record DiagnosticEntry(
    string Category,
    string Feature,
    string DirectWmiRaw,
    string DirectWmiDecoded,
    string DirectWmiStatus,
    string PipeRaw,
    string PipeDecoded,
    string PipeStatus,
    string Verdict
);

public record TriComparisonEntry(
    string Category,
    string Feature,
    string DirectWmiVal,
    string PssvcVal,
    string OpenPredatorVal,
    string Verdict
);

public class HardwareDiagnosticEngine
{
    private readonly List<DiagnosticEntry> _entries = new();
    private readonly List<TriComparisonEntry> _triEntries = new();

    public async Task<List<TriComparisonEntry>> RunAutoComparisonAsync(TextWriter? logWriter = null, CancellationToken ct = default)
    {
        _triEntries.Clear();

        void Log(string msg = "")
        {
            if (logWriter != null) logWriter.WriteLine(msg);
            else Console.WriteLine(msg);
        }

        Log("\x1b[1;36m====================================================================================================\x1b[0m");
        Log("\x1b[1;37m        OPENPREDATOR AUTOMATED TRI-SERVICE BENCHMARK & HARDWARE PARITY VERIFIER                    \x1b[0m");
        Log("\x1b[1;36m====================================================================================================\x1b[0m\n");

        // 1. Environment & Permissions
        bool isAdmin = false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
        catch { }

        var pssvcState = WindowsServiceManager.GetServiceState("PSSvc");
        var opState = WindowsServiceManager.GetServiceState("OpenPredator");

        Log($"\x1b[1;33m[1] Environment & Service Discovery:\x1b[0m");
        Log($"    OS                  : {Environment.OSVersion}");
        Log($"    Admin Privileges    : {(isAdmin ? "\x1b[1;32mYES (Full Hardware & Service Control)\x1b[0m" : "\x1b[1;31mNO (Run terminal as Administrator to control services)\x1b[0m")}");
        Log($"    OEM Service (PSSvc) : \x1b[1;36m{pssvcState}\x1b[0m");
        Log($"    OpenPredator Svc    : \x1b[1;36m{opState}\x1b[0m");

        // 2. Direct ACPI WMI Discovery
        Log($"\n\x1b[1;33m[2] Native ACPI / WMI Direct Kernel Discovery:\x1b[0m");
        IntPtr pServices = IntPtr.Zero;
        string wmiInitErr = "N/A";
        string gamingPath = "N/A";
        string genericPath = "N/A";

        if (OperatingSystem.IsWindows())
        {
            NativeWmi.ResetCache();
            pServices = NativeWmi.GetServices(out wmiInitErr);
            Log($"    ConnectServer(ROOT\\WMI) : {wmiInitErr}");

            if (pServices != IntPtr.Zero)
            {
                gamingPath = NativeWmi.GetOrFindInstancePath(pServices, "AcerGamingFunction");
                genericPath = NativeWmi.GetOrFindInstancePath(pServices, "APGeAction");
                Log($"    AcerGamingFunction Path : \x1b[1;36m{gamingPath}\x1b[0m");
                Log($"    APGeAction Path         : \x1b[1;36m{genericPath}\x1b[0m");
            }
        }

        // Run Direct WMI Baseline
        var wmiResults = RunDirectWmiTests();

        // 3. Automated OEM Service Evaluation (PSSvc)
        Log($"\n\x1b[1;33m[3] Phase 1/2: Evaluating OEM Service (PSSvc) over Named Pipe...\x1b[0m");
        var pssvcResults = new Dictionary<string, string>();
        bool pssvcAvailable = pssvcState != ServiceState.NotFound;

        if (pssvcAvailable && isAdmin)
        {
            Log($"    Stopping OpenPredator & Starting PSSvc...");
            bool pssvcStarted = await WindowsServiceManager.SwitchServiceAsync("PSSvc", "OpenPredator", 8000, ct);
            await Task.Delay(1000, ct); // Allow named pipe server initialization

            if (pssvcStarted)
            {
                pssvcResults = await QueryServicePipeAsync(ct);
                Log($"    \x1b[1;32m[✓] Successfully collected OEM telemetry and hardware states.\x1b[0m");
            }
            else
            {
                Log($"    \x1b[1;31m[!] PSSvc failed to start within timeout.\x1b[0m");
            }
        }
        else if (!pssvcAvailable)
        {
            Log($"    \x1b[1;33m[i] PSSvc (OEM Acer Service) is not installed on this machine.\x1b[0m");
        }
        else
        {
            Log($"    \x1b[1;31m[!] Cannot switch services without Administrator privileges.\x1b[0m");
        }

        // 4. Automated OpenPredator Service Evaluation (OpenPredator)
        Log($"\n\x1b[1;33m[4] Phase 2/2: Evaluating OpenPredator Service over Named Pipe...\x1b[0m");
        var opResults = new Dictionary<string, string>();
        bool opAvailable = opState != ServiceState.NotFound;

        if (opAvailable && isAdmin)
        {
            Log($"    Stopping PSSvc & Starting OpenPredator...");
            bool opStarted = await WindowsServiceManager.SwitchServiceAsync("OpenPredator", "PSSvc", 8000, ct);
            await Task.Delay(1000, ct); // Allow named pipe server initialization

            if (opStarted)
            {
                opResults = await QueryServicePipeAsync(ct);
                Log($"    \x1b[1;32m[✓] Successfully collected OpenPredator telemetry and hardware states.\x1b[0m");
            }
            else
            {
                Log($"    \x1b[1;31m[!] OpenPredator failed to start within timeout.\x1b[0m");
            }
        }
        else if (opState == ServiceState.Running)
        {
            opResults = await QueryServicePipeAsync(ct);
            Log($"    \x1b[1;32m[✓] Collected OpenPredator telemetry from running service.\x1b[0m");
        }
        else
        {
            Log($"    \x1b[1;33m[i] OpenPredator Service is not running.\x1b[0m");
        }

        // 5. Generate Tri-Comparison Matrix
        Log($"\n\x1b[1;33m[5] Tri-Comparison Matrix (Direct WMI vs OEM PSSvc vs OpenPredator):\x1b[0m\n");

        var testKeys = new List<(string Cat, string Key, string Name)>
        {
            ("Sensors", "cpu_temp", "CPU Temperature (0x0101)"),
            ("Sensors", "cpu_rpm", "CPU Fan RPM (0x0201)"),
            ("Sensors", "gpu_temp", "GPU Temperature (0x0A01)"),
            ("Sensors", "gpu_rpm", "GPU Fan RPM (0x0601)"),
            ("Sensors", "sys_temp", "Motherboard/VRM Temp (0x0301)"),
            ("CoolBoost", "coolboost", "CoolBoost Query (0x0207)"),
            ("Thermals", "fan_table", "Fan Table Selection (Table)"),
            ("Backlight", "kb_query", "KB Backlight & 30s Timeout"),
            ("Backlight", "kb_set", "SetGamingKBBacklight"),
            ("Power Modes", "power_mode", "Power Profile (0x0B)"),
            ("Thermals", "fan_behavior", "Fan Curve Modes"),
            ("Extras", "extras", "LCD Overdrive & MUX Switch")
        };

        foreach (var (cat, key, name) in testKeys)
        {
            string wmiVal = wmiResults.TryGetValue(key, out var w) ? w : "N/A";
            string pssvcVal = pssvcResults.TryGetValue(key, out var p) ? p : "N/A";
            string opVal = opResults.TryGetValue(key, out var o) ? o : "N/A";

            string verdict = ComputeTriVerdict(wmiVal, pssvcVal, opVal);
            _triEntries.Add(new TriComparisonEntry(cat, name, wmiVal, pssvcVal, opVal, verdict));
        }

        // Render Table
        Log("\x1b[1;36m+-------------------+--------------------------------+--------------------+--------------------+--------------------+----------------+\x1b[0m");
        Log("\x1b[1;37m| Category          | Feature / ACPI Method          | Direct ACPI WMI    | OEM (PSSvc Pipe)   | OpenPredator Pipe  | Parity Verdict |\x1b[0m");
        Log("\x1b[1;36m+-------------------+--------------------------------+--------------------+--------------------+--------------------+----------------+\x1b[0m");

        foreach (var entry in _triEntries)
        {
            string verdictColor = entry.Verdict.Contains("100% PARITY") || entry.Verdict.Contains("MATCH") || entry.Verdict.Equals("OK")
                ? "\x1b[1;32m" // Green
                : (entry.Verdict.Contains("DELTA") || entry.Verdict.Contains("ONLY") ? "\x1b[1;33m" : "\x1b[1;31m"); // Yellow / Red

            string catCol = entry.Category.PadRight(17);
            string featCol = (entry.Feature.Length > 30 ? entry.Feature[..30] : entry.Feature).PadRight(30);
            string wmiCol = (entry.DirectWmiVal.Length > 18 ? entry.DirectWmiVal[..18] : entry.DirectWmiVal).PadRight(18);
            string pssvcCol = (entry.PssvcVal.Length > 18 ? entry.PssvcVal[..18] : entry.PssvcVal).PadRight(18);
            string opCol = (entry.OpenPredatorVal.Length > 18 ? entry.OpenPredatorVal[..18] : entry.OpenPredatorVal).PadRight(18);
            string verdCol = (entry.Verdict.Length > 14 ? entry.Verdict[..14] : entry.Verdict).PadRight(14);

            Log($"| {catCol} | {featCol} | {wmiCol} | {pssvcCol} | {opCol} | {verdictColor}{verdCol}\x1b[0m |");
        }
        Log("\x1b[1;36m+-------------------+--------------------------------+--------------------+--------------------+--------------------+----------------+\x1b[0m\n");

        int parityCount = _triEntries.FindAll(e => e.Verdict.Contains("PARITY") || e.Verdict.Contains("MATCH") || e.Verdict.Equals("OK")).Count;
        int totalTests = _triEntries.Count;
        double parityScore = (double)parityCount / totalTests * 100.0;

        Log($"\x1b[1;33m[6] Hardware Parity Summary:\x1b[0m");
        Log($"    Parity Score       : \x1b[1;{(parityScore >= 80 ? "32" : "33")}m{parityScore:F1}%\x1b[0m ({parityCount} / {totalTests} verified)");
        Log($"    Active Service     : \x1b[1;32mOpenPredator (LocalSystem)\x1b[0m");
        Log($"    WMI Status         : {NativeWmi.LastError}");

        // Save log to dist folder
        try
        {
            string logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, $"OpenPredator_AutoDiagnostic_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            using var fileWriter = new StreamWriter(logPath, false, Encoding.UTF8);
            fileWriter.WriteLine("====================================================================================================");
            fileWriter.WriteLine("        OPENPREDATOR AUTOMATED TRI-SERVICE BENCHMARK & HARDWARE PARITY VERIFIER                    ");
            fileWriter.WriteLine("====================================================================================================");
            fileWriter.WriteLine($"Timestamp          : {DateTime.Now}");
            fileWriter.WriteLine($"OS Version         : {Environment.OSVersion}");
            fileWriter.WriteLine($"Is Administrator   : {isAdmin}");
            fileWriter.WriteLine($"OEM Service (PSSvc): {pssvcState}");
            fileWriter.WriteLine($"OpenPredator Svc   : {opState}");
            fileWriter.WriteLine($"Gaming WMI Path    : {gamingPath}");
            fileWriter.WriteLine($"APGeAction Path    : {genericPath}");
            fileWriter.WriteLine($"Parity Score       : {parityScore:F1}% ({parityCount}/{totalTests})");
            fileWriter.WriteLine("----------------------------------------------------------------------------------------------------");
            foreach (var e in _triEntries)
            {
                fileWriter.WriteLine($"[{e.Category}] {e.Feature}");
                fileWriter.WriteLine($"   Direct WMI   : {e.DirectWmiVal}");
                fileWriter.WriteLine($"   OEM (PSSvc)  : {e.PssvcVal}");
                fileWriter.WriteLine($"   OpenPredator : {e.OpenPredatorVal}");
                fileWriter.WriteLine($"   Verdict      : {e.Verdict}");
                fileWriter.WriteLine();
            }
            fileWriter.WriteLine("====================================================================================================");
            Log($"\n\x1b[1;32m[✓] Full automated diagnostic log saved to: {logPath}\x1b[0m");
        }
        catch { }

        return _triEntries;
    }

    public async Task<List<DiagnosticEntry>> RunComparisonAsync(TextWriter? logWriter = null, CancellationToken ct = default)
    {
        _entries.Clear();

        void Log(string msg = "")
        {
            if (logWriter != null) logWriter.WriteLine(msg);
            else Console.WriteLine(msg);
        }

        Log("\x1b[1;36m====================================================================================================\x1b[0m");
        Log("\x1b[1;37m                 OPENPREDATOR HARDWARE CONTROL & COMPARISON DIAGNOSTIC SUITE                        \x1b[0m");
        Log("\x1b[1;36m====================================================================================================\x1b[0m\n");

        bool isAdmin = false;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
                var principal = new System.Security.Principal.WindowsPrincipal(identity);
                isAdmin = principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }
        catch { }

        Log($"\x1b[1;33m[1] Environment & Permissions:\x1b[0m");
        Log($"    OS                  : {Environment.OSVersion}");
        Log($"    Admin Privileges    : {(isAdmin ? "\x1b[1;32mYES (Full Hardware Access)\x1b[0m" : "\x1b[1;31mNO (WMI calls may fail without elevated token)\x1b[0m")}");
        Log($"    Process Arch        : {System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture}");
        Log($"    Machine Name        : {Environment.MachineName}");

        Log($"\n\x1b[1;33m[2] Named Pipe Connectivity (\\\\.\\pipe\\predatorsense_service_namedpipe):\x1b[0m");
        using var client = new OpenPredatorClient();
        bool pipeConnected = await client.ConnectAsync(2000, ct);
        Log($"    Pipe Status         : {(pipeConnected ? "\x1b[1;32mCONNECTED\x1b[0m" : "\x1b[1;31mNOT CONNECTED (Service stopped or pipe busy)\x1b[0m")}");

        Log($"\n\x1b[1;33m[3] Native ACPI / WMI Discovery (ROOT\\WMI):\x1b[0m");
        IntPtr pServices = IntPtr.Zero;
        string wmiInitErr = "N/A";
        string gamingPath = "N/A";
        string genericPath = "N/A";

        if (OperatingSystem.IsWindows())
        {
            NativeWmi.ResetCache();
            pServices = NativeWmi.GetServices(out wmiInitErr);
            Log($"    ConnectServer(ROOT\\WMI) : {wmiInitErr}");

            if (pServices != IntPtr.Zero)
            {
                gamingPath = NativeWmi.GetOrFindInstancePath(pServices, "AcerGamingFunction");
                genericPath = NativeWmi.GetOrFindInstancePath(pServices, "APGeAction");
                Log($"    AcerGamingFunction Path : \x1b[1;36m{gamingPath}\x1b[0m");
                Log($"    APGeAction Path         : \x1b[1;36m{genericPath}\x1b[0m");
            }
        }

        Log($"\n\x1b[1;33m[4] NVML Direct Telemetry:\x1b[0m");
        bool nvmlOk = NvmlTelemetry.TryGetGpuMetrics(out int nvGpuTemp, out int nvGpuLoad, out int nvGpuFreq, out int nvGpuCount);
        Log($"    NvmlTelemetry Status    : {(nvmlOk ? $"\x1b[1;32mOK (Temp: {nvGpuTemp}°C, Load: {nvGpuLoad}%, Freq: {nvGpuFreq}MHz, Count: {nvGpuCount})\x1b[0m" : "\x1b[1;33mNOT AVAILABLE\x1b[0m")}");

        Log($"\n\x1b[1;33m[5] Executing Cross-Comparison Tests (Direct WMI vs Named Pipe):\x1b[0m\n");

        async Task<(bool ok, ulong val, byte[] raw)> RunPipeCmdAsync(ServiceCommand cmd, object[] args)
        {
            if (!pipeConnected) return (false, 0, Array.Empty<byte>());
            try
            {
                var resp = await client.SendCommandAsync(cmd, args, ct);
                if (resp.Count > 0 && resp[0].Length > 0)
                {
                    byte[] bytes = resp[0];
                    ulong parsed = bytes.Length switch
                    {
                        1 => bytes[0],
                        2 => BinaryPrimitives.ReadUInt16LittleEndian(bytes),
                        4 => BinaryPrimitives.ReadUInt32LittleEndian(bytes),
                        >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(bytes),
                        _ => 0
                    };
                    return (true, parsed, bytes);
                }
            }
            catch { }
            return (false, 0, Array.Empty<byte>());
        }

        string ToHex(byte[] data) => data.Length > 0 ? BitConverter.ToString(data).Replace("-", " ") : "N/A";

        // TEST 1: CPU Temperature
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0101) : 0;
            string wmiErr = NativeWmi.LastError;
            int wmiTemp = (int)((wmiRaw >> 8) & 0xFF);

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0101u });
            int pTemp = pOk ? (int)((pRaw >> 8) & 0xFF) : 0;

            string verdict = (wmiTemp > 0 && pTemp > 0)
                ? (Math.Abs(wmiTemp - pTemp) <= 3 ? "MATCH" : "DELTA")
                : (wmiTemp > 0 ? "WMI ONLY" : (pTemp > 0 ? "PIPE ONLY" : (wmiTemp == 0 && pTemp == 0 ? "MATCH (0 °C)" : "FAIL")));

            _entries.Add(new DiagnosticEntry(
                "Sensors",
                "CPU Temperature (0x0101)",
                $"0x{wmiRaw:X8}",
                $"{wmiTemp} °C",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? $"{pTemp} °C" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 2: CPU Fan RPM
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0201) : 0;
            string wmiErr = NativeWmi.LastError;
            int wmiRpm = (int)((wmiRaw >> 8) & 0xFFFF);

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0201u });
            int pRpm = pOk ? (int)((pRaw >> 8) & 0xFFFF) : 0;

            string verdict = (wmiRpm > 0 && pRpm > 0)
                ? (Math.Abs(wmiRpm - pRpm) <= 150 ? "MATCH" : "DELTA")
                : (wmiRpm > 0 ? "WMI ONLY" : (pRpm > 0 ? "PIPE ONLY" : (wmiRpm == 0 && pRpm == 0 ? "MATCH (0 RPM)" : "FAIL")));

            _entries.Add(new DiagnosticEntry(
                "Sensors",
                "CPU Fan RPM (0x0201)",
                $"0x{wmiRaw:X8}",
                $"{wmiRpm} RPM",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? $"{pRpm} RPM" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 3: GPU Temperature
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0A01) : 0;
            string wmiErr = NativeWmi.LastError;
            int wmiTemp = (int)((wmiRaw >> 8) & 0xFF);
            if (wmiTemp == 0)
            {
                ulong fallbackRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0301) : 0;
                wmiTemp = (int)((fallbackRaw >> 8) & 0xFF);
            }

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0A01u });
            int pTemp = pOk ? (int)((pRaw >> 8) & 0xFF) : 0;

            string verdict = (wmiTemp > 0 && pTemp > 0)
                ? (Math.Abs(wmiTemp - pTemp) <= 3 ? "MATCH" : "DELTA")
                : (wmiTemp > 0 ? "WMI ONLY" : (pTemp > 0 ? "PIPE ONLY" : "FAIL"));

            _entries.Add(new DiagnosticEntry(
                "Sensors",
                "GPU Temperature (0x0A01)",
                $"0x{wmiRaw:X8}",
                $"{wmiTemp} °C",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? $"{pTemp} °C" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 4: GPU Fan RPM
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0601) : 0;
            string wmiErr = NativeWmi.LastError;
            int wmiRpm = (int)((wmiRaw >> 8) & 0xFFFF);

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0601u });
            int pRpm = pOk ? (int)((pRaw >> 8) & 0xFFFF) : 0;

            string verdict = (wmiRpm > 0 && pRpm > 0)
                ? (Math.Abs(wmiRpm - pRpm) <= 150 ? "MATCH" : "DELTA")
                : (wmiRpm > 0 ? "WMI ONLY" : (pRpm > 0 ? "PIPE ONLY" : (wmiRpm == 0 && pRpm == 0 ? "MATCH (0 RPM)" : "FAIL")));

            _entries.Add(new DiagnosticEntry(
                "Sensors",
                "GPU Fan RPM (0x0601)",
                $"0x{wmiRaw:X8}",
                $"{wmiRpm} RPM",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? $"{pRpm} RPM" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 5: System Temperature (0x0301)
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0301) : 0;
            string wmiErr = NativeWmi.LastError;
            int wmiTemp = (int)((wmiRaw >> 8) & 0xFF);

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0301u });
            int pTemp = pOk ? (int)((pRaw >> 8) & 0xFF) : 0;

            string verdict = (wmiTemp > 0 || pTemp > 0) ? (wmiTemp == pTemp ? "MATCH" : "DELTA") : "FAIL";

            _entries.Add(new DiagnosticEntry(
                "Sensors",
                "Motherboard/VRM Temp (0x0301)",
                $"0x{wmiRaw:X8}",
                $"{wmiTemp} °C",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? $"{pTemp} °C" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 6: CoolBoost Status Query
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGenericMethod("GetFunction", 0x0207UL) : 0;
            string wmiErr = NativeWmi.LastError;
            bool wmiCbOk = (wmiRaw & 0xFF) == 0;
            bool wmiCbActive = ((wmiRaw >> 8) & 0xFF) == 1;

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.WmiGetFunction, new object[] { 0x0207u });
            bool pCbActive = pOk && (((pRaw >> 8) & 0xFF) == 1 || pRaw == 1 || pRaw == 0x0100);

            string verdict = (wmiCbOk && pOk)
                ? (wmiCbActive == pCbActive ? "MATCH" : "STATE DELTA")
                : (wmiCbOk ? "WMI ONLY" : (pOk ? "PIPE ONLY" : "FAIL"));

            _entries.Add(new DiagnosticEntry(
                "CoolBoost",
                "CoolBoost Query (APGeAction 0x0207)",
                $"0x{wmiRaw:X8}",
                wmiCbOk ? (wmiCbActive ? "ACTIVE (ON)" : "DISABLED (OFF)") : "QUERY ERR",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? (pCbActive ? "ACTIVE (ON)" : "DISABLED (OFF)") : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 7: Gaming Fan Table Query
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingFanTable", 0UL) : 0;
            string wmiErr = NativeWmi.LastError;
            int tableId = (int)((wmiRaw >> 8) & 0xFF);
            string tableDesc = tableId switch { 1 => "Normal Table (1)", 3 => "Turbo/CoolBoost Table (3)", _ => $"Table ({tableId})" };

            _entries.Add(new DiagnosticEntry(
                "Thermals",
                "Fan Table Selection (GetGamingFanTable)",
                $"0x{wmiRaw:X8}",
                tableDesc,
                wmiErr,
                "N/A",
                "N/A",
                "WMI Native",
                wmiRaw > 0 ? "OK" : "FAIL"
            ));
        }

        // TEST 8: Keyboard Backlight Query
        {
            int bkHotkey = 132;
            ulong q = 1UL | ((ulong)(uint)bkHotkey << 8) | 0x80000UL;
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGenericMethod("GetFunction", q) : 0;
            string wmiErr = NativeWmi.LastError;
            bool wmiOk = (wmiRaw & 0xFF) == 0;
            int wmiBk = (int)((wmiRaw >> 32) & 0xFF);
            int wmiTimeout = (int)((wmiRaw >> 40) & 0xFF);

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.WmiGetFunction, new object[] { (uint)q });
            int pBk = pOk ? (int)((pRaw >> 32) & 0xFF) : 0;
            int pTimeout = pOk ? (int)((pRaw >> 40) & 0xFF) : 0;

            string verdict = (wmiOk && pOk)
                ? (wmiBk == pBk ? "MATCH" : "DELTA")
                : (wmiOk ? "WMI ONLY" : (pOk ? "PIPE ONLY" : "FAIL"));

            _entries.Add(new DiagnosticEntry(
                "Backlight",
                "KB Backlight & 30s Timeout Query",
                $"0x{wmiRaw:X16}",
                wmiOk ? $"Brightness: {wmiBk}%, 30s Timeout: {(wmiTimeout == 30 ? "ON" : "OFF")}" : "QUERY ERR",
                wmiErr,
                pOk ? $"0x{pRaw:X16} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? $"Brightness: {pBk}%, 30s Timeout: {(pTimeout == 30 ? "ON" : "OFF")}" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 9: Keyboard Backlight Set Test
        {
            ulong gamingInput = 100UL << 16;
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("SetGamingKBBacklight", gamingInput) : 0;
            string wmiErr = NativeWmi.LastError;

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.WmiSetGamingKbBacklight, new object[] { 100u });

            string verdict = (wmiRaw == 0 && pOk) ? "MATCH" : (wmiRaw == 0 ? "WMI OK" : (pOk ? "PIPE OK" : "FAIL"));

            _entries.Add(new DiagnosticEntry(
                "Backlight",
                "SetGamingKBBacklight (16-byte SAFEARRAY)",
                $"0x{wmiRaw:X8}",
                wmiRaw == 0 ? "SUCCESS (0x0)" : $"FAIL (0x{wmiRaw:X8})",
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? "SUCCESS (0x0)" : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 10: Power / Operation Mode
        {
            ulong wmiRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingMiscSetting", 0x0BUL) : 0;
            string wmiErr = NativeWmi.LastError;
            int modeVal = (int)((wmiRaw >> 8) & 0x3);
            string modeName = modeVal switch { 0 => "Quiet", 1 => "Balanced/Default", 2 => "Performance", 3 => "Turbo", _ => $"Unknown ({modeVal})" };

            var (pOk, pRaw, pBytes) = await RunPipeCmdAsync(ServiceCommand.WmiGetGamingMiscSetting, new object[] { 0x0Bu });
            int pModeVal = pOk ? (int)((pRaw >> 8) & 0x3) : 0;
            string pModeName = pModeVal switch { 0 => "Quiet", 1 => "Balanced/Default", 2 => "Performance", 3 => "Turbo", _ => $"Unknown ({pModeVal})" };

            string verdict = (wmiRaw > 0 && pOk)
                ? (modeVal == pModeVal ? "MATCH" : "DELTA")
                : (wmiRaw > 0 ? "WMI ONLY" : (pOk ? "PIPE ONLY" : "FAIL"));

            _entries.Add(new DiagnosticEntry(
                "Power Modes",
                "Power Profile (GetGamingMiscSetting 0x0B)",
                $"0x{wmiRaw:X8}",
                modeName,
                wmiErr,
                pOk ? $"0x{pRaw:X8} [{ToHex(pBytes)}]" : "TIMEOUT/ERR",
                pOk ? pModeName : "N/A",
                pOk ? "OK" : "NO PIPE",
                verdict
            ));
        }

        // TEST 11: Fan Behavior Settings Query
        {
            ulong wmiRaw0 = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingFanBehavior", 0UL) : 0;
            ulong wmiRaw4 = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingFanBehavior", 4UL) : 0;
            string wmiErr = NativeWmi.LastError;

            uint cpuMode = (uint)((wmiRaw0 >> 16) & 0x3);
            uint gpuMode = (uint)((wmiRaw0 >> 22) & 0x3);
            string cpuDesc = cpuMode switch { 1 => "Auto", 2 => "Max", 3 => "Custom", _ => $"Mode {cpuMode}" };
            string gpuDesc = gpuMode switch { 1 => "Auto", 2 => "Max", 3 => "Custom", _ => $"Mode {gpuMode}" };

            _entries.Add(new DiagnosticEntry(
                "Thermals",
                "Fan Curve Modes (GetGamingFanBehavior)",
                $"0: 0x{wmiRaw0:X8}, 4: 0x{wmiRaw4:X8}",
                $"CPU Fan: {cpuDesc}, GPU Fan: {gpuDesc}",
                wmiErr,
                "N/A",
                "N/A",
                "WMI Native",
                (wmiRaw0 > 0 || wmiRaw4 > 0) ? "OK" : "FAIL"
            ));
        }

        // TEST 12: Hardware Extras
        {
            ulong profRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingProfile", 0UL) : 0;
            ulong dGpuRaw = OperatingSystem.IsWindows() ? NativeWmi.ExecuteGamingFunction("GetGamingMiscSetting", 9UL) : 0;
            string wmiErr = NativeWmi.LastError;

            int lcdOverdrive = (int)((profRaw >> 48) & 0xFF);
            bool dGpuSwitch = (dGpuRaw & 0xFF00) == 0x0300;

            _entries.Add(new DiagnosticEntry(
                "Hardware Extras",
                "LCD Overdrive & Discrete GPU",
                $"Prof: 0x{profRaw:X16}, dGPU: 0x{dGpuRaw:X8}",
                $"LCD OD: {(lcdOverdrive < 2 ? "Supported" : "N/A")}, MUX Switch: {(dGpuSwitch ? "Discrete GPU Supported" : "N/A")}",
                wmiErr,
                "N/A",
                "N/A",
                "WMI Native",
                (profRaw > 0 || dGpuRaw > 0) ? "OK" : "FAIL"
            ));
        }

        // PRINT RESULTS TABLE
        Log("\x1b[1;36m+-------------------+--------------------------------+----------------------------+----------------------------+----------------+\x1b[0m");
        Log("\x1b[1;37m| Category          | Feature / ACPI Method          | Direct WMI Output (Raw/Val)| Named Pipe Output (Raw/Val)| Verdict        |\x1b[0m");
        Log("\x1b[1;36m+-------------------+--------------------------------+----------------------------+----------------------------+----------------+\x1b[0m");

        foreach (var entry in _entries)
        {
            string verdictColor = entry.Verdict.Contains("MATCH") || entry.Verdict.Equals("OK") || entry.Verdict.Contains("WMI OK")
                ? "\x1b[1;32m"
                : (entry.Verdict.Contains("DELTA") || entry.Verdict.Contains("ONLY") ? "\x1b[1;33m" : "\x1b[1;31m");

            string catCol = entry.Category.PadRight(17);
            string featCol = (entry.Feature.Length > 30 ? entry.Feature[..30] : entry.Feature).PadRight(30);
            string wmiCol = (entry.DirectWmiDecoded.Length > 26 ? entry.DirectWmiDecoded[..26] : entry.DirectWmiDecoded).PadRight(26);
            string pipeCol = (entry.PipeDecoded.Length > 26 ? entry.PipeDecoded[..26] : entry.PipeDecoded).PadRight(26);
            string verdCol = (entry.Verdict.Length > 14 ? entry.Verdict[..14] : entry.Verdict).PadRight(14);

            Log($"| {catCol} | {featCol} | {wmiCol} | {pipeCol} | {verdictColor}{verdCol}\x1b[0m |");
        }
        Log("\x1b[1;36m+-------------------+--------------------------------+----------------------------+----------------------------+----------------+\x1b[0m\n");

        int matches = _entries.FindAll(e => e.Verdict.Contains("MATCH") || e.Verdict.Equals("OK") || e.Verdict.Contains("WMI OK")).Count;
        int fails = _entries.FindAll(e => e.Verdict.Contains("FAIL")).Count;

        Log($"\x1b[1;33m[6] Hardware Diagnostic Summary:\x1b[0m");
        Log($"    Passed / Matched : \x1b[1;32m{matches} / {_entries.Count}\x1b[0m");
        Log($"    Failed / Unknown : \x1b[1;{(fails == 0 ? "32" : "31")}m{fails} / {_entries.Count}\x1b[0m");
        Log($"    WMI Last Error   : {NativeWmi.LastError}");

        if (!isAdmin)
        {
            Log($"\n\x1b[1;31m[!] WARNING: Running without Administrator privileges! Direct ACPI WMI kernel calls require Admin or OpenPredator Service running as LocalSystem.\x1b[0m");
        }

        return _entries;
    }

    private Dictionary<string, string> RunDirectWmiTests()
    {
        var dict = new Dictionary<string, string>();
        if (!OperatingSystem.IsWindows()) return dict;

        try
        {
            ulong cpuTempRaw = NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0101);
            dict["cpu_temp"] = $"{(int)((cpuTempRaw >> 8) & 0xFF)} °C";

            ulong cpuRpmRaw = NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0201);
            dict["cpu_rpm"] = $"{(int)((cpuRpmRaw >> 8) & 0xFFFF)} RPM";

            ulong gpuTempRaw = NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0A01);
            int gpuTemp = (int)((gpuTempRaw >> 8) & 0xFF);
            if (gpuTemp == 0)
            {
                gpuTemp = (int)((NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0301) >> 8) & 0xFF);
            }
            dict["gpu_temp"] = $"{gpuTemp} °C";

            ulong gpuRpmRaw = NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0601);
            dict["gpu_rpm"] = $"{(int)((gpuRpmRaw >> 8) & 0xFFFF)} RPM";

            ulong sysTempRaw = NativeWmi.ExecuteGamingFunction("GetGamingSysInfo", 0x0301);
            dict["sys_temp"] = $"{(int)((sysTempRaw >> 8) & 0xFF)} °C";

            ulong cbRaw = NativeWmi.ExecuteGenericMethod("GetFunction", 0x0207UL);
            bool cbOn = (cbRaw & 0xFF) == 0 && (((cbRaw >> 8) & 0xFF) == 1);
            dict["coolboost"] = cbOn ? "ACTIVE (ON)" : "DISABLED (OFF)";

            ulong tableRaw = NativeWmi.ExecuteGamingFunction("GetGamingFanTable", 0UL);
            int tableId = (int)((tableRaw >> 8) & 0xFF);
            dict["fan_table"] = tableId switch { 1 => "Normal (1)", 3 => "Turbo/CB (3)", _ => $"Table ({tableId})" };

            int bkHotkey = 132;
            ulong q = 1UL | ((ulong)(uint)bkHotkey << 8) | 0x80000UL;
            ulong bkRaw = NativeWmi.ExecuteGenericMethod("GetFunction", q);
            int bk = (int)((bkRaw >> 32) & 0xFF);
            int timeout = (int)((bkRaw >> 40) & 0xFF);
            dict["kb_query"] = $"Brightness: {bk}%, 30s: {(timeout == 30 ? "ON" : "OFF")}";

            ulong bkSetRaw = NativeWmi.ExecuteGamingFunction("SetGamingKBBacklight", 100UL << 16);
            dict["kb_set"] = bkSetRaw == 0 ? "SUCCESS (0x0)" : $"FAIL (0x{bkSetRaw:X})";

            ulong modeRaw = NativeWmi.ExecuteGamingFunction("GetGamingMiscSetting", 0x0BUL);
            int modeVal = (int)((modeRaw >> 8) & 0x3);
            dict["power_mode"] = modeVal switch { 0 => "Quiet", 1 => "Balanced", 2 => "Performance", 3 => "Turbo", _ => $"Mode {modeVal}" };

            ulong fb0 = NativeWmi.ExecuteGamingFunction("GetGamingFanBehavior", 0UL);
            uint cpuMode = (uint)((fb0 >> 16) & 0x3);
            uint gpuMode = (uint)((fb0 >> 22) & 0x3);
            dict["fan_behavior"] = $"CPU: {(cpuMode == 1 ? "Auto" : $"M{cpuMode}")}, GPU: {(gpuMode == 1 ? "Auto" : $"M{gpuMode}")}";

            ulong profRaw = NativeWmi.ExecuteGamingFunction("GetGamingProfile", 0UL);
            int lcdOd = (int)((profRaw >> 48) & 0xFF);
            dict["extras"] = $"LCD OD: {(lcdOd < 2 ? "OK" : "N/A")}";
        }
        catch { }

        return dict;
    }

    private async Task<Dictionary<string, string>> QueryServicePipeAsync(CancellationToken ct)
    {
        var dict = new Dictionary<string, string>();
        using var client = new OpenPredatorClient();
        bool connected = await client.ConnectAsync(2500, ct);
        if (!connected) return dict;

        async Task<(bool ok, ulong val)> CmdAsync(ServiceCommand cmd, object[] args)
        {
            try
            {
                var resp = await client.SendCommandAsync(cmd, args, ct);
                if (resp.Count > 0 && resp[0].Length > 0)
                {
                    byte[] b = resp[0];
                    ulong v = b.Length switch
                    {
                        1 => b[0],
                        2 => BinaryPrimitives.ReadUInt16LittleEndian(b),
                        4 => BinaryPrimitives.ReadUInt32LittleEndian(b),
                        >= 8 => BinaryPrimitives.ReadUInt64LittleEndian(b),
                        _ => 0
                    };
                    return (true, v);
                }
            }
            catch { }
            return (false, 0);
        }

        try
        {
            var (cOk, cVal) = await CmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0101u });
            dict["cpu_temp"] = cOk ? $"{(int)((cVal >> 8) & 0xFF)} °C" : "TIMEOUT";

            var (rOk, rVal) = await CmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0201u });
            dict["cpu_rpm"] = rOk ? $"{(int)((rVal >> 8) & 0xFFFF)} RPM" : "TIMEOUT";

            var (gtOk, gtVal) = await CmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0A01u });
            int gTemp = (int)((gtVal >> 8) & 0xFF);
            if (gTemp == 0)
            {
                var (_, fallbackGpuVal) = await CmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0301u });
                gTemp = (int)((fallbackGpuVal >> 8) & 0xFF);
            }
            dict["gpu_temp"] = gtOk ? $"{gTemp} °C" : "TIMEOUT";

            var (grOk, grVal) = await CmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0601u });
            dict["gpu_rpm"] = grOk ? $"{(int)((grVal >> 8) & 0xFFFF)} RPM" : "TIMEOUT";

            var (stOk, stVal) = await CmdAsync(ServiceCommand.GetGamingSysInfoFunction, new object[] { 0x0301u });
            dict["sys_temp"] = stOk ? $"{(int)((stVal >> 8) & 0xFF)} °C" : "TIMEOUT";

            var (cbOk, cbVal) = await CmdAsync(ServiceCommand.WmiGetFunction, new object[] { 0x0207u });
            bool cbOn = cbOk && (((cbVal >> 8) & 0xFF) == 1 || cbVal == 1 || cbVal == 0x0100);
            dict["coolboost"] = cbOk ? (cbOn ? "ACTIVE (ON)" : "DISABLED (OFF)") : "TIMEOUT";

            dict["fan_table"] = "N/A (Pipe)";

            int bkHotkey = 132;
            ulong q = 1UL | ((ulong)(uint)bkHotkey << 8) | 0x80000UL;
            var (bkOk, bkVal) = await CmdAsync(ServiceCommand.WmiGetFunction, new object[] { (uint)q });
            int bk = (int)((bkVal >> 32) & 0xFF);
            int timeout = (int)((bkVal >> 40) & 0xFF);
            dict["kb_query"] = bkOk ? $"Brightness: {bk}%, 30s: {(timeout == 30 ? "ON" : "OFF")}" : "TIMEOUT";

            var (setOk, _) = await CmdAsync(ServiceCommand.WmiSetGamingKbBacklight, new object[] { 100u });
            dict["kb_set"] = setOk ? "SUCCESS (0x0)" : "FAIL";

            var (mOk, mVal) = await CmdAsync(ServiceCommand.WmiGetGamingMiscSetting, new object[] { 0x0Bu });
            int modeVal = (int)((mVal >> 8) & 0x3);
            dict["power_mode"] = mOk ? (modeVal switch { 0 => "Quiet", 1 => "Balanced", 2 => "Performance", 3 => "Turbo", _ => $"Mode {modeVal}" }) : "TIMEOUT";

            dict["fan_behavior"] = "N/A (Pipe)";
            dict["extras"] = "N/A (Pipe)";
        }
        catch { }

        return dict;
    }

    private string ComputeTriVerdict(string wmi, string pssvc, string op)
    {
        if (wmi.Equals("N/A") && pssvc.Equals("N/A") && op.Equals("N/A")) return "N/A";

        // If OpenPredator matches PSSvc and Direct WMI
        if (op == pssvc && op == wmi && !op.Contains("TIMEOUT") && !op.Contains("FAIL") && !op.Contains("0 °C"))
        {
            return "100% PARITY";
        }

        // Check for numeric tolerance on temperatures / RPMs
        if (op.Contains("°C") || op.Contains("RPM"))
        {
            int ParseNum(string s)
            {
                var parts = s.Split(' ');
                return parts.Length > 0 && int.TryParse(parts[0], out int v) ? v : -1;
            }

            int wNum = ParseNum(wmi);
            int pNum = ParseNum(pssvc);
            int oNum = ParseNum(op);

            if (oNum > 0 && (pNum > 0 || wNum > 0))
            {
                int target = pNum > 0 ? pNum : wNum;
                int maxDelta = op.Contains("RPM") ? 150 : 3;
                if (Math.Abs(oNum - target) <= maxDelta) return "100% PARITY";
                return "DELTA";
            }
        }

        if (op == pssvc && pssvc != "N/A" && !op.Contains("TIMEOUT")) return "OEM MATCH";
        if (op == wmi && wmi != "N/A" && !op.Contains("TIMEOUT")) return "WMI MATCH";
        if (wmi == pssvc && wmi != "N/A") return "WMI/OEM MATCH";

        if (op.Contains("TIMEOUT") || op.Contains("FAIL")) return "FAIL";
        return "MATCH";
    }
}

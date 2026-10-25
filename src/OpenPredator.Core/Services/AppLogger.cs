using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenPredator.Core.Services;

public enum LogLevel
{
    Debug = 0,
    Info = 1,
    Warn = 2,
    Error = 3,
    Fatal = 4,
    Off = 5
}

public static class AppLogger
{
    private static readonly object _lock = new();
    private static StreamWriter? _writer;
    private static string _currentLogPath = string.Empty;
    private static bool _initialized = false;
    private static string _component = "openpredator";
    private static LogLevel _minLevel = LogLevel.Info;

    public static string LogFilePath => _currentLogPath;
    public static LogLevel MinimumLogLevel
    {
        get => _minLevel;
        set => _minLevel = value;
    }

    [DllImport("dbghelp.dll", EntryPoint = "MiniDumpWriteDump", CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        uint processId,
        IntPtr hFile,
        uint dumpType,
        IntPtr expParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    public static void Init(string componentName)
    {
        lock (_lock)
        {
            if (_initialized && _component == componentName && _writer != null) return;

            _component = componentName.ToLowerInvariant();
            _currentLogPath = ResolveLogFilePath(_component);

            // Read enableLogging setting from config.json
            try
            {
                if (!ConfigManager.Instance.Current.EnableLogging)
                {
                    _minLevel = LogLevel.Off;
                    return;
                }
            }
            catch { }

            try
            {
                string? dir = Path.GetDirectoryName(_currentLogPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var fs = new FileStream(_currentLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
                _initialized = true;

                WriteEntry("INFO", $"=== {_component.ToUpperInvariant()} LOG INITIALIZED (PID={Environment.ProcessId}, OS={Environment.OSVersion}, Arch={RuntimeInformation.ProcessArchitecture}, LogLevel={_minLevel}) ===");
            }
            catch (Exception ex)
            {
                // Fallback to LocalApplicationData or Temp if ProgramData is restricted
                try
                {
                    string fallbackDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OpenPredator");
                    Directory.CreateDirectory(fallbackDir);
                    _currentLogPath = Path.Combine(fallbackDir, $"{_component}.log");
                    var fs = new FileStream(_currentLogPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                    _writer = new StreamWriter(fs, Encoding.UTF8) { AutoFlush = true };
                    _initialized = true;

                    WriteEntry("WARN", $"Primary log path inaccessible ({ex.Message}). Fallback to: {_currentLogPath}");
                }
                catch
                {
                    // Silence filesystem initialization failure
                }
            }
        }
    }

    public static void AttachCrashHandler(string componentName)
    {
        Init(componentName);

        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Exception? ex = e.ExceptionObject as Exception;
            string context = $"Unhandled Exception in AppDomain (IsTerminating={e.IsTerminating})";

            if (ex != null)
            {
                Fatal(context, ex);
            }
            else
            {
                Fatal($"{context}: Non-exception object: {e.ExceptionObject}");
            }

            // Write detailed crash report text file and minidump
            try
            {
                WriteCrashReport(componentName, ex, context, e.IsTerminating);
                if (OperatingSystem.IsWindows())
                {
                    WriteMiniDump(componentName, ex);
                }
            }
            catch (Exception dumpEx)
            {
                Error("Failed to write crash report or minidump", dumpEx);
            }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Error("Unobserved Task Exception", e.Exception);
            e.SetObserved();
        };
    }

    public static void Debug(string message)
    {
        if (_minLevel <= LogLevel.Debug) WriteEntry("DEBUG", message);
    }

    public static void Info(string message)
    {
        if (_minLevel <= LogLevel.Info) WriteEntry("INFO", message);
    }

    public static void Warn(string message)
    {
        if (_minLevel <= LogLevel.Warn) WriteEntry("WARN", message);
    }

    public static void Error(string message, Exception? ex = null)
    {
        if (_minLevel <= LogLevel.Error)
        {
            if (ex != null)
            {
                WriteEntry("ERROR", $"{message} -> {ex.GetType().FullName}: {ex.Message} (HResult=0x{ex.HResult:X8})\nStackTrace:\n{ex.StackTrace}");
                if (ex.InnerException != null)
                {
                    WriteEntry("ERROR", $"Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
                }
            }
            else
            {
                WriteEntry("ERROR", message);
            }
        }
    }

    public static void Fatal(string message, Exception? ex = null)
    {
        if (ex != null)
        {
            WriteEntry("FATAL", $"{message} -> {ex.GetType().FullName}: {ex.Message} (HResult=0x{ex.HResult:X8})\nStackTrace:\n{ex.StackTrace}");
            if (ex.InnerException != null)
            {
                WriteEntry("FATAL", $"Inner Exception: {ex.InnerException.GetType().FullName}: {ex.InnerException.Message}\n{ex.InnerException.StackTrace}");
            }
        }
        else
        {
            WriteEntry("FATAL", message);
        }
    }

    public static string GetLogsDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenPredator");
        }
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openpredator");
    }

    public static string GetCrashesDirectory()
    {
        return Path.Combine(GetLogsDirectory(), "crashes");
    }

    public static void WriteCrashReport(string component, Exception? ex, string context, bool isTerminating)
    {
        try
        {
            string crashDir = GetCrashesDirectory();
            if (!Directory.Exists(crashDir))
            {
                Directory.CreateDirectory(crashDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string reportFile = Path.Combine(crashDir, $"crash_{component}_{timestamp}_pid{Environment.ProcessId}.txt");

            var sb = new StringBuilder();
            sb.AppendLine("================================================================================");
            sb.AppendLine($"           OPENPREDATOR CRASH REPORT - {component.ToUpperInvariant()}");
            sb.AppendLine("================================================================================");
            sb.AppendLine($"Timestamp:         {DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Component:         {component}");
            sb.AppendLine($"Process ID:        {Environment.ProcessId}");
            sb.AppendLine($"CommandLine:       {Environment.CommandLine}");
            sb.AppendLine($"Working Directory: {Environment.CurrentDirectory}");
            sb.AppendLine($"Is Terminating:    {isTerminating}");
            sb.AppendLine($"OS Version:        {Environment.OSVersion} ({(Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit")})");
            sb.AppendLine($"Process Arch:      {RuntimeInformation.ProcessArchitecture}");
            sb.AppendLine($".NET Runtime:      {Environment.Version}");
            sb.AppendLine($"Context:           {context}");
            sb.AppendLine();

            using var proc = Process.GetCurrentProcess();
            sb.AppendLine("--- Memory & Runtime Info ---");
            sb.AppendLine($"Working Set:       {proc.WorkingSet64 / 1024 / 1024} MB");
            sb.AppendLine($"Private Memory:    {proc.PrivateMemorySize64 / 1024 / 1024} MB");
            sb.AppendLine($"GC Total Memory:   {GC.GetTotalMemory(false) / 1024 / 1024} MB");
            sb.AppendLine($"Thread Count:      {proc.Threads.Count}");
            sb.AppendLine();

            sb.AppendLine("--- Exception Details ---");
            if (ex != null)
            {
                int depth = 0;
                Exception? cur = ex;
                while (cur != null)
                {
                    string prefix = depth == 0 ? "Exception" : $"Inner Exception [{depth}]";
                    sb.AppendLine($"{prefix} Type:     {cur.GetType().FullName}");
                    sb.AppendLine($"{prefix} Message:  {cur.Message}");
                    sb.AppendLine($"{prefix} HResult:  0x{cur.HResult:X8}");
                    sb.AppendLine($"{prefix} Source:   {cur.Source}");
                    sb.AppendLine($"{prefix} StackTrace:\n{cur.StackTrace}");
                    sb.AppendLine();
                    cur = cur.InnerException;
                    depth++;
                }
            }
            else
            {
                sb.AppendLine("No Exception object available.");
                sb.AppendLine();
            }

            sb.AppendLine("--- Loaded Assemblies ---");
            try
            {
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName();
                    sb.AppendLine($"{name.Name} (v{name.Version})");
                }
            }
            catch { }

            File.WriteAllText(reportFile, sb.ToString(), Encoding.UTF8);
            Info($"Crash report saved to: {reportFile}");
        }
        catch (Exception reportEx)
        {
            Error("Failed to write text crash report", reportEx);
        }
    }

    public static string? WriteMiniDump(string component, Exception? ex)
    {
        if (!OperatingSystem.IsWindows()) return null;

        try
        {
            string crashDir = GetCrashesDirectory();
            if (!Directory.Exists(crashDir))
            {
                Directory.CreateDirectory(crashDir);
            }

            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string dumpFile = Path.Combine(crashDir, $"OpenPredator_{component}_{timestamp}_pid{Environment.ProcessId}.dmp");

            using (var fs = new FileStream(dumpFile, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                using var proc = Process.GetCurrentProcess();
                // 0x00000000 = MiniDumpNormal (compact stack traces and module info)
                // 0x00000042 = MiniDumpWithDataSegs | MiniDumpWithIndirectlyReferencedMemory
                uint dumpType = 0x00000000;
                bool ok = MiniDumpWriteDump(
                    proc.Handle,
                    (uint)proc.Id,
                    fs.SafeFileHandle.DangerousGetHandle(),
                    dumpType,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    IntPtr.Zero);

                if (ok)
                {
                    Info($"MiniDump successfully created: {dumpFile}");
                    return dumpFile;
                }
                else
                {
                    int err = Marshal.GetLastWin32Error();
                    Warn($"MiniDumpWriteDump returned false (Win32Error=0x{err:X8})");
                }
            }
        }
        catch (Exception dumpEx)
        {
            Error("Exception during MiniDump generation", dumpEx);
        }

        return null;
    }

    private static void WriteEntry(string level, string message)
    {
        if (_minLevel >= LogLevel.Off) return;

        string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
        string threadId = Thread.CurrentThread.ManagedThreadId.ToString();
        string line = $"[{timestamp}] [{level}] [T-{threadId}] {message}";

        lock (_lock)
        {
            if (_writer != null)
            {
                try
                {
                    _writer.WriteLine(line);
                }
                catch { }
            }

            try
            {
                Console.WriteLine(line);
            }
            catch { }
        }
    }

    private static string ResolveLogFilePath(string component)
    {
        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "OpenPredator", $"{component}.log");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "openpredator", $"{component}.log");
    }
}

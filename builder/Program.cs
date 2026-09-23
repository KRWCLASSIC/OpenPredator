using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace OpenPredator.Builder;

public static class Program
{
    private static readonly string RootDir = FindRepoRoot();

    private static string FindRepoRoot()
    {
        // 1. Check current working directory
        string cwd = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(cwd, "OpenPredator.sln")))
        {
            return cwd;
        }

        // 2. Search upward from AppContext.BaseDirectory
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "OpenPredator.sln")))
            {
                return dir.FullName;
            }
            dir = dir.Parent;
        }

        return cwd;
    }

    public static async Task<int> Main(string[] args)
    {
        string rawTarget = args.Length > 0 ? args[0].Trim() : "all";

        if (rawTarget.Equals("help", StringComparison.OrdinalIgnoreCase) ||
            rawTarget.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
            rawTarget.Equals("-h", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        // Parse optional OS prefix (e.g. windows:all, linux:dev, win:installer)
        string targetRid = GetDefaultRid();
        string command = rawTarget;

        int colonIdx = rawTarget.IndexOf(':');
        if (colonIdx > 0)
        {
            string osPrefix = rawTarget.Substring(0, colonIdx).ToLowerInvariant();
            command = rawTarget.Substring(colonIdx + 1).ToLowerInvariant();

            targetRid = osPrefix switch
            {
                "windows" or "win" => "win-x64",
                "linux" or "lin" => "linux-x64",
                _ => GetDefaultRid()
            };
        }
        else
        {
            command = rawTarget.ToLowerInvariant();
        }

        PrintHeader(command, targetRid);

        if (OperatingSystem.IsWindows() && targetRid.StartsWith("linux", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Notice] Cross-OS NativeAOT compilation from Windows to Linux is not supported directly.");
            Console.WriteLine("         To build Linux native binaries, please run './build.sh' inside WSL2, Linux, or a Linux Docker container.\n");
            Console.ResetColor();
            return 1;
        }

        if (OperatingSystem.IsLinux() && targetRid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("[Notice] Cross-OS NativeAOT compilation from Linux to Windows is not supported directly.");
            Console.WriteLine("         To build Windows native binaries, please run '.\\build.bat' on a Windows machine.\n");
            Console.ResetColor();
            return 1;
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            int result = command switch
            {
                "all" => await BuildAllAsync(targetRid, isDebug: false),
                "dev" => await BuildDevAsync(targetRid),
                "installer" => await BuildInstallerAsync(targetRid, skipBuild: false),
                "portable" or "zip" => await BuildPortableAsync(targetRid, skipBuild: false),
                "pack" or "package" => await BuildPackAsync(targetRid),
                "clean" => Clean(),
                "version" or "ver" => HandleVersionCommand(args.Length > 1 ? args[1] : null),
                _ => HandleUnknownCommand(command)
            };

            stopwatch.Stop();
            if (result == 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[Success] Build pipeline completed successfully in {stopwatch.Elapsed.TotalSeconds:F2}s.\n");
                Console.ResetColor();
            }
            else
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[Failed] Build pipeline failed with exit code {result}.\n");
                Console.ResetColor();
            }

            return result;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[Error] Unhandled build exception: {ex.Message}\n");
            Console.ResetColor();
            return 1;
        }
    }

    private static string GetDefaultRid()
    {
        if (OperatingSystem.IsWindows()) return "win-x64";
        if (OperatingSystem.IsLinux()) return "linux-x64";
        return RuntimeInformation.RuntimeIdentifier;
    }

    private static string GetCurrentVersion()
    {
        string propsPath = Path.Combine(RootDir, "Directory.Build.props");
        if (File.Exists(propsPath))
        {
            var content = File.ReadAllText(propsPath);
            var m = System.Text.RegularExpressions.Regex.Match(content, @"<Version>(.*?)</Version>");
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                return m.Groups[1].Value.Trim();
            }
        }
        return "1.0.0";
    }

    private static void PrintHeader(string command, string rid)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================");
        Console.WriteLine("               OpenPredator Build Pipeline               ");
        Console.WriteLine("=========================================================");
        Console.ResetColor();
        Console.WriteLine($"  Target Command : \x1b[1;37m{command}\x1b[0m");
        Console.WriteLine($"  Target Runtime : \x1b[1;36m{rid}\x1b[0m");
        Console.WriteLine($"  Project Version: \x1b[1;32mv{GetCurrentVersion()}\x1b[0m");
        Console.WriteLine($"  Repository Root: \x1b[90m{RootDir}\x1b[0m\n");
    }

    private static void PrintHelp()
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================");
        Console.WriteLine("               OpenPredator Build Runner                 ");
        Console.WriteLine("=========================================================");
        Console.ResetColor();
        Console.WriteLine("\nUsage: build [target] [options]\n");
        Console.WriteLine("Targets:");
        Console.WriteLine("  all                 Release build of Service & CLI (excludes test suite) [Default]");
        Console.WriteLine("  installer           Release build + Windows Inno Setup installer packaging");
        Console.WriteLine("  portable            Release build + Portable standalone ZIP packaging");
        Console.WriteLine("  pack                Full release packaging (Installer + Portable ZIP + SHA256SUMS)");
        Console.WriteLine("  dev                 Debug build including test suite (openpredator-testsuite)");
        Console.WriteLine("  clean               Clean all dist/, .temp/, artifacts/, bin/, and obj/ directories");
        Console.WriteLine("  version [new_ver]   Inspect or bump version across Directory.Build.props & Inno Setup");
        Console.WriteLine("  help                Display this help screen\n");
        Console.WriteLine("Target Prefix Syntax:");
        Console.WriteLine("  windows:all         Build Release binaries targeting Windows (win-x64)");
        Console.WriteLine("  windows:installer   Build and package Windows Setup installer");
        Console.WriteLine("  windows:portable    Build and package Windows portable ZIP");
        Console.WriteLine("  windows:pack        Full Windows release packaging");
        Console.WriteLine("  linux:all           Build Release binaries targeting Linux (linux-x64)");
        Console.WriteLine("  linux:dev           Build Debug binaries + test suite for Linux\n");
    }

    private static int HandleVersionCommand(string? newVersion)
    {
        string propsPath = Path.Combine(RootDir, "Directory.Build.props");
        string issPath = Path.Combine(RootDir, "builder", "OpenPredatorSetup.iss");

        if (string.IsNullOrWhiteSpace(newVersion))
        {
            Console.WriteLine("\x1b[1;36m=== Current Project Versions ===\x1b[0m\n");
            if (File.Exists(propsPath))
            {
                var content = File.ReadAllText(propsPath);
                var mVer = System.Text.RegularExpressions.Regex.Match(content, @"<Version>(.*?)</Version>");
                var mAss = System.Text.RegularExpressions.Regex.Match(content, @"<AssemblyVersion>(.*?)</AssemblyVersion>");
                Console.WriteLine($"  Directory.Build.props : Version={mVer.Groups[1].Value}, AssemblyVersion={mAss.Groups[1].Value}");
            }
            if (File.Exists(issPath))
            {
                var content = File.ReadAllText(issPath);
                var mIss = System.Text.RegularExpressions.Regex.Match(content, @"#define MyAppVersion ""(.*?)""");
                var mInfo = System.Text.RegularExpressions.Regex.Match(content, @"VersionInfoVersion=(.*)");
                Console.WriteLine($"  OpenPredatorSetup.iss : MyAppVersion={mIss.Groups[1].Value}, VersionInfoVersion={mInfo.Groups[1].Value}");
            }
            Console.WriteLine("\n\x1b[90mTo update version: build version <new_version> (e.g. 1.0.1 or 1.0.1.1)\x1b[0m\n");
            return 0;
        }

        newVersion = newVersion.Trim().TrimStart('v', 'V');
        var parts = newVersion.Split('.');
        if (parts.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Invalid version format: '{newVersion}'. Expected at least Major.Minor (e.g. 1.0.1 or 1.0.1.1).");
            Console.ResetColor();
            return 1;
        }

        string semVer;
        string fourPartVer;

        if (parts.Length == 2)
        {
            semVer = $"{parts[0]}.{parts[1]}.0";
            fourPartVer = $"{parts[0]}.{parts[1]}.0.0";
        }
        else if (parts.Length == 3)
        {
            semVer = $"{parts[0]}.{parts[1]}.{parts[2]}";
            fourPartVer = $"{parts[0]}.{parts[1]}.{parts[2]}.0";
        }
        else
        {
            semVer = $"{parts[0]}.{parts[1]}.{parts[2]}";
            fourPartVer = $"{parts[0]}.{parts[1]}.{parts[2]}.{parts[3]}";
        }

        Console.WriteLine($"\x1b[1;36m>>> Updating Project Version to {semVer} ({fourPartVer})...\x1b[0m\n");

        // 1. Update Directory.Build.props
        if (File.Exists(propsPath))
        {
            string content = File.ReadAllText(propsPath);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<Version>.*?</Version>", $"<Version>{semVer}</Version>");
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<AssemblyVersion>.*?</AssemblyVersion>", $"<AssemblyVersion>{fourPartVer}</AssemblyVersion>");
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<FileVersion>.*?</FileVersion>", $"<FileVersion>{fourPartVer}</FileVersion>");
            File.WriteAllText(propsPath, content);
            Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Updated {Path.GetRelativePath(RootDir, propsPath)}");
        }

        // 2. Update OpenPredatorSetup.iss
        if (File.Exists(issPath))
        {
            string content = File.ReadAllText(issPath);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"#define MyAppVersion "".*?""", $"#define MyAppVersion \"{semVer}\"");
            content = System.Text.RegularExpressions.Regex.Replace(content, @"VersionInfoVersion=.*", $"VersionInfoVersion={fourPartVer}");
            File.WriteAllText(issPath, content);
            Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Updated {Path.GetRelativePath(RootDir, issPath)}");
        }

        Console.WriteLine($"\n\x1b[1;32m[Success]\x1b[0m All version fields updated to: \x1b[1;37m{semVer}\x1b[0m (Assembly/File: \x1b[1;36m{fourPartVer}\x1b[0m)\n");
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown build target: '{command}'");
        Console.ResetColor();
        PrintHelp();
        return 1;
    }

    private static void EnsureProcessesTerminatedOnWindows()
    {
        if (!OperatingSystem.IsWindows()) return;

        Console.WriteLine("\x1b[90m[Pre-Build] Checking and stopping running daemons & processes...\x1b[0m");
        RunProcessSilently("sc.exe", "stop OpenPredator");
        RunProcessSilently("net.exe", "stop OpenPredator");
        RunProcessSilently("sc.exe", "stop OpenPredatorService");
        RunProcessSilently("net.exe", "stop OpenPredatorService");
        RunProcessSilently("taskkill.exe", "/F /T /IM openpredator.exe");
        RunProcessSilently("taskkill.exe", "/F /T /IM openpredator-service.exe");
        RunProcessSilently("taskkill.exe", "/F /T /IM openpredator-testsuite.exe");
        RunProcessSilently("taskkill.exe", "/F /FI \"IMAGENAME eq OpenPredator*Setup.exe\"");
        RunProcessSilently("taskkill.exe", "/F /IM Setup.exe");
        Thread.Sleep(300);
    }

    private static async Task<int> BuildAllAsync(string rid, bool isDebug)
    {
        EnsureProcessesTerminatedOnWindows();

        string config = isDebug ? "Debug" : "Release";
        Console.WriteLine($"\x1b[1;36m>>> Compiling Core Components ({config}, {rid})...\x1b[0m\n");

        // 1. Publish Service
        int svcResult = await RunDotnetPublishAsync("src/OpenPredator.Service/OpenPredator.Service.csproj", config, rid);
        if (svcResult != 0) return svcResult;

        // 2. Publish CLI
        int cliResult = await RunDotnetPublishAsync("src/OpenPredator.Cli/OpenPredator.Cli.csproj", config, rid);
        if (cliResult != 0) return cliResult;

        Console.WriteLine($"\n\x1b[1;32m[OK]\x1b[0m Binaries published to: \x1b[1;37mdist/{rid}/\x1b[0m");
        return 0;
    }

    private static async Task<int> BuildDevAsync(string rid)
    {
        EnsureProcessesTerminatedOnWindows();

        Console.WriteLine($"\x1b[1;36m>>> Compiling Full Development Stack with Testing Suite (Debug, {rid})...\x1b[0m\n");

        int res = await BuildAllAsync(rid, isDebug: true);
        if (res != 0) return res;

        // Publish Testing Suite
        Console.WriteLine("\n\x1b[1;36m>>> Compiling OpenPredator Testing Suite...\x1b[0m");
        int testSuiteRes = await RunDotnetPublishAsync("src/OpenPredator.TestingSuite/OpenPredator.TestingSuite.csproj", "Debug", rid);
        if (testSuiteRes != 0) return testSuiteRes;

        Console.WriteLine($"\n\x1b[1;32m[OK]\x1b[0m Dev stack published to: \x1b[1;37mdist/{rid}/\x1b[0m");
        return 0;
    }

    private static async Task<int> BuildInstallerAsync(string rid, bool skipBuild)
    {
        if (!skipBuild)
        {
            Console.WriteLine("\x1b[1;36m[Step 1/2] Compiling fresh release binaries...\x1b[0m");
            int buildRes = await BuildAllAsync(rid, isDebug: false);
            if (buildRes != 0) return buildRes;
            Console.WriteLine("\n\x1b[1;36m[Step 2/2] Packaging installer...\x1b[0m");
        }

        if (OperatingSystem.IsWindows() || rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
        {
            string? isccPath = FindInnoSetupCompiler();
            if (string.IsNullOrEmpty(isccPath) || !File.Exists(isccPath))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Error] Inno Setup 6 (ISCC.exe) not found on system.");
                Console.WriteLine("        Please install Inno Setup 6 or add ISCC.exe to your PATH.");
                Console.ResetColor();
                return 1;
            }

            Console.WriteLine($"Compiling Inno Setup script with: {isccPath}");
            string issPath = Path.Combine(RootDir, "builder", "OpenPredatorSetup.iss");
            int isccExit = await RunProcessAsync(isccPath, $"\"{issPath}\"", RootDir);
            if (isccExit != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Error] Inno Setup packaging failed.");
                Console.ResetColor();
                return isccExit;
            }

            Console.WriteLine($"\n\x1b[1;32m[OK]\x1b[0m Installer package created in: \x1b[1;37mdist/installer/\x1b[0m");
            return 0;
        }
            // Linux distribution tarball
            string version = GetCurrentVersion();
            string distDir = Path.Combine(RootDir, "dist", rid);
            string outDir = Path.Combine(RootDir, "dist", "installer");
            Directory.CreateDirectory(outDir);
            string tarFile = Path.Combine(outDir, $"OpenPredator-v{version}-{rid}.tar.gz");

            Console.WriteLine($"Creating distribution archive: {tarFile}");
            int tarExit = await RunProcessAsync("tar", $"-czf \"{tarFile}\" -C \"{distDir}\" .", RootDir);
            if (tarExit == 0)
            {
                Console.WriteLine($"\n\x1b[1;32m[OK]\x1b[0m Linux archive created at: \x1b[1;37m{tarFile}\x1b[0m");
                return 0;
            }
            return tarExit;
        }
    }

    private static async Task<int> BuildPortableAsync(string rid, bool skipBuild)
    {
        if (!skipBuild)
        {
            Console.WriteLine("\x1b[1;36m[Step 1/2] Compiling fresh release binaries...\x1b[0m");
            int buildRes = await BuildAllAsync(rid, isDebug: false);
            if (buildRes != 0) return buildRes;
            Console.WriteLine("\n\x1b[1;36m[Step 2/2] Packaging portable ZIP...\x1b[0m");
        }

        string version = GetCurrentVersion();
        string distBinDir = Path.Combine(RootDir, "dist", rid);
        string portableOutDir = Path.Combine(RootDir, "dist", "portable");
        Directory.CreateDirectory(portableOutDir);

        string zipName = $"OpenPredator-v{version}-{rid}-portable.zip";
        string zipPath = Path.Combine(portableOutDir, zipName);
        string stagingDir = Path.Combine(RootDir, ".temp", "portable_staging");

        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }
        Directory.CreateDirectory(stagingDir);

        // 1. Copy Executables
        if (Directory.Exists(distBinDir))
        {
            foreach (var file in Directory.GetFiles(distBinDir))
            {
                string fn = Path.GetFileName(file);
                File.Copy(file, Path.Combine(stagingDir, fn), true);
            }
        }

        // 2. Copy Documentation
        string licenseSrc = Path.Combine(RootDir, "LICENSE");
        if (File.Exists(licenseSrc))
        {
            File.Copy(licenseSrc, Path.Combine(stagingDir, "LICENSE.txt"), true);
        }

        string readmeSrc = Path.Combine(RootDir, "README.md");
        if (File.Exists(readmeSrc))
        {
            File.Copy(readmeSrc, Path.Combine(stagingDir, "README.txt"), true);
        }

        // 3. Copy Windows Service Helper Scripts from builder directory
        if (rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
        {
            string installScriptSrc = Path.Combine(RootDir, "builder", "install-service.bat");
            if (File.Exists(installScriptSrc))
            {
                File.Copy(installScriptSrc, Path.Combine(stagingDir, "install-service.bat"), true);
            }

            string uninstallScriptSrc = Path.Combine(RootDir, "builder", "uninstall-service.bat");
            if (File.Exists(uninstallScriptSrc))
            {
                File.Copy(uninstallScriptSrc, Path.Combine(stagingDir, "uninstall-service.bat"), true);
            }
        }

        // 4. Create Zip Archive
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Optimal, false);
        Directory.Delete(stagingDir, true);

        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Portable ZIP package created: \x1b[1;37mdist/portable/{zipName}\x1b[0m");
        return 0;
    }

    private static async Task<int> BuildPackAsync(string rid)
    {
        Console.WriteLine("\x1b[1;36m[1/3] Compiling fresh release binaries...\x1b[0m");
        int buildRes = await BuildAllAsync(rid, isDebug: false);
        if (buildRes != 0) return buildRes;

        Console.WriteLine("\n\x1b[1;36m[2/3] Packaging installer...\x1b[0m");
        int instRes = await BuildInstallerAsync(rid, skipBuild: true);
        if (instRes != 0) return instRes;

        Console.WriteLine("\n\x1b[1;36m[3/3] Packaging portable ZIP...\x1b[0m");
        int portRes = await BuildPortableAsync(rid, skipBuild: true);
        if (portRes != 0) return portRes;

        Console.WriteLine("\n\x1b[1;36m>>> Computing SHA256 Checksums for Release Artifacts...\x1b[0m");
        GenerateSha256Sums();

        return 0;
    }

    private static void GenerateSha256Sums()
    {
        string distDir = Path.Combine(RootDir, "dist");
        if (!Directory.Exists(distDir)) return;

        var entries = new List<(string RelativePath, string Hash)>();

        foreach (var file in Directory.GetFiles(distDir, "*.*", SearchOption.AllDirectories))
        {
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext is not (".exe" or ".zip" or ".gz" or ".tar" or ".deb" or ".rpm")) continue;
            if (Path.GetFileName(file).Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) continue;

            byte[] bytes = File.ReadAllBytes(file);
            byte[] hashBytes = SHA256.HashData(bytes);
            string hexHash = Convert.ToHexStringLower(hashBytes);

            string relPath = Path.GetRelativePath(distDir, file).Replace('\\', '/');
            entries.Add((relPath, hexHash));
        }

        var sb = new StringBuilder();
        Console.WriteLine("\n\x1b[1;37m--------------------------------------------------------------------------------\x1b[0m");
        Console.WriteLine("\x1b[1;36mSHA256 CHECKSUMS\x1b[0m");
        Console.WriteLine("\x1b[1;37m--------------------------------------------------------------------------------\x1b[0m");

        foreach (var (relPath, hash) in entries)
        {
            string line = $"{hash}  {relPath}";
            sb.AppendLine(line);
            Console.WriteLine($"  {hash}  \x1b[1;33m{relPath}\x1b[0m");
        }
        Console.WriteLine("\x1b[1;37m--------------------------------------------------------------------------------\x1b[0m\n");

        string sumsFile = Path.Combine(distDir, "SHA256SUMS.txt");
        File.WriteAllText(sumsFile, sb.ToString());
        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Saved checksums manifest: \x1b[1;37mdist/SHA256SUMS.txt\x1b[0m");
    }

    private static int Clean()
    {
        Console.WriteLine("\x1b[1;36m>>> Cleaning artifacts and build output directories...\x1b[0m\n");

        string[] dirsToClean =
        [
            Path.Combine(RootDir, "dist"),
            Path.Combine(RootDir, ".temp"),
            Path.Combine(RootDir, "artifacts")
        ];

        foreach (var dir in dirsToClean)
        {
            if (Directory.Exists(dir))
            {
                try
                {
                    Directory.Delete(dir, recursive: true);
                    Console.WriteLine($"\x1b[1;32m[Removed]\x1b[0m {Path.GetRelativePath(RootDir, dir)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"\x1b[1;33m[Warning]\x1b[0m Could not delete {dir}: {ex.Message}");
                }
            }
        }

        CleanBinAndObj(Path.Combine(RootDir, "src"));
        CleanBinAndObj(Path.Combine(RootDir, "builder"));

        Console.WriteLine("\n\x1b[1;32m[OK]\x1b[0m Clean operation complete.");
        return 0;
    }

    private static void CleanBinAndObj(string startDir)
    {
        if (!Directory.Exists(startDir)) return;

        foreach (var sub in Directory.GetDirectories(startDir, "*", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(sub);
            if (name.Equals("bin", StringComparison.OrdinalIgnoreCase) || name.Equals("obj", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    Directory.Delete(sub, recursive: true);
                    Console.WriteLine($"\x1b[1;32m[Removed]\x1b[0m {Path.GetRelativePath(RootDir, sub)}");
                }
                catch { }
            }
        }
    }

    private static async Task<int> RunDotnetPublishAsync(string projectRelativePath, string configuration, string rid)
    {
        string projectPath = Path.Combine(RootDir, projectRelativePath);
        string args = $"publish \"{projectPath}\" -c {configuration} -r {rid} --self-contained true";

        Console.WriteLine($"\x1b[90m$ dotnet {args}\x1b[0m");
        return await RunProcessAsync("dotnet", args, RootDir);
    }

    private static async Task<int> RunProcessAsync(string fileName, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = false,
            RedirectStandardError = false
        };

        using var process = Process.Start(psi);
        if (process == null) return -1;

        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    private static void RunProcessSilently(string fileName, string arguments)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(2000);
        }
        catch { }
    }

    private static string? FindInnoSetupCompiler()
    {
        string[] candidates =
        [
            @"C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
            @"C:\Program Files\Inno Setup 6\ISCC.exe",
            @"C:\Inno Setup 6\ISCC.exe"
        ];

        foreach (var path in candidates)
        {
            if (File.Exists(path)) return path;
        }

        // Check if ISCC is in PATH
        string? envPath = Environment.GetEnvironmentVariable("PATH");
        if (!string.IsNullOrEmpty(envPath))
        {
            foreach (var segment in envPath.Split(Path.PathSeparator))
            {
                string isccInPath = Path.Combine(segment.Trim(), "ISCC.exe");
                if (File.Exists(isccInPath)) return isccInPath;
            }
        }

        return "ISCC.exe";
    }
}

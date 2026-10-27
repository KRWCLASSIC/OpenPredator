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
        string cwd = Directory.GetCurrentDirectory();
        if (File.Exists(Path.Combine(cwd, "OpenPredator.sln")))
        {
            return cwd;
        }

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
        var options = ParseCommandLine(args);

        if (options.Command.Equals("help", StringComparison.OrdinalIgnoreCase))
        {
            PrintHelp();
            return 0;
        }

        string targetRid = options.TargetRid;
        string command = options.Command;

        PrintHeader(command, targetRid, options.TargetProject, options.IsDebug);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            int result = command switch
            {
                "project" => await BuildProjectCommandAsync(options.TargetProject!, targetRid, options.IsDebug),
                "build" or "all" => await BuildAllAsync(targetRid, isDebug: options.IsDebug),
                "installer" => await BuildInstallerAsync(targetRid, isDev: options.IsDebug, skipBuild: false),
                "portable" or "zip" => await BuildPortableAsync(targetRid, isDev: options.IsDebug, skipBuild: false),
                "pack" or "package" => await BuildPackAsync(targetRid, isDev: options.IsDebug),
                "run" => await BuildRunAsync(targetRid, isDev: options.IsDebug),
                "clean" => Clean(),
                "version" or "ver" => HandleVersionCommand(options.ExtraArg),
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
        return "Undefined";
    }

    private static string GetFourPartVersion()
    {
        string propsPath = Path.Combine(RootDir, "Directory.Build.props");
        if (File.Exists(propsPath))
        {
            var content = File.ReadAllText(propsPath);
            var m = System.Text.RegularExpressions.Regex.Match(content, @"<AssemblyVersion>(.*?)</AssemblyVersion>");
            if (m.Success && !string.IsNullOrWhiteSpace(m.Groups[1].Value))
            {
                return m.Groups[1].Value.Trim();
            }
        }
        string ver = GetCurrentVersion();
        return ver == "Undefined" ? "0.0.0.0" : $"{ver}.0";
    }

    private static void PrintHeader(string command, string rid, string? targetProject = null, bool isDebug = false)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine("=========================================================");
        Console.WriteLine("               OpenPredator Build Pipeline               ");
        Console.WriteLine("=========================================================");
        Console.ResetColor();

        string displayCommand = targetProject != null
            ? $"{targetProject} (targeted)"
            : command;

        string displayConfig = isDebug ? "Debug (--dev)" : "Release";
        string mode = isDebug ? "dev" : "release";

        Console.WriteLine($"  Target Command : \x1b[1;37m{displayCommand}\x1b[0m");
        Console.WriteLine($"  Target Runtime : \x1b[1;36m{rid}\x1b[0m");
        Console.WriteLine($"  Configuration  : \x1b[1;35m{displayConfig}\x1b[0m");
        Console.WriteLine($"  Output Folder  : \x1b[1;33mdist/{mode}/{rid}/\x1b[0m");
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
        Console.WriteLine("  build / all           Compile core applications (GUI, Service, CLI) [Default]");
        Console.WriteLine("  <project>             Targeted compile: gui, cli, service, tui / testsuite");
        Console.WriteLine("  installer             Package installer (Windows Setup Wizard / Linux Portable)");
        Console.WriteLine("  portable / zip        Package standalone portable archive");
        Console.WriteLine("  pack                  Full packaging pipeline (Installer + Portable + SHA256SUMS)");
        Console.WriteLine("  run                   Compile and launch elevated service window + dist shell");
        Console.WriteLine("  clean                 Purge dist/, .temp/, bin/, and obj/ directories");
        Console.WriteLine("  version [new_ver]     Inspect or bump version in Directory.Build.props");
        Console.WriteLine("  help                  Display this help screen\n");
        Console.WriteLine("Options:");
        Console.WriteLine("  --dev / -d            Build debug configuration with TestingSuite (TUI) and PDBs\n");
    }

    private sealed record CommandLineOptions(
        string Command,
        string TargetRid,
        string? TargetProject,
        bool IsDebug,
        string? ExtraArg
    );

    private sealed record ResolvedProjectInfo(
        string CanonicalName,
        string RelativeCsprojPath,
        string FullCsprojPath,
        string ExeName,
        bool IsApplication
    );

    private static readonly Dictionary<string, string> ProjectAliases = new(StringComparer.OrdinalIgnoreCase)
    {
        ["gui"] = "OpenPredator.Gui",
        ["opensense"] = "OpenPredator.Gui",
        ["sense"] = "OpenPredator.Gui",
        ["ui"] = "OpenPredator.Gui",
        ["openpredatorgui"] = "OpenPredator.Gui",
        ["openpredator.gui"] = "OpenPredator.Gui",

        ["service"] = "OpenPredator.Service",
        ["svc"] = "OpenPredator.Service",
        ["daemon"] = "OpenPredator.Service",
        ["openpredatorservice"] = "OpenPredator.Service",
        ["openpredator.service"] = "OpenPredator.Service",

        ["cli"] = "OpenPredator.Cli",
        ["console"] = "OpenPredator.Cli",
        ["cmd"] = "OpenPredator.Cli",
        ["openpredatorcli"] = "OpenPredator.Cli",
        ["openpredator.cli"] = "OpenPredator.Cli",

        ["testingsuite"] = "OpenPredator.TestingSuite",
        ["testsuite"] = "OpenPredator.TestingSuite",
        ["tui"] = "OpenPredator.TestingSuite",
        ["test"] = "OpenPredator.TestingSuite",
        ["tests"] = "OpenPredator.TestingSuite",
        ["openpredatortestingsuite"] = "OpenPredator.TestingSuite",
        ["openpredator.testingsuite"] = "OpenPredator.TestingSuite",
    };

    private static bool IsProjectNameOrAlias(string token)
    {
        if (ProjectAliases.ContainsKey(token)) return true;
        if (token.StartsWith("OpenPredator.", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    private static CommandLineOptions ParseCommandLine(string[] args)
    {
        string targetRid = GetDefaultRid();
        string? command = null;
        string? targetProject = null;
        bool isDebug = false;
        string? extraArg = null;

        for (int i = 0; i < args.Length; i++)
        {
            string token = args[i].Trim().Trim('"', '\'');
            if (string.IsNullOrEmpty(token)) continue;

            if (token.Equals("help", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-h", StringComparison.OrdinalIgnoreCase) ||
                token == "/?")
            {
                return new CommandLineOptions("help", targetRid, null, false, null);
            }

            // Configuration flags: --dev, -d, --release
            if (token.Equals("--dev", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-d", StringComparison.OrdinalIgnoreCase))
            {
                isDebug = true;
                continue;
            }

            if (token.Equals("--release", StringComparison.OrdinalIgnoreCase))
            {
                isDebug = false;
                continue;
            }

            // Project flag: --project <Name>, -p <Name>
            string? projCandidate = null;
            if (token.Equals("--project", StringComparison.OrdinalIgnoreCase) ||
                token.Equals("-p", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Length)
                {
                    projCandidate = args[++i].Trim();
                }
            }

            if (projCandidate != null)
            {
                targetProject = projCandidate;
                command = "project";
                continue;
            }

            string lowerToken = token.ToLowerInvariant();
            if (lowerToken is "build" or "all" or "installer" or "portable" or "zip" or "pack" or "package" or "run" or "clean")
            {
                command = lowerToken;
                continue;
            }

            if (lowerToken is "version" or "ver")
            {
                command = "version";
                if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                {
                    extraArg = args[++i].Trim();
                }
                continue;
            }

            // Direct project shorthand (e.g. build gui, build cli, build service, build tui)
            if (IsProjectNameOrAlias(token))
            {
                targetProject = token;
                command = "project";
                continue;
            }

            command ??= lowerToken;
        }

        if (!string.IsNullOrEmpty(targetProject))
        {
            command = "project";
        }

        command ??= "all";

        return new CommandLineOptions(command, targetRid, targetProject, isDebug, extraArg);
    }

    private static ResolvedProjectInfo? ResolveProject(string rawInput, out string? errorMessage)
    {
        errorMessage = null;
        string clean = rawInput.Trim().Trim('"', '\'');
        if (clean.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            clean = Path.GetFileNameWithoutExtension(clean);
        }

        if (ProjectAliases.TryGetValue(clean, out string? canonical))
        {
            clean = canonical;
        }

        string candidateName = clean.StartsWith("OpenPredator.", StringComparison.OrdinalIgnoreCase)
            ? clean
            : $"OpenPredator.{clean}";

        string srcDir = Path.Combine(RootDir, "src");
        if (!Directory.Exists(srcDir))
        {
            errorMessage = $"Directory not found: {srcDir}";
            return null;
        }

        DirectoryInfo? matchedDir = null;
        foreach (var dir in new DirectoryInfo(srcDir).GetDirectories())
        {
            if (dir.Name.Equals(candidateName, StringComparison.OrdinalIgnoreCase) ||
                dir.Name.Equals(clean, StringComparison.OrdinalIgnoreCase))
            {
                matchedDir = dir;
                break;
            }
        }

        if (matchedDir == null)
        {
            errorMessage = $"Could not resolve project '{rawInput}'. Available application targets:\n" +
                           $"  - gui (OpenPredator.Gui / OpenSense)\n" +
                           $"  - service (OpenPredator.Service)\n" +
                           $"  - cli (OpenPredator.Cli)\n" +
                           $"  - tui (OpenPredator.TestingSuite)";
            return null;
        }

        string csprojPath = Path.Combine(matchedDir.FullName, $"{matchedDir.Name}.csproj");
        if (!File.Exists(csprojPath))
        {
            var anyCsproj = Directory.GetFiles(matchedDir.FullName, "*.csproj");
            if (anyCsproj.Length > 0)
            {
                csprojPath = anyCsproj[0];
            }
            else
            {
                errorMessage = $"No .csproj found in {matchedDir.FullName}";
                return null;
            }
        }

        string csprojContent = File.ReadAllText(csprojPath);
        bool isApp = csprojContent.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase) ||
                     csprojContent.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase);

        if (!isApp)
        {
            errorMessage = $"'{matchedDir.Name}' is a class library, not an application.\n" +
                           $"Targeted builds only build executable application projects:\n" +
                           $"  - gui (OpenPredator.Gui)\n" +
                           $"  - service (OpenPredator.Service)\n" +
                           $"  - cli (OpenPredator.Cli)\n" +
                           $"  - tui (OpenPredator.TestingSuite)";
            return null;
        }

        string targetName = matchedDir.Name;
        var mTarget = System.Text.RegularExpressions.Regex.Match(csprojContent, @"<TargetName>(.*?)</TargetName>");
        if (mTarget.Success && !string.IsNullOrWhiteSpace(mTarget.Groups[1].Value))
        {
            targetName = mTarget.Groups[1].Value.Trim();
        }

        string exeName = OperatingSystem.IsWindows() ? $"{targetName}.exe" : targetName;
        string relativeCsproj = Path.GetRelativePath(RootDir, csprojPath).Replace('\\', '/');

        return new ResolvedProjectInfo(
            CanonicalName: matchedDir.Name,
            RelativeCsprojPath: relativeCsproj,
            FullCsprojPath: csprojPath,
            ExeName: exeName,
            IsApplication: true
        );
    }

    private static void TerminateProcessForProject(ResolvedProjectInfo project)
    {
        if (!OperatingSystem.IsWindows()) return;

        Console.WriteLine($"\x1b[90m[Pre-Build] Checking running instances of {project.ExeName}...\x1b[0m");
        RunProcessSilently("taskkill.exe", $"/F /T /IM {project.ExeName}");

        if (project.CanonicalName.Contains("Service", StringComparison.OrdinalIgnoreCase))
        {
            RunProcessSilently("sc.exe", "stop OpenPredator");
            RunProcessSilently("net.exe", "stop OpenPredator");
            RunProcessSilently("sc.exe", "stop OpenPredatorService");
            RunProcessSilently("net.exe", "stop OpenPredatorService");
        }

        Thread.Sleep(200);
    }

    private static async Task<int> BuildProjectCommandAsync(string targetProject, string rid, bool isDebug)
    {
        var resolved = ResolveProject(targetProject, out string? error);
        if (resolved == null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[Error] {error}\n");
            Console.ResetColor();
            return 1;
        }

        TerminateProcessForProject(resolved);

        string config = isDebug ? "Debug" : "Release";
        var (distDir, intermediateDir) = DistManager.EnsureDistLayout(RootDir, rid, isDebug);

        Console.WriteLine($"\x1b[1;36m>>> Targeted Build: {resolved.CanonicalName} ({config}, {rid})...\x1b[0m\n");

        string stagingDir = Path.Combine(RootDir, ".temp", "staging", rid, resolved.CanonicalName);
        int publishResult = await RunDotnetPublishAsync(resolved.RelativeCsprojPath, config, rid, stagingDir, isDebug);
        if (publishResult != 0)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[Error] Failed to compile {resolved.CanonicalName}.");
            Console.ResetColor();
            return publishResult;
        }

        string finalExe = DistManager.DeployApp(stagingDir, distDir, intermediateDir, resolved.ExeName, isDebug);
        DistManager.PrintStructureTree(RootDir, distDir);

        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Application ready: \x1b[1;37m{Path.GetRelativePath(RootDir, finalExe)}\x1b[0m");
        return 0;
    }

    private static int HandleVersionCommand(string? newVersion)
    {
        string propsPath = Path.Combine(RootDir, "Directory.Build.props");

        if (string.IsNullOrWhiteSpace(newVersion))
        {
            Console.WriteLine("\x1b[1;36m=== Current Project Versions ===\x1b[0m\n");
            if (File.Exists(propsPath))
            {
                var content = File.ReadAllText(propsPath);
                var mVer = System.Text.RegularExpressions.Regex.Match(content, @"<Version>(.*?)</Version>");
                var mAss = System.Text.RegularExpressions.Regex.Match(content, @"<AssemblyVersion>(.*?)</AssemblyVersion>");
                var mFile = System.Text.RegularExpressions.Regex.Match(content, @"<FileVersion>(.*?)</FileVersion>");
                Console.WriteLine($"  Directory.Build.props : Version={mVer.Groups[1].Value}, AssemblyVersion={mAss.Groups[1].Value}, FileVersion={mFile.Groups[1].Value}");
                Console.WriteLine($"  OpenPredatorSetup.iss : (Dynamically inherits from build props via /DMyAppVersion & /DVersionInfoVersion)");
            }
            Console.WriteLine("\n\x1b[90mTo update version: build version <new_version> (e.g. 1.1.0 or 1.1.0.1)\x1b[0m\n");
            return 0;
        }

        newVersion = newVersion.Trim().TrimStart('v', 'V');
        var parts = newVersion.Split('.');
        if (parts.Length < 2)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"[Error] Invalid version format: '{newVersion}'. Expected at least Major.Minor (e.g. 1.1.0).");
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

        if (File.Exists(propsPath))
        {
            string content = File.ReadAllText(propsPath);
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<Version>.*?</Version>", $"<Version>{semVer}</Version>");
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<AssemblyVersion>.*?</AssemblyVersion>", $"<AssemblyVersion>{fourPartVer}</AssemblyVersion>");
            content = System.Text.RegularExpressions.Regex.Replace(content, @"<FileVersion>.*?</FileVersion>", $"<FileVersion>{fourPartVer}</FileVersion>");
            File.WriteAllText(propsPath, content);
            Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Updated {Path.GetRelativePath(RootDir, propsPath)}");
        }

        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m OpenPredatorSetup.iss dynamically inherits from build props at compile time.");
        Console.WriteLine($"\n\x1b[1;32m[Success]\x1b[0m All version fields updated to: \x1b[1;37m{semVer}\x1b[0m (Assembly/File: \x1b[1;36m{fourPartVer}\x1b[0m)\n");
        return 0;
    }

    private static int HandleUnknownCommand(string command)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Unknown build target: '{command}'");
        Console.ResetColor();
        Console.WriteLine("\nDid you mean to build a specific project? Example: \x1b[1;37m./build gui\x1b[0m\n");
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
        RunProcessSilently("taskkill.exe", "/F /T /IM openpredator-gui.exe");
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
        var (distDir, intermediateDir) = DistManager.EnsureDistLayout(RootDir, rid, isDebug);

        Console.WriteLine($"\x1b[1;36m>>> Compiling Core Components ({config}, {rid})...\x1b[0m\n");

        // 1. Publish Service
        string svcStaging = Path.Combine(RootDir, ".temp", "staging", rid, "OpenPredator.Service");
        int svcResult = await RunDotnetPublishAsync("src/OpenPredator.Service/OpenPredator.Service.csproj", config, rid, svcStaging, isDebug);
        if (svcResult != 0) return svcResult;
        string svcExe = OperatingSystem.IsWindows() ? "openpredator-service.exe" : "openpredator-service";
        DistManager.DeployApp(svcStaging, distDir, intermediateDir, svcExe, isDebug);

        // 2. Publish CLI
        string cliStaging = Path.Combine(RootDir, ".temp", "staging", rid, "OpenPredator.Cli");
        int cliResult = await RunDotnetPublishAsync("src/OpenPredator.Cli/OpenPredator.Cli.csproj", config, rid, cliStaging, isDebug);
        if (cliResult != 0) return cliResult;
        string cliExe = OperatingSystem.IsWindows() ? "openpredator.exe" : "openpredator";
        DistManager.DeployApp(cliStaging, distDir, intermediateDir, cliExe, isDebug);

        // 3. Publish GUI
        string guiStaging = Path.Combine(RootDir, ".temp", "staging", rid, "OpenPredator.Gui");
        int guiResult = await RunDotnetPublishAsync("src/OpenPredator.Gui/OpenPredator.Gui.csproj", config, rid, guiStaging, isDebug);
        if (guiResult != 0) return guiResult;
        string guiExe = OperatingSystem.IsWindows() ? "openpredator-gui.exe" : "openpredator-gui";
        DistManager.DeployApp(guiStaging, distDir, intermediateDir, guiExe, isDebug);

        // 4. Publish TestingSuite if --dev
        if (isDebug)
        {
            Console.WriteLine("\n\x1b[1;36m>>> Compiling Testing Suite (--dev)...\x1b[0m");
            string tuiStaging = Path.Combine(RootDir, ".temp", "staging", rid, "OpenPredator.TestingSuite");
            int tuiResult = await RunDotnetPublishAsync("src/OpenPredator.TestingSuite/OpenPredator.TestingSuite.csproj", config, rid, tuiStaging, isDebug);
            if (tuiResult != 0) return tuiResult;
            string tuiExe = OperatingSystem.IsWindows() ? "openpredator-testsuite.exe" : "openpredator-testsuite";
            DistManager.DeployApp(tuiStaging, distDir, intermediateDir, tuiExe, isDebug);
        }

        DistManager.PrintStructureTree(RootDir, distDir);
        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Executables staged to: \x1b[1;37m{Path.GetRelativePath(RootDir, distDir)}\x1b[0m");
        return 0;
    }

    private static async Task<int> BuildInstallerAsync(string rid, bool isDev, bool skipBuild)
    {
        string mode = isDev ? "dev" : "release";
        string distDir = Path.Combine(RootDir, "dist", mode, rid);

        if (!skipBuild)
        {
            Console.WriteLine("\x1b[1;36m[Step 1/2] Compiling fresh binaries...\x1b[0m");
            int buildRes = await BuildAllAsync(rid, isDebug: isDev);
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

            string version = GetCurrentVersion();
            string fourPartVer = GetFourPartVersion();
            string outputFilename = isDev
                ? $"OpenPredator-v{version}-{rid}-Setup-dev"
                : $"OpenPredator-v{version}-{rid}-Setup";

            Console.WriteLine($"Compiling Inno Setup script with: {isccPath} (v{version} / {fourPartVer})");
            string issPath = Path.Combine(RootDir, "builder", "OpenPredatorSetup.iss");

            string isccArgs = $"/DMyAppVersion=\"{version}\" /DVersionInfoVersion=\"{fourPartVer}\" /DSourceDir=\"{distDir}\" /DOutputFilename=\"{outputFilename}\" /DIsDev=\"{(isDev ? "1" : "0")}\" \"{issPath}\"";
            int isccExit = await RunProcessAsync(isccPath, isccArgs, RootDir);
            if (isccExit != 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[Error] Inno Setup packaging failed.");
                Console.ResetColor();
                return isccExit;
            }

            Console.WriteLine($"\n\x1b[1;32m[OK]\x1b[0m Installer package created: \x1b[1;37mdist/installer/{outputFilename}.exe\x1b[0m");
            return 0;
        }
        else
        {
            // Linux: standalone portable archive is the clean standard distribution
            Console.WriteLine("\x1b[1;36m[Notice]\x1b[0m Linux targets use standalone portable archives. Creating portable package...");
            return await BuildPortableAsync(rid, isDev: isDev, skipBuild: true);
        }
    }

    private static async Task<int> BuildPortableAsync(string rid, bool isDev, bool skipBuild)
    {
        string mode = isDev ? "dev" : "release";
        string distDir = Path.Combine(RootDir, "dist", mode, rid);

        if (!skipBuild)
        {
            Console.WriteLine("\x1b[1;36m[Step 1/2] Compiling fresh binaries...\x1b[0m");
            int buildRes = await BuildAllAsync(rid, isDebug: isDev);
            if (buildRes != 0) return buildRes;
            Console.WriteLine("\n\x1b[1;36m[Step 2/2] Packaging portable archive...\x1b[0m");
        }

        string version = GetCurrentVersion();
        string portableOutDir = Path.Combine(RootDir, "dist", "portable");
        Directory.CreateDirectory(portableOutDir);

        string zipName = isDev
            ? $"OpenPredator-v{version}-{rid}-portable-dev.zip"
            : $"OpenPredator-v{version}-{rid}-portable.zip";

        string zipPath = Path.Combine(portableOutDir, zipName);
        string stagingDir = Path.Combine(RootDir, ".temp", $"portable_staging_{mode}_{rid}");

        if (Directory.Exists(stagingDir))
        {
            Directory.Delete(stagingDir, true);
        }
        Directory.CreateDirectory(stagingDir);

        // 1. Copy Primary Executables
        if (Directory.Exists(distDir))
        {
            foreach (var file in Directory.GetFiles(distDir))
            {
                string fn = Path.GetFileName(file);
                File.Copy(file, Path.Combine(stagingDir, fn), true);
            }
        }

        // 2. In Dev builds, copy Intermediate/ containing .pdb symbols
        if (isDev)
        {
            string intermediateSrc = Path.Combine(distDir, "Intermediate");
            if (Directory.Exists(intermediateSrc))
            {
                string intermediateDest = Path.Combine(stagingDir, "Intermediate");
                Directory.CreateDirectory(intermediateDest);
                foreach (var file in Directory.GetFiles(intermediateSrc))
                {
                    string fn = Path.GetFileName(file);
                    File.Copy(file, Path.Combine(intermediateDest, fn), true);
                }
            }
        }

        // 3. Copy Documentation
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

        // 4. Copy Windows Service Helper Scripts
        if (OperatingSystem.IsWindows() || rid.StartsWith("win", StringComparison.OrdinalIgnoreCase))
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

        // 5. Create Zip Archive
        if (File.Exists(zipPath))
        {
            File.Delete(zipPath);
        }

        ZipFile.CreateFromDirectory(stagingDir, zipPath, CompressionLevel.Optimal, false);
        Directory.Delete(stagingDir, true);

        Console.WriteLine($"\x1b[1;32m[OK]\x1b[0m Portable package created: \x1b[1;37mdist/portable/{zipName}\x1b[0m");
        return 0;
    }

    private static async Task<int> BuildPackAsync(string rid, bool isDev)
    {
        Console.WriteLine($"\x1b[1;36m[1/3] Compiling complete stack (Dev: {isDev}, Runtime: {rid})...\x1b[0m");
        int buildRes = await BuildAllAsync(rid, isDebug: isDev);
        if (buildRes != 0) return buildRes;

        Console.WriteLine("\n\x1b[1;36m[2/3] Packaging installer / system package...\x1b[0m");
        int instRes = await BuildInstallerAsync(rid, isDev: isDev, skipBuild: true);
        if (instRes != 0) return instRes;

        Console.WriteLine("\n\x1b[1;36m[3/3] Packaging portable standalone archive...\x1b[0m");
        int portRes = await BuildPortableAsync(rid, isDev: isDev, skipBuild: true);
        if (portRes != 0) return portRes;

        Console.WriteLine("\n\x1b[1;36m>>> Computing SHA256 Checksums for Release Artifacts...\x1b[0m");
        GenerateSha256Sums();

        return 0;
    }

    private static async Task<int> BuildRunAsync(string rid, bool isDev)
    {
        Console.WriteLine($"\x1b[1;36m>>> Compiling OpenPredator Stack for Run (Dev: {isDev}, Runtime: {rid})...\x1b[0m\n");
        int buildRes = await BuildAllAsync(rid, isDebug: isDev);
        if (buildRes != 0) return buildRes;

        string mode = isDev ? "dev" : "release";
        string distDir = Path.Combine(RootDir, "dist", mode, rid);

        Console.WriteLine("\n\x1b[1;32m>>> Launching Elevated Service & Developer Shell...\x1b[0m\n");

        if (OperatingSystem.IsWindows())
        {
            string serviceExe = Path.Combine(distDir, "openpredator-service.exe");
            if (File.Exists(serviceExe))
            {
                Console.WriteLine($"  [Elevated Service] Spawning {Path.GetFileName(serviceExe)} in Administrator console...");
                var servicePsi = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k title OpenPredator Service ^& \"\"{serviceExe}\" --run\"",
                    UseShellExecute = true,
                    Verb = "RunAs"
                };
                try
                {
                    Process.Start(servicePsi);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  [Warning] Failed to spawn elevated service window: {ex.Message}");
                }
            }

            Console.WriteLine($"  [Dist Shell] Spawning shell inside {distDir}...");
            var shellPsi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/k title OpenPredator Shell ^& cd /d \"{distDir}\"",
                UseShellExecute = true
            };
            try
            {
                Process.Start(shellPsi);
            }
            catch { }
        }
        else
        {
            string serviceExe = Path.Combine(distDir, "openpredator-service");
            string? terminal = DetectLinuxTerminal();
            if (string.IsNullOrEmpty(terminal))
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("\n[Error] No terminal emulator detected. Please set the $TERMINAL or $TERM_PROGRAM environment variable (e.g. export TERMINAL=kitty).\n");
                Console.ResetColor();
                return 1;
            }

            if (File.Exists(serviceExe))
            {
                Console.WriteLine($"  [Elevated Service] Spawning {Path.GetFileName(serviceExe)} via sudo in {terminal}...");
                LaunchLinuxTerminal(terminal, $"sudo \"{serviceExe}\" --run");
            }

            Console.WriteLine($"  [Dist Shell] Spawning shell in {terminal} at {distDir}...");
            LaunchLinuxTerminal(terminal, null, workingDir: distDir);
        }

        Console.WriteLine("\x1b[1;32m[OK]\x1b[0m Runtime environment launched successfully.");
        return 0;
    }

    private static string? DetectLinuxTerminal()
    {
        string? terminalEnv = Environment.GetEnvironmentVariable("TERMINAL");
        if (!string.IsNullOrEmpty(terminalEnv) && IsCommandAvailable(terminalEnv))
        {
            return terminalEnv;
        }

        string? termProg = Environment.GetEnvironmentVariable("TERM_PROGRAM");
        if (!string.IsNullOrEmpty(termProg) && IsCommandAvailable(termProg))
        {
            return termProg;
        }

        return null;
    }

    private static bool IsCommandAvailable(string cmd)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "which",
                Arguments = cmd,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true
            };
            using var proc = Process.Start(psi);
            proc?.WaitForExit(500);
            return proc?.ExitCode == 0;
        }
        catch { return false; }
    }

    private static void LaunchLinuxTerminal(string terminal, string? commandToRun, string? workingDir = null)
    {
        var psi = new ProcessStartInfo
        {
            FileName = terminal,
            UseShellExecute = true,
            WorkingDirectory = workingDir ?? RootDir
        };

        if (!string.IsNullOrEmpty(commandToRun))
        {
            if (terminal.Contains("gnome-terminal") || terminal.Contains("mate-terminal") || terminal.Contains("xfce4-terminal"))
            {
                psi.Arguments = $"-- bash -c \"{commandToRun}; exec bash\"";
            }
            else
            {
                psi.Arguments = $"-e bash -c \"{commandToRun}; exec bash\"";
            }
        }

        try
        {
            Process.Start(psi);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"  \x1b[1;33m[Warning]\x1b[0m Could not spawn terminal '{terminal}': {ex.Message}");
        }
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

    private static async Task<int> RunDotnetPublishAsync(string projectRelativePath, string configuration, string rid, string stagingDir, bool isDebug)
    {
        if (Directory.Exists(stagingDir))
        {
            try { Directory.Delete(stagingDir, true); } catch { }
        }
        Directory.CreateDirectory(stagingDir);

        string projectPath = Path.Combine(RootDir, projectRelativePath);
        string args = $"publish \"{projectPath}\" -c {configuration} -r {rid} -o \"{stagingDir}\" --self-contained true";

        if (!isDebug)
        {
            args += " -p:StripSymbols=true -p:DebugType=none";
        }
        else
        {
            args += " -p:DebugType=portable";
        }

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

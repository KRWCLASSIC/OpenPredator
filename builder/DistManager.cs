using System;
using System.IO;

namespace OpenPredator.Builder;

/// <summary>
/// Distribution directory manager and artifact layout coordinator.
/// Orchestrates clean deployment of executables to dist/release/<rid>/ and dist/dev/<rid>/,
/// separating debug symbols (.pdb) into Intermediate/ and printing layout visualization.
/// </summary>
public static class DistManager
{
    public static (string DistDir, string IntermediateDir) EnsureDistLayout(string rootDir, string rid, bool isDev)
    {
        string mode = isDev ? "dev" : "release";
        string distDir = Path.Combine(rootDir, "dist", mode, rid);
        string intermediateDir = Path.Combine(distDir, "Intermediate");

        Directory.CreateDirectory(distDir);
        Directory.CreateDirectory(intermediateDir);

        return (distDir, intermediateDir);
    }

    public static string DeployApp(string stagingDir, string distDir, string intermediateDir, string primaryExeName, bool isDev)
    {
        string targetExePath = Path.Combine(distDir, primaryExeName);

        if (!Directory.Exists(stagingDir))
        {
            return targetExePath;
        }

        foreach (var file in Directory.GetFiles(stagingDir))
        {
            string fileName = Path.GetFileName(file);

            if (string.Equals(fileName, primaryExeName, StringComparison.OrdinalIgnoreCase))
            {
                CopyOrOverwrite(file, targetExePath);
            }
            else if (fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase))
            {
                string pdbDest = Path.Combine(intermediateDir, fileName);
                CopyOrOverwrite(file, pdbDest);
            }
            else
            {
                // Place other supporting metadata into Intermediate/
                string dest = Path.Combine(intermediateDir, fileName);
                CopyOrOverwrite(file, dest);
            }
        }

        return targetExePath;
    }

    public static void PrintStructureTree(string rootDir, string distDir)
    {
        if (!Directory.Exists(distDir)) return;

        Console.WriteLine("\n\x1b[1;36m>>> Distribution Layout:\x1b[0m");
        string relDist = Path.GetRelativePath(rootDir, distDir).Replace('\\', '/');
        Console.WriteLine($"\x1b[1;37m{relDist}/\x1b[0m");

        // 1. Primary executables in root of dist/<mode>/<rid>/
        var rootFiles = Directory.GetFiles(distDir);
        var subDirs = Directory.GetDirectories(distDir);

        for (int i = 0; i < rootFiles.Length; i++)
        {
            string file = rootFiles[i];
            bool isLast = (i == rootFiles.Length - 1) && (subDirs.Length == 0);
            string prefix = isLast ? "└── " : "├── ";
            string fileName = Path.GetFileName(file);
            long size = new FileInfo(file).Length;
            string sizeStr = FormatFileSize(size);

            string fileColor = fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !fileName.Contains('.')
                ? "\x1b[1;32m"
                : "\x1b[37m";

            Console.WriteLine($"{prefix}{fileColor}{fileName}\x1b[0m \x1b[90m({sizeStr})\x1b[0m");
        }

        // 2. Subdirectories (such as Intermediate/)
        for (int i = 0; i < subDirs.Length; i++)
        {
            string dir = subDirs[i];
            bool isLastDir = i == subDirs.Length - 1;
            string dirPrefix = isLastDir ? "└── " : "├── ";
            string childIndent = isLastDir ? "    " : "│   ";
            string dirName = Path.GetFileName(dir);

            bool isIntermediate = dirName.Equals("Intermediate", StringComparison.OrdinalIgnoreCase);
            string dirColor = isIntermediate ? "\x1b[90m" : "\x1b[1;33m";

            Console.WriteLine($"{dirPrefix}{dirColor}{dirName}/\x1b[0m");

            var childFiles = Directory.GetFiles(dir);
            for (int j = 0; j < childFiles.Length; j++)
            {
                string file = childFiles[j];
                bool isLastFile = j == childFiles.Length - 1;
                string filePrefix = isLastFile ? "└── " : "├── ";
                string fileName = Path.GetFileName(file);
                long size = new FileInfo(file).Length;
                string sizeStr = FormatFileSize(size);

                string fileColor = fileName.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)
                    ? "\x1b[90m"
                    : "\x1b[37m";

                Console.WriteLine($"{childIndent}{filePrefix}{fileColor}{fileName}\x1b[0m \x1b[90m({sizeStr})\x1b[0m");
            }
        }
        Console.WriteLine();
    }

    private static void CopyOrOverwrite(string source, string destination)
    {
        string? destDir = Path.GetDirectoryName(destination);
        if (destDir != null && !Directory.Exists(destDir))
        {
            Directory.CreateDirectory(destDir);
        }

        File.Copy(source, destination, overwrite: true);
    }

    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / 1024.0 / 1024.0:F2} MB";
    }
}

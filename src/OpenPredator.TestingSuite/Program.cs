using System;
using System.Threading.Tasks;
using OpenPredator.Client;
using OpenPredator.TestingSuite.Diagnostics;

namespace OpenPredator.TestingSuite;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0)
        {
            string arg0 = args[0].ToLowerInvariant();
            if (arg0 is "auto" or "auto-diag" or "--auto" or "diag" or "--diag" or "debug" or "check")
            {
                var diagEngine = new HardwareDiagnosticEngine();
                bool isSnapshotOnly = args.Length > 1 && (args[1].Equals("--snap", StringComparison.OrdinalIgnoreCase) || args[1].Equals("snap", StringComparison.OrdinalIgnoreCase) || args[1].Equals("--snapshot", StringComparison.OrdinalIgnoreCase));

                if (isSnapshotOnly)
                {
                    await diagEngine.RunComparisonAsync();
                }
                else
                {
                    await diagEngine.RunAutoComparisonAsync();
                }
                return 0;
            }
        }

        using var term = new TerminalEngine();
        using var client = new OpenPredatorClient();

        // Attempt connect to OpenPredator or PSSvc named pipe
        await client.ConnectAsync(1500);

        var tui = new InteractiveTui(client, term);
        await tui.RunAsync();

        term.ClearScreen();
        Console.WriteLine("Exited OpenPredator Testing Suite.");
        return 0;
    }
}

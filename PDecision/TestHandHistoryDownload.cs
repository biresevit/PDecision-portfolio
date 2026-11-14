using DBLib;
using DecisionMaking;
using HandHistoryLib;

namespace PDecision
{
    /// <summary>
    /// Console-only test for the hand-history download (DecisionMaking.CustomerHandHistory.DownloadToDirectoryAsync),
    /// run via "PDecision.exe --test-hh-download [--mac=AA-BB-CC-DD-EE-FF] [--days=30] [--gametype=NLH] [--out=path]"
    /// so it can be exercised repeatedly without going through the "Hand histories" window. Output also goes to
    /// &lt;outDir&gt;\_result.txt, since a WinExe process has no console attached when launched from a script
    /// (AllocConsole opens a separate window whose text a calling script cannot capture).
    /// </summary>
    internal static class TestHandHistoryDownload
    {
        public static int Run(string[] args)
        {
            string macArg = null;
            int days = 30;
            EnumGameType? gameType = null;
            string outDir = Path.Combine(Path.GetTempPath(), "pdecision_hh_test");
            foreach (var arg in args)
            {
                if (arg.StartsWith("--mac=")) macArg = arg["--mac=".Length..].ToUpperInvariant();
                else if (arg.StartsWith("--days=")) days = int.Parse(arg["--days=".Length..]);
                else if (arg.StartsWith("--gametype=")) gameType = Enum.Parse<EnumGameType>(arg["--gametype=".Length..], ignoreCase: true);
                else if (arg.StartsWith("--out=")) outDir = arg["--out=".Length..];
            }

            if (Directory.Exists(outDir))
            {
                Directory.Delete(outDir, recursive: true);
            }
            Directory.CreateDirectory(outDir);
            string resultFile = Path.Combine(outDir, "_result.txt");
            using var log = new StreamWriter(resultFile, append: false) { AutoFlush = true };
            void Line(string text)
            {
                Console.WriteLine(text);
                log.WriteLine(text);
            }

            List<string> macs = macArg != null ? new List<string> { macArg } : MachineConfig.GetLocalMacAddresses();
            Line($"Resolving machine registration for MAC(s): {string.Join(", ", macs)}");

            var machine = MachineConfig.Load(Singleton.ConnString, macs, Singleton.Instance.logger, out string error, out _);
            if (machine == null)
            {
                Line("FAILED: " + error);
                return 1;
            }
            Line($"Registered: customer={machine.CustomerName} (Id={machine.CustomerId}), casino={machine.Casino}, platform={machine.Platform}");

            CustomerHandHistory.MacAddress = machine.MacAddress;
            CustomerHandHistory.CustomerId = machine.CustomerId;

            DateTime toUtc = DateTime.UtcNow;
            DateTime fromUtc = toUtc.AddDays(-days);
            Line($"Downloading {(gameType.HasValue ? gameType.Value.ToString() : "all game types")} from {fromUtc:yyyy-MM-dd} to {toUtc:yyyy-MM-dd} into {outDir}");

            try
            {
                var hhs3 = new HandHistoryS3(connectionString: Singleton.S3ConnString, containerName: Singleton.S3ContainerName, logger: Singleton.Instance.logger);
                int count = CustomerHandHistory.DownloadToDirectoryAsync(machine.Casino, fromUtc, toUtc, gameType, outDir, hhs3).GetAwaiter().GetResult();
                Line($"OK: downloaded {count} hand(s) into {outDir}");
                return 0;
            }
            catch (Exception ex)
            {
                Line("FAILED: " + ex);
                return 1;
            }
        }
    }
}

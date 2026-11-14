using System.IO;
using System.Runtime.InteropServices;

namespace PDecision
{
    internal static class Program
    {
        [DllImport("kernel32.dll")]
        private static extern bool AllocConsole();

        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--test-hh-download")
            {
                try
                {
                    AllocConsole();
                    // WinExe apps have no console at startup, so Console.Out/Error were bound to invalid handles;
                    // they must be reopened after AllocConsole for writes to actually reach the new console window.
                    Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
                    Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
                    Environment.ExitCode = TestHandHistoryDownload.Run(args[1..]);
                }
                catch (Exception ex)
                {
                    File.WriteAllText(Path.Combine(Path.GetTempPath(), "pdecision_test_crash.txt"), ex.ToString());
                    Environment.ExitCode = 1;
                }
                return;
            }

            EnsureLocalAuthentication();

            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.Run(new DecisionUI());
        }

        /// <summary>
        /// PokerSuite.Shared refuses to create a logger, load settings or open the database without
        /// C:\PokerSuite\Authentication.txt. Nothing on a server checks that file; PDecision's real gate is the
        /// paired machine registration (dbo.CustomerMachine). So a PC without one gets it here, written by the
        /// library itself, and pairing stays the only setup step. An existing file is never touched.
        /// </summary>
        private static void EnsureLocalAuthentication()
        {
            const string path = @"C:\PokerSuite\Authentication.txt";
            try
            {
                if (File.Exists(path))
                    return;
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                PokerSuite.Shared.Helpers.AuthenticationHelpers.WriteAuthenticationDataToFile(Guid.NewGuid().ToString(), Environment.MachineName, 0);
            }
            catch (Exception)
            {
                // Startup then reports the library's own error, as before.
            }
        }
    }
}

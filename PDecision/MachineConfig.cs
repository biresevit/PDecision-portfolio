using DBLib;
using Microsoft.Data.SqlClient;
using NLog;
using PokerSuite.Shared.Constants;
using System.Data;
using PokerSuite.Shared.Utilities;

namespace PDecision
{
    /// <summary>
    /// A casino this PC plays, with the platform its hands are saved under.
    /// </summary>
    internal sealed record MachineCasino(EnumCasino Casino, enPlatform Platform);

    /// <summary>
    /// Which customer this PC belongs to and which casinos and platforms it plays, from dbo.CustomerMachine and
    /// dbo.MachineCasino in Azure SQL. The PC is found using the same network adapter discovery as Setup and Updater.
    /// </summary>
    internal class MachineConfig
    {
        public int MachineId { get; init; }
        public string MacAddress { get; init; }
        public int CustomerId { get; init; }
        public string CustomerName { get; init; }
        public EnumCasino Casino { get; init; }
        public enPlatform Platform { get; init; }
        // every casino the PC plays, the registered Casino/Platform first; one PDecision serves them all
        public IReadOnlyList<MachineCasino> Casinos { get; init; }

        /// <summary>
        /// The casino of a request's platform, or null if the PC doesn't play it. A PC playing one casino keeps
        /// its registration whatever the client says, as before.
        /// </summary>
        public MachineCasino CasinoOf(enPlatform platform)
        {
            if (Casinos.Count == 1)
            {
                return Casinos[0];
            }
            return Casinos.FirstOrDefault(c => c.Platform == platform);
        }

        // enPlatform.BluffPokerPlatform = 18 is not in PokerSuite.Shared 1.10.0.1 (17 is HHPoker). Remove these
        // two members once the package that adds it is referenced.
        public const enPlatform BluffPokerPlatform = (enPlatform)18;

        public static string PlatformName(enPlatform platform)
        {
            return platform == BluffPokerPlatform ? nameof(BluffPokerPlatform) : platform.ToString();
        }

        private static string Describe(MachineCasino c) => $"casino {c.Casino}, platform {PlatformName(c.Platform)}";

        private static MachineCasino ParseCasino(string casino, string platform, out string error)
        {
            error = null;
            if (!Enum.TryParse(casino, ignoreCase: true, out EnumCasino enumCasino))
            {
                error = $"registered casino '{casino}' is not an EnumCasino value";
                return null;
            }
            enPlatform enumPlatform = BluffPokerPlatform;
            if (!string.Equals(platform, nameof(BluffPokerPlatform), StringComparison.OrdinalIgnoreCase) &&
                !Enum.TryParse(platform, ignoreCase: true, out enumPlatform))
            {
                error = $"registered platform '{platform}' is not an enPlatform value";
                return null;
            }
            return new MachineCasino(enumCasino, enumPlatform);
        }

        /// <summary>
        /// Registered MAC candidates (AA-BB-CC-DD-EE-FF), connected or not.
        /// </summary>
        public static List<string> GetLocalMacAddresses()
        {
            return MachineNetworkIdentity.GetMacAddresses().ToList();
        }

        /// <summary>
        /// Reads the registration of this PC. Returns null and sets error if it is not registered, inactive or misconfigured.
        /// Logs the MAC addresses being looked up unconditionally (not just on success), and the full exception
        /// (not just its message) on failure, so a "failed to authenticate"-type report can actually be diagnosed.
        /// </summary>
        public static MachineConfig Load(string connectionString, List<string> macs, Logger logger, out string error, out bool notRegistered)
        {
            return Load(connectionString, macs, logger, out error, out notRegistered, out _);
        }

        /// <param name="transient">true when the lookup itself failed (network not up yet, SQL unreachable,
        /// timeout), so trying again later can succeed; false for a definite answer such as "not registered".</param>
        public static MachineConfig Load(string connectionString, List<string> macs, Logger logger, out string error, out bool notRegistered, out bool transient)
        {
            error = null;
            notRegistered = false;
            transient = false;
            logger?.Info($"Resolving machine registration for MAC address(es): {string.Join(", ", macs)}");
            if (macs.Count == 0)
            {
                error = "no Ethernet or Wi-Fi adapter with a valid MAC address found";
                logger?.Error(error);
                return null;
            }

            try
            {
                using var con = new SqlConnection(connectionString);
                con.Open();
                using var cmd = new SqlCommand("dbo.Machine_GetConfig", con) { CommandType = CommandType.StoredProcedure, CommandTimeout = 15 };
                cmd.Parameters.AddWithValue("@MacAddresses", string.Join(",", macs));
                using var reader = cmd.ExecuteReader();
                if (!reader.Read())
                {
                    notRegistered = true;
                    error = $"this PC is not registered or not active. Register one of its MAC addresses: {string.Join(", ", macs)}";
                    logger?.Error(error);
                    return null;
                }

                var primary = ParseCasino(reader.GetString(reader.GetOrdinal("Casino")), reader.GetString(reader.GetOrdinal("Platform")), out error);
                if (primary == null)
                {
                    logger?.Error(error);
                    return null;
                }
                int machineId = reader.GetInt32(reader.GetOrdinal("MachineId"));
                string macAddress = reader.GetString(reader.GetOrdinal("MacAddress"));
                int customerId = reader.GetInt32(reader.GetOrdinal("CustomerId"));
                string customerName = reader.GetString(reader.GetOrdinal("CustomerName"));

                // second result set: every casino of the PC (none from a database without dbo.MachineCasino)
                var casinos = new List<MachineCasino> { primary };
                if (reader.NextResult())
                {
                    while (reader.Read())
                    {
                        var casino = ParseCasino(reader.GetString(0), reader.GetString(1), out string casinoError);
                        if (casino == null)
                        {
                            logger?.Error(casinoError + ", ignored");
                        }
                        else if (!casinos.Any(c => c.Casino == casino.Casino))
                        {
                            casinos.Add(casino);
                        }
                    }
                }
                if (casinos.GroupBy(c => c.Platform).Any(g => g.Count() > 1))
                {
                    error = "two registered casinos of this PC use the same platform: " + string.Join(", ", casinos.Select(Describe));
                    logger?.Error(error);
                    return null;
                }

                var result = new MachineConfig
                {
                    MachineId = machineId,
                    MacAddress = macAddress,
                    CustomerId = customerId,
                    CustomerName = customerName,
                    Casino = primary.Casino,
                    Platform = primary.Platform,
                    Casinos = casinos,
                };
                logger?.Info($"Machine {result.MacAddress} of customer {result.CustomerName} plays {string.Join(", ", casinos.Select(Describe))}");
                return result;
            }
            catch (Exception ex)
            {
                transient = true;
                error = "could not read the machine registration: " + ex.Message;
                // ex.Message alone hides which kind of failure this was (SQL login rejected vs. unreachable
                // server vs. TLS/firewall vs. something else) - log the full exception so that's diagnosable.
                logger?.Error("Machine registration lookup failed (MAC(s): " + string.Join(", ", macs) + "): " + ex);
                return null;
            }
        }
    }
}

using DBLib;
using DecisionMaking;
using DecisionMaking.Accounts;
using DecisionMaking.DecisionMaking;
using DecisionMaking.statistika;
using DecisionMaking.TCP;
using HandStateLib;
using NLog;
using PipesLib;
using PokerSuite.Shared.Constants;
using PokerSuite.Shared.Helpers;
using PokerSuite.Shared.Models.Game;
using PokerSuite.Shared.Models.Parameters;
using PokerSuite.Shared.Models.Settings;
using PokerSuite.Shared.Services;
using PokerSuite.Shared.Utilities;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace PDecision
{
    internal class Controller
    {
        private const string pipeNameDM = "RequestDM";
        private const string pipeNameHH = "RequestHH";
        private const string successStr = "success";
        private const string failStr = "fail";
        // root folder for the raw HandState dump used to diagnose errors after the fact (see SaveHandStateDump)
        private const string handStateDumpDir = @"C:\PokerSuite\HandStates";
        // one lock per table/hero, reused across hands, so writes for the same hand (from AddHandState and
        // GetDecision, which otherwise run under separate locks) can't race on the sequence number
        private readonly ConcurrentDictionary<string, object> handStateDumpLocks = new();
        private ConcurrentDictionary<string, HandState> dictPrevHS;
        // first frame of the current hand per table and bot, as the client sent it
        private readonly ConcurrentDictionary<string, HandState> dictHandStart = new();
        private ConcurrentDictionary<string, WriteHHToFile> dictWriteHH;
        private ConcurrentDictionary<string, WriteHHToFile> dictWriteHHAI;
        private readonly Logger logger = LogHelpers.GetStandardLogger(enServiceType.PDecision, Assembly.GetExecutingAssembly());
        private ConcurrentDictionary<string, DecisionMaker> dictDM;
        private ConcurrentDictionary<string, HHBuilder> dictHH;
        // each table should have own pipe as it could take several seconds to process request, this way it can work in parallel
        private const int numPipes = 30;
        // requests for the same table and hero are processed one after another, different tables in parallel
        private readonly ConcurrentDictionary<string, object> dmLocks = new();
        private readonly ConcurrentDictionary<string, object> hhLocks = new();
        private ApiSettings apiSettings;
        private ApiServer api;

        public ServiceState State { get; private set; } = ServiceState.Initializing;
        private HandHistorySettings hhSettings;
        private DatabaseSettings dbSettings;
        // why the service failed to start, shown by /api/health
        public string FailureReason { get; private set; }
        // true when this PC's MAC addresses are not registered (or not active) in dbo.CustomerMachine
        public bool MachineNotRegistered { get; private set; }
        // MAC addresses of this PC's physical network adapters, used to look up the registration
        public List<string> MacAddresses { get; private set; } = new();
        // customer, casino and platform of this PC, from dbo.CustomerMachine
        private MachineConfig machine;

        // this PC's own registered casino and customer, for UI tools that must never let the user pick a
        // different one (e.g. downloading hand histories: a PC only ever sees its own customer's hands)
        public EnumCasino? Casino => machine?.Casino;
        public enPlatform? Platform => machine?.Platform;
        public bool HandHistoryUploadToAzure => hhSettings?.UploadToAzure ?? false;
        public EnumCasino? HandHistoryCasino => machine?.Casino;
        public enPlatform? HandHistoryPlatform => machine?.Platform;
        // every casino this PC plays: one PDecision serves all of its poker sites
        public IReadOnlyList<MachineCasino> Casinos => machine?.Casinos;
        public string CustomerName => machine?.CustomerName;

        private SQLDataService sqlData;
        private AppSettings _appSettings;

        public Controller()
        {

        }

        /// <summary>
        /// This runs on a background thread (DecisionUI's constructor does "Task.Run(() => Initialize())"
        /// without awaiting or observing it), so an exception anywhere in InitializeCore that isn't caught
        /// here would otherwise vanish silently: no log, no FailureReason, no UI message - the app just never
        /// finishes starting, with nothing to show why.
        /// </summary>
        public bool Initialize()
        {
            try
            {
                return InitializeCore();
            }
            catch (Exception ex)
            {
                FailureReason = "unexpected error during startup: " + ex.Message;
                logger.Error("Unhandled exception in Controller.Initialize: " + ex);
                State = ServiceState.Failed;
                return false;
            }
        }

        private bool InitializeCore()
        {
            // the API starts first, so clients can poll /api/health while the service is initializing
            apiSettings = ApiSettings.Load();
            hhSettings = HandHistorySettings.Load();
            WriteHHToFile.UploadEnabled = hhSettings.UploadToAzure;
            WriteHHToFile.LocalCopyDir = string.IsNullOrWhiteSpace(hhSettings.LocalCopyDir) ? null : hhSettings.LocalCopyDir;
            logger.Info($"Hand histories: upload to Azure {hhSettings.UploadToAzure}, local copy {WriteHHToFile.LocalCopyDir ?? "off"}");

            Standby = !string.IsNullOrWhiteSpace(apiSettings.TakeoverUrls);
            api = new ApiServer(this, apiSettings);
            if (!Task.Run(() => api.StartAsync()).Result)
            {
                State = ServiceState.Failed;
                return false;
            }

            // every Azure SQL call of the service (machine registration, bot accounts, card sharing, hand-history index) uses this login
            dbSettings = DatabaseSettings.Load();
            if (!string.IsNullOrWhiteSpace(dbSettings.ConnectionString))
            {
                Singleton.ConnString = dbSettings.ConnectionString;
            }
            if (string.IsNullOrWhiteSpace(Singleton.ConnString))
            {
                FailureReason = "no database connection configured (Database:ConnectionString in appsettings.json)";
                logger.Error(FailureReason);
                State = ServiceState.Failed;
                return false;
            }
            logger.Info($"Database login: {new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(Singleton.ConnString).UserID}");

            var macs = MachineConfig.GetLocalMacAddresses();
            string onlyMac = MachineSettings.Load().MacAddress.Trim().Replace(':', '-').ToUpperInvariant();
            if (onlyMac.Length > 0)
            {
                if (!macs.Contains(onlyMac, StringComparer.OrdinalIgnoreCase))
                {
                    FailureReason = $"Machine:MacAddress {onlyMac} is not an adapter of this PC ({string.Join(", ", macs)})";
                    logger.Error(FailureReason);
                    State = ServiceState.Failed;
                    return false;
                }
                macs = new List<string> { onlyMac };
            }
            MacAddresses = macs;
            // MachineConfig.Load logs the MAC(s) it looked up and the full failure detail itself (not just a
            // short message), regardless of outcome - needed to diagnose a report of "nothing got logged".
            // Right after Windows starts the network is often not up yet, and a single failed lookup used to leave
            // the service Failed for good (alive, so the updater kept waiting on it). A lookup that could not reach
            // the database is retried with backoff while health stays "initializing"; a definite answer is final.
            string machineError;
            bool notRegistered;
            int attempt = 0;
            while (true)
            {
                machine = MachineConfig.Load(Singleton.ConnString, macs, logger, out machineError, out notRegistered, out bool transient);
                if (machine != null || !transient)
                {
                    break;
                }
                attempt++;
                int delaySeconds = Math.Min(60, 5 * attempt);
                FailureReason = $"{machineError} (attempt {attempt}, retrying in {delaySeconds} s)";
                logger.Warn($"Machine registration lookup will be retried in {delaySeconds} s (attempt {attempt})");
                Thread.Sleep(TimeSpan.FromSeconds(delaySeconds));
            }
            if (attempt > 0 && machine != null)
            {
                FailureReason = null;
                logger.Info($"Machine registration lookup succeeded after {attempt} retr{(attempt == 1 ? "y" : "ies")}");
            }
            if (machine == null)
            {
                MachineNotRegistered = notRegistered;
                FailureReason = machineError;
                State = ServiceState.Failed;
                return false;
            }
            // hand histories are uploaded to and downloaded from this customer's own area
            CustomerHandHistory.MacAddress = machine.MacAddress;
            CustomerHandHistory.CustomerId = machine.CustomerId;
            // never touch (or log) the bot accounts, winners or stats of a casino this PC isn't registered for
            Singleton.RegisteredCasinos = machine.Casinos.Select(c => c.Casino).ToList();
            Singleton.RegisteredCustomerId = machine.CustomerId;

            SettingsHelpers.LoadAppSettings(
                appSettings: out _appSettings,
                settingsFileMustExist: false,
                serviceType: enServiceType.PDecision);

            sqlData = new SQLDataService(out var result);
            if (result.IsFailure)
            {
                logger.Error($"Failed to initilalize SQLDataService: {result.GenerateReport()}");
            }
            else
            {
                logger.Info($"Success initialize SQLDataService");
            }

            // The solver solutions catalogue loads in the background: waiting for it took ~30 s of every start.
            DecisionMaking.PostflopStrategy.solver.SolverGtoPlus.InitInBackground = true;

            Task.Run(async () =>
            {
                Singleton.ReadAuthenticationData();
                await StatsLoader.Initialize(rootDir: Singleton.DefaultRootDir);
            }).Wait();

            dictWriteHH = new();
            dictWriteHHAI = new();
            dictPrevHS = new();
            dictDM = new();
            dictHH = new();

            //TestUploadHH();
            //TestDownloadHH();
            // Startup self-checks (blind levels, decision maker smoke tests, SQL/hh round trips) used to run
            // here and block startup on every launch; they now live as proper tests in PDecision.Tests.
            if (apiSettings.EnableNamedPipes)
            {
                for (int i = 0; i < numPipes; i++)
                {
                    PipeReceiveSend pipeDM = new PipeReceiveSend($"{pipeNameDM}-{i}");
                    PipeReceiveSend pipeHH = new PipeReceiveSend($"{pipeNameHH}-{i}");

                    Task.Run(() => pipeDM.Run(ProcessDM));
                    Task.Run(() => pipeHH.Run(ProcessHH));
                }
                logger.Info($"Named pipes enabled: {numPipes} pairs");
            }
            State = ServiceState.Ready;
            if (Standby)
            {
                logger.Info($"Standby on {apiSettings.Urls}: ready to take over {apiSettings.TakeoverUrls} (POST /api/takeover)");
            }
            else
            {
                StartBackgroundWork();
            }
            return true;
        }

        // hand-history sync and settings refresh run in the PDecision that serves the live port only
        private void StartBackgroundWork()
        {
            StartHandHistorySync();
            StartSettingsRefresh();
        }

        // set from Api:TakeoverUrls before the API starts; cleared once this instance serves the live port
        public bool Standby { get; private set; }
        private readonly SemaphoreSlim takeoverLock = new(1, 1);

        /// <summary>
        /// Binds Api:TakeoverUrls as soon as the old PDecision released it (polling every 100 ms for up to 60 s),
        /// then starts the background work. The caller stops the old PDecision after sending this request, so the
        /// live port is unserved only between the old process exiting and this bind.
        /// </summary>
        public async Task<ApiResult> TakeoverAsync()
        {
            if (!await takeoverLock.WaitAsync(0))
            {
                return new ApiResult(409, ApiServer.Error("takeover already in progress"));
            }
            try
            {
                if (!Standby)
                {
                    return new ApiResult(409, ApiServer.Error("not in standby"));
                }
                if (State != ServiceState.Ready)
                {
                    return new ApiResult(503, ApiServer.Error("service is " + State.ToString().ToLowerInvariant()));
                }
                var live = new ApiSettings
                {
                    Urls = apiSettings.TakeoverUrls,
                    ApiKey = apiSettings.ApiKey,
                    EnableNamedPipes = apiSettings.EnableNamedPipes,
                    HumanLikeDelays = apiSettings.HumanLikeDelays,
                };
                var server = new ApiServer(this, live);
                var watch = Stopwatch.StartNew();
                bool bound = false;
                while (watch.Elapsed < TimeSpan.FromSeconds(60))
                {
                    if (await server.StartAsync(quiet: true))
                    {
                        bound = true;
                        break;
                    }
                    await Task.Delay(100);
                }
                if (!bound)
                {
                    logger.Error($"Takeover failed: {live.Urls} still in use after 60 s");
                    return new ApiResult(504, ApiServer.Error($"{live.Urls} still in use after 60 s"));
                }
                logger.Info($"Took over {live.Urls} after {watch.ElapsedMilliseconds} ms");
                var staging = api;
                api = server;
                Standby = false;
                StartBackgroundWork();
                // the staging port answers this request first, then closes
                _ = Task.Run(async () => { await Task.Delay(2000); await staging.StopAsync(); });
                return new ApiResult(200, System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["status"] = "ready", ["urls"] = live.Urls, ["waitedMs"] = watch.ElapsedMilliseconds }));
            }
            finally
            {
                takeoverLock.Release();
            }
        }

        // The old service registration (ServiceInitialising, Heartbeat, ServiceClosing) is gone: its database is
        // not reachable from the PCs, nothing reads it, and each call waited out a 15 s connection timeout at
        // startup and at shutdown.
        public async Task Shutdown()
        {
            if (api != null)
            {
                await api.StopAsync();
            }
        }

        private void TestTCPBTNSeatEmpty()
        {
            var md = new DecisionMaker("horsepower", "horsepower", 6, EnumCasino.Poker1234Evenbet, 20, 10, EnumGameType.PLO5, 0, 0);
            HandState hs = new(6);
            hs.Bets = new List<double>() { 10, 20, 40, 40, 0, 0 };
            hs.EmptySeats = new List<bool>() { false, false, false, false, true, true };
            hs.InGame = new List<bool>() { true, true, true, true, false, false };
            hs.Stacks = new List<double>() { 2000, 2000, 2000, 2000, 0, 0 };
            hs.OmahaCards = new List<string>() { "7c", "ad", "8d", "ks", "3d" };
            hs.Names = new List<string>() { "horsepower", "Communiquee", "Luckbox", "Efiolent", "", "" };
            hs.OriginalNames = new List<string>(hs.Names);
            hs.Pot = 0;
            hs.HandID = new Random().Next(1000000, 9999999).ToString();
            hs.DealerID = 4;
            hs.IsNewHand = true;
            var dec = md.MakeDecision(hs, true, null);
            hs.Bets[0] += dec.BetSize;
            hs.Stacks[0] -= dec.BetSize;
            hs.IsNewHand = false;
            hs.Bets[0] += dec.BetSize;
            hs.Stacks[0] -= dec.BetSize;
            hs.Bets[1] = 100;
            hs.Bets[2] = 100;
            hs.Stacks[1] -= 100;
            hs.Stacks[2] -= 100;
            dec = md.MakeDecision(hs, true, null);
            hs.Stacks[0] -= dec.BetSize;
            hs.Stacks[1] -= dec.BetSize - hs.Bets[1];
            hs.Stacks[2] -= dec.BetSize - hs.Bets[2];
            hs.Bets[0] = 0;
            hs.Bets[1] = 0;
            hs.Bets[2] = 710;
            hs.Stacks[2] = 0;
            hs.Pot = 900;
            hs.Cards[2] = "qc";
            hs.Cards[3] = "tc";
            hs.Cards[4] = "2s";
            dec = md.MakeDecision(hs, true, null);
        }

        private void TestTCPSBSeatEmpty()
        {
            var md = new DecisionMaker("myHero", "myHero", 6, EnumCasino.Poker1234Evenbet, 10, 5, EnumGameType.PLO4, 0, 0);
            HandState hs = new(6);
            hs.Bets = new List<double>() { 0, 0, 10, 0, 0, 0 };
            hs.EmptySeats = new List<bool>() { false, true, false, false, true, true };
            hs.InGame = new List<bool>() { true, false, true, true, false, false };
            hs.Stacks = new List<double>() { 1000, 0, 1000, 1000, 0, 0 };
            hs.OmahaCards = new List<string>() { "ks", "kd", "ad", "ah" };
            hs.Names = new List<string>() { "myHero", "", "myplayer1", "myplayer2", "", "" };
            hs.OriginalNames = new List<string>(hs.Names);
            hs.Pot = 0;
            hs.HandID = new Random().Next(1000000, 9999999).ToString();
            hs.DealerID = 0;
            hs.IsNewHand = true;
            hs.InGame[3] = false;
            var dec = md.MakeDecision(hs, true, null);
            hs.Bets[0] += dec.BetSize;
            hs.Stacks[0] -= dec.BetSize;
            hs.IsNewHand = false;
            hs.Bets[0] += dec.BetSize;
            hs.Stacks[0] -= dec.BetSize;
            hs.Bets[1] = 100;
            hs.Bets[2] = 100;
            hs.Stacks[1] -= 100;
            hs.Stacks[2] -= 100;
            dec = md.MakeDecision(hs, true, null);
            hs.Stacks[0] -= dec.BetSize;
            hs.Stacks[1] -= dec.BetSize - hs.Bets[1];
            hs.Stacks[2] -= dec.BetSize - hs.Bets[2];
            hs.Bets[0] = 0;
            hs.Bets[1] = 0;
            hs.Bets[2] = 710;
            hs.Stacks[2] = 0;
            hs.Pot = 900;
            hs.Cards[2] = "qc";
            hs.Cards[3] = "tc";
            hs.Cards[4] = "2s";
            dec = md.MakeDecision(hs, true, null);
        }

        private bool TestDownloadHH()
        {
            try
            {
                return HHInserter.DownloadHH(DateTime.UtcNow.AddHours(-10));
            }
            catch (Exception ex)
            {
                logger.Error($"Failed TestDownloadHH{ex.ToString()}");
                return false;
            }
        }

        private bool TestUploadHH()
        {
            WriteHHToFile writeHH = new(string.Empty, string.Empty, EnumGameType.PLO4, EnumCasino.Poker1234Evenbet);
            string handID = GetRandomHandID();
            string hhText = @"#Game No : 2726369
                            ***** Hand History for Game 2726369 *****
                            $0.50/$1 USD PL Omaha - Wednesday, November 13, 04:58:28 CEST 2024
                            Table BahaPLO450.1.00 (Real Money)
                            Seat 1 is the button
                            Total number of players: 4/6
                            Seat 1: mind ( $106.31 USD )
                            Seat 2: chillnozy ( $102.16 USD )
                            Seat 3: grace ( $172 USD )
                            Seat 6: brown ( $147.68 USD )
                            chillnozy posts small blind [$0.50 USD]
                            grace posts big blind [$1 USD]
                            ** Dealing down cards **
                            Dealt to grace [  Jd 7h Tc Kh ]
                            brown folds
                            mind folds
                            chillnozy raises [$2 USD]
                            grace calls [$1.5 USD]
                            ** Dealing Flop ** [ As, Ad, 2d ]
                            chillnozy bets [$1.5 USD]
                            grace folds
                            chillnozy does not show cards.
                            chillnozy wins $6.5 USD
                            Game #2726378 starts.";
            return writeHH.UploadToAzure(handID, "testhero", hhText, 2.0);
        }
        private string GetRandomHandID()
        {
            return new Random().Next(1000000, 9999999).ToString();
        }

        private EnumGameType GetEnumGameType(enGameType gameType, enTableType tableType)
        {
            // SNG/Mtt are only supported for NLH; PLO tables are always cash
            if (gameType == enGameType.NLH && (tableType == enTableType.SNG || tableType == enTableType.MTT))
            {
                return EnumGameType.Tournament;
            }

            switch (gameType)
            {
                case enGameType.NLH:
                    return EnumGameType.NLH;
                case enGameType.PLO4:
                    return EnumGameType.PLO4;
                case enGameType.PLO5:
                    return EnumGameType.PLO5;
                case enGameType.PLO6:
                    return EnumGameType.PLO5;
                case enGameType.Unknown:
                    return EnumGameType.NLH;
                default:
                    return EnumGameType.NLH;
            }
        }

        private void SaveHand(HHBuilder hhBuilder, double ante, double straddle, HandState hs, WriteHHToFile writeHH, WriteHHToFile writeHHAI)
        {
            hhBuilder.ReplaceNames();
            hhBuilder.ValidateActions();

            var hhText = writeHH.GetHHText(hhBuilder.DBHand, hhBuilder.bbSize, hhBuilder.sbSize, ante, straddle);
            writeHH.Write(hhBuilder.DBHand, hhBuilder.bbSize, hhBuilder.sbSize, ante, straddle);

            string handKey = TCPCom.GetHandKey(hhBuilder.DBHand.handId, hhBuilder.hero);
            if (TCPCom.IsHandPlayedByAI(hhBuilder.DBHand.handId, hhBuilder.hero))
            {
                logger.Info($"writting AI hh and sending to AI: {handKey}");

                var multiplierBlinds = HHBuilder.GetMultiplierBlindsAI(hhBuilder.bbSize);
                HHBuilder.NormalizeBlinds(multiplierBlinds, hhBuilder.DBHand);
                var hhTextAI = writeHHAI.GetHHText(hhBuilder.DBHand, hhBuilder.bbSize * multiplierBlinds, hhBuilder.sbSize * multiplierBlinds, hs.Ante * multiplierBlinds, hs.Straddle * multiplierBlinds);
                if (hhTextAI == "")
                {
                    logger.Error($"hhText is empty for AI {handKey}");
                }
                else
                {
                    HHBuilder.WriteHHToAIDir(hhBuilder.DBHand.handId, hhBuilder.hero, hhTextAI);

                    bool success = Task.Run(async () =>
                    {
                        if (dictDM.TryGetValue(GetTableKey(hs), out var decisionMaker))
                        {
                            await decisionMaker.SendHH(hhTextAI);
                        }
                        else
                        {
                            logger.Error($"Failed to find decisionMaker for:{hs.HandID}, {hs.TableID}, {hs.OriginalNames[0]}");
                        }
                    }).Wait(10 * 1000);

                    if (success)
                    {
                        logger.Info("Sent hh for AI: " + handKey);
                    }
                    else
                    {
                        logger.Error("Failed to send hh for AI: " + handKey);
                    }
                }
            }
        }


        /// <summary>
        /// Checks a frame on its own and against the previous accepted frame of the same table and bot.
        /// Returns null if it can be used, otherwise the reason it is skipped.
        /// </summary>
        private string ValidateHS(HandState hs, HandState prevHS, HandState handStart)
        {
            // due to animation cards might not be initialized happens only for generating hand histories
            if (hs.GameType == enGameType.PLO4 || hs.GameType == enGameType.PLO5)
            {
                if (hs.OmahaCards == null || hs.OmahaCards.Count(_ => _ == string.Empty) > 0)
                {
                    return "hero's Omaha hole cards are missing";
                }
            }
            else
            {
                if (hs.Cards[0] == string.Empty || hs.Cards[1] == string.Empty)
                {
                    return "hero's hole cards are missing";
                }
            }

            // due to animation of dealing flop cards, some flop cards might be dealt while other not yet
            string flopCards = hs.Cards[2] + hs.Cards[3] + hs.Cards[4];
            if (flopCards.Length > 0 && flopCards.Length != 6)
            {
                return "flop is only partly dealt";
            }

            HandState.RoundValues(hs);
            if (!HandState.Validate(hs, hs.BBSize, hs.SBSize, log: false))
            {
                return "frame is inconsistent on its own (HandState.Validate)";
            }
            else if (prevHS != null && !HandState.Validate(prevHS, hs, hs.SBSize, log: false))
            {
                return "frame is inconsistent with the previous frame (HandState.Validate prev/curr)";
            }

            HSValidator.AdjustEmptySeatsNewHand(hs);

            if (!HSValidator.AdjustHS(hs, hs.BBSize, hs.SBSize, handStart))
            {
                return "blinds do not match the positions (HSValidator.AdjustHS)";
            }
            return null;
        }

        // casino and platform come from this PC's registration (dbo.MachineCasino), never from the client: the
        // request's platform only picks one of the PC's casinos (ParseHandState rejects any other)
        private EnumCasino GetEnumCasino(enPlatform platform)
        {
            return (machine.CasinoOf(platform) ?? machine.Casinos[0]).Casino;
        }

        /// <summary>
        /// Downloads the hands of this customer's other machines right away (catch-up of a new PC) and then every few minutes.
        /// </summary>
        private void StartHandHistorySync()
        {
            int minutes = hhSettings.DownloadIntervalMinutes;
            if (minutes <= 0)
            {
                logger.Info("Hand history download from the customer's other machines is off");
                return;
            }

            Task.Run(async () =>
            {
                while (true)
                {
                    foreach (var casino in machine.Casinos)
                    {
                        await HandHistorySync.DownloadNewHandsAsync(casino.Casino);
                    }
                    await Task.Delay(TimeSpan.FromMinutes(minutes));
                }
            });
            logger.Info($"Hand history download from the customer's other machines every {minutes} min");
        }

        /// <summary>
        /// Re-reads casino settings (UseHelp, profile vs humans), bot accounts with their profiles and known winners from
        /// Azure SQL every few minutes, so changes take effect without restarting the service.
        /// </summary>
        private void StartSettingsRefresh()
        {
            int minutes = dbSettings.RefreshIntervalMinutes;
            if (minutes <= 0)
            {
                logger.Info("Settings and bot accounts are read at startup only");
                return;
            }

            Task.Run(async () =>
            {
                while (true)
                {
                    await Task.Delay(TimeSpan.FromMinutes(minutes));
                    try
                    {
                        StatsLoader.RefreshAccountsAndSettings();
                    }
                    catch (Exception ex)
                    {
                        logger.Error($"Refresh of settings and bot accounts failed: {ex}");
                    }
                }
            });
            logger.Info($"Settings and bot accounts are re-read every {minutes} min");
        }

        // per table state is kept per table and hero, so several of our bots can sit at the same table and one bot at several tables
        private static string GetTableKey(HandState hs)
        {
            return hs.TableID + "|" + hs.OriginalNames[0];
        }

        /// <summary>
        /// Parses and checks a HandState request. Returns null and sets error if it can't be used.
        /// </summary>
        private HandState ParseHandState(string jsonMsg, string caller, out string error)
        {
            error = null;
            HandState hs = null;
            if (string.IsNullOrWhiteSpace(jsonMsg))
            {
                error = "request body is empty";
            }
            else
            {
                try
                {
                    hs = HandState.Deserialize(jsonMsg);
                }
                catch (Exception ex)
                {
                    error = "request body is not a valid HandState: " + ex.Message;
                }

                if (error == null && hs == null)
                {
                    error = "request body is not a valid HandState";
                }
                else if (error == null && string.IsNullOrEmpty(hs.TableID))
                {
                    error = "TableID is empty";
                }
                else if (error == null && (hs.OriginalNames == null || hs.OriginalNames.Count == 0 || string.IsNullOrEmpty(hs.OriginalNames[0])))
                {
                    error = "OriginalNames[0] (the hero's account name) is empty";
                }
            }

            if (error != null)
            {
                logger.Error($"Received invalid hand state in {caller}: {error}");
                return null;
            }
            var registered = machine.CasinoOf(hs.Platform);
            if (registered == null)
            {
                logger.Error($"Received hand state in {caller} for platform {MachineConfig.PlatformName(hs.Platform)}, which is not one of this PC's poker sites");
                error = $"platform {MachineConfig.PlatformName(hs.Platform)} is not registered for this PC";
                return null;
            }
            hs.Platform = registered.Platform;
            TournamentBuyIns.Set(GetEnumCasino(hs.Platform), hs.TableID, TournamentBuyIns.FromRequest(jsonMsg));
            NormalizeCards(hs);
            SaveHandStateDump(hs);
            return hs;
        }

        /// <summary>
        /// Dumps every HandState this bot receives (from both AddHandState and GetDecision) as a json file under
        /// C:\PokerSuite\HandStates\{username}_{handId}\, numbered 0.json, 1.json, CONFIGURE_LOCALLY in the order received, so
        /// a reported error can be traced back to the exact sequence of frames that led to it.
        /// </summary>
        private void SaveHandStateDump(HandState hs)
        {
            try
            {
                string username = SanitizeForPath(hs.OriginalNames != null && hs.OriginalNames.Count > 0 ? hs.OriginalNames[0] : "unknown");
                string handId = SanitizeForPath(hs.HandID);
                string folder = Path.Combine(handStateDumpDir, $"{username}_{handId}");

                lock (handStateDumpLocks.GetOrAdd(GetTableKey(hs), _ => new object()))
                {
                    Directory.CreateDirectory(folder);
                    int seq = Directory.GetFiles(folder, "*.json").Length;
                    string json = JsonSerializer.Serialize(hs, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(Path.Combine(folder, $"{seq}.json"), json);
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Failed to save HandState dump for {hs.OriginalNames?[0]} {hs.HandID}: {ex}");
            }
        }

        private static string SanitizeForPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return "unknown";
            }
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(c, '_');
            }
            return value;
        }

        /// <summary>
        /// Cards in lower case ("ts", "ahkd"), the format DecisionMaking and the hand evaluators work with.
        /// A client sending "Ts" would otherwise break the hand evaluation of straights and trips.
        /// </summary>
        private static void NormalizeCards(HandState hs)
        {
            static List<string> Lower(List<string> cards) => cards?.Select(c => c?.Trim().ToLowerInvariant() ?? "").ToList();
            hs.Cards = Lower(hs.Cards) ?? hs.Cards;
            hs.OmahaCards = Lower(hs.OmahaCards) ?? hs.OmahaCards;
            hs.Showdowns = Lower(hs.Showdowns) ?? hs.Showdowns;
        }

        /// <summary>
        /// Adds a table state to the hand history of its table. 200 with accepted false means the frame was skipped as inconsistent.
        /// </summary>
        public ApiResult AddHandState(string jsonMsg)
        {
            var hs = ParseHandState(jsonMsg, nameof(AddHandState), out string error);
            if (hs == null)
            {
                return new ApiResult(400, ApiServer.Error(error));
            }


            string key = GetTableKey(hs);
            try
            {
                lock (hhLocks.GetOrAdd(key, _ => new object()))
                {
                    if (!dictHH.TryGetValue(key, out var hhBuilder))
                    {
                        var level = StrategyUtil.GetLevelForBBSize(hs.BBSize);
                        // the table name is used in the hand-history text; TableHandle is optional and often 0
                        string tableName = string.IsNullOrWhiteSpace(hs.TableName) ? hs.TableID : hs.TableName;
                        hhBuilder = new HHBuilder(level, GetEnumCasino(hs.Platform), EnumTableType.SlowTable, tableName, hs.OriginalNames[0], hs.BBSize, hs.SBSize, hs.OriginalNames.Count, GetEnumGameType(hs.GameType, hs.TableType));

                        dictHH.TryAdd(key, hhBuilder);
                        logger.Info($"Created hhBuilder for {hs.OriginalNames[0]}, seats: {hs.Bets.Count}, {hs.Platform}, {hs.GameType}, {hs.SBSize}/{hs.BBSize}");
                    }

                    // null for the first frame of a table and bot
                    dictPrevHS.TryGetValue(key, out var prevHS);

                    // mostly due to animation, e.g. bets were not set completely, so we skip frame
                    // ValidateHS adjusts the frame in place, but the next frame must be compared with what the client sent
                    HandState.RoundValues(hs);
                    HandState sentFrame = StrategyUtil.DeepCopy<HandState>(hs);
                    if (!dictHandStart.TryGetValue(key, out var handStart) || handStart.HandID != hs.HandID)
                    {
                        handStart = sentFrame;
                        dictHandStart[key] = handStart;
                    }
                    string skipReason = ValidateHS(hs, prevHS, handStart);
                    if (skipReason != null && (HSValidator.IsEndOfHandSweep(sentFrame) || HSValidator.IsStreetSweep(prevHS, sentFrame)))
                    {
                        // The client animating the bets into the pot, after the last fold or at the end of a
                        // betting round: expected every hand and of no use to the hand history, so it is
                        // skipped without being logged.
                        return new ApiResult(200, "{\"accepted\":false}");
                    }
                    if (skipReason != null && sentFrame.Winners != null && sentFrame.Winners.Any(w => w > 0))
                    {
                        // The settlement frame: the pot is already paid out, so it fails the action-frame checks.
                        // The client sends the same frame to CompleteHand, which feeds it to the builder (shown
                        // cards, the last actions) and takes its payout as authoritative. Nothing to log here.
                        return new ApiResult(200, "{\"accepted\":false}");
                    }
                    if (skipReason != null)
                    {
                        // Debug, not Info: this is the hand-history recorder (AddHandState), not the decision path
                        // (GetDecision never calls ValidateHS), and these skips are routinely caused by blind-collection
                        // animation frames rather than a real problem. Full prev/curr HandState only needed when
                        // diagnosing a specific skip, so keep it out of the default Info-level log.
                        if (logger.IsDebugEnabled)
                        {
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            string prevJson = prevHS == null ? "null" : JsonSerializer.Serialize(prevHS, options);
                            string currJson = JsonSerializer.Serialize(hs, options);
                            logger.Debug($"Skipped frame {hs.TableID} {hs.OriginalNames[0]} hand {hs.HandID}: {skipReason}\nprevHS={prevJson}\ncurrHS={currJson}");
                        }
                        return new ApiResult(200, "{\"accepted\":false}");
                    }

                    // the next frame is validated against this accepted one, as the client sent it
                    dictPrevHS[key] = sentFrame;

                    // hand histories are stored per casino: blob folder, Azure HH entry and local stats DB
                    EnumCasino casino = GetEnumCasino(hs.Platform);
                    if (!dictWriteHH.TryGetValue(key, out var writeHH))
                    {
                        writeHH = new WriteHHToFile(hhBuilder.path, hhBuilder.tableName, hhBuilder.GameType, casino);
                        dictWriteHH.TryAdd(key, writeHH);
                    }

                    if (!dictWriteHHAI.TryGetValue(key, out var writeHHAI))
                    {
                        writeHHAI = new WriteHHRussian(hhBuilder.path, hhBuilder.tableName, hhBuilder.GameType, casino);
                        dictWriteHHAI.TryAdd(key, writeHHAI);
                    }

                    if (hs.HandID != hhBuilder.DBHand.handId && hhBuilder.DBHand.handId != "")
                    {
                        SaveHand(hhBuilder, hs.Ante, hs.Straddle, hs, writeHH, writeHHAI);
                    }

                    hhBuilder.UpdateHand(hs);
                    // The UI activity feed is for decision requests/responses only; AddHandState is the
                    // hand-history recorder, a separate, much higher-volume path that shouldn't clutter it.
                    return new ApiResult(200, "{\"accepted\":true}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"{ex}");
                return new ApiResult(500, ApiServer.Error("internal error, see the service log"));
            }
        }

        /// <summary>Writes a settled hand immediately, including the last hand before a table closes.</summary>
        public ApiResult CompleteHand(string jsonMsg)
        {
            var hs = ParseHandState(jsonMsg, nameof(CompleteHand), out string error);
            if (hs == null)
            {
                return new ApiResult(400, ApiServer.Error(error));
            }
            if (hs.Winners == null || !hs.Winners.Any(w => w > 0))
            {
                return new ApiResult(400, ApiServer.Error("a completed hand must include a payout"));
            }
            string key = GetTableKey(hs);
            try
            {
                lock (hhLocks.GetOrAdd(key, _ => new object()))
                {
                    if (!dictHH.TryGetValue(key, out var builder) || builder.DBHand.handId != hs.HandID ||
                        !dictPrevHS.TryGetValue(key, out var last) || last.HandID != hs.HandID ||
                        hs.Winners.Count != builder.NumPlayers ||
                        !dictWriteHH.TryGetValue(key, out var writer) ||
                        !dictWriteHHAI.TryGetValue(key, out var writerAI))
                    {
                        return new ApiResult(200, "{\"accepted\":false}");
                    }
                    // Settlement snapshots often fail the regular action-frame validator
                    // because the pot disappears as chips are awarded, so AddHandState may have
                    // skipped this frame. It is the hand's most reliable frame: EvenBet marks the
                    // winners and shows the cards of a showdown. Feed it to the builder (the
                    // builder ignores it if it already took the same frame), then take its payout
                    // as authoritative for the final hand history.
                    builder.UpdateHand(StrategyUtil.DeepCopy<HandState>(hs));
                    builder.DBHand.winners = hs.Winners.ToList();
                    SaveHand(builder, hs.Ante, hs.Straddle, hs, writer, writerAI);
                    dictHH.TryRemove(key, out _);
                    dictPrevHS.TryRemove(key, out _);
                    dictHandStart.TryRemove(key, out _);
                    dictWriteHH.TryRemove(key, out _);
                    dictWriteHHAI.TryRemove(key, out _);
                    return new ApiResult(200, "{\"accepted\":true}");
                }
            }
            catch (Exception ex)
            {
                logger.Error($"Could not complete hand {hs.HandID}: {ex}");
                return new ApiResult(500, ApiServer.Error("internal error, see the service log"));
            }
        }

        /// <summary>
        /// Returns the serialized Decision for the hero (seat 0). The reply already includes the human-like wait.
        /// </summary>
        public ApiResult GetDecision(string jsonMsg)
        {
            var hs = ParseHandState(jsonMsg, nameof(GetDecision), out string error);
            if (hs == null)
            {
                return new ApiResult(400, ApiServer.Error(error));
            }

            string key = GetTableKey(hs);
            try
            {
                lock (dmLocks.GetOrAdd(key, _ => new object()))
                {
                    if (!dictDM.TryGetValue(key, out var dm))
                    {
                        Stopwatch sw = new Stopwatch();
                        sw.Start();
                        dm = new DecisionMaker(hs.OriginalNames[0], hs.OriginalNames[0], hs.Bets.Count, GetEnumCasino(hs.Platform),
                                              hs.BBSize, hs.SBSize, GetEnumGameType(hs.GameType, hs.TableType), hs.Straddle, hs.Ante, apiSettings.HumanLikeDelays);
                        sw.Stop();
                        dictDM.TryAdd(key, dm);
                        logger.Info($"Created DM within: {Math.Round(sw.Elapsed.TotalSeconds, 2)}s for {hs.OriginalNames[0]}, seats: {hs.Bets.Count}, {hs.Platform}, {hs.GameType}, {hs.SBSize}/{hs.BBSize}");
                    }

                    var decision = dm.MakeDecision(hs, hs.IsMyTurn, null);
                    DecisionUI.LogActivity($"Decision   bot={hs.OriginalNames[0]}  table={hs.TableID}  phase={decision.Phase}  action={decision.Action}  bet={decision.BetSize}");
                    return new ApiResult(200, Decision.Serialize(decision));
                }
            }
            catch (Exception ex)
            {
                logger.Error($"{ex}");
                return new ApiResult(500, ApiServer.Error("internal error, see the service log"));
            }
        }

        // legacy named pipe handlers: the HH pipe replies success / fail, the DM pipe the Decision or an empty line
        private string ProcessHH(string jsonMsg)
        {
            var result = AddHandState(jsonMsg);
            return result.StatusCode == 200 && result.Body.Contains("true") ? successStr : failStr;
        }

        private string ProcessDM(string jsonMsg)
        {
            var result = GetDecision(jsonMsg);
            return result.StatusCode == 200 ? result.Body : "";
        }

        private async Task<Result> InterfacesThatNeedCallingToRecordHandActivity()
        {
            Result result = new();

            try
            {
                // Hand starting

                enPlatform platform = enPlatform.Unknown;

                // The unique ID according to the poker site, as a string whether it's a number or guid or whatever, this
                // is important as it'll be used to identify the table later - if it's a table the DB doesn't know, it'll register it
                string nativeTableId = "TheTableId";

                // Not really important, just for displaying in the web app etc. if we have a human name for the table; if we don't, set to same as nativeTableId
                string tableName = nativeTableId;

                // Important, used to create the hand record in the DB and identify it later when HandEnded called
                string nativeHandId = "1234567890";

                enGameType gameType = enGameType.NLH; // Or whatever

                byte seatCount = 6;     // The number of seats at the table, whether occupied or not
                byte playerCount = 3;   // The number of player dealtn into the hand
                decimal ante = 0;       // The ante for the table, used if it's a new table and needs registering
                decimal smallBlind = 1; // The small blind for the table, used if it's a new table and needs registering
                decimal bigBlind = 2;   // The big blind for the table, used if it's a new table and needs registering

                // This is the database playerId, which you may not have - you can supply the nativePlayerId and it'll look
                // for it in the DB and if the player exists all's good - but it's less efficient than giving it the unique
                // PlayerId key, probably worth getting the PlayerId, storing it and using that each time, eg;

                string nativePlayerId = "WhateverUniqueKeyThePlatformUses";

                // This will cache the whole list of players for the specified platform, and use the cached version for each call after that
                var playerModel = sqlData.GetCachedPlayerSummary(platform, nativePlayerId: nativePlayerId);

                if (playerModel == null)
                    throw new Exception($"Unknown player {nativePlayerId} on {platform}");

                // Need to build a list of the player at the table

                PlayerModelMinimal playerModel1Example = new(
                    playerId: playerModel.PlayerId,
                    isOurBot: true, // Whether it's one of our bots or not (not if this is the specific hero we are acting as)
                    nativePlayerId: playerModel.NativePlayerId, // Not needed if playerId supplied above, but will be used to add the player if it's a new villain
                    displayName: playerModel.DisplayName,       // Not needed if playerId supplied above, but will be used to add the player if it's a new villain
                    stackSize: 12345.67m,
                    position: enTablePosition.Dealer, // Or whatever, or enTablePosition.Unknown
                    isInGame: true, // If the player gets hole cards
                    isSatOut: false); // If the player is actively sat out (i.e. not new player waiting for the blind to come round, say)

                List<PlayerModelMinimal> playerList = [playerModel1Example]; // One per player, whather in game or sat out etc.

                //result.Append(
                //    await sqlData.HandStartedAsync(
                //        platform: platform,
                //        nativeTableId: nativeTableId,
                //        tableName: tableName,
                //        nativeHandId: nativeHandId,
                //        gameType: gameType,
                //        seatCount: seatCount,
                //        playerCount: playerCount,
                //        ante: ante,
                //        smallBlind: smallBlind,
                //        bigBlind: bigBlind,
                //        playerList: playerList));

                if (result.IsSuccess)
                {
                    // For each player that makes it to the showdown

                    CardListModel holeCards = new(["Kh", "Js"]);

                    string handHistoryText = "The HH text";

                    //result.Append(
                    //    await sqlData.HandEndedAsync(
                    //        platform: platform,
                    //        nativeTableId: nativeTableId,
                    //        nativePlayerId: nativePlayerId,
                    //        holeCards: holeCards, // Or null if not known
                    //        processedHandHistoryText: handHistoryText));
                }
            }
            catch (Exception ex)
            {
                result.Exception(ex);
            }

            ProcessResult(result);

            return result;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void ProcessResult(Result result, bool clearResult = true, bool rememberFailure = true)
        {
            if (logger != null)
            {
                if (result.Items.Count != 0)
                {
                    var loggingToFile = _appSettings.Logging.LogDirectoryPath.HasContent();

                    if (result.AnythingToLog)
                        result.LogMe(logger, _appSettings, includeTimestamp: loggingToFile, includeType: loggingToFile);

                    if (clearResult)
                        result.Clear(rememberFailure);
                }
            }
        }
    }
}

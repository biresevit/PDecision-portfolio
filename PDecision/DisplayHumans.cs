using ClientApp.DB;
using DBLib;
using DecisionMaking;
using DecisionMaking.DecisionMaking;
using DecisionMaking.statistika;
using HandHistoryLib;
using NLog;
using PokerSuite.Shared.Constants;
using PokerSuite.Shared.Helpers;
using System.ComponentModel;
using System.Diagnostics;

namespace Controller.tests
{
    public partial class DisplayHumans : Form
    {
        private readonly EnumCasino casino;
        private HandHistoryS3 hhs3;
        private string hhDir;
        private Dictionary<EnumGameType, Dictionary<string, Tuple<double, int>>> dictWinLoss;
        private List<string> botUsers;

        /// <summary>
        /// casino is this PC's own registration (Controller.Casino); there is no picker, a PC only ever
        /// downloads its own customer's hands, for the casino it is registered for.
        /// </summary>
        public DisplayHumans(EnumCasino casino, string customerName)
        {
            this.casino = casino;
            InitializeComponent();
            Text = $"Hand histories - {customerName} ({casino})";
        }

        protected override void OnLoad(EventArgs e)
        {
            if (LicenseManager.UsageMode == LicenseUsageMode.Designtime)
            {
                return;
            }

            if (!DesignMode)
            {
                cbGameType.Items.Clear();
                foreach (EnumGameType gameType in Enum.GetValues(typeof(EnumGameType)))
                {
                    if (gameType != EnumGameType.Spins)
                    {
                        cbGameType.Items.Add(gameType);
                    }
                }
                hhDir = @"c:/pokersuite/hh";
                hhs3 = new(connectionString: Singleton.S3ConnString, containerName: Singleton.S3ContainerName, logger: Singleton.Instance.logger);

                var fromPicker = (DateTimePicker)dtpFrom.Control;
                var toPicker = (DateTimePicker)dtpTo.Control;
                fromPicker.Value = DateTime.Now.AddDays(-30);
                toPicker.Value = DateTime.Now;
            }

            base.OnLoad(e);
        }


        private void CalculateWinnings()
        {
            // BotAccount_ListForCasino is the source of active bot identities.
            // Refresh it for each report so newly registered accounts appear.
            botUsers = DbStrategySettings.ReadBotAccounts(casino)
                .Select(account => account.Username).ToList();
            var botNames = new HashSet<string>(botUsers, StringComparer.OrdinalIgnoreCase);
            var files = Directory.GetFiles(hhDir, "*.txt");
            var fileGt = new List<Tuple<string[], EnumGameType>>();
            foreach (var file in files)
            {
                EnumGameType gt = HandHistoryWinnings.DetectGameType(file);
                var lines = File.ReadAllLines(file);
                fileGt.Add(new(lines, gt));
            }
            // GetWinLoss's second argument is an ignore list, used elsewhere for
            // human reports. Calculate every player here, then keep only bots.
            var allWinnings = HandHistoryWinnings.GetWinLoss(fileGt, new List<string>());
            dictWinLoss = allWinnings.ToDictionary(
                game => game.Key,
                game => game.Value.Where(player => botNames.Contains(player.Key))
                    .ToDictionary(player => player.Key, player => player.Value,
                                  StringComparer.OrdinalIgnoreCase));
        }

        private int LoadData()
        {
            if (dictWinLoss == null || cbGameType.SelectedItem == null)
            {
                return 0;
            }
            grid.Rows.Clear();
            EnumGameType gameType = (EnumGameType)cbGameType.SelectedItem;
            // no downloaded hand was that game type (e.g. nothing downloaded yet, or only other game types
            // were played): an empty grid, not a KeyNotFoundException
            if (!dictWinLoss.TryGetValue(gameType, out var winLossForGameType))
            {
                return 0;
            }

            foreach (var winn in winLossForGameType)
            {
                string player = winn.Key;
                int numHands = winn.Value.Item2;
                double winnings = Math.Round(winn.Value.Item1, 2);
                grid.Rows.Add(winn.Key, numHands, winnings);
            }
            return winLossForGameType.Count;
        }

        private void btnDownloads_Click(object sender, EventArgs e)
        {
            Stopwatch sw = new();
            sw.Start();
            bool ok = DownloadHandsOfThisCustomer(out var countHH);
            sw.Stop();
            if (ok)
            {
                MessageBox.Show($"Downloaded {countHH} hands within:{sw.ElapsedMilliseconds}ms");
            }
            else
            {
                MessageBox.Show($"Failed to download hand histories");
            }
        }

        /// <summary>
        /// All of this customer's hands for its own casino (dbo.CustomerHandHistory, scoped to this PC's MAC
        /// address/customer by the stored procedures - see DecisionMaking.CustomerHandHistory). Unlike the
        /// legacy dbo.HandHistory this used to read from, there is no cross-customer data here to filter out
        /// by casino alone: HH_ListAfter can only ever return hands of the caller's own customer.
        /// Restricted to the game type selected in cbGameType, or every game type when nothing is selected
        /// there yet - matches what "show data" will filter to afterward.
        /// The actual download (DecisionMaking.CustomerHandHistory.DownloadToDirectoryAsync) has no Form
        /// dependency, so it can also be exercised by a non-UI test tool.
        /// </summary>
        public bool DownloadHandsOfThisCustomer(out int countHH)
        {
            countHH = 0;
            try
            {
                if (!CustomerHandHistory.IsConfigured)
                {
                    Singleton.Instance.logger.Error("Download hand histories: machine registration (MacAddress/CustomerId) not loaded yet");
                    return false;
                }

                UploadStatistics.DeleteContentOfDirectory(hhDir);

                // the pickers hold local time (how the user picked them); the table stores UTC
                DateTime fromUtc = ((DateTimePicker)dtpFrom.Control).Value.Date.ToUniversalTime();
                DateTime toUtc = ((DateTimePicker)dtpTo.Control).Value.Date.AddDays(1).AddTicks(-1).ToUniversalTime();
                EnumGameType? gameType = cbGameType.SelectedItem as EnumGameType?;

                int count = 0;
                Task.Run(async () => count = await CustomerHandHistory.DownloadToDirectoryAsync(casino, fromUtc, toUtc, gameType, hhDir, hhs3)).Wait();
                countHH = count;
                return true;
            }
            catch (Exception ex)
            {
                Singleton.Instance.logger.Error("ex in download hands: " + ex.ToString());
                return false;
            }
        }

        private void btnLoad_Click(object sender, EventArgs e)
        {
            try
            {
                CalculateWinnings();
            }
            catch (Exception ex)
            {
                Singleton.Instance.logger.Error("Could not calculate bot hand-history winnings: " + ex);
                MessageBox.Show("Could not load bot accounts or hand histories. See the PDecision log.");
                return;
            }
            if (botUsers.Count == 0)
            {
                MessageBox.Show($"No active bot accounts are registered for {casino} in pokerdb.");
                return;
            }
            int rows = LoadData();
            if (rows == 0)
            {
                MessageBox.Show($"No {cbGameType.SelectedItem} hands for registered bots among the downloaded hands. " +
                    "Download fetches the game type selected here (or every type, if nothing was selected " +
                    "yet when you clicked download) for the chosen date range - try downloading again with " +
                    "this game type selected, or widen the date range.");
            }
        }

        private void cbGameType_Click(object sender, EventArgs e)
        {
            LoadData();
        }
    }
}

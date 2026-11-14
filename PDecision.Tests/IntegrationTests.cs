using DBLib;
using DecisionMaking;
using DecisionMaking.DecisionMaking;
using DecisionMaking.statistika;
using DecisionMaking.TCP;
using HandStateLib;
using NUnit.Framework;
using PokerSuite.Shared.Constants;

namespace PDecision.Tests
{
    /// <summary>
    /// These used to run as blocking self-checks on every PDecision startup (Controller.Init). They need the
    /// same setup Controller.Init() did first (auth data + loaded strategy/population stats under
    /// Singleton.rootDir) and/or a live SQL connection (same as PDecision's own SQLDataService), so they are
    /// not part of the default test run - run them explicitly (e.g. "dotnet test --filter TestCategory=Integration")
    /// against an environment that has both configured.
    /// </summary>
    [Explicit("Requires the same local strategy-data/auth setup and live SQL connection as PDecision's own runtime environment")]
    [Category("Integration")]
    public class IntegrationTests
    {
        [OneTimeSetUp]
        public async Task LoadStrategyData()
        {
            Singleton.ReadAuthenticationData();
            await StatsLoader.Initialize(rootDir: Singleton.DefaultRootDir);
        }

        [Test]
        public async Task GameStateRoundTripsThroughSQL()
        {
            string handID = new Random().Next(1000000, 9999999).ToString();
            var pokersite = EnumCasino.Poker1234Evenbet;
            await DBOtherAccounts.AddNewEntryAsync(handID, "ksadqd9h", 2, pokersite);
            await DBOtherAccounts.AddNewEntryAsync(handID, "8c8s7d7c", 1, pokersite);
            await DBOtherAccounts.AddNewEntryAsync(handID, "2c2s3c4c", 3, pokersite);
            var res = await DBOtherAccounts.GetHandIDAsync(handID, pokersite);
            Assert.That(res.Count, Is.EqualTo(3));
        }

        [Test]
        public void InsertHandsWhilePlayingLocallyDoesNotThrow()
        {
            string hhPath = Singleton.rootDir + $"hhgen/{EnumCasino.Poker1234Evenbet}/hh/NLH";
            Dictionary<string, string> dict = new Dictionary<string, string>();
            Assert.DoesNotThrow(() =>
                UploadStatistics.InsertHandsWhilePlayingLocally(hhPath, ref dict, EnumCasino.Poker1234Evenbet, EnumHandLevel.NL2, 6, EnumGameType.NLH));
            Assert.DoesNotThrow(() =>
                UploadStatistics.InsertHandsWhilePlayingLocally(hhPath, ref dict, EnumCasino.Poker1234Evenbet, EnumHandLevel.NL2, 6, EnumGameType.NLH));
        }

        [Test]
        public void DecisionMakerNeverThrowsForAStraightforwardNLHHand()
        {
            var dm = new DecisionMaker("hero", "hero", 6, EnumCasino.Poker1234Evenbet,
                                       1, 0.5, EnumGameType.NLH, 0, 0);
            HandState hs = new HandState(6);
            hs.HandID = new Random().Next(1000000, 9999999).ToString();
            hs.DealerID = 0;
            hs.EmptySeats = new List<bool>() { false, false, false, true, true, true };
            hs.InGame = new List<bool>() { true, true, true, false, false, false };
            hs.Stacks = new List<double>() { 500, 200, 500, 0, 0, 0 };
            hs.Bets = new List<double>() { 0, 0.5, 1, 0, 0, 0 };
            hs.Cards[0] = "as";
            hs.Cards[1] = "ad";
            hs.OriginalNames = new List<string>() { "hero", "Peter", "Franc", "", "", "" };
            hs.Names = new List<string>(hs.OriginalNames);
            hs.IsNewHand = true;
            var d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));

            hs.IsNewHand = false;
            hs.InGame[1] = false;
            hs.Cards[2] = "qc";
            hs.Cards[3] = "tc";
            hs.Cards[4] = "2s";
            hs.Pot = d.BetSize + d.BetSize + dm.SBSize; // BB calls, SB folds
            hs.Stacks[0] -= d.BetSize;
            hs.Stacks[2] -= d.BetSize + dm.BBSize;
            hs.Bets = new List<double>() { 0, 0, 0, 0, 0, 0 };
            d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));

            hs.Cards[5] = "3h";
            hs.Pot += d.BetSize * 2;
            hs.Stacks[0] -= d.BetSize;
            hs.Stacks[2] -= d.BetSize;
            d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));

            hs.Cards[6] = "7h";
            hs.Pot += d.BetSize * 2;
            hs.Stacks[0] -= d.BetSize;
            hs.Stacks[2] -= d.BetSize;
            d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));

            hs.Bets[0] = d.BetSize;
            hs.Bets[2] = d.BetSize * 2;
            hs.Stacks[0] -= d.BetSize;
            hs.Stacks[2] -= 2 * d.BetSize;
            d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));
        }

        [Test]
        public void DecisionMakerNeverThrowsForAStraightforwardPLO4Hand()
        {
            var dm = new DecisionMaker("amberveil", "amberveil", 6, EnumCasino.Poker1234Evenbet,
                                       1, 0.5, EnumGameType.PLO4, 0, 0);
            HandState hs = new HandState(6);
            hs.DealerID = 0;
            hs.HandID = "45454252";
            hs.EmptySeats = new List<bool>() { false, false, false, true, true, false };
            hs.InGame = new List<bool>() { true, true, true, false, false, false };
            hs.Stacks = new List<double>() { 100, 100, 100, 0, 0, 100 };
            hs.Bets = new List<double>() { 0, 0.5, 1, 0, 0, 3.5 };
            hs.Cards[0] = "as";
            hs.Cards[1] = "ad";
            hs.OmahaCards = new List<string>() { "as", "ad", "kd", "ks" };
            hs.OriginalNames = new List<string>() { "amberveil", "", "Daveeed", "", "", "" };
            hs.Names = new List<string>(hs.OriginalNames);

            hs.IsNewHand = true;
            var d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));

            hs.IsNewHand = false;
            hs.InGame[1] = false;
            hs.Cards[2] = "qc";
            hs.Cards[3] = "tc";
            hs.Cards[4] = "2s";
            hs.Pot = d.BetSize + d.BetSize + dm.SBSize; // BB calls, SB folds
            hs.Stacks[0] -= d.BetSize;
            hs.Stacks[2] -= d.BetSize + dm.BBSize;
            hs.Bets = new List<double>() { 0, 0, 0, 0, 0, 0 };
            d = dm.MakeDecision(hs, true, null);
            Assert.That(d.Action, Is.Not.EqualTo(EnumDecisionType.Exception));
        }
    }
}

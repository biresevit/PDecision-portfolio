using DBLib;
using DecisionMaking.Accounts;
using DecisionMaking.DecisionMaking;
using HandStateLib;
using NUnit.Framework;
using PokerSuite.Shared.Constants;
using PokerUtil;

namespace PDecision.Tests
{
    /// <summary>
    /// Conditions of card sharing and the solver strategy against humans: minimum big blind (ring), minimum buy-in
    /// (tournaments) and the player lists in dbo.CasinoSettingsPlayer, which apply at any stake.
    /// </summary>
    [TestFixture]
    public class StrategySettingConditionsTests
    {
        private static HashSet<string> List(params string[] names) =>
            new(names.Select(StrategyUtil.ConvertName), StringComparer.OrdinalIgnoreCase);

        // hero on seat 0, then three opponents; folded marks seats that are out of the hand
        private static HandState Hand(string[] opponents, params int[] folded)
        {
            var hs = new HandState(opponents.Length + 1)
            {
                OriginalNames = new List<string> { "ourbot" }.Concat(opponents).ToList(),
                Names = new List<string> { "ourbot" }.Concat(opponents).ToList(),
                InGame = Enumerable.Range(0, opponents.Length + 1).Select(i => !folded.Contains(i)).ToList(),
            };
            return hs;
        }

        [TestCase(null, 0.02, true)]
        [TestCase(4.0, 4.0, true)]
        [TestCase(4.0, 3.9999999999, true)]
        [TestCase(4.0, 2.0, false)]
        [TestCase(2.0, 10.0, true)]
        public void MinimumBigBlindAppliesToRingGames(double? min, double bb, bool expected)
        {
            Assert.That(OtherAccounts.MeetsMinimumStake(min, 1000.0, bb, EnumGameType.NLH, null), Is.EqualTo(expected));
        }

        [TestCase(null, null, true)]
        [TestCase(null, 5.0, true)]
        [TestCase(20.0, 20.0, true)]
        [TestCase(20.0, 10.0, false)]
        [TestCase(20.0, null, false)]
        public void MinimumBuyInAppliesToTournaments(double? min, double? buyIn, bool expected)
        {
            // the big blind (chips) never matters in a tournament
            Assert.That(OtherAccounts.MeetsMinimumStake(1000.0, min, 0.5, EnumGameType.Tournament, buyIn), Is.EqualTo(expected));
        }

        [Test]
        public void ListedPlayerAppliesAtAnyStake()
        {
            var hs = Hand(new[] { "fish1", "winner" });
            hs.TableID = "listed-table";
            var listed = List("winner");
            Assert.That(OtherAccounts.AppliesToHand(4.0, 50.0, listed, EnumCasino.BluffPoker, 0.02, EnumGameType.NLH, hs), Is.True);
            Assert.That(OtherAccounts.AppliesToHand(4.0, 50.0, listed, EnumCasino.BluffPoker, 0.02, EnumGameType.Tournament, hs), Is.True);
            Assert.That(OtherAccounts.AppliesToHand(4.0, 50.0, List("other"), EnumCasino.BluffPoker, 0.02, EnumGameType.NLH, hs), Is.False);
            Assert.That(OtherAccounts.AppliesToHand(4.0, 50.0, List("other"), EnumCasino.BluffPoker, 4.0, EnumGameType.NLH, hs), Is.True);
        }

        [Test]
        public void TournamentBuyInIsKeptPerTable()
        {
            var hs = Hand(new[] { "fish1" });
            hs.TableID = "mtt-table-7";
            TournamentBuyIns.Set(EnumCasino.BluffPoker, hs.TableID, 25.0);
            Assert.That(OtherAccounts.AppliesToHand(null, 20.0, List(), EnumCasino.BluffPoker, 100, EnumGameType.Tournament, hs), Is.True);
            Assert.That(OtherAccounts.AppliesToHand(null, 30.0, List(), EnumCasino.BluffPoker, 100, EnumGameType.Tournament, hs), Is.False);
            // another site's table with the same id has no buy-in
            Assert.That(OtherAccounts.AppliesToHand(null, 20.0, List(), EnumCasino.Evenbet, 100, EnumGameType.Tournament, hs), Is.False);
            TournamentBuyIns.Set(EnumCasino.BluffPoker, hs.TableID, null);
            Assert.That(TournamentBuyIns.Get(EnumCasino.BluffPoker, hs.TableID), Is.Null);
        }

        [TestCase("{\"TableID\":\"1\",\"TournamentBuyIn\":22.5}", 22.5)]
        [TestCase("{\"TableID\":\"1\",\"TournamentBuyIn\":0}", null)]
        [TestCase("{\"TableID\":\"1\",\"TournamentBuyIn\":\"22\"}", null)]
        [TestCase("{\"TableID\":\"1\"}", null)]
        [TestCase("not json \"TournamentBuyIn\"", null)]
        public void BuyInIsReadFromTheRequest(string json, double? expected)
        {
            Assert.That(TournamentBuyIns.FromRequest(json), Is.EqualTo(expected));
        }

        [Test]
        public void RequestWithBuyInStillDeserializesAsHandState()
        {
            var hs = HandState.Deserialize("{\"TableID\":\"t1\",\"BBSize\":2,\"TournamentBuyIn\":22.5}");
            Assert.That(hs, Is.Not.Null);
            Assert.That(hs.TableID, Is.EqualTo("t1"));
        }

        [Test]
        public void ListedPlayerInTheHand_Matches_IgnoringCase()
        {
            var hs = Hand(new[] { "fish1", "Player2", "fish3" });
            Assert.That(OtherAccounts.ContainsListedPlayerInHand(List("player1", "PLAYER2"), hs), Is.True);
        }

        [Test]
        public void ListedPlayerWhoFolded_DoesNotMatch()
        {
            var hs = Hand(new[] { "fish1", "player2", "fish3" }, 2);
            Assert.That(OtherAccounts.ContainsListedPlayerInHand(List("player2"), hs), Is.False);
        }

        [Test]
        public void HeroSeatIsNeverMatched()
        {
            var hs = Hand(new[] { "fish1" });
            Assert.That(OtherAccounts.ContainsListedPlayerInHand(List("ourbot"), hs), Is.False);
        }

        [Test]
        public void LongNamesMatchLikeTheWinnerList()
        {
            // ConvertName keeps 12 characters, the same rule as dbo.BotWinner names
            var hs = Hand(new[] { "averyveryverylongname" });
            Assert.That(OtherAccounts.ContainsListedPlayerInHand(List("averyveryverylongname"), hs), Is.True);
        }

        [Test]
        public void EmptyListOrMissingHand_NeverMatches()
        {
            var hs = Hand(new[] { "player1" });
            Assert.That(OtherAccounts.ContainsListedPlayerInHand(List(), hs), Is.False);
            Assert.That(OtherAccounts.ContainsListedPlayerInHand(List("player1"), null), Is.False);
        }

        [Test]
        public void SettingsAreLoggedInWords()
        {
            var text = DecisionMaking.DbStrategySettings.Describe(new DecisionMaking.CasinoSettings
            {
                UseHelp = true, HelpMinBigBlind = 4, HelpMinBuyIn = 20, HelpPlayers = List("player1", "player2"),
            });
            Assert.That(text, Is.EqualTo("card sharing ON in ring games from big blind 4, in SNG/MTT from buy-in 20 and in every hand " +
                                         "with 2 listed players; solver strategy vs humans OFF (account profiles against every human)"));
        }
    }
}

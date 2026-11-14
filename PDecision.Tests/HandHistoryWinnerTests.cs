using System.Globalization;
using System.Text.RegularExpressions;
using DBLib;
using DecisionMaking;
using NUnit.Framework;
using PokerSuite.Shared.Constants;

namespace PDecision.Tests
{
    /// <summary>
    /// Who wins, and how much, in the hand histories PDecision writes. Every case checks that the
    /// "wins" lines pay out exactly what was put in, because the bot-winnings report and the
    /// backoffice replay both compute results from those lines.
    /// </summary>
    [TestFixture]
    public class HandHistoryWinnerTests
    {
        private const double BB = 2, SB = 1;

        // ---------- NLH ----------

        [Test]
        public void Nlh_ThreeWayShowdown_BestHandListedLastWinsWholePot()
        {
            // Button folds; SB, BB and UTG see a checked-down river. The player who acted last has the best hand.
            var h = new HandSpec(EnumGameType.NLH, "hero", "sb", "bb", "utg")
                .Stacks(200, 200, 200, 200)
                .Board("Kh", "7d", "2c", "9s", "3h")
                .Hero("Qd", "Qc")
                .Act("utg", EnumActions.Call_, 2, 2)
                .Act("hero", EnumActions.Fold)
                .Act("sb", EnumActions.Call_, 1, 2)
                .Act("bb", EnumActions.Check)
                .Street(EnumPhase.Flop).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("utg", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("utg", EnumActions.Check)
                .Street(EnumPhase.River).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("utg", EnumActions.Check)
                .Shows("sb", "AhKd").Shows("bb", "7c7s").Shows("utg", "9d9c");

            var won = h.Settle(out _);
            // 9-9 makes a set of nines, beating 7-7 (set of sevens) and A-K (pair of kings).
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["utg"] = 6m }));
        }

        [Test]
        public void Nlh_SplitPot_TieDividesPotEvenly()
        {
            var h = new HandSpec(EnumGameType.NLH, "hero", "sb", "bb")
                .Stacks(100, 100, 100)
                .Board("Ah", "Kd", "Qc", "Js", "2h")
                .Hero("Tc", "3d")
                .Act("hero", EnumActions.Raise, 6, 6)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Shows("hero", "Tc3d").Shows("bb", "Th4c");

            var won = h.Settle(out _);
            // Both play the Broadway straight. Pot = 6 + 6 + SB 1 = 13.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 6.5m, ["bb"] = 6.5m }));
        }

        [Test]
        public void Nlh_ShortStackAllInWinsMainPot_SidePotGoesToBestRemainingHand()
        {
            // short (20) is all in; hero and bb both have 100 and get it all in. short has the best hand,
            // hero the second best.
            var h = new HandSpec(EnumGameType.NLH, "hero", "sb", "bb", "short")
                .Stacks(100, 100, 100, 20)
                .Board("2c", "7d", "9h", "Js", "4c")
                .Hero("Kd", "Kc")
                .Act("short", EnumActions.Raise, 20, 20)
                .Act("hero", EnumActions.Raise, 100, 100)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 98, 100)
                .Shows("short", "AsAh").Shows("hero", "KdKc").Shows("bb", "QdQc");

            var won = h.Settle(out _);
            // Main pot: 3 x 20 + SB 1 = 61 to short. Side pot: 2 x 80 = 160 to hero.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["short"] = 61m, ["hero"] = 160m }));
        }

        [Test]
        public void Nlh_HeroCardsHiddenAtShowdown_HeroStillWinsWithBetterHand()
        {
            // The table's animation hid hero's cards; the opponent's are visible. The table awards the pot by
            // the real cards, so the history must too.
            var h = new HandSpec(EnumGameType.NLH, "hero", "sb", "bb")
                .Stacks(100, 100, 100)
                .Board("Ah", "8d", "5c", "2s", "Kc")
                .Hero("As", "Ad")
                .Act("hero", EnumActions.Raise, 6, 6)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("bb", EnumActions.Check).Act("hero", EnumActions.Bet, 8, 8).Act("bb", EnumActions.Call_, 8, 8)
                .Street(EnumPhase.Turn).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Shows("bb", "AcKd");

            var won = h.Settle(out _);
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 29m }));
        }

        [Test]
        public void Nlh_HeroMucksLosingHand_OpponentWins()
        {
            var h = new HandSpec(EnumGameType.NLH, "hero", "sb", "bb")
                .Stacks(100, 100, 100)
                .Board("Ah", "8d", "5c", "2s", "Kc")
                .Hero("Qs", "Qd")
                .Act("hero", EnumActions.Raise, 6, 6)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("bb", EnumActions.Bet, 10, 10).Act("hero", EnumActions.Call_, 10, 10)
                .Shows("bb", "AcKd");

            var won = h.Settle(out _);
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["bb"] = 33m }));
        }

        // ---------- PLO4 ----------

        [Test]
        public void Plo4_Hand163026171_StraightWinsMainAndSidePot_AndGetsUncalledBetBack()
        {
            // The reported hand, as the builder recorded it: the hero's showdown holds only two of his four cards.
            var h = new HandSpec(EnumGameType.PLO4, "jeremy", "WashingtonS", "ScottocS", "mason", "monica")
                .Stacks(193, 252.58, 103.79, 99.2, 25.16)
                .Board("4s", "6c", "Kh", "3s", "9d")
                .Hero("Ac", "As", "3h", "5c")
                .Act("mason", EnumActions.Fold)
                .Act("monica", EnumActions.Raise, 7, 7)
                .Act("jeremy", EnumActions.Raise, 24, 24)
                .Act("WashingtonS", EnumActions.Raise, 43.54, 44.54)
                .Act("ScottocS", EnumActions.Raise, 63.14, 65.14)
                .Act("monica", EnumActions.Call_, 18.16, 25.16)
                .Act("jeremy", EnumActions.Raise, 169, 193)
                .Act("WashingtonS", EnumActions.Raise, 208.04, 252.58)
                .Act("ScottocS", EnumActions.Fold)
                .Shows("jeremy", "AcAs").Shows("WashingtonS", "2h2d2c5d").Shows("monica", "7cKsQcTs");

            var won = h.Settle(out string text);
            // 2-3-4-6 straight with 2d 5d. Main pot 4 x 25.16 = 100.64, side pot 2 x 167.84 + 39.98 = 375.66,
            // plus the 59.58 jeremy could not call.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["WashingtonS"] = 535.88m }));
            Assert.That(text, Does.Not.Contain("jeremy wins"));
        }

        [Test]
        public void Plo4_MustUseExactlyTwoHoleCards()
        {
            // Four hearts on board. "flusher" has one heart, which is a flush in Hold'em but not in Omaha.
            var h = new HandSpec(EnumGameType.PLO4, "hero", "sb", "bb")
                .Stacks(100, 100, 100)
                .Board("2h", "7h", "9h", "Jh", "9c")
                .Hero("Kd", "Kc", "8s", "3d")
                .Act("hero", EnumActions.Raise, 6, 6)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Shows("hero", "KdKc8s3d").Shows("bb", "AhQsQd4c");

            var won = h.Settle(out _);
            // bb's lone Ah cannot make a flush, so bb has queens and nines; hero has kings and nines.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 13m }));
        }

        [Test]
        public void Plo4_ThreeWayAllIn_ShortStackWinsMain_SecondBestWinsSide()
        {
            var h = new HandSpec(EnumGameType.PLO4, "hero", "sb", "bb", "short")
                .Stacks(150, 150, 150, 30)
                .Board("Qs", "Js", "4d", "8c", "2h")
                .Hero("Ts", "9d", "3c", "3h")
                .Act("short", EnumActions.Raise, 30, 30)
                .Act("hero", EnumActions.Raise, 150, 150)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 148, 150)
                .Shows("short", "QhQdAcKc").Shows("hero", "Ts9d3c3h").Shows("bb", "AsKsJdJh");

            var won = h.Settle(out _);
            // short: set of queens; hero: Q-J-T-9-8 straight; bb: set of jacks.
            // The straight beats both sets, so hero wins the main pot (3 x 30 + 1) and the side pot (2 x 120).
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 331m }));
        }

        [Test]
        public void Plo4_TieSplitsPot()
        {
            var h = new HandSpec(EnumGameType.PLO4, "hero", "sb", "bb")
                .Stacks(100, 100, 100)
                .Board("Ks", "Qd", "Jc", "4h", "2d")
                .Hero("As", "Tc", "5h", "5d")
                .Act("hero", EnumActions.Raise, 6, 6)
                .Act("sb", EnumActions.Call_, 5, 6)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Shows("hero", "AsTc5h5d").Shows("sb", "AdTh6c6s").Shows("bb", "8c8d3s3h");

            var won = h.Settle(out _);
            // hero and sb both make the Broadway straight; pot 18 is split, bb gets nothing.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 9m, ["sb"] = 9m }));
        }

        // ---------- PLO5 ----------

        [Test]
        public void Plo5_BestOfThreeShownHandsWins_AndShowLinesListAllFiveCards()
        {
            var h = new HandSpec(EnumGameType.PLO5, "hero", "sb", "bb", "utg")
                .Stacks(200, 200, 200, 200)
                .Board("Th", "8h", "3c", "2d", "Kh")
                .Hero("Ah", "5h", "Qs", "Qd", "4c")
                .Act("utg", EnumActions.Raise, 6, 6)
                .Act("hero", EnumActions.Call_, 6, 6)
                .Act("sb", EnumActions.Call_, 5, 6)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("utg", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("utg", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("sb", EnumActions.Check).Act("bb", EnumActions.Check).Act("utg", EnumActions.Check).Act("hero", EnumActions.Check)
                .Shows("sb", "KsKdJc9s7c").Shows("bb", "TcTd8s4s4d").Shows("utg", "QhJh2s2c6d").Shows("hero", "Ah5hQsQd4c");

            var won = h.Settle(out string text);
            // hero: A-high heart flush (Ah 5h + Th 8h Kh) beats utg's Q-high flush and both sets.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 24m }));
            Assert.That(text, Does.Contain("utg shows [  Qh, Jh, 2s, 2c, 6d ]"));
            Assert.That(text, Does.Contain("hero shows [  Ah, 5h, Qs, Qd, 4c ]"));
        }

        [Test]
        public void Plo5_ShortStackAllInWinsMain_OtherWinsSide()
        {
            var h = new HandSpec(EnumGameType.PLO5, "hero", "sb", "bb")
                .Stacks(100, 100, 40)
                .Board("9c", "9d", "5s", "Jh", "2c")
                .Hero("Ks", "Kd", "Qc", "8h", "7h")
                .Act("hero", EnumActions.Raise, 100, 100)
                .Act("sb", EnumActions.Call_, 99, 100)
                .Act("bb", EnumActions.Call_, 38, 40)
                .Shows("hero", "KsKdQc8h7h").Shows("sb", "AsAhQd6c3d").Shows("bb", "9s5d4c4h3s");

            var won = h.Settle(out _);
            // bb: nines full of fives wins the main pot 3 x 40 = 120; sb's aces beat hero's kings for the side pot 2 x 60.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["bb"] = 120m, ["sb"] = 120m }));
        }

        [Test]
        public void Plo6_WrittenAsPlo5_UsesAllSixHoleCards()
        {
            var h = new HandSpec(EnumGameType.PLO5, "hero", "sb", "bb")
                .Stacks(100, 100, 100)
                .Board("Ac", "7c", "2c", "Ks", "4d")
                .Hero("Qc", "Jc", "8d", "8s", "6h", "5h")
                .Act("hero", EnumActions.Raise, 6, 6)
                .Act("sb", EnumActions.Fold)
                .Act("bb", EnumActions.Call_, 4, 6)
                .Street(EnumPhase.Flop).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.Turn).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Street(EnumPhase.River).Act("bb", EnumActions.Check).Act("hero", EnumActions.Check)
                .Shows("hero", "QcJc8d8s6h5h").Shows("bb", "AdAsKdKhTc9c");

            var won = h.Settle(out _);
            // Both make an ace-high club flush from the three board clubs: hero's A-Q-J-7-2 (Qc Jc) beats
            // bb's A-T-9-7-2 (Tc 9c). Only a six-card hand finds hero's Qc Jc pair.
            Assert.That(won, Is.EquivalentTo(new Dictionary<string, decimal> { ["hero"] = 13m }));
        }

        [Test]
        public void Plo4_Hand163026171_RussianFormat_PaysWinnerTotalWithProfit()
        {
            var h = new HandSpec(EnumGameType.PLO4, "jeremy", "WashingtonS", "ScottocS", "mason", "monica")
                .Stacks(193, 252.58, 103.79, 99.2, 25.16)
                .Board("4s", "6c", "Kh", "3s", "9d")
                .Hero("Ac", "As", "3h", "5c")
                .Act("mason", EnumActions.Fold)
                .Act("monica", EnumActions.Raise, 7, 7)
                .Act("jeremy", EnumActions.Raise, 24, 24)
                .Act("WashingtonS", EnumActions.Raise, 43.54, 44.54)
                .Act("ScottocS", EnumActions.Raise, 63.14, 65.14)
                .Act("monica", EnumActions.Call_, 18.16, 25.16)
                .Act("jeremy", EnumActions.Raise, 169, 193)
                .Act("WashingtonS", EnumActions.Raise, 208.04, 252.58)
                .Act("ScottocS", EnumActions.Fold)
                .Shows("WashingtonS", "2h2d2c5d").Shows("monica", "7cKsQcTs");

            string text = h.Text(russian: true);
            Assert.That(text, Does.Contain("WashingtonS collected H535.88 from pot"));
            Assert.That(text, Does.Contain("WashingtonS got profit H283.30"));
            Assert.That(text, Does.Not.Contain("jeremy collected"));
        }

        // ---------- hand ranking and pot splitting ----------

        [TestCase("Ah 5c", "9c Kd", "2c 3d 4h 9s 9d", 1, TestName = "Wheel straight beats three of a kind")]
        [TestCase("5c 6d", "Ah 5s", "2c 3d 4h 9s 9d", 1, TestName = "Six-high straight beats the wheel")]
        [TestCase("Ah 3d", "Qh Jd", "Kc Kd 7h 4s 2c", 1, TestName = "Kicker decides a shared pair")]
        [TestCase("8h 3h", "Ts Jd", "9h 7h 2h Qc 8c", 1, TestName = "Flush beats straight")]
        [TestCase("9c 9d", "Ah Kh", "9h 7h 2h 7c Qh", 1, TestName = "Full house beats flush")]
        [TestCase("Ah 2h", "Kd Kc", "3h 4h 5h Ks Kh", 1, TestName = "Steel wheel straight flush beats four of a kind")]
        [TestCase("2d 3d", "4h 5s", "Ac Kc Qc Jc Tc", 0, TestName = "Royal flush on the board ties")]
        [TestCase("Ah 2d", "Qh Qd", "Kh Kc Kd Ks 3c", 1, TestName = "Kicker decides four of a kind")]
        [TestCase("Kd 4c", "Ks 2d", "Kh Kc 7h 7s 3c", 0, TestName = "Same full house ties")]
        public void Holdem_HandOrder(string a, string b, string board, int expected)
        {
            var boardCards = ShowdownEvaluator.ParseCards(board);
            long va = ShowdownEvaluator.Evaluate(ShowdownEvaluator.ParseCards(a), boardCards, omaha: false);
            long vb = ShowdownEvaluator.Evaluate(ShowdownEvaluator.ParseCards(b), boardCards, omaha: false);
            Assert.That(Math.Sign(va.CompareTo(vb)), Is.EqualTo(expected));
        }

        [Test]
        public void Omaha_UsesExactlyTwoHoleCardsAndThreeBoardCards()
        {
            var board = ShowdownEvaluator.ParseCards("Ah Kh Qh Jh 2c");
            // One heart in hand makes no flush; Th 9s make the K-high straight with three board cards.
            long oneHeart = ShowdownEvaluator.Evaluate(ShowdownEvaluator.ParseCards("Th 9s 3d 3c"), board, omaha: true);
            long tripTwos = ShowdownEvaluator.Evaluate(ShowdownEvaluator.ParseCards("2d 2s 3d 4c"), board, omaha: true);
            long twoHearts = ShowdownEvaluator.Evaluate(ShowdownEvaluator.ParseCards("Th 9h 3d 3c"), board, omaha: true);
            Assert.That(oneHeart, Is.GreaterThan(tripTwos), "the straight beats three twos");
            Assert.That(twoHearts, Is.GreaterThan(oneHeart), "two hearts in hand make a flush");
        }

        [TestCase("XxXx")]
        [TestCase("AcK")]
        [TestCase("Zc4d")]
        public void UnknownCards_AreNotAHand(string cards)
        {
            var board = ShowdownEvaluator.ParseCards("2c 7d 9h Js 4c");
            Assert.That(ShowdownEvaluator.Evaluate(ShowdownEvaluator.ParseCards(cards), board, omaha: false), Is.EqualTo(-1));
        }

        [Test]
        public void TenWrittenAs10_IsParsed()
        {
            Assert.That(ShowdownEvaluator.ParseCards("10hJs"), Is.EqualTo(new List<(int, int)> { (10, 2), (11, 3) }));
        }

        [Test]
        public void SplitPot_OddCentGoesToFirstPlayerLeftOfButton()
        {
            var even = ShowdownSettlement.Settle(new Dictionary<string, decimal> { ["a"] = 0.03m, ["b"] = 0.02m, ["c"] = 0.02m },
                new HashSet<string> { "a", "b", "c" }, new Dictionary<string, long> { ["a"] = 5, ["b"] = 5, ["c"] = 5 }, new[] { "b", "c", "a" });
            // 0.06 shared three ways; the 0.01 that only a put in comes back to a.
            Assert.That(even!.Select(x => (x.Pot, x.Player, x.Amount)),
                Is.EqualTo(new[] { (0, "b", 0.02m), (0, "c", 0.02m), (0, "a", 0.02m), (1, "a", 0.01m) }));

            var odd = ShowdownSettlement.Settle(new Dictionary<string, decimal> { ["a"] = 0.02m, ["b"] = 0.02m, ["c"] = 0.01m },
                new HashSet<string> { "a", "b" }, new Dictionary<string, long> { ["a"] = 5, ["b"] = 5 }, new[] { "b", "a", "c" });
            // 0.05 shared two ways: the first player left of the button gets the odd cent.
            Assert.That(odd!.Select(x => (x.Player, x.Amount)), Is.EqualTo(new[] { ("b", 0.03m), ("a", 0.02m) }));
        }

        [Test]
        public void Pot_WithSeveralOwnersAndNoKnownCards_IsNotSettled()
        {
            var awards = ShowdownSettlement.Settle(new Dictionary<string, decimal> { ["a"] = 10, ["b"] = 10 },
                new HashSet<string> { "a", "b" }, new Dictionary<string, long>(), new[] { "a", "b" });
            Assert.That(awards, Is.Null);
        }

        // ---------- helpers ----------

        private static readonly Regex Win = new(@"^(?<player>.+?) wins \$(?<amount>\d+(?:\.\d+)?) USD", RegexOptions.Multiline);
        private static readonly Regex Put = new(@"^(?<player>.+?) (?:posts (?:small blind|big blind)|calls|bets|raises) \[\$(?<amount>\d+(?:\.\d+)?) USD\]", RegexOptions.Multiline);

        private sealed class HandSpec
        {
            private readonly EnumGameType game;
            private readonly string[] names;
            private double[] stacks = Array.Empty<double>();
            private string[] board = Array.Empty<string>();
            private string[] hole = Array.Empty<string>();
            private readonly List<DBLib.Action> actions = new();
            private readonly List<(string player, string cards)> shows = new();
            private EnumPhase street = EnumPhase.Preflop;

            // names are in seat order starting with the button: button, small blind, big blind, CONFIGURE_LOCALLY
            public HandSpec(EnumGameType game, params string[] names) { this.game = game; this.names = names; }
            public HandSpec Stacks(params double[] s) { stacks = s; return this; }
            public HandSpec Board(params string[] b) { board = b; return this; }
            public HandSpec Hero(params string[] c) { hole = c; return this; }
            public HandSpec Street(EnumPhase p) { street = p; return this; }
            public HandSpec Shows(string player, string cards) { shows.Add((player, cards)); return this; }
            public HandSpec Act(string player, EnumActions a, double actual = 0, double total = 0)
            {
                actions.Add(new DBLib.Action { player = player, action_name = a, actualBet = actual, totalBet = total, phase = street });
                return this;
            }

            public Dictionary<string, decimal> Settle(out string text)
            {
                text = Text(russian: false);
                var won = new Dictionary<string, decimal>();
                foreach (Match m in Win.Matches(text))
                {
                    string p = m.Groups["player"].Value.Trim();
                    won[p] = won.GetValueOrDefault(p) + decimal.Parse(m.Groups["amount"].Value, CultureInfo.InvariantCulture);
                }
                decimal put = Put.Matches(text).Sum(m => decimal.Parse(m.Groups["amount"].Value, CultureInfo.InvariantCulture));
                Assert.That(won.Values.Sum(), Is.EqualTo(put), "the wins lines must pay out exactly what was put in");
                return won;
            }

            public string Text(bool russian)
            {
                int n = names.Length;
                var hand = new Hand(n);
                hand.handId = "1";
                hand.hero = names[0];
                hand.dealer = 0;
                hand.players = names.ToList();
                hand.emptySeats = Enumerable.Repeat(false, n).ToList();
                hand.startStacks = stacks.ToList();
                hand.actions = actions;
                // The builder keeps the hero's first two hole cards in cards[0..1] and the board in cards[2..6];
                // Omaha hands also carry every hole card in OmahaCards.
                hand.cards = new List<string> { hole[0], hole[1] }.Concat(board).ToList();
                if (game != EnumGameType.NLH)
                {
                    hand.OmahaCards = hole.ToList();
                }
                hand.summary = shows.Select(s => new Showdown { player = s.player, hand = s.cards, won = 0 }).ToList();

                var writer = russian
                    ? new WriteHHRussian(string.Empty, "Test", game, EnumCasino.Poker1234Evenbet)
                    : new WriteHHToFile(string.Empty, "Test", game, EnumCasino.Poker1234Evenbet);
                string text = writer.GetHHText(hand, BB, SB, 0, 0);
                TestContext.Out.WriteLine(text);
                return text;
            }
        }
    }
}

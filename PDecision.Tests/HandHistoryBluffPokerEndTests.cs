using DBLib;
using DecisionMaking;
using DecisionMaking.DecisionMaking;
using HandStateLib;
using NUnit.Framework;

namespace PDecision.Tests
{
    /// <summary>
    /// BluffPoker ends an uncontested hand with an idle state: no board and no player dealt in. PokerController
    /// completes that last frame with the hand's board and the winner still in the hand, so the fold to the
    /// river bet is written on the river. Hand 1cc78d1e-…-9 (TorontoIII, 2026-11-12) showed "Cyly91 folds"
    /// preflop, right after Cyly91 called, and nothing after lore's river bet.
    /// </summary>
    [TestFixture]
    public class HandHistoryBluffPokerEndTests
    {
        private const string HandId = "1cc78d1e-test-9";
        // hero-relative seats: Matty808 (hero, SB), 3 empty, Cyly91 (BB), lore (button)
        private static readonly string[] Names = { "Matty808", "", "", "", "Cyly91", "lore" };
        private static readonly string[] Flop = { "5h", "Qd", "Kc" };

        private static HandState Frame(bool[] inGame, double[] bets, double[] stacks, double pot, params string[] board)
        {
            var hs = new HandState(6)
            {
                HandID = HandId,
                DealerID = 5,
                BBSize = 2,
                SBSize = 1,
                InGame = new List<bool>(inGame),
                Bets = new List<double>(bets),
                Stacks = new List<double>(stacks),
                EmptySeats = new List<bool> { false, true, true, true, false, false },
                OriginalNames = new List<string>(Names),
                Pot = pot,
            };
            hs.Names = new List<string>(Names);
            hs.Cards[0] = "Ac";
            hs.Cards[1] = "3c";
            for (int i = 0; i < board.Length; i++)
            {
                hs.Cards[2 + i] = board[i];
            }
            return hs;
        }

        private static bool[] In(bool hero, bool cyly, bool lore) => new[] { hero, false, false, false, cyly, lore };
        private static double[] Bets(double hero, double cyly, double lore) => new[] { hero, 0, 0, 0, cyly, lore };
        private static double[] Stacks(double hero, double cyly, double lore) => new[] { hero, 0, 0, 0, cyly, lore };

        private static List<HandState> RiverBetFrames()
        {
            string[] turn = Flop.Append("Ad").ToArray(), river = turn.Append("2s").ToArray();
            var frames = new List<HandState>
            {
                Frame(In(true, true, true), Bets(1, 2, 0), Stacks(397, 400, 190.9), 0),
                Frame(In(true, true, true), Bets(1, 2, 5), Stacks(397, 400, 185.9), 0),          // lore raises to 5
                Frame(In(false, true, true), Bets(1, 2, 5), Stacks(397, 400, 185.9), 0),         // Matty808 folds
                Frame(In(false, true, true), Bets(1, 5, 5), Stacks(397, 397, 185.9), 0),         // Cyly91 calls
                Frame(In(false, true, true), Bets(0, 0, 0), Stacks(397, 397, 185.9), 11, Flop),  // flop
                Frame(In(false, true, true), Bets(0, 0, 4), Stacks(397, 397, 181.9), 11, Flop),  // lore bets 4
                Frame(In(false, true, true), Bets(0, 16, 4), Stacks(397, 381, 181.9), 11, Flop), // Cyly91 raises to 16
                Frame(In(false, true, true), Bets(0, 16, 55), Stacks(397, 381, 130.9), 11, Flop),// lore raises to 55
                Frame(In(false, true, true), Bets(0, 55, 55), Stacks(397, 342, 130.9), 11, Flop),// Cyly91 calls
                Frame(In(false, true, true), Bets(0, 0, 0), Stacks(397, 342, 130.9), 121, turn), // turn, both check
                Frame(In(false, true, true), Bets(0, 0, 0), Stacks(397, 342, 130.9), 121, river),// river
                Frame(In(false, true, true), Bets(0, 0, 126.9), Stacks(397, 342, 4), 121, river),// lore bets 126.9
            };
            foreach (var f in frames)
            {
                f.IsNewHand = f == frames[0];
            }
            return frames;
        }

        private static string Build(List<HandState> frames, HandState final, EnumGameType game = EnumGameType.NLH, double sb = 1, double bb = 2)
        {
            var builder = new HHBuilder(EnumHandLevel.NL200, EnumCasino.BluffPoker, EnumTableType.SlowTable, "TorontoIII", "Matty808", bb, sb, 6, game);
            foreach (var f in frames.Append(final))
            {
                f.BBSize = bb;   // the builder takes the hand's blinds from its frames
                f.SBSize = sb;
            }
            foreach (var f in frames)
            {
                builder.UpdateHand(StrategyUtil.DeepCopy<HandState>(f));
            }
            builder.UpdateHand(StrategyUtil.DeepCopy<HandState>(final));
            builder.DBHand.winners = final.Winners.ToList();
            builder.ReplaceNames();
            builder.ValidateActions();
            var writer = new WriteHHToFile(builder.path, builder.tableName, game, EnumCasino.BluffPoker);
            return writer.GetHHText(builder.DBHand, builder.bbSize, builder.sbSize, 0, 0);
        }

        private static List<string> Actions(string text, string street) =>
            text.Split('\n').Select(l => l.Trim()).SkipWhile(l => !l.StartsWith(street)).Skip(1)
                .TakeWhile(l => !l.StartsWith("**") && !l.Contains(" wins ") && !l.Contains("show")).ToList();

        [Test]
        public void IdleEndOfHand_AsBluffPokerSendsIt_PutsTheRiverFoldPreflop()
        {
            // No board, nobody dealt in, the payout in the stacks: what the builder got before the fix.
            var idle = Frame(In(false, false, false), Bets(0, 0, 0), Stacks(397, 342, 251.9), 0);
            idle.Winners[5] = 247.9;
            idle.IsWinners[5] = true;
            string text = Build(RiverBetFrames(), idle);
            Assert.That(Actions(text, "** Dealing down cards").Count(l => l == "Cyly91 folds"), Is.EqualTo(1), text);
        }

        [TestCase(EnumGameType.NLH, 2.5, 5.0, "$2.50/$5.00 USD fastforward NL Texas Hold'em")]
        [TestCase(EnumGameType.NLH, 2.0, 5.0, "$2.00/$5.00 USD fastforward NL Texas Hold'em")]
        [TestCase(EnumGameType.PLO5, 1.0, 2.0, "$1.00/$2.00 USD PL Omaha 5 Card")]
        [TestCase(EnumGameType.PLO4, 1.0, 2.0, "$1.00/$2.00 USD PL Omaha")]
        public void Header_ShowsTheTablesSmallBlindAndGame(EnumGameType game, double sb, double bb, string expected)
        {
            string[] river = { "5h", "Qd", "Kc", "Ad", "2s" };
            var final = Frame(In(false, false, true), Bets(0, 0, 0), Stacks(397, 342, 251.9), 0, river);
            final.Winners[5] = 247.9;
            final.IsWinners[5] = true;
            string text = Build(RiverBetFrames(), final, game, sb, bb);
            Assert.That(text, Does.Contain(expected + " - "), text);
        }

        [Test]
        public void CompletedEndOfHand_WritesTheFoldAfterTheRiverBet()
        {
            // PokerController's completed frame: the river board and the winner kept in the hand.
            string[] river = { "5h", "Qd", "Kc", "Ad", "2s" };
            var final = Frame(In(false, false, true), Bets(0, 0, 0), Stacks(397, 342, 251.9), 0, river);
            final.Winners[5] = 247.9;
            final.IsWinners[5] = true;
            string text = Build(RiverBetFrames(), final);
            Assert.That(Actions(text, "** Dealing down cards"), Does.Not.Contain("Cyly91 folds"), text);
            Assert.That(Actions(text, "** Dealing River"), Is.EqualTo(new[] { "Cyly91 checks", "lore bets [$126.9 USD]", "Cyly91 folds" }), text);
        }
    }
}

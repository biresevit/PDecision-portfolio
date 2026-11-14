using DecisionMaking;
using HandStateLib;
using NUnit.Framework;

namespace PDecision.Tests
{
    /// <summary>
    /// The hand-history recorder skips the frame where the client sweeps the bets into the pot after the
    /// last fold. It comes once per uncontested hand, so it is skipped without being logged.
    /// </summary>
    [TestFixture]
    public class HandHistoryFrameTests
    {
        private static HandState Frame(bool[] inGame, double[] bets, double pot, params string[] board)
        {
            var hs = new HandState(6)
            {
                HandID = "163041396",
                DealerID = 4,
                InGame = new List<bool>(inGame),
                Bets = new List<double>(bets),
                Stacks = new List<double> { 198, 0, 0, 9.69, 208.66, 170.27 },
                EmptySeats = new List<bool> { false, true, true, false, false, false },
                OriginalNames = new List<string> { "john", "", "", "Kristall", "andrew", "Bowen" },
                Pot = pot,
            };
            hs.Names = new List<string>(hs.OriginalNames);
            hs.Cards[0] = "8h";
            hs.Cards[1] = "7h";
            for (int i = 0; i < board.Length; i++)
            {
                hs.Cards[2 + i] = board[i];
            }
            return hs;
        }

        [Test]
        public void SweepAfterThePreflopFold_IsRecognised()
        {
            // The frame from the PDecision log: everyone folded to Bowen, bets already in the 20.02 pot.
            var hs = Frame(new[] { false, false, false, false, false, true }, new double[6], 20.02);
            Assert.That(HSValidator.IsEndOfHandSweep(hs), Is.True);
        }

        [Test]
        public void SweepAfterAPostflopFold_IsRecognised()
        {
            var hs = Frame(new[] { false, false, false, false, false, true }, new double[6], 33.61, "6s", "8d", "Qh");
            Assert.That(HSValidator.IsEndOfHandSweep(hs), Is.True);
        }

        [Test]
        public void BetsCollectedAtTheEndOfAStreet_AreAStreetSweep()
        {
            // Hand 163054147 on table 128053412: the turn bets (48 and 109.99) swept into the 72.96 pot.
            var before = Frame(new[] { true, true, false, false, false, false }, new double[] { 48, 109.99, 0, 0, 0, 0 }, 72.96, "6s", "8d", "Qh", "2c");
            var swept = Frame(new[] { true, true, false, false, false, false }, new double[6], 168.96, "6s", "8d", "Qh", "2c");
            Assert.That(HSValidator.IsStreetSweep(before, swept), Is.True);
            // Preflop, blinds swept before the pot shows them (hand 163054214).
            var blinds = Frame(new[] { true, false, true, false, false, false }, new double[] { 1, 0, 2, 0, 0, 0 }, 0);
            var collected = Frame(new[] { true, false, true, false, false, false }, new double[6], 2);
            Assert.That(HSValidator.IsStreetSweep(blinds, collected), Is.True);
        }

        [Test]
        public void NextStreetWinnerOrNewHand_IsNotAStreetSweep()
        {
            var before = Frame(new[] { true, true, false, false, false, false }, new double[] { 6, 6, 0, 0, 0, 0 }, 3);
            // The flop is dealt: a new street, the frame carries information.
            var flop = Frame(new[] { true, true, false, false, false, false }, new double[6], 15, "6s", "8d", "Qh");
            Assert.That(HSValidator.IsStreetSweep(before, flop), Is.False);
            // A winner is marked: the settlement frame, used by CompleteHand.
            var settled = Frame(new[] { true, true, false, false, false, false }, new double[6], 0);
            settled.Winners[1] = 15;
            Assert.That(HSValidator.IsStreetSweep(before, settled), Is.False);
            // Another hand, or no previous frame.
            var nextHand = Frame(new[] { true, true, false, false, false, false }, new double[6], 15);
            nextHand.HandID = "163041397";
            Assert.That(HSValidator.IsStreetSweep(before, nextHand), Is.False);
            Assert.That(HSValidator.IsStreetSweep(null, flop), Is.False);
            // Bets still on the table: the hand is being played.
            var raise = Frame(new[] { true, true, false, false, false, false }, new double[] { 6, 18, 0, 0, 0, 0 }, 3);
            Assert.That(HSValidator.IsStreetSweep(before, raise), Is.False);
        }

        [Test]
        public void FramesWithAHandStillBeingPlayed_AreNotASweep()
        {
            // Two players still in, bets on the table.
            var live = Frame(new[] { true, false, false, false, false, true }, new double[] { 2, 0, 0, 0, 0, 6 }, 3);
            Assert.That(HSValidator.IsEndOfHandSweep(live), Is.False);
            // Last fold just happened but the bets are not swept yet: still useful, it carries the final bets.
            var unswept = Frame(new[] { false, false, false, false, false, true }, new double[] { 2, 0, 0, 9.01, 0, 37.64 }, 0);
            Assert.That(HSValidator.IsEndOfHandSweep(unswept), Is.False);
            // Between streets with several players in: bets swept, but the hand goes on.
            var betweenStreets = Frame(new[] { true, false, false, false, false, true }, new double[6], 12, "6s", "8d", "Qh");
            Assert.That(HSValidator.IsEndOfHandSweep(betweenStreets), Is.False);
        }
    }
}

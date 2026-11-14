using DecisionMaking;
using HandStateLib;
using NUnit.Framework;

namespace PDecision.Tests
{
    /// <summary>
    /// Big blind, everyone folded except a short all-in: nothing to call and nobody to bet against, so the
    /// hero checks (BluffPoker hand 18, 3 Nov 2026: the strategy folded it, and raised all-in with aces).
    /// </summary>
    [TestFixture]
    public class FreeCheckTests
    {
        // 8 seats, hero (seat 0) in the big blind with 200, dealer on seat 6, seat 7 is the small blind.
        private static HandState Frame(double sbBet, double sbStack, int dealer = 6, params string[] board)
        {
            var hs = new HandState(8)
            {
                HandID = "free-check-1",
                DealerID = dealer,
                BBSize = 200,
                SBSize = 100,
                InGame = new List<bool> { true, false, false, false, false, false, false, true },
                Bets = new List<double> { 200, 0, 0, 0, 0, 0, 0, sbBet },
                Stacks = new List<double> { 13930, 15950, 14795, 30210, 0, 0, 14765, sbStack },
                EmptySeats = new List<bool> { false, false, false, false, true, true, false, false },
            };
            hs.Cards[0] = "2s";
            hs.Cards[1] = "7c";
            for (int i = 0; i < board.Length; i++)
            {
                hs.Cards[2 + i] = board[i];
            }
            return hs;
        }

        [Test]
        public void SmallBlindAllInForLessThanTheBigBlind_IsAFreeCheck()
        {
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(Frame(150, 0)), Is.True);
        }

        [Test]
        public void SmallBlindAllInForExactlyTheBigBlind_IsAFreeCheck()
        {
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(Frame(200, 0)), Is.True);
        }

        [Test]
        public void SmallBlindLimpingWithChipsBehind_LeavesTheDecisionToTheStrategy()
        {
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(Frame(200, 1950)), Is.False);
        }

        [Test]
        public void SmallBlindAllInForMoreThanTheBigBlind_MustBeCalledOrFolded()
        {
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(Frame(500, 0)), Is.False);
        }

        [Test]
        public void HeroNotInTheBigBlind_IsNotAFreeCheck()
        {
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(Frame(150, 0, dealer: 0)), Is.False);
        }

        [Test]
        public void AfterTheFlop_IsNotAFreeCheck()
        {
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(Frame(150, 0, 6, "6s", "8d", "Qh")), Is.False);
        }

        [Test]
        public void EveryoneElseFolded_IsNotAFreeCheck()
        {
            var hs = Frame(150, 0);
            hs.InGame[7] = false;
            Assert.That(HSValidator.IsFreeCheckAgainstShortAllIn(hs), Is.False);
        }
    }
}

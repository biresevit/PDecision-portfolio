using DBLib;
using DecisionMaking.DecisionMaking;
using NUnit.Framework;

namespace PDecision.Tests
{
    /// <summary>
    /// Used to run as a blocking self-check on every PDecision startup (Controller.Init), which slowed down
    /// the service start for no benefit over running it here instead. Pure in-memory logic, no DB/file
    /// dependency, so unlike the DecisionMaker smoke tests in IntegrationTests.cs this one runs by default.
    /// </summary>
    public class DecisionMakerTests
    {
        [Test]
        public void BlindLevelsRoundTrip()
        {
            var level = EnumHandLevel.NL200;
            var bbSize = StrategyUtil.GetBBSizeForLevel(level);
            var sbSize = StrategyUtil.GetSBSizeForLevel(level);
            level = StrategyUtil.GetLevelForBBSize(bbSize);
            Assert.That(bbSize, Is.EqualTo(2));
            Assert.That(sbSize, Is.EqualTo(1));
            Assert.That(level, Is.EqualTo(EnumHandLevel.NL200));

            level = EnumHandLevel.NL25;
            bbSize = StrategyUtil.GetBBSizeForLevel(level);
            sbSize = StrategyUtil.GetSBSizeForLevel(level);
            level = StrategyUtil.GetLevelForBBSize(bbSize);
            Assert.That(bbSize, Is.EqualTo(0.25));
            Assert.That(sbSize, Is.EqualTo(0.1));
            Assert.That(level, Is.EqualTo(EnumHandLevel.NL25));
        }
    }
}

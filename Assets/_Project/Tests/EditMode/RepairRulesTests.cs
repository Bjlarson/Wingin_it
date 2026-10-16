using NUnit.Framework;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Repair tiers: the worst of body state and lost parts, and the fixed time for each.</summary>
    public class RepairRulesTests
    {
        static int Lost(params PlanePartType[] parts)
        {
            int mask = 0;
            foreach (var part in parts)
                mask |= PartFlightEffects.Bit(part);
            return mask;
        }

        [Test]
        public void HealthyBody_NothingLost_IsLight() =>
            Assert.AreEqual(RepairTier.Light, RepairRules.GetTier(BodyDamageState.Healthy, 0));

        [Test]
        public void DamagedBody_IsHeavy() =>
            Assert.AreEqual(RepairTier.Heavy, RepairRules.GetTier(BodyDamageState.Damaged, 0));

        [Test]
        public void CriticalBody_IsCritical() =>
            Assert.AreEqual(RepairTier.Critical, RepairRules.GetTier(BodyDamageState.Critical, 0));

        [TestCase(PlanePartType.HorizontalStabilizer)]
        [TestCase(PlanePartType.VerticalStabilizer)]
        [TestCase(PlanePartType.Engine)]
        public void LostStabilizerOrDeadEngine_IsHeavy_EvenWithAHealthyBody(PlanePartType part) =>
            Assert.AreEqual(RepairTier.Heavy, RepairRules.GetTier(BodyDamageState.Healthy, Lost(part)));

        [TestCase(PlanePartType.WingLeft)]
        [TestCase(PlanePartType.WingRight)]
        public void LostWing_IsCritical_EvenWithAHealthyBody(PlanePartType wing) =>
            Assert.AreEqual(RepairTier.Critical, RepairRules.GetTier(BodyDamageState.Healthy, Lost(wing)));

        [Test]
        public void WorstOfBodyAndParts_Wins()
        {
            Assert.AreEqual(RepairTier.Critical, RepairRules.GetTier(BodyDamageState.Critical, Lost(PlanePartType.Engine)));
            Assert.AreEqual(RepairTier.Critical, RepairRules.GetTier(BodyDamageState.Damaged, Lost(PlanePartType.WingLeft)));
            Assert.AreEqual(RepairTier.Heavy, RepairRules.GetTier(BodyDamageState.Damaged, Lost(PlanePartType.VerticalStabilizer)));
        }

        [Test]
        public void Durations_Are_5_10_15_Seconds()
        {
            var rules = new RepairRules();
            Assert.AreEqual(5f, rules.GetDuration(RepairTier.Light));
            Assert.AreEqual(10f, rules.GetDuration(RepairTier.Heavy));
            Assert.AreEqual(15f, rules.GetDuration(RepairTier.Critical));
        }
    }
}

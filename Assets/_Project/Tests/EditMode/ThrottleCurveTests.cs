using NUnit.Framework;
using WingingIt.Bots;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class ThrottleCurveTests
    {
        const float Idle = 28.8f, Min = 40f, Max = 110f; // Fighter: stall 32 × 0.9

        [Test]
        public void AboveTheIdleBand_MinToMaxSpeed_AsBefore()
        {
            Assert.AreEqual(75f, ThrottleCurve.AirSpeed(0.5f, Idle, Min, Max), 1e-3f);
            Assert.AreEqual(Max, ThrottleCurve.AirSpeed(1f, Idle, Min, Max), 1e-3f);
            Assert.AreEqual(1f, ThrottleCurve.Power(0.5f), 1e-4f);
        }

        [Test]
        public void ZeroThrottle_IsIdle_JustUnderTheStall_WithTheEngineTurning()
        {
            Assert.AreEqual(Idle, ThrottleCurve.AirSpeed(0f, Idle, Min, Max), 1e-3f);
            Assert.AreEqual(0f, ThrottleCurve.Power(0f), 1e-4f);
        }

        [Test]
        public void IdleBand_EasesDownWithoutAJump()
        {
            float band = ThrottleCurve.IdleBand;
            Assert.AreEqual(ThrottleCurve.AirSpeed(band, Idle, Min, Max), ThrottleCurve.AirSpeed(band - 1e-5f, Idle, Min, Max), 0.05f);
            float mid = ThrottleCurve.AirSpeed(band * 0.5f, Idle, Min, Max);
            Assert.Greater(mid, Idle);
            Assert.Less(mid, Min + (Max - Min) * band);
        }

        [Test]
        public void IdleNeverAboveMinSpeed()
        {
            Assert.AreEqual(Min, ThrottleCurve.AirSpeed(0f, 60f, Min, Max), 1e-3f);
        }

        [Test]
        public void Bots_NeverIdleInTheAir()
        {
            // Already at the bottom of the band, asking for less than min speed: hold, don't pull to idle.
            Assert.AreEqual(0f, BotSteering.ThrottleCommand(ThrottleCurve.IdleBand, 20f, Min, Max, 1f, false), 1e-4f);
            Assert.Greater(BotSteering.ThrottleCommand(0f, 20f, Min, Max, 1f, false), 0f);
        }
    }
}

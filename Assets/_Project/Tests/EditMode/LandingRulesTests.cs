using NUnit.Framework;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Touchdown judging: which faults crash, which bounce, and which order they're checked in.</summary>
    public class LandingRulesTests
    {
        const float MinSpeed = 40f; // Fighter: clean touchdowns up to 64 m/s

        LandingRules _rules;

        [SetUp]
        public void SetUp() => _rules = new LandingRules();

        TouchdownInfo Judge(bool gear = true, float speed = 50f, float sink = 3f, float pitch = 2f, float roll = 0f) =>
            _rules.Evaluate(gear, speed, sink, pitch, roll, MinSpeed);

        [Test]
        public void GentleTouchdown_Lands()
        {
            var info = Judge();
            Assert.AreEqual(TouchdownResult.Landed, info.Result);
            Assert.AreEqual(TouchdownFault.None, info.Fault);
            Assert.AreEqual(3f, info.SinkRate);
        }

        [Test]
        public void GearNotLocked_Crashes_EvenWhenPerfect()
        {
            var info = Judge(gear: false);
            Assert.AreEqual(TouchdownResult.Crashed, info.Result);
            Assert.AreEqual(TouchdownFault.GearNotLocked, info.Fault);
        }

        [TestCase(16f, 0f)]
        [TestCase(-16f, 0f)]
        [TestCase(0f, 16f)]
        [TestCase(0f, -16f)]
        public void PastTheAngleLimit_Crashes(float pitch, float roll)
        {
            var info = Judge(pitch: pitch, roll: roll);
            Assert.AreEqual(TouchdownResult.Crashed, info.Result);
            Assert.AreEqual(TouchdownFault.BadAngle, info.Fault);
        }

        [Test]
        public void AtTheAngleLimit_Lands() =>
            Assert.AreEqual(TouchdownResult.Landed, Judge(pitch: 15f, roll: -15f).Result);

        [Test]
        public void HardSink_Bounces_VeryHardSink_Crashes()
        {
            Assert.AreEqual(TouchdownResult.Landed, Judge(sink: _rules.bounceSinkRate).Result);

            var hard = Judge(sink: _rules.bounceSinkRate + 1f);
            Assert.AreEqual(TouchdownResult.Bounced, hard.Result);
            Assert.AreEqual(TouchdownFault.HardSink, hard.Fault);

            var crash = Judge(sink: _rules.crashSinkRate + 1f);
            Assert.AreEqual(TouchdownResult.Crashed, crash.Result);
            Assert.AreEqual(TouchdownFault.SinkTooFast, crash.Fault);
        }

        [Test]
        public void TooFast_Bounces_LimitScalesWithClassMinSpeed()
        {
            float limit = _rules.MaxTouchdownSpeed(MinSpeed);
            Assert.AreEqual(64f, limit, 0.001f);
            Assert.AreEqual(TouchdownResult.Landed, Judge(speed: limit).Result);

            var fast = Judge(speed: limit + 1f);
            Assert.AreEqual(TouchdownResult.Bounced, fast.Result);
            Assert.AreEqual(TouchdownFault.TooFast, fast.Fault);

            Assert.AreEqual(TouchdownResult.Landed, _rules.Evaluate(true, limit + 1f, 3f, 0f, 0f, 50f).Result,
                "a faster class (Interceptor) gets a higher limit");
        }

        [Test]
        public void WorstFaultWins()
        {
            Assert.AreEqual(TouchdownFault.GearNotLocked, Judge(gear: false, roll: 30f, sink: 20f).Fault);
            Assert.AreEqual(TouchdownFault.BadAngle, Judge(roll: 30f, sink: 20f).Fault);
            Assert.AreEqual(TouchdownFault.SinkTooFast, Judge(sink: 20f, speed: 90f).Fault);
            Assert.AreEqual(TouchdownFault.HardSink, Judge(sink: 8f, speed: 90f).Fault);
        }

        [Test]
        public void NearlyLevel_IsALandingAttempt_ADive_IsNot()
        {
            Assert.IsTrue(_rules.IsLandingAttempt(-5f, 25f), "wingtip strike while landing");
            Assert.IsFalse(_rules.IsLandingAttempt(-60f, 0f), "nose dive into the ground");
            Assert.IsFalse(_rules.IsLandingAttempt(0f, 90f), "knife-edge");
        }

        [Test]
        public void ClimbingAway_IsNeverAHardSink() =>
            Assert.AreEqual(TouchdownResult.Landed, Judge(sink: -2f).Result);
    }
}

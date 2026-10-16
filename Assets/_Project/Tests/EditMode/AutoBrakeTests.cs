using NUnit.Framework;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Runway auto-braking: rolling on toward the stop point, then braking along a curve that ends on it.</summary>
    public class AutoBrakeTests
    {
        const float Dt = 0.02f;

        AutoBrake _brake;

        [SetUp]
        public void SetUp() => _brake = new AutoBrake();

        /// <summary>Rolls from <paramref name="speed"/> with the stop point <paramref name="distance"/> m ahead until stopped.</summary>
        (float traveled, float seconds, float hardestDecel) RollOut(float speed, float distance, bool canTaxi = true)
        {
            float traveled = 0f, t = 0f, hardest = 0f;
            for (; t < 120f && (t == 0f || speed > 0f); t += Dt)
            {
                float next = _brake.NextSpeed(speed, distance - traveled, canTaxi, Dt);
                hardest = System.Math.Max(hardest, (speed - next) / Dt);
                speed = next;
                traveled += speed * Dt;
            }
            Assert.AreEqual(0f, speed, "never stopped");
            return (traveled, t, hardest);
        }

        [Test]
        public void NoStopPoint_BrakesSteadily()
        {
            float next = _brake.NextSpeed(40f, null, true, 1f);
            Assert.AreEqual(40f - _brake.brakeDeceleration, next, 1e-4f);
            Assert.AreEqual(0f, _brake.NextSpeed(1f, null, true, 1f), "doesn't roll backwards");
        }

        [Test]
        public void FarFromTheStopPoint_KeepsRolling()
        {
            Assert.Greater(_brake.StoppingSpeed(400f), 40f);
            Assert.AreEqual(40f, _brake.NextSpeed(40f, 400f, true, Dt));
        }

        [Test]
        public void CloseToTheStopPoint_Brakes()
        {
            float limit = _brake.StoppingSpeed(50f);
            Assert.Less(limit, 40f);
            float next = _brake.NextSpeed(40f, 50f, true, Dt);
            Assert.Less(next, 40f);
            Assert.AreEqual(40f - _brake.maxDeceleration * Dt, next, 1e-4f, "catches up as hard as the brakes allow");
        }

        [Test]
        public void SlowAndFar_TaxisUp_UnlessTheEngineIsDead()
        {
            Assert.Greater(_brake.NextSpeed(5f, 300f, true, Dt), 5f);
            Assert.AreEqual(5f, _brake.NextSpeed(5f, 300f, false, Dt), "a dead engine coasts");
            Assert.AreEqual(_brake.taxiSpeed, _brake.NextSpeed(_brake.taxiSpeed, 300f, true, Dt), "no faster than taxi speed");
        }

        [Test]
        public void ReachedOrPassedTheStopPoint_BrakesHard()
        {
            Assert.AreEqual(10f - _brake.maxDeceleration * Dt, _brake.NextSpeed(10f, 0f, true, Dt), 1e-4f);
            Assert.AreEqual(10f - _brake.maxDeceleration * Dt, _brake.NextSpeed(10f, -5f, true, Dt), 1e-4f);
        }

        [TestCase(40f, 400f)] // a normal landing at the near end of the runway
        [TestCase(64f, 300f)] // the fastest clean touchdown
        [TestCase(40f, 60f)] // landed long: has to brake hard
        [TestCase(0f, 150f)] // stopped short: taxis in
        public void RollOut_StopsOnTheStopPoint(float speed, float distance)
        {
            var (traveled, _, hardest) = RollOut(speed, distance);
            Assert.AreEqual(distance, traveled, 1.5f, "stops on the stop point");
            Assert.LessOrEqual(hardest, _brake.maxDeceleration + 0.01f);
        }

        [Test]
        public void NormalLanding_StopsInAReasonableTime()
        {
            var (_, seconds, _) = RollOut(40f, 400f);
            Assert.Less(seconds, 16f);
        }
    }
}

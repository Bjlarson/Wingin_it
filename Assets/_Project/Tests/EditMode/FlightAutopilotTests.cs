using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class FlightAutopilotTests
    {
        static void Steer(Vector3 target, Quaternion rotation, out float pitch, out float yaw, out float roll) =>
            FlightAutopilot.SteerTowards(Vector3.zero, rotation, target, 4f, 12f, out pitch, out yaw, out roll);

        [Test]
        public void TargetAbove_PitchesUp()
        {
            Steer(new Vector3(0f, 50f, 100f), Quaternion.identity, out float pitch, out _, out _);
            Assert.Greater(pitch, 0f);
        }

        [Test]
        public void TargetRight_YawsAndRollsRight()
        {
            Steer(new Vector3(100f, 0f, 100f), Quaternion.identity, out _, out float yaw, out float roll);
            Assert.Greater(yaw, 0f);
            Assert.Greater(roll, 0f);
        }

        [Test]
        public void TargetAhead_LevelsWings()
        {
            // Banked left (right wing up): should roll right to level out.
            var banked = Quaternion.Euler(0f, 0f, 30f);
            Steer(banked * new Vector3(0f, 0f, 100f), banked, out _, out _, out float roll);
            Assert.Greater(roll, 0f);
        }

        [Test]
        public void TargetBehind_PullsHard()
        {
            Steer(new Vector3(0f, 5f, -100f), Quaternion.identity, out float pitch, out _, out _);
            Assert.AreEqual(1f, pitch);
        }
    }
}

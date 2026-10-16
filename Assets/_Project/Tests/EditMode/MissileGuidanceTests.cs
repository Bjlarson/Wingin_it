using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class MissileGuidanceTests
    {
        [Test]
        public void Steer_TargetDeadAhead_KeepsHeading()
        {
            Vector3 dir = MissileGuidance.Steer(Vector3.forward, Vector3.zero, 200f,
                new Vector3(0f, 0f, 500f), Vector3.forward * 100f, 30f, 0.02f);
            Assert.AreEqual(0f, Vector3.Angle(Vector3.forward, dir), 1e-3f);
        }

        [Test]
        public void Steer_TurnsNoFasterThanTheTurnRate()
        {
            // Target way off to the right: the missile can only turn 30 deg/s × 0.1 s = 3 deg.
            Vector3 dir = MissileGuidance.Steer(Vector3.forward, Vector3.zero, 200f,
                new Vector3(500f, 0f, 0f), Vector3.zero, 30f, 0.1f);
            Assert.AreEqual(3f, Vector3.Angle(Vector3.forward, dir), 1e-2f);
            Assert.Greater(dir.x, 0f, "turns toward the target");
        }

        [Test]
        public void Steer_LeadsACrossingTarget()
        {
            // Target ahead crossing left to right: aim ahead of it (to its right), not at it.
            Vector3 targetPos = new Vector3(0f, 0f, 400f);
            Vector3 dir = MissileGuidance.Steer(Vector3.forward, Vector3.zero, 200f,
                targetPos, Vector3.right * 100f, 360f, 1f);
            Assert.Greater(dir.x, 0.1f);
        }

        [Test]
        public void InCone_MatchesTheHalfAngle()
        {
            Vector3 point = Quaternion.Euler(0f, 20f, 0f) * Vector3.forward * 100f;
            Assert.IsTrue(MissileGuidance.InCone(Vector3.zero, Vector3.forward, point, 25f));
            Assert.IsFalse(MissileGuidance.InCone(Vector3.zero, Vector3.forward, point, 15f));
            Assert.IsFalse(MissileGuidance.InCone(Vector3.zero, Vector3.forward, -point, 90f), "behind is never in the cone");
        }

        [Test]
        public void DistanceToSegment_FindsTheClosestApproach()
        {
            float d = MissileGuidance.DistanceToSegment(new Vector3(0f, 3f, 5f), Vector3.zero, new Vector3(0f, 0f, 10f), out float t);
            Assert.AreEqual(3f, d, 1e-4f);
            Assert.AreEqual(0.5f, t, 1e-4f);

            d = MissileGuidance.DistanceToSegment(new Vector3(0f, 0f, -4f), Vector3.zero, new Vector3(0f, 0f, 10f), out t);
            Assert.AreEqual(4f, d, 1e-4f);
            Assert.AreEqual(0f, t);
        }

        [Test]
        public void MissileSplash_FallsOffToTheMinimumAtTheEdge()
        {
            var stats = ScriptableObject.CreateInstance<MissileStats>();
            stats.splashDamage = 30f;
            stats.splashRadius = 12f;
            stats.splashFalloffMin = 0.5f;
            Assert.AreEqual(30f, stats.SplashDamageAt(0f), 1e-4f);
            Assert.AreEqual(15f, stats.SplashDamageAt(12f), 1e-4f);
            Assert.AreEqual(0f, stats.SplashDamageAt(12.1f));
            Object.DestroyImmediate(stats);
        }
    }
}

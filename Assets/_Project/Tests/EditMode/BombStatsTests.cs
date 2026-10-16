using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class BombStatsTests
    {
        BombStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<BombStats>();
            _stats.gravity = 10f;
            _stats.groundDamage = 150f;
            _stats.planeSplashDamage = 15f;
            _stats.splashRadius = 20f;
            _stats.splashFalloffMin = 0.3f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        [Test]
        public void FreeFall_KeepsHorizontalSpeed_AndDropsWithGravity()
        {
            Vector3 p = _stats.PositionAt(new Vector3(0f, 100f, 0f), new Vector3(0f, 0f, 50f), 2f);
            Assert.AreEqual(100f, p.z, 1e-4f);
            Assert.AreEqual(100f - 0.5f * 10f * 4f, p.y, 1e-4f);
            Assert.AreEqual(-20f, _stats.VelocityAt(Vector3.zero, 2f).y, 1e-4f);
        }

        [Test]
        public void Stepping_MatchesTheClosedForm()
        {
            // Bombs advance in physics steps; the prediction jumps straight to time t. They must agree.
            Vector3 pos = new Vector3(0f, 300f, 0f), vel = new Vector3(5f, -3f, 90f);
            Vector3 start = pos, startVel = vel;
            const float dt = 0.02f;
            for (int i = 0; i < 250; i++)
            {
                pos = _stats.PositionAt(pos, vel, dt);
                vel = _stats.VelocityAt(vel, dt);
            }
            Vector3 direct = _stats.PositionAt(start, startVel, 250 * dt);
            Assert.Less(Vector3.Distance(pos, direct), 0.01f);
        }

        [Test]
        public void GroundDamage_DwarfsPlaneDamage()
        {
            Assert.AreEqual(150f, _stats.GroundDamageAt(0f), 1e-4f);
            Assert.AreEqual(15f, _stats.PlaneSplashDamageAt(0f), 1e-4f);
            Assert.AreEqual(45f, _stats.GroundDamageAt(20f), 1e-4f);
            Assert.AreEqual(0f, _stats.GroundDamageAt(20.01f));
        }
    }
}

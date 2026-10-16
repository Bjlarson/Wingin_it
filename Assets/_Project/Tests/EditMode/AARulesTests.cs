using NUnit.Framework;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>AA gun rules: who's hostile, the lock-on delay and fire cone, spread tightening, and outrunning the turret.</summary>
    public class AARulesTests
    {
        AAStats _stats;

        [SetUp]
        public void CreateStats() => _stats = ScriptableObject.CreateInstance<AAStats>();

        [TearDown]
        public void DestroyStats() => Object.DestroyImmediate(_stats);

        [Test]
        public void OtherTeams_AreHostile_OwnTeamIsNot()
        {
            Assert.IsTrue(AARules.IsHostile(0, 1));
            Assert.IsFalse(AARules.IsHostile(0, 0));
        }

        [Test]
        public void NeutralGuns_ShootNobody() => Assert.IsFalse(AARules.IsHostile(Airfield.Neutral, 1));

        [Test]
        public void HoldsFire_UntilTheLockOnDelay()
        {
            Assert.IsFalse(AARules.CanFire(_stats.acquireTime * 0.9f, 0f, _stats));
            Assert.IsTrue(AARules.CanFire(_stats.acquireTime, 0f, _stats));
        }

        [Test]
        public void HoldsFire_WhileTheBarrelsAreOffTheLeadPoint() =>
            Assert.IsFalse(AARules.CanFire(_stats.acquireTime + 5f, _stats.fireCone + 0.5f, _stats));

        [Test]
        public void Spread_TightensTheLongerItTracks()
        {
            float first = AARules.Spread(_stats.acquireTime, _stats);
            float mid = AARules.Spread(_stats.acquireTime + _stats.settleTime * 0.5f, _stats);
            float settled = AARules.Spread(_stats.acquireTime + _stats.settleTime * 2f, _stats);
            Assert.AreEqual(_stats.startSpread, first, 1e-4f);
            Assert.Less(mid, first);
            Assert.AreEqual(_stats.settledSpread, settled, 1e-4f);
        }

        [Test]
        public void LineOfSightRate_CrossingTarget()
        {
            // 100 m/s crossing at 500 m = 0.2 rad/s.
            float rate = AARules.LineOfSightRate(Vector3.zero, new Vector3(0f, 0f, 500f), new Vector3(100f, 0f, 0f));
            Assert.AreEqual(0.2f * Mathf.Rad2Deg, rate, 0.01f);
        }

        [Test]
        public void LineOfSightRate_HeadOnTarget_IsZero() =>
            Assert.AreEqual(0f, AARules.LineOfSightRate(Vector3.zero, new Vector3(0f, 0f, 500f), new Vector3(0f, 0f, -100f)), 1e-4f);

        [Test]
        public void DefaultTuning_KeepsUpWithACamper_ButNotAFastLowPass()
        {
            // A Fighter circling the field at cruise, 400 m out and 300 m up: the turret keeps up.
            float camper = AARules.LineOfSightRate(Vector3.zero, new Vector3(400f, 300f, 0f), new Vector3(0f, 0f, 80f));
            Assert.Less(camper, _stats.traverseRate, "a lingering plane gets tracked");

            // An Interceptor at top speed passing 50 m from the gun: it outruns the turret.
            float pass = AARules.LineOfSightRate(Vector3.zero, new Vector3(50f, 20f, 0f), new Vector3(0f, 0f, 140f));
            Assert.Greater(pass, _stats.traverseRate, "a fast, low pass gets through");
        }
    }
}

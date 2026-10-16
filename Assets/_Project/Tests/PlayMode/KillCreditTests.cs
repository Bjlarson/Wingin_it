using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Who gets the kill when a plane is shot down or crashes after being hit.</summary>
    public class KillCreditTests
    {
        PlaneController _plane;
        PlaneHealth _health;
        PlaneController _attacker;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
            Assert.IsNotEmpty(FlightTestScene.Dummies, "the scene needs a dummy to act as the attacker");
            // The dummy stays parked: only its identity as a damage source matters here.
            _attacker = FlightTestScene.Dummies[0].GetComponent<PlaneController>();
        }

        void Hit(PlanePartType type, float amount, Object source)
        {
            var part = _health.GetPart(type);
            _health.ApplyDamage(part, amount, part.transform.position, source);
        }

        void HitToDestruction(PlanePartType type, Object source) => Hit(type, _health.GetPart(type).MaxHealth, source);

        [Test]
        public void ShotDown_CreditsTheShooter_AndRaisesEvents()
        {
            KillInfo? local = null, global = null;
            _health.Killed += k => local = k;
            void OnAny(KillInfo k) => global = k;
            PlaneHealth.AnyKilled += OnAny;
            try
            {
                HitToDestruction(PlanePartType.Body, _attacker);
            }
            finally
            {
                PlaneHealth.AnyKilled -= OnAny;
            }

            Assert.IsTrue(local.HasValue && global.HasValue);
            Assert.AreEqual(_attacker, local.Value.Killer);
            Assert.AreEqual(_health, global.Value.Victim);
            Assert.AreEqual(DeathCause.ShotDown, local.Value.Cause);
            Assert.AreEqual(_attacker, _health.LastKill.Killer);
        }

        [Test]
        public void CrashSoonAfterAHit_CreditsTheLastAttacker()
        {
            Hit(PlanePartType.WingLeft, 5f, _attacker);
            _plane.Crash(_plane.Position);
            Assert.AreEqual(DeathCause.Collision, _health.LastKill.Cause);
            Assert.AreEqual(_attacker, _health.LastKill.Killer);
        }

        [Test]
        public void CrashWithNoAttacker_CreditsNobody()
        {
            _plane.Crash(_plane.Position);
            Assert.IsNull(_health.LastKill.Killer);
            Assert.IsNull(_health.LastKill.Source);
        }

        [UnityTest]
        public IEnumerator LightHit_ExpiresAfterTheWindow()
        {
            _health.KillCreditWindow = 0.1f;
            Hit(PlanePartType.WingLeft, 5f, _attacker);
            Assert.IsFalse(_health.IsCrippled);
            yield return new WaitForSeconds(0.3f);
            _plane.Crash(_plane.Position);
            Assert.IsNull(_health.LastKill.Killer, "a scratch long ago doesn't earn a kill");
        }

        [UnityTest]
        public IEnumerator CrippledPlane_KeepsCreditPastTheWindow()
        {
            _health.KillCreditWindow = 0.1f;
            HitToDestruction(PlanePartType.WingRight, _attacker);
            Assert.IsTrue(_health.IsCrippled);
            yield return new WaitForSeconds(0.3f);
            _plane.Crash(_plane.Position);
            Assert.AreEqual(_attacker, _health.LastKill.Killer, "whoever crippled it gets the crash");
        }

        [Test]
        public void SelfAndUnattributedDamage_DontStealCredit()
        {
            Hit(PlanePartType.WingLeft, 5f, _attacker);
            Hit(PlanePartType.WingLeft, 5f, _plane);
            Hit(PlanePartType.Engine, 5f, null);
            Assert.AreEqual(_attacker, _health.LastAttacker);
            _plane.Crash(_plane.Position);
            Assert.AreEqual(_attacker, _health.LastKill.Killer);
        }

        [Test]
        public void LatestAttacker_TakesTheCredit()
        {
            var second = FlightTestScene.Dummies.Length > 1 ? FlightTestScene.Dummies[1].GetComponent<PlaneController>() : null;
            if (second == null)
                Assert.Ignore("needs two dummies");
            HitToDestruction(PlanePartType.WingLeft, _attacker);
            Hit(PlanePartType.Body, 5f, second);
            _plane.Crash(_plane.Position);
            Assert.AreEqual(second, _health.LastKill.Killer);
        }

        [Test]
        public void Restore_ClearsTheAttacker()
        {
            HitToDestruction(PlanePartType.WingLeft, _attacker);
            _health.RestoreAll();
            Assert.IsNull(_health.LastAttacker);
            _plane.Crash(_plane.Position);
            Assert.IsNull(_health.LastKill.Killer, "a repaired plane starts with a clean slate");
        }
    }
}

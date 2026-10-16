using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Landing in the home repair zone: the tier sets the timer, finishing restores everything, leaving cancels it.</summary>
    public class RepairSceneTests
    {
        PlaneController _plane;
        PlaneHealth _health;
        PlaneRepair _repair;
        Airfield _home;
        int _started, _completed, _cancelled;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
            _repair = _plane.GetComponent<PlaneRepair>();
            Assert.IsNotNull(_repair, "the player plane has no PlaneRepair");
            _home = Airfield.NearestFriendly(_plane.transform.position, _plane.Team);
            Assert.IsNotNull(_home, "no friendly airfield in the scene");
            _started = _completed = _cancelled = 0;
            _repair.Started += _ => _started++;
            _repair.Completed += _ => _completed++;
            _repair.Cancelled += _ => _cancelled++;
        }

        IEnumerator Wait(float seconds) => RunwayLanding.Fly(_plane, default, seconds);

        /// <summary>Lands near the threshold, applies <paramref name="damage"/> during the rollout, and stops in the zone.</summary>
        IEnumerator LandAndStopInTheZone(System.Action damage = null)
        {
            yield return RunwayLanding.FinalApproachAndLand(_plane, _home);
            damage?.Invoke();
            yield return RunwayLanding.RollToAStop(_plane);
            Assert.IsTrue(_home.RepairZone.Contains(_plane.Position), "stopped in the repair zone");
        }

        void DamageBodyTo(float fraction)
        {
            var body = _health.GetPart(PlanePartType.Body);
            _health.ApplyDamage(body, body.Health - body.MaxHealth * fraction, _plane.Position);
        }

        void Destroy(PlanePartType type) => _health.ApplyDamage(type, 10000f, _plane.Position);

        [UnityTest]
        public IEnumerator Undamaged_StopsInTheZone_LightRepair_ThenWaitsToLeave()
        {
            yield return LandAndStopInTheZone();
            Assert.IsTrue(_repair.IsRepairing, "repair starts once stopped in the zone");
            Assert.AreEqual(RepairTier.Light, _repair.Tier);
            Assert.AreEqual(_repair.Rules.lightTime, _repair.Duration);
            Assert.AreSame(_home.RepairZone, _repair.Zone);

            yield return Wait(_repair.Duration + 0.2f);
            Assert.IsFalse(_repair.IsRepairing);
            Assert.AreEqual(1, _completed);

            yield return Wait(1f);
            Assert.AreEqual(1, _started, "doesn't repair again until it leaves the zone");
        }

        [UnityTest]
        public IEnumerator LostWingAndDeadEngine_CriticalRepair_RestoresEverything()
        {
            yield return LandAndStopInTheZone(() =>
            {
                Destroy(PlanePartType.WingLeft);
                Destroy(PlanePartType.Engine);
                DamageBodyTo(0.5f);
            });
            Assert.IsTrue(_repair.IsRepairing);
            Assert.AreEqual(RepairTier.Critical, _repair.Tier, "a lost wing is Critical");
            Assert.AreEqual(_repair.Rules.criticalTime, _repair.Duration);

            yield return Wait(_repair.Duration - 1f);
            Assert.IsTrue(_repair.IsRepairing, "still running a second before the end");
            Assert.AreNotEqual(0, _health.LostPartsMask, "nothing is fixed until the timer finishes");

            yield return Wait(1.2f);
            Assert.AreEqual(1, _completed);
            Assert.AreEqual(0, _health.LostPartsMask, "wing reattached, engine restarted");
            for (int i = 0; i < PlanePart.TypeCount; i++)
            {
                var part = _health.GetPart((PlanePartType)i);
                Assert.AreEqual(part.MaxHealth, part.Health, $"{part.partType} back to full HP");
            }
            Assert.AreEqual(BodyDamageState.Healthy, _health.BodyState);
        }

        [UnityTest]
        public IEnumerator LeavingTheZoneEarly_Cancels_WithNoPartialRepair()
        {
            yield return LandAndStopInTheZone(() => DamageBodyTo(0.5f));
            Assert.IsTrue(_repair.IsRepairing);
            Assert.AreEqual(RepairTier.Heavy, _repair.Tier);
            var body = _health.GetPart(PlanePartType.Body);
            float bodyHealth = body.Health;

            var zone = _home.RepairZone;
            for (float t = 0f; t < 20f && zone.Contains(_plane.Position); t += Time.fixedDeltaTime)
                yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = 0.6f }, Time.fixedDeltaTime);
            Assert.IsFalse(zone.Contains(_plane.Position), "taxied out of the zone");
            Assert.IsTrue(_plane.IsAlive);

            Assert.IsFalse(_repair.IsRepairing);
            Assert.AreEqual(1, _cancelled);
            Assert.AreEqual(0, _completed);
            Assert.AreEqual(bodyHealth, body.Health, "no partial repair");
            Assert.IsFalse(_health.IsInvulnerable, "leaving the zone ends invulnerability");
            Assert.IsFalse(_repair.Bubble.activeSelf, "and hides the bubble");
        }

        [UnityTest]
        public IEnumerator WhileRepairing_Invulnerable_WithBubble_UntilTheTimerEnds()
        {
            Assert.IsFalse(_health.IsInvulnerable, "not invulnerable before landing");
            yield return LandAndStopInTheZone(() => DamageBodyTo(0.5f));
            Assert.IsTrue(_repair.IsRepairing);
            Assert.IsTrue(_health.IsInvulnerable, "invulnerable once the repair starts");
            Assert.IsNotNull(_repair.Bubble);
            Assert.IsTrue(_repair.Bubble.activeSelf, "bubble shows while repairing");
            Assert.IsNull(_repair.Bubble.GetComponent<Collider>(), "the bubble is never a hitbox");

            Assert.IsNotEmpty(FlightTestScene.Dummies, "the scene needs a dummy to act as the attacker");
            var attacker = FlightTestScene.Dummies[0].GetComponent<PlaneController>();
            var body = _health.GetPart(PlanePartType.Body);
            float bodyHealth = body.Health;
            Assert.AreEqual(0f, _health.ApplyDamage(body, 10000f, _plane.Position, attacker), "takes no damage");
            Assert.AreEqual(0f, _health.ApplyDamage(PlanePartType.WingLeft, 10000f, _plane.Position, attacker));
            Assert.AreEqual(bodyHealth, body.Health);
            Assert.AreEqual(0, _health.LostPartsMask, "can't lose a part while repairing");
            Assert.IsNull(_health.LastAttacker, "a blocked hit credits nobody");
            Assert.IsTrue(_plane.IsAlive);

            yield return Wait(_repair.Duration + 0.2f);
            Assert.AreEqual(1, _completed);
            Assert.IsFalse(_health.IsInvulnerable, "invulnerability ends with the repair");
            Assert.IsFalse(_repair.Bubble.activeSelf, "bubble hidden after the repair");
            Assert.Greater(_health.ApplyDamage(body, 10f, _plane.Position, attacker), 0f, "vulnerable again while still parked");
        }
    }
}

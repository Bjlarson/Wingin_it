using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Class abilities: activation and cooldown, and each ability's effect, against real planes in the FlightTest scene.</summary>
    public class AbilityTests
    {
        static readonly Vector3 Start = new Vector3(0f, 600f, -1500f);

        PlaneController _plane;
        PlaneAbility _ability;
        PlaneHealth _health;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _ability = _plane.GetComponent<PlaneAbility>();
            _health = _plane.GetComponent<PlaneHealth>();
            Assert.IsNotNull(_ability, "plane has no PlaneAbility");
        }

        FlightStats Class(string displayName) => FlightTestScene.FindClass(c => c.displayName == displayName);

        void Become(string cls)
        {
            _plane.SetStats(Class(cls));
            _plane.Respawn(Start, Quaternion.identity);
        }

        /// <summary>A dummy flown by the test (straight and level unless given input), as <paramref name="cls"/> on <paramref name="team"/>.</summary>
        PlaneController Dummy(int index, string cls, int team, Vector3 offset)
        {
            var dummy = FlightTestScene.Dummies[index];
            dummy.enabled = false;
            dummy.gameObject.SetActive(true);
            var plane = dummy.GetComponent<PlaneController>();
            plane.SetStats(Class(cls));
            plane.Team = team;
            plane.Respawn(Start + offset, Quaternion.identity);
            return plane;
        }

        static IEnumerator Fly(float seconds, PlaneController plane = null, bool useAbility = false)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                if (plane != null)
                    plane.SetInput(new FlightInput { UseAbility = useAbility });
                yield return new WaitForFixedUpdate();
            }
        }

        [TestCase("Fighter", typeof(BarrelRollAbility))]
        [TestCase("Interceptor", typeof(AfterburnerAbility))]
        [TestCase("Heavy Fighter", typeof(ArmorShieldAbility))]
        [TestCase("Support", typeof(RepairAuraAbility))]
        public void EachClass_HasItsAbility(string cls, System.Type ability)
        {
            _plane.SetStats(Class(cls));
            Assert.IsInstanceOf(ability, _ability.Stats);
            Assert.IsTrue(_ability.IsReady, "ready from spawn");
        }

        [UnityTest]
        public IEnumerator HoldingTheButton_ActivatesOnce_ThenCoolsDown()
        {
            Become("Heavy Fighter");
            var s = _ability.Stats;
            int activations = 0, ends = 0;
            _ability.Activated += _ => activations++;
            _ability.Ended += _ => ends++;

            yield return Fly(0.2f, _plane, useAbility: true);
            Assert.IsTrue(_ability.IsActive);
            Assert.That(_ability.ActiveTimeLeft, Is.LessThan(s.duration));

            yield return Fly(s.duration + 0.2f, _plane, useAbility: true); // still held: no second activation
            Assert.AreEqual(1, activations);
            Assert.AreEqual(1, ends);
            Assert.IsFalse(_ability.IsActive);
            Assert.IsFalse(_ability.IsReady);
            Assert.That(_ability.CooldownLeft, Is.InRange(s.cooldown - 1f, s.cooldown));

            yield return Fly(0.1f, _plane); // release
            Assert.IsFalse(_ability.TryActivate(), "can't activate while recharging");
            yield return Fly(0.1f, _plane, useAbility: true);
            Assert.AreEqual(1, activations);
        }

        [UnityTest]
        public IEnumerator Dying_EndsTheEffect_AndRespawningMakesItReady()
        {
            Become("Heavy Fighter");
            Assert.IsTrue(_ability.TryActivate());
            Assert.Less(_health.DamageTakenMultiplier, 1f);

            _plane.Crash(_plane.Position);
            Assert.IsFalse(_ability.IsActive);
            Assert.AreEqual(1f, _health.DamageTakenMultiplier, "shield drops on death");
            Assert.IsFalse(_ability.TryActivate(), "dead planes can't use abilities");

            _plane.Respawn(Start, Quaternion.identity);
            yield return null;
            Assert.IsTrue(_ability.IsReady);
        }

        [UnityTest]
        public IEnumerator BarrelRoll_RollsAFullCircle_AndShakesOffAChasingMissile()
        {
            Become("Fighter");
            var hunter = Dummy(0, "Interceptor", team: 1, offset: Vector3.back * 600f);
            var launcher = hunter.GetComponent<PlaneMissiles>();
            int evaded = 0;
            launcher.Evaded += (_, p) => { if (p == _plane) evaded++; };

            yield return Fly(0.2f);
            Assert.IsTrue(launcher.LaunchAt(_plane));
            yield return Fly(0.5f);
            Assert.AreEqual(1, PlaneMissiles.GetThreat(_plane).Incoming, "missile is chasing us");

            Assert.IsTrue(_ability.TryActivate());
            Assert.AreEqual(1, evaded, "the roll shakes the missile off at once");
            Assert.AreEqual(0, PlaneMissiles.GetThreat(_plane).Incoming);

            yield return Fly(0.05f);
            Assert.IsTrue(_plane.IsBarrelRolling);
            Vector3 rightMidRoll = _plane.Rotation * Vector3.right;
            Assert.Less(rightMidRoll.y, -0.05f, "rolling right: the right wing drops first");

            yield return Fly(_ability.Stats.duration + 0.1f);
            Assert.IsFalse(_plane.IsBarrelRolling);
            Assert.Greater(Vector3.Dot(_plane.Rotation * Vector3.up, Vector3.up), 0.95f, "back to wings level after 360°");
        }

        [UnityTest]
        public IEnumerator BarrelRoll_ResetsLocksOnUs()
        {
            Become("Fighter");
            var hunter = Dummy(0, "Fighter", team: 1, offset: Vector3.back * 300f); // same class: the gap holds
            var launcher = hunter.GetComponent<PlaneMissiles>();

            yield return Fly(launcher.Stats.lockTime + 0.3f);
            Assert.IsTrue(launcher.IsLocked, "the hunter locks us from behind");

            Assert.IsTrue(_ability.TryActivate());
            Assert.IsFalse(launcher.IsLocked);
            Assert.Less(launcher.LockProgress, 0.1f);
            Assert.IsFalse(PlaneMissiles.GetThreat(_plane).Locked);
        }

        [UnityTest]
        public IEnumerator Afterburner_PushesPastTopSpeed_ThenWearsOff()
        {
            Become("Interceptor");
            var stats = _plane.Stats;
            yield return Fly(1f);
            Assert.Less(_plane.Speed, stats.maxSpeed, "cruising below top speed");

            Assert.IsTrue(_ability.TryActivate());
            yield return Fly(2.5f);
            Assert.Greater(_plane.Speed, stats.maxSpeed, "afterburner beats top speed at cruise throttle");
            Assert.IsTrue(_ability.IsActive);

            yield return Fly(_ability.Stats.duration);
            Assert.IsFalse(_ability.IsActive);
            Assert.AreEqual(stats.maxSpeed, _plane.Modifiers.Get(FlightStat.MaxSpeed), 1e-3f, "modifiers removed");
            Assert.AreEqual(stats.minSpeed, _plane.Modifiers.Get(FlightStat.MinSpeed), 1e-3f);
        }

        [Test]
        public void Afterburner_NeedsAWorkingEngine()
        {
            Become("Interceptor");
            var engine = _health.GetPart(PlanePartType.Engine);
            _health.ApplyDamage(engine, engine.MaxHealth, _plane.Position);
            Assert.IsFalse(_ability.TryActivate());
        }

        [Test]
        public void ArmorShield_HalvesDamageWhileUp()
        {
            Become("Heavy Fighter");
            float shield = ((ArmorShieldAbility)_ability.Stats).damageTaken;
            Assert.IsTrue(_ability.TryActivate());
            Assert.AreEqual(20f * shield, _health.ApplyDamage(PlanePartType.Body, 20f, _plane.Position), 1e-3f);
            Assert.AreEqual(10f * shield, _health.ApplyDamage(PlanePartType.WingLeft, 10f, _plane.Position), 1e-3f, "every part");
        }

        [UnityTest]
        public IEnumerator ArmorShield_FullDamageAfterItEnds()
        {
            Become("Heavy Fighter");
            Assert.IsTrue(_ability.TryActivate());
            yield return Fly(_ability.Stats.duration + 0.1f);
            Assert.AreEqual(1f, _health.DamageTakenMultiplier);
            Assert.AreEqual(20f, _health.ApplyDamage(PlanePartType.Body, 20f, _plane.Position), 1e-3f);
        }

        [UnityTest]
        public IEnumerator RepairAura_HealsAlliesInRange_AndItselfSlower_ButNotEnemiesOrLostParts()
        {
            Become("Support");
            var aura = (RepairAuraAbility)_ability.Stats;
            var ally = Dummy(0, "Support", team: 0, offset: Vector3.right * 30f);
            var farAlly = Dummy(1, "Support", team: 0, offset: Vector3.right * (aura.radius + 60f));
            var enemy = Dummy(2, "Support", team: 1, offset: Vector3.left * 30f);
            var allyHealth = ally.GetComponent<PlaneHealth>();
            var farHealth = farAlly.GetComponent<PlaneHealth>();
            var enemyHealth = enemy.GetComponent<PlaneHealth>();

            foreach (var h in new[] { _health, allyHealth, farHealth, enemyHealth })
                h.ApplyDamage(PlanePartType.Body, 50f, h.Plane.Position);
            allyHealth.ApplyDamage(PlanePartType.WingLeft, 1000f, ally.Position);
            float Body(PlaneHealth h) => h.GetPart(PlanePartType.Body).Health;
            float self0 = Body(_health), ally0 = Body(allyHealth), far0 = Body(farHealth), enemy0 = Body(enemyHealth);

            Assert.IsTrue(_ability.TryActivate());
            yield return Fly(1f);

            float allyGain = Body(allyHealth) - ally0;
            float selfGain = Body(_health) - self0;
            float expected = aura.healPerSecond * allyHealth.GetPart(PlanePartType.Body).MaxHealth;
            Assert.AreEqual(expected, allyGain, expected * 0.15f, "ally heals at the aura rate");
            Assert.AreEqual(allyGain * aura.selfHealMultiplier, selfGain, expected * 0.1f, "self heals slower");
            Assert.AreEqual(far0, Body(farHealth), "ally out of range");
            Assert.AreEqual(enemy0, Body(enemyHealth), "enemies never heal");
            Assert.IsTrue(allyHealth.GetPart(PlanePartType.WingLeft).IsDestroyed, "lost parts stay lost");
        }

        [Test]
        public void SupportRadar_RevealsNearbyEnemiesToItsTeam()
        {
            Become("Support");
            float range = ((RepairAuraAbility)_ability.Stats).radarRange;
            var near = Dummy(0, "Fighter", team: 1, offset: Vector3.forward * (range - 100f));
            var far = Dummy(1, "Fighter", team: 1, offset: Vector3.forward * (range + 200f));
            var ally = Dummy(2, "Fighter", team: 0, offset: Vector3.forward * 100f);

            Assert.IsTrue(PlaneAbility.IsRevealedTo(near, 0));
            Assert.IsFalse(PlaneAbility.IsRevealedTo(far, 0), "out of radar range");
            Assert.IsFalse(PlaneAbility.IsRevealedTo(ally, 0), "allies aren't radar contacts");
            Assert.IsFalse(PlaneAbility.IsRevealedTo(near, 1), "only the Support's own team sees them");

            _plane.SetStats(Class("Fighter"));
            Assert.IsFalse(PlaneAbility.IsRevealedTo(near, 0), "no Support, no radar");
        }
    }
}

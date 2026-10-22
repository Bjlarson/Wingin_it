using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Dev;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Ram damage (Ram Royale) between two real planes in the FlightTest scene.</summary>
    public class RamDamageTests
    {
        PlaneController _plane;
        PlaneController _other;
        PlaneHealth _health;
        PlaneHealth _otherHealth;
        RamInfo? _ram;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
            Assert.IsNotEmpty(FlightTestScene.Dummies, "the scene needs a dummy to ram");
            var dummy = FlightTestScene.Dummies[0];
            dummy.gameObject.SetActive(true);
            dummy.GetComponent<DummyPilot>().enabled = false; // flown straight by the test instead
            _other = dummy.GetComponent<PlaneController>();
            _otherHealth = _other.GetComponent<PlaneHealth>();
            _ram = null;
            PlaneController.AnyRammed += OnRammed;
        }

        [TearDown]
        public void Unsubscribe() => PlaneController.AnyRammed -= OnRammed;

        void OnRammed(RamInfo info) => _ram ??= info;

        /// <summary>Puts both planes in the air with ram damage on (respawning clears it), flying straight.</summary>
        void Place(Vector3 ours, Quaternion ourRotation, float ourThrottle, Vector3 theirs, Quaternion theirRotation, float theirThrottle,
            bool boost = false)
        {
            _plane.Respawn(ours, ourRotation, ourThrottle);
            _other.Respawn(theirs, theirRotation, theirThrottle);
            _plane.RamDamage = _other.RamDamage = true;
            _plane.SetInput(new FlightInput { Boost = boost });
            _other.SetInput(default);
        }

        IEnumerator FlyUntilRam(float seconds)
        {
            float t = Time.time;
            while (_ram == null && Time.time - t < seconds)
                yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator HeadOn_KillsBoth_EachCreditingTheOther()
        {
            Place(new Vector3(0f, 500f, -150f), Quaternion.identity, 0.8f,
                new Vector3(0f, 500f, 150f), Quaternion.Euler(0f, 180f, 0f), 0.8f);
            yield return FlyUntilRam(5f);

            Assert.IsTrue(_ram.HasValue, "never touched");
            Assert.IsFalse(_plane.IsAlive, "a head-on ram kills the rammer");
            Assert.IsFalse(_other.IsAlive, "and the plane it rammed");
            Assert.AreEqual(DeathCause.Rammed, _health.LastKill.Cause);
            Assert.AreEqual(_other, _health.LastKill.Killer);
            Assert.AreEqual(_plane, _otherHealth.LastKill.Killer);
        }

        [UnityTest]
        public IEnumerator FromBehind_TheVictimTakesMore_AndTheRammerFliesOn()
        {
            // Full throttle and boost into the tail of a plane idling ahead.
            Place(new Vector3(0f, 500f, -60f), Quaternion.identity, 1f,
                new Vector3(0f, 500f, 0f), Quaternion.identity, 0f, boost: true);
            yield return FlyUntilRam(8f);

            Assert.IsTrue(_ram.HasValue, "never caught up");
            var ram = _ram.Value;
            Assert.AreEqual(_plane, ram.Hitter);
            Assert.Greater(ram.ClosingSpeed, _plane.RamRules.minClosingSpeed);
            // The rammer takes its share of the hit, the victim all of it (or loses the part it was hit on), split by mass.
            var rules = _plane.RamRules;
            float ourMass = _plane.Stats.ramMass, theirMass = _other.Stats.ramMass;
            float fullHit = rules.damagePerSpeed * ram.ClosingSpeed;
            Assert.AreEqual(fullHit * rules.hitterShare * 2f * theirMass / (ourMass + theirMass), ram.HitterDamage, 0.5f,
                "the rammer takes only its share");
            float victimHit = fullHit * 2f * ourMass / (ourMass + theirMass);
            Assert.IsTrue(ram.VictimDamage >= victimHit - 0.5f || _otherHealth.LostPartsMask != 0,
                $"the plane hit from behind takes the worst of it ({ram.VictimDamage:0} of {victimHit:0})");
            Assert.IsTrue(_plane.IsAlive, "the rammer survives a hit from behind");
            Assert.AreEqual(ram.VictimDamage, TotalLost(_otherHealth), 1e-3f, "the damage lands on the plane rammed");

            // One contact counts once: they pass through each other instead of ramming again every step.
            float otherLost = TotalLost(_otherHealth);
            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(otherLost, TotalLost(_otherHealth), 1e-3f, "rammed twice by one contact");
        }

        [UnityTest]
        public IEnumerator RamDamageOff_TheRammerCrashes_AsBefore()
        {
            Place(new Vector3(0f, 500f, -150f), Quaternion.identity, 0.8f,
                new Vector3(0f, 500f, 150f), Quaternion.Euler(0f, 180f, 0f), 0.8f);
            _plane.RamDamage = _other.RamDamage = false;
            float t = Time.time;
            while (_plane.IsAlive && _other.IsAlive && Time.time - t < 5f)
                yield return new WaitForFixedUpdate();

            Assert.IsNull(_ram, "no ram without ram damage");
            Assert.IsFalse(_plane.IsAlive && _other.IsAlive, "never touched");
        }

        static float TotalLost(PlaneHealth health)
        {
            float lost = 0f;
            for (int i = 0; i < PlanePart.TypeCount; i++)
            {
                var part = health.GetPart((PlanePartType)i);
                lost += part.MaxHealth - part.Health;
            }
            return lost;
        }
    }
}

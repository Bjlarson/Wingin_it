using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Dev;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Target drones: tow runs ahead of the player and breaking when shot.</summary>
    public class DummyPilotTests
    {
        DummyPilot _drone;
        PlaneController _plane;
        PlaneHealth _health;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            Assert.IsNotEmpty(FlightTestScene.Dummies, "FlightTest scene has no target drones");
            _drone = FlightTestScene.Dummies[0];
            _drone.gameObject.SetActive(true); // flown by its own pilot this time
            _plane = _drone.GetComponent<PlaneController>();
            _health = _drone.GetComponent<PlaneHealth>();
            yield return new WaitForFixedUpdate();
        }

        [Test]
        public void EverySceneDrone_IsAnEnemyOfThePlayer()
        {
            foreach (var drone in FlightTestScene.Dummies)
                Assert.IsFalse(drone.GetComponent<PlaneController>().IsAllyOf(FlightTestScene.Player), drone.name);
        }

        [UnityTest]
        public IEnumerator TowRun_HoldsHeadingAndAltitude_ThenReturnsToPattern()
        {
            var start = new Vector3(0f, 500f, -1200f);
            _health.ApplyDamage(PlanePartType.Body, 10f, start, FlightTestScene.Player); // a tow run starts fresh
            _drone.StartTowRun(start, Vector3.right, duration: 3f);

            Assert.IsTrue(_drone.IsOnTowRun);
            Assert.AreEqual(_health.GetPart(PlanePartType.Body).MaxHealth, _health.GetPart(PlanePartType.Body).Health, 0.01f);
            Assert.Less(Vector3.Distance(start, _plane.Position), 0.01f);

            for (float t = 0f; t < 2.5f; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                Vector3 forward = _plane.Rotation * Vector3.forward;
                forward.y = 0f;
                Assert.Less(Vector3.Angle(forward, Vector3.right), 5f, $"drifted off the tow heading at {t:0.0}s");
                Assert.AreEqual(start.y, _plane.Position.y, 20f, $"drifted off the tow altitude at {t:0.0}s");
            }

            yield return new WaitForSeconds(1f);
            Assert.IsFalse(_drone.IsOnTowRun);
        }

        [UnityTest]
        public IEnumerator TowRun_CancelsAPendingRespawn()
        {
            _plane.Crash(_plane.Position);
            yield return null;
            Assert.IsFalse(_plane.IsAlive);

            var start = new Vector3(0f, 500f, -1200f);
            _drone.StartTowRun(start, Vector3.forward, duration: 10f);
            Assert.IsTrue(_plane.IsAlive);

            // The old respawn would have teleported it back to its spawn point.
            yield return new WaitForSeconds(_drone.GetComponent<PlaneRespawner>().RespawnDelay + 0.5f);
            // Still on the tow line (every drone spawn point is well off it), whatever the class's speed.
            Vector3 p = _plane.Position;
            Assert.IsTrue(Mathf.Abs(p.x - start.x) < 20f && Mathf.Abs(p.y - start.y) < 20f && p.z > start.z,
                $"the drone was respawned at its spawn point (it's at {p})");
        }

        [UnityTest]
        public IEnumerator Evasive_BreaksWhenAnotherPlaneHitsIt()
        {
            _drone.EvadeWhenHit = true;
            var part = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(part, 1f, part.transform.position, FlightTestScene.Player);
            Assert.IsTrue(_drone.IsEvading);

            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_plane.CurrentInput.Boost, "a break uses boost");
        }

        [Test]
        public void Evasive_IgnoresHitsWithNoAttacker()
        {
            _drone.EvadeWhenHit = true;
            var part = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(part, 1f, part.transform.position);
            Assert.IsFalse(_drone.IsEvading);
        }

        [Test]
        public void Calm_NeverBreaks()
        {
            _drone.EvadeWhenHit = false;
            var part = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(part, 1f, part.transform.position, FlightTestScene.Player);
            Assert.IsFalse(_drone.IsEvading);
        }

        [Test]
        public void TowRun_IgnoresHits()
        {
            _drone.EvadeWhenHit = true;
            _drone.StartTowRun(new Vector3(0f, 500f, -1200f), Vector3.forward, duration: 10f);
            var part = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(part, 1f, part.transform.position, FlightTestScene.Player);
            Assert.IsFalse(_drone.IsEvading);
        }
    }
}

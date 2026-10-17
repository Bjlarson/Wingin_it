using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Dev;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>
    /// <see cref="BotPilot"/>: flying orders through the same input path as a player. A target drone is turned into a
    /// bot (its <see cref="DummyPilot"/> switched off); runs at 4× speed.
    /// </summary>
    public class BotPilotTests
    {
        BotPilot _bot;
        PlaneController _plane;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = Drone("Fighter");
            _plane.GetComponent<DummyPilot>().enabled = false;
            _bot = _plane.gameObject.AddComponent<BotPilot>();
            _plane.gameObject.SetActive(true);
            _plane.Respawn(new Vector3(0f, 500f, -1500f), Quaternion.identity);
            yield return new WaitForFixedUpdate();
            Time.timeScale = 4f;
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        static PlaneController Drone(string className)
        {
            var dummy = FlightTestScene.Dummies.FirstOrDefault(d => d.GetComponent<PlaneController>().Stats.displayName == className);
            Assert.IsNotNull(dummy, $"no {className} drone in the FlightTest scene");
            return dummy.GetComponent<PlaneController>();
        }

        /// <summary>Steps physics until <paramref name="done"/> or <paramref name="seconds"/> of game time pass.</summary>
        static IEnumerator Fly(float seconds, Func<bool> done = null)
        {
            for (float t = 0f; t < seconds && (done == null || !done()); t += Time.fixedDeltaTime)
                yield return new WaitForFixedUpdate();
        }

        [Test]
        public void StartsCruising()
        {
            Assert.AreEqual(BotManeuver.Cruise, _bot.Maneuver);
            Assert.Contains(_bot, BotPilot.All);
        }

        [UnityTest]
        public IEnumerator Cruise_HoldsHeadingAndAltitude_AtTheOrderedSpeed()
        {
            float speed = _bot.CruiseSpeed + 15f;
            _bot.Cruise(speed);
            yield return Fly(12f);

            Assert.IsTrue(_plane.IsAlive);
            Vector3 forward = Vector3.ProjectOnPlane(_plane.Rotation * Vector3.forward, Vector3.up);
            Assert.Less(Vector3.Angle(forward, Vector3.forward), 5f, "drifted off heading");
            Assert.AreEqual(500f, _plane.Position.y, 25f, "drifted off altitude");
            Assert.AreEqual(speed, _plane.Speed, 4f, "didn't settle on the ordered speed");
        }

        [UnityTest]
        public IEnumerator FlyTo_ReachesAPointBehindAndBelow_ThenOrbitsIt()
        {
            var point = new Vector3(800f, 400f, -2200f); // behind and to the right: needs a full turn
            _bot.FlyTo(point);
            yield return Fly(40f, () => _bot.HasArrived);

            Assert.IsTrue(_plane.IsAlive);
            Assert.IsTrue(_bot.HasArrived, $"didn't arrive (at {_plane.Position})");
            Assert.AreEqual(BotManeuver.Orbit, _bot.Maneuver);

            yield return Fly(20f);
            Vector3 flat = _plane.Position - point;
            flat.y = 0f;
            Assert.Less(flat.magnitude, 600f, "wandered off the orbit");
            Assert.AreEqual(point.y, _plane.Position.y, 40f, "not orbiting at the point's altitude");
        }

        [UnityTest]
        public IEnumerator FlyTo_WithALostWing_StillGetsThere()
        {
            var health = _plane.GetComponent<PlaneHealth>();
            var wing = health.GetPart(PlanePartType.WingLeft);
            health.ApplyDamage(wing, wing.MaxHealth * 2f, wing.transform.position);
            Assert.IsTrue(wing.IsDestroyed);

            _bot.FlyTo(new Vector3(-600f, 550f, 300f));
            yield return Fly(45f, () => _bot.HasArrived || !_plane.IsAlive);

            Assert.IsTrue(_plane.IsAlive, "crashed flying one-winged");
            Assert.IsTrue(_bot.HasArrived, $"didn't make it one-winged (at {_plane.Position})");
        }

        [UnityTest]
        public IEnumerator Pursue_ClosesOnAnOrbitingTarget_AndGetsAGunSolution()
        {
            var target = Drone("Heavy Fighter");
            target.gameObject.SetActive(true);
            _bot.Pursue(target);

            bool solution = false;
            float closest = float.MaxValue;
            yield return Fly(45f, () =>
            {
                solution |= _bot.HasGunSolution;
                closest = Mathf.Min(closest, Vector3.Distance(_plane.Position, target.Position));
                return solution && closest < 300f;
            });

            Assert.IsTrue(_plane.IsAlive);
            Assert.IsTrue(solution, "never got the target in gun range");
            Assert.Less(closest, 300f, "never closed in");
        }

        [UnityTest]
        public IEnumerator Triggers_FireTheWeapons_ThroughTheInput()
        {
            _bot.FireGuns = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_plane.CurrentInput.FireGuns);
            Assert.IsTrue(_plane.GetComponent<PlaneGuns>().IsFiring);

            _bot.ReleaseButtons();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_plane.CurrentInput.FireGuns);
        }

        [UnityTest]
        public IEnumerator Pursue_TargetDies_FallsBackToCruise()
        {
            var target = Drone("Heavy Fighter");
            target.gameObject.SetActive(true);
            _bot.Pursue(target);
            yield return new WaitForFixedUpdate();

            target.Crash(target.Position);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.AreEqual(BotManeuver.Cruise, _bot.Maneuver);
            Assert.IsNull(_bot.Target);
        }

        [UnityTest]
        public IEnumerator FormUp_HoldsTheSlotOnALeader()
        {
            // Leader: the Support drone on its slow orbit. Slot: 60 m behind and 60 m left.
            var leader = Drone("Support");
            leader.gameObject.SetActive(true);
            var offset = new Vector3(-60f, 0f, -60f);
            _plane.Respawn(BotSteering.SlotPosition(leader.Position, leader.Rotation, new Vector3(-150f, 30f, -300f)), leader.Rotation);
            _bot.FormUp(leader, offset);

            yield return Fly(20f);
            float worst = 0f;
            for (float t = 0f; t < 10f; t += Time.fixedDeltaTime)
            {
                worst = Mathf.Max(worst, Vector3.Distance(_plane.Position,
                    BotSteering.SlotPosition(leader.Position, leader.Rotation, offset)));
                yield return new WaitForFixedUpdate();
            }

            Assert.IsTrue(_plane.IsAlive);
            Assert.Less(worst, 80f, "couldn't hold the formation slot");
        }

        [UnityTest]
        public IEnumerator Crash_LetsGoOfTheButtons()
        {
            _bot.FireGuns = _bot.Boost = true;
            yield return new WaitForFixedUpdate();
            _plane.Crash(_plane.Position);
            Assert.IsFalse(_bot.FireGuns);
            Assert.IsFalse(_bot.Boost);
        }
    }
}

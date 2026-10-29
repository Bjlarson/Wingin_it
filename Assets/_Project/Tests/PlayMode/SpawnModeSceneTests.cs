using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The level's spawn mode: air spawns over the home airfield, or parked on its runway for a takeoff.</summary>
    public class SpawnModeSceneTests
    {
        PlaneController _plane;
        PlaneRespawner _respawner;
        LandingGear _gear;
        Airfield _home;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _respawner = _plane.GetComponent<PlaneRespawner>();
            _gear = _plane.GetComponent<LandingGear>();
            _home = Airfield.NearestFriendly(_plane.Position, _plane.Team);
            Assert.IsNotNull(LevelSettings.Current, "FlightTest has no LevelSettings");
        }

        void UseRunwaySpawns() => LevelSettings.Current.SpawnMode = SpawnMode.Runway;

        [Test]
        public void FlightTestLevel_AirSpawns()
        {
            Assert.AreEqual(SpawnMode.Air, LevelSettings.CurrentSpawnMode);
            _respawner.SpawnPoseFor(_plane.Position, out bool onRunway);
            Assert.IsFalse(onRunway);
        }

        [UnityTest]
        public IEnumerator RunwayLevel_RespawnsParkedBesideTheRunway_GearLocked()
        {
            UseRunwaySpawns();
            _plane.Crash(_plane.Position);
            yield return new WaitForSeconds(_respawner.RespawnDelay + 0.3f);

            Assert.IsTrue(_plane.IsAlive);
            Assert.IsTrue(_plane.IsGrounded, "on its wheels");
            Assert.IsTrue(_gear.IsLocked, "gear down and locked");
            Assert.Less(_plane.Speed, 0.01f, "parked");
            Assert.IsFalse(_home.IsOverRunway(_plane.Position), "beside the home runway, not on it");
            Vector3 flat = Vector3.ProjectOnPlane(_home.RunwaySpawnSlot(0).position - _plane.Position, Vector3.up);
            Assert.Less(flat.magnitude, 1f, "in the first takeoff slot");
            Assert.Less(Vector3.Angle(_plane.Rotation * Vector3.forward, _home.RunwayDirection), 1f, "facing down the runway");
        }

        [UnityTest]
        public IEnumerator RunwaySpawn_StaysParkedAtIdle_InsteadOfTaxiingToTheRepairZone()
        {
            UseRunwaySpawns();
            _respawner.SpawnNow();
            Vector3 start = _plane.Position;
            yield return RunwayLanding.Fly(_plane, default, 3f);

            Assert.IsTrue(_plane.IsAlive);
            Assert.IsTrue(_plane.IsGrounded);
            Assert.IsTrue(_plane.IsAutoBraking, "brake holds it");
            Assert.Less(Vector3.Distance(start, _plane.Position), 0.5f, "didn't move");
            Assert.IsFalse(_plane.GetComponent<PlaneRepair>().IsRepairing);
        }

        [UnityTest]
        public IEnumerator RunwaySpawn_ThrottleUpAndPullUp_TakesOff_AndTheGearRetracts()
        {
            UseRunwaySpawns();
            _respawner.SpawnNow();
            yield return TakeOff(new FlightInput { Throttle = 1f });

            Assert.IsTrue(_plane.IsAlive, "crashed on takeoff");
            Assert.IsFalse(_plane.IsGrounded, $"never lifted off (speed {_plane.Speed:0.0}, throttle {_plane.Throttle:0.00})");
            Assert.Greater(_gear.HeightAboveGround, _gear.RetractHeight, "climbed out");
            yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = 1f }, _gear.RetractTime + 0.3f);
            Assert.AreEqual(GearState.Up, _gear.State);
        }

        [UnityTest]
        public IEnumerator RunwaySpawn_TouchBoostTakeoff_KeepsCruiseThrottle_AfterLiftoff()
        {
            UseRunwaySpawns();
            _respawner.SpawnNow();
            // Touch: auto-throttle, no throttle control. Boost releases the brake and starts the takeoff run.
            yield return RunwayLanding.Fly(_plane, new FlightInput { AutoThrottle = true, Boost = true }, 1f);
            yield return TakeOff(new FlightInput { AutoThrottle = true });

            Assert.IsTrue(_plane.IsAlive, "crashed on takeoff");
            Assert.IsFalse(_plane.IsGrounded, "never lifted off");
            Assert.Greater(_plane.Throttle, 0.3f, "auto-throttle didn't pull back to idle on the climb-out");
        }

        [UnityTest]
        public IEnumerator RunwaySpawn_TakesTheNextSlot_WhenAPlaneIsWaitingInTheFirst()
        {
            var other = FlightTestScene.Dummies[0];
            other.enabled = false; // parked, not flying its pattern
            other.gameObject.SetActive(true);
            var otherPlane = other.GetComponent<PlaneController>();
            yield return null;
            Pose first = _home.RunwaySpawnSlot(0);
            otherPlane.SpawnOnGround(first.position, first.rotation);

            UseRunwaySpawns();
            _respawner.SpawnNow();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(_plane.IsGrounded);
            Vector3 second = _home.RunwaySpawnSlot(1).position;
            Assert.Less(Vector3.ProjectOnPlane(second - _plane.Position, Vector3.up).magnitude, 1f, "in slot 1");
            Assert.Greater(Vector3.Distance(_plane.Position, otherPlane.Position), Airfield.RunwaySlotSpacing - 1f);
        }

        [UnityTest]
        public IEnumerator DevKeyFlip_SpawnNowSwitchesBetweenAirAndRunway()
        {
            UseRunwaySpawns();
            _respawner.SpawnNow();
            Assert.IsTrue(_plane.IsGrounded);

            LevelSettings.Current.SpawnMode = SpawnMode.Air;
            _respawner.SpawnNow();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_plane.IsGrounded);
            Assert.AreEqual(GearState.Up, _gear.State);
            Assert.Less(Vector3.Distance(_plane.Position, _home.AirSpawn.position), 5f);
        }

        /// <summary>Rolls with <paramref name="input"/> until flying speed, then pulls up and climbs (max 30 s).</summary>
        IEnumerator TakeOff(FlightInput input)
        {
            var stats = _plane.Stats;
            _plane.SetInput(input);
            yield return new WaitForFixedUpdate(); // let the gear measure its height after the spawn
            for (float t = 0f; t < 30f && _plane.IsAlive && _gear.HeightAboveGround <= _gear.RetractHeight + 5f; t += Time.fixedDeltaTime)
            {
                var step = input;
                step.Pitch = _plane.Speed >= stats.stallSpeed + 2f
                    ? Mathf.Clamp((12f - RunwayLanding.Pitch(_plane)) * 0.1f, -1f, 1f) // hold ~12° nose up
                    : 0f;
                _plane.SetInput(step);
                yield return new WaitForFixedUpdate();
            }
        }
    }
}

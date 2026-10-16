using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The FlightTest scene's home airfield: ownership, layout, and respawning over it.</summary>
    public class AirfieldSceneTests
    {
        PlaneController _plane;
        Airfield _home;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _home = Airfield.NearestFriendly(_plane.transform.position, _plane.Team);
            Assert.IsNotNull(_home, "the player's team has no airfield");
        }

        [Test]
        public void PlayerTeam_HasAHomeAirfield_WithSpawnsAndARepairZone()
        {
            Assert.AreEqual(1, Airfield.All.Count);
            Assert.IsTrue(_home.IsHome);
            Assert.IsNotNull(_home.AirSpawn);
            Assert.IsNotNull(_home.RunwaySpawn);
            Assert.IsNotNull(_home.RepairZone);
            Assert.IsNull(Airfield.NearestFriendly(_plane.transform.position, 1), "the dummies' team has no airfield here");
        }

        [Test]
        public void RepairZone_IsOnTheRunway_AndTheRunwayIsSolid()
        {
            Vector3 zoneCenter = _home.RepairZone.transform.position;
            Assert.IsTrue(_home.IsOverRunway(zoneCenter + Vector3.up));

            Assert.IsTrue(Physics.Raycast(zoneCenter + Vector3.up * 50f, Vector3.down, out var hit, 100f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore));
            Assert.AreEqual("Runway", hit.collider.name);
            Assert.AreEqual(_home.transform.position.y + 0.1f, hit.point.y, 0.01f, "runway surface height");
            Assert.IsTrue(_home.RepairZone.Contains(hit.point + Vector3.up * 3f), "a plane parked on the runway is inside");
        }

        [Test]
        public void SpawnPoints_AreOnTheApproach_HeadingDownTheRunway()
        {
            foreach (var spawn in new[] { _home.AirSpawn, _home.RunwaySpawn })
            {
                Vector3 local = _home.transform.InverseTransformPoint(spawn.position);
                Assert.Less(local.z, -_home.RunwaySize.y * 0.4f, $"{spawn.name} is at the approach end");
                Assert.Less(Vector3.Angle(spawn.forward, _home.RunwayDirection), 1f, $"{spawn.name} faces down the runway");
            }
            Assert.IsTrue(_home.IsOverRunway(_home.RunwaySpawn.position), "runway spawn is on the runway");
            Assert.Greater(_home.AirSpawn.position.y, 200f, "air spawn is well clear of the ground");
        }

        [UnityTest]
        public IEnumerator Player_RespawnsOverTheHomeAirfield()
        {
            var respawner = _plane.GetComponent<PlaneRespawner>();
            Pose pose = respawner.SpawnPoseFor(_plane.transform.position, out bool onRunway);
            Assert.IsFalse(onRunway, "the FlightTest level air-spawns");
            Assert.AreEqual(_home.AirSpawn.position, pose.position);

            _plane.Respawn(new Vector3(800f, 400f, 600f), Quaternion.identity);
            _plane.Crash(_plane.transform.position);
            yield return new WaitForSeconds(respawner.RespawnDelay + 0.3f);

            Assert.IsTrue(_plane.IsAlive);
            Assert.Less(Vector3.Distance(_plane.transform.position, _home.AirSpawn.position), 60f,
                "respawned at the airfield's air spawn (plus a moment of flight)");
        }
    }
}

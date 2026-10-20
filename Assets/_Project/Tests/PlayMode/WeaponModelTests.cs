using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>The weapons pack (ArtSource/Weapons) replaces the placeholder boxes on what the plane and the AA guns fire.</summary>
    public class WeaponModelTests
    {
        static readonly Vector3 Start = new Vector3(0f, 600f, -1500f);

        PlaneController _plane;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _plane.SetStats(FlightTestScene.FindClass(c => c.displayName == "Heavy Fighter")); // guns, rockets, bombs
            _plane.Respawn(Start, Quaternion.identity);
            yield return null;
        }

        static MeshFilter[] Meshes(string rootName, string mesh) =>
            GameObject.Find(rootName) is { } root
                ? root.GetComponentsInChildren<MeshFilter>().Where(f => f.sharedMesh != null && f.sharedMesh.name == mesh).ToArray()
                : new MeshFilter[0];

        IEnumerator Hold(FlightInput input, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                _plane.SetInput(input);
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator Rockets_FlyTheRocketModel_AtTheirStatsLength()
        {
            bool fired = false;
            _plane.GetComponent<PlaneRockets>().Fired += _ => fired = true;
            yield return Hold(new FlightInput { FireRockets = true }, 0.3f);
            Assert.IsTrue(fired, "rockets fired");

            var rockets = Meshes($"{_plane.name} Rockets", "Rocket");
            Assert.IsNotEmpty(rockets, "rockets use the rocket model");
            Assert.IsFalse(rockets[0].GetComponent<Collider>(), "no collider for a sweep to hit");
            float tip = rockets[0].sharedMesh.bounds.max.z * rockets[0].transform.lossyScale.z;
            Assert.AreEqual(_plane.Stats.rockets.rocketLength * 0.5f, tip, 0.01f, "nose tip at half the rocket's length");
        }

        [UnityTest]
        public IEnumerator Bombs_DropTheBombModel()
        {
            yield return Hold(new FlightInput { DropBomb = true }, 0.3f);
            Assert.IsNotEmpty(Meshes($"{_plane.name} Bombs", "Bomb"), "bombs use the bomb model");
        }

        [UnityTest]
        public IEnumerator Guns_FireTracerModels_WithAMuzzleFlash()
        {
            bool flashed = false;
            float end = Time.time + 0.5f;
            while (Time.time < end)
            {
                _plane.SetInput(new FlightInput { FireGuns = true });
                yield return null;
                flashed |= _plane.GetComponentsInChildren<Transform>().Any(t => t.name == "Muzzle Flash");
            }
            Assert.IsNotEmpty(Meshes($"{_plane.name} Bullets", "Tracer"), "tracers use the tracer model");
            Assert.IsTrue(flashed, "a muzzle flash shows while firing");

            yield return Hold(default, 0.2f);
            Assert.IsFalse(_plane.GetComponentsInChildren<Transform>().Any(t => t.name == "Muzzle Flash"), "flashes go out");
        }

        [Test]
        public void AAGuns_HaveMuzzleFlashes()
        {
            Assert.IsNotEmpty(FlightTestScene.AAGuns);
            foreach (var gun in FlightTestScene.AAGuns)
                Assert.AreEqual(2, gun.GetComponentsInChildren<Transform>(true).Count(t => t.name == "Muzzle Flash"),
                    $"{gun.name} has a flash per barrel");
        }
    }
}

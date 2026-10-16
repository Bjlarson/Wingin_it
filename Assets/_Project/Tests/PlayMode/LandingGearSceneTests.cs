using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The player's automatic landing gear in the FlightTest scene: a low pass over the home runway.</summary>
    public class LandingGearSceneTests
    {
        PlaneController _plane;
        LandingGear _gear;
        Airfield _home;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _gear = _plane.GetComponent<LandingGear>();
            Assert.IsNotNull(_gear, "Player Plane has no LandingGear");
            _home = Airfield.NearestFriendly(_plane.transform.position, _plane.Team);
        }

        /// <summary>Starts a shallow descent <paramref name="height"/> m over the start of the home runway.</summary>
        void StartApproach(float height, float noseDown)
        {
            Vector3 start = _home.transform.TransformPoint(new Vector3(0f, height, -_home.RunwaySize.y * 0.45f));
            _plane.Respawn(start, Quaternion.LookRotation(_home.RunwayDirection) * Quaternion.Euler(noseDown, 0f, 0f));
        }

        IEnumerator Fly(FlightInput input, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.fixedDeltaTime)
            {
                _plane.SetInput(input);
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator Cruising_GearStaysUp()
        {
            yield return Fly(default, 1f);
            Assert.Greater(_gear.HeightAboveGround, _gear.RetractHeight);
            Assert.AreEqual(GearState.Up, _gear.State);
        }

        [UnityTest]
        public IEnumerator LowDescent_DeploysAndLocks_ThenClimbRetracts()
        {
            StartApproach(25f, 4f);
            yield return Fly(default, 0.3f);
            Assert.AreEqual(GearState.Deploying, _gear.State);
            Assert.Less(_gear.HeightAboveGround, _gear.DeployHeight);
            Assert.AreEqual(_plane.Stats.maxSpeed * _gear.GearMaxSpeed, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.01f,
                "deployed gear adds drag");

            yield return Fly(default, _gear.DeployTime);
            Assert.IsTrue(_plane.IsAlive, "the low pass shouldn't hit anything");
            Assert.AreEqual(GearState.Locked, _gear.State);
            var leg = _plane.transform.Find("Model/Body/Main Gear L");
            Assert.IsNotNull(leg, "plane has a left main gear leg");
            yield return null; // visuals update in LateUpdate
            Assert.IsTrue(leg.gameObject.activeInHierarchy, "wheels are visible");

            yield return Fly(new FlightInput { Pitch = 0.5f }, 1.5f);
            yield return Fly(default, _gear.RetractTime + 1f);
            Assert.Greater(_gear.HeightAboveGround, _gear.RetractHeight);
            Assert.AreEqual(GearState.Up, _gear.State);
            Assert.AreEqual(_plane.Stats.maxSpeed, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.01f, "drag gone");
            yield return null;
            Assert.IsFalse(leg.gameObject.activeInHierarchy, "wheels stowed");
        }

        [UnityTest]
        public IEnumerator Respawn_PutsTheGearUp()
        {
            StartApproach(25f, 4f);
            yield return Fly(default, _gear.DeployTime + 0.3f);
            Assert.AreEqual(GearState.Locked, _gear.State);

            _plane.Respawn(_home.AirSpawn.position, _home.AirSpawn.rotation);
            Assert.AreEqual(GearState.Up, _gear.State);
            Assert.AreEqual(_plane.Stats.maxSpeed, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.01f);
        }
    }
}

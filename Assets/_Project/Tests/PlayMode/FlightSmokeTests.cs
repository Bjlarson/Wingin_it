using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// Flies the real FlightTest scene with scripted inputs to catch sign errors and instability.
    /// Player input is disabled so the test fully owns the plane's FlightInput.
    /// </summary>
    public class FlightSmokeTests
    {
        PlaneController _plane;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
        }

        IEnumerator Fly(FlightInput input, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                _plane.SetInput(input);
                yield return new WaitForFixedUpdate();
            }
        }

        [UnityTest]
        public IEnumerator HandsOff_StaysLevelAndAirborne()
        {
            float startAlt = _plane.transform.position.y;
            yield return Fly(default, 6f);
            Assert.IsTrue(_plane.IsAlive);
            Assert.AreEqual(startAlt, _plane.transform.position.y, 40f, "hands-off flight should hold altitude roughly");
            Assert.Greater(_plane.transform.up.y, 0.95f, "hands-off flight should stay wings-level");
        }

        [UnityTest]
        public IEnumerator RollReleased_AutoLevels()
        {
            yield return Fly(new FlightInput { Roll = 1f }, 0.4f);
            Assert.Less(_plane.transform.up.y, 0.9f, "roll input should bank the plane");
            yield return Fly(default, 4f);
            Assert.Greater(_plane.transform.up.y, 0.95f, "plane should roll back to level");
        }

        [UnityTest]
        public IEnumerator HoldStyle_RollReleased_StaysBanked()
        {
            yield return Fly(new FlightInput { Roll = 1f, Style = ControlStyle.Hold }, 0.4f);
            Assert.Less(_plane.transform.up.y, 0.9f, "roll input should bank the plane");
            // The roll rate dies away at the class's responsiveness, so the plane rolls on a little past where the
            // stick let go (~30° for the Fighter) before it settles: measure the bank once it has.
            yield return Fly(new FlightInput { Style = ControlStyle.Hold }, 1f);
            float banked = _plane.transform.up.y;
            Assert.Less(banked, 0.9f, "still banked once the roll stops");
            yield return Fly(new FlightInput { Style = ControlStyle.Hold }, 2f);
            Assert.AreEqual(banked, _plane.transform.up.y, 0.1f, "released stick holds the bank instead of leveling");
        }

        [UnityTest]
        public IEnumerator BankStyle_StickLeft_BanksAndTurnsLeft_CenterLevels()
        {
            Vector3 startFwd = _plane.transform.forward;
            yield return Fly(new FlightInput { Roll = -1f, Style = ControlStyle.Bank }, 3f);
            Assert.Less(_plane.transform.up.y, 0.6f, "holding the stick left holds a bank");
            Assert.Greater(_plane.transform.up.y, 0.3f, "but not a roll over: the bank angle is capped");
            float signedTurn = Vector3.SignedAngle(Flat(startFwd), Flat(_plane.transform.forward), Vector3.up);
            Assert.Less(signedTurn, -10f, "stick left turns left");
            yield return Fly(new FlightInput { Style = ControlStyle.Bank }, 3f);
            Assert.Greater(_plane.transform.up.y, 0.95f, "centered stick levels the wings");
        }

        [UnityTest]
        public IEnumerator PitchUp_Climbs_AndBleedsSpeed()
        {
            float startAlt = _plane.transform.position.y;
            float startSpeed = _plane.Speed;
            yield return Fly(new FlightInput { Pitch = 1f }, 0.6f);
            yield return Fly(default, 1.5f);
            Assert.Greater(_plane.transform.position.y, startAlt + 5f);
            Assert.Less(_plane.Speed, startSpeed);
        }

        [UnityTest]
        public IEnumerator RollRight_TurnsRight()
        {
            Vector3 startFwd = _plane.transform.forward;
            yield return Fly(new FlightInput { Roll = 1f }, 0.3f);
            yield return Fly(new FlightInput { Pitch = 0.6f, Roll = 0f }, 2f);
            float signedTurn = Vector3.SignedAngle(Flat(startFwd), Flat(_plane.transform.forward), Vector3.up);
            Assert.Greater(signedTurn, 10f, "banking right and pulling should turn right");
        }

        [UnityTest]
        public IEnumerator Boost_DrainsMeterAndSpeedsUp()
        {
            float startSpeed = _plane.Speed;
            yield return Fly(new FlightInput { Boost = true }, 1.5f);
            Assert.IsTrue(_plane.IsBoosting);
            Assert.Less(_plane.BoostMeter, 0.8f);
            Assert.Greater(_plane.Speed, startSpeed + 10f);
        }

        [UnityTest]
        public IEnumerator DiveIntoGround_Crashes_ThenRespawns()
        {
            bool crashed = false;
            _plane.Crashed += (_, _) => crashed = true;
            _plane.Respawn(new Vector3(0f, 120f, 0f), Quaternion.LookRotation(Vector3.down, Vector3.forward));
            float timeout = Time.time + 5f;
            while (!crashed && Time.time < timeout)
                yield return Fly(default, 0.05f);
            Assert.IsTrue(crashed, "diving into the ground should crash");
            Assert.IsFalse(_plane.IsAlive);
            Assert.Greater(_plane.transform.position.y, -1f, "plane should stop at the ground, not tunnel through it");
            Assert.AreEqual(DeathCause.Collision, _plane.LastDeathCause);

            yield return new WaitForSeconds(_plane.GetComponent<PlaneRespawner>().RespawnDelay + 0.5f);
            Assert.IsTrue(_plane.IsAlive, "plane should respawn");
        }

        [UnityTest]
        public IEnumerator OutsideBoundary_TurnsBack()
        {
            var boundary = FlightBoundary.Active;
            Assert.IsNotNull(boundary);
            // Put the plane just outside, heading further out.
            var pos = boundary.transform.position + new Vector3(boundary.radius + 50f, 600f, 0f);
            _plane.Respawn(pos, Quaternion.LookRotation(Vector3.right));
            yield return Fly(default, 12f);
            Assert.IsTrue(_plane.IsAlive);
            Assert.Less(Vector3.Dot(_plane.transform.forward, Vector3.right), 0f, "plane should have turned back toward the center");
        }

        static Vector3 Flat(Vector3 v) => Vector3.ProjectOnPlane(v, Vector3.up).normalized;
    }
}

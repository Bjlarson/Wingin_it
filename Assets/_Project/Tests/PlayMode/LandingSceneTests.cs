using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Touchdowns on the home runway in the FlightTest scene: clean landing, rollout, takeoff, bounces and crashes.</summary>
    public class LandingSceneTests
    {
        PlaneController _plane;
        LandingGear _gear;
        PlaneHealth _health;
        Airfield _home;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _gear = _plane.GetComponent<LandingGear>();
            _health = _plane.GetComponent<PlaneHealth>();
            _home = Airfield.NearestFriendly(_plane.transform.position, _plane.Team);
            Assert.IsNotNull(_home, "no friendly airfield in the scene");
        }

        /// <summary>
        /// Puts the plane <paramref name="height"/> m over the start of the home runway, heading down it, with the
        /// gear already locked (unless <paramref name="gearDown"/> is false).
        /// </summary>
        void StartApproach(float height, float noseDown = 2f, float bank = 0f, bool gearDown = true, float? throttle = null, float ahead = 0f)
        {
            Vector3 start = _home.transform.TransformPoint(new Vector3(0f, height, -_home.RunwaySize.y * 0.45f + ahead));
            _plane.Respawn(start, Quaternion.LookRotation(_home.RunwayDirection) * Quaternion.Euler(noseDown, 0f, bank), throttle);
            if (gearDown)
                _gear.Snap(true);
        }

        float Pitch => RunwayLanding.Pitch(_plane);

        IEnumerator FlyToTouchdown(float verticalSpeed, float throttle, float bank = 0f, float timeout = 12f) =>
            RunwayLanding.FlyToTouchdown(_plane, verticalSpeed, throttle, bank, timeout);

        IEnumerator Fly(FlightInput input, float seconds) => RunwayLanding.Fly(_plane, input, seconds);

        /// <summary>A clean landing, the approach starting <paramref name="ahead"/> m further down the runway.</summary>
        IEnumerator Land(float ahead = 0f)
        {
            StartApproach(20f, throttle: ThrottleCurve.IdleBand, ahead: ahead); // the lowest flying power, held down to the runway
            yield return FlyToTouchdown(-3f, throttle: 0f);
            Assert.AreEqual(TouchdownResult.Landed, _plane.LastTouchdown.Result,
                $"fault {_plane.LastTouchdown.Fault}: speed {_plane.LastTouchdown.Speed:0.0}, sink {_plane.LastTouchdown.SinkRate:0.0}");
        }

        [UnityTest]
        public IEnumerator CleanApproach_Lands_ThenRollsOutOnTheRunway()
        {
            yield return Land();
            Assert.IsTrue(_plane.IsGrounded);
            Assert.IsTrue(_plane.IsAlive);
            Assert.Less(_plane.LastTouchdown.SinkRate, _plane.LandingRules.bounceSinkRate);

            yield return Fly(new FlightInput { Throttle = -1f }, 2f);
            Assert.IsTrue(_plane.IsAlive);
            Assert.IsTrue(_plane.IsGrounded, "stays on the wheels");
            Assert.IsTrue(_plane.IsAutoBraking, "idle on the runway: the auto-brake has it");
            Assert.IsTrue(_home.IsOverRunway(_plane.Position, 5f), "still on the runway");
            Assert.AreEqual(GearState.Locked, _gear.State);
            Assert.IsTrue(LandingGear.ProbeGround(_plane.Position, out var ground));
            Assert.AreEqual(_gear.WheelClearance, ground.distance, 0.05f, "sitting on its wheels");
            Assert.AreEqual(0f, Pitch, 0.5f, "nose settles level");
        }

        IEnumerator FinalApproachAndLand() => RunwayLanding.FinalApproachAndLand(_plane, _home);

        [UnityTest]
        public IEnumerator LandingAssist_ByTheBookApproach_StaysOnTheGlidePath()
        {
            Vector3 start = _home.GlidePathPoint(800f);
            float slope = _home.GlideSlope.angle;
            _plane.Respawn(start, Quaternion.LookRotation(_home.RunwayDirection) * Quaternion.Euler(slope, 0f, 0f), throttle: ThrottleCurve.IdleBand);
            yield return new WaitForFixedUpdate();
            float sink = -_plane.Speed * Mathf.Sin(slope * Mathf.Deg2Rad);
            int checks = 0;
            for (float t = 0f; t < 10f; t += Time.fixedDeltaTime)
            {
                var guide = _home.GetApproach(_plane.Position, _plane.Velocity);
                Assert.IsTrue(guide.OnApproach, $"lost the approach {guide.Distance:0} m out");
                Assert.IsTrue(guide.OnPath, $"off the path {guide.Distance:0} m out: {guide.HeightError:0.0} m high, {guide.Lateral:0.0} m right");
                checks++;
                float pathAngle = Mathf.Asin(Mathf.Clamp(sink / Mathf.Max(1f, _plane.Speed), -1f, 1f)) * Mathf.Rad2Deg;
                _plane.SetInput(new FlightInput { Pitch = Mathf.Clamp((pathAngle - Pitch) * 0.08f, -1f, 1f), Throttle = 0f });
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(checks, 100);

            // Turning away from the runway ends the approach.
            _plane.Respawn(start, Quaternion.LookRotation(-_home.RunwayDirection));
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_home.GetApproach(_plane.Position, _plane.Velocity).OnApproach);
        }

        IEnumerator RollToAStop(float timeout = 25f) => RunwayLanding.RollToAStop(_plane, timeout);

        [UnityTest]
        public IEnumerator Landing_AutoBrakes_ToAStopInTheRepairZone()
        {
            yield return FinalApproachAndLand();
            Assert.AreEqual(0f, _plane.Throttle, "touchdown pulls the throttle to idle");
            float start = Time.time;
            yield return RollToAStop();
            Debug.Log($"Auto-brake: stopped {Time.time - start:0.0} s after touchdown");

            Assert.IsTrue(_plane.IsGrounded);
            Assert.IsTrue(_plane.IsAutoBraking, "holds the plane parked");
            var zone = _home.RepairZone;
            Assert.IsTrue(zone.Contains(_plane.Position), "stopped in the repair zone");
            Vector3 local = zone.transform.InverseTransformPoint(_plane.Position);
            Assert.AreEqual(0f, local.z, 3f, "on the zone's center cross");

            yield return Fly(default, 2f);
            Assert.AreEqual(0f, _plane.Speed, "stays parked");
        }

        [UnityTest]
        public IEnumerator LandedLong_StillStopsInTheRepairZone()
        {
            yield return Land(ahead: 110f); // starts late: touches down around the middle of the zone
            Assert.Greater(_home.RepairZone.transform.InverseTransformPoint(_plane.Position).z, -60f, "landed long");
            yield return RollToAStop();
            Assert.IsTrue(_home.RepairZone.Contains(_plane.Position), "brakes hard enough to stay in the zone");
        }

        [UnityTest]
        public IEnumerator Throttle_ReleasesTheAutoBrake()
        {
            yield return Land();
            yield return RollToAStop();
            yield return Fly(new FlightInput { Throttle = 1f }, 1f);
            Assert.IsFalse(_plane.IsAutoBraking);
            Assert.Greater(_plane.Speed, 1f, "rolls forward again");
        }

        [UnityTest]
        public IEnumerator NotOurRunway_AutoBrake_JustStops()
        {
            yield return FinalApproachAndLand();
            _plane.Team = 1; // the home airfield is now the enemy's: no repair zone to stop in
            float start = Time.time;
            yield return RollToAStop(10f);
            Assert.Less(Time.time - start, 7f, "brakes straight away");
            Assert.IsFalse(_home.RepairZone.Contains(_plane.Position), "stopped short of their repair zone");
        }

        [UnityTest]
        public IEnumerator Grounded_PitchUpAtFlyingSpeed_TakesOff_AndRetractsTheGear()
        {
            yield return Land();
            var takeoff = new FlightInput { Throttle = 1f, Pitch = 1f };
            for (float t = 0f; t < 10f && _plane.IsGrounded; t += Time.fixedDeltaTime)
                yield return Fly(takeoff, Time.fixedDeltaTime);
            Assert.IsFalse(_plane.IsGrounded, "lifted off");
            Assert.GreaterOrEqual(_plane.Speed, _plane.Stats.stallSpeed);

            float liftoffHeight = _plane.Position.y;
            yield return Fly(new FlightInput { Throttle = 1f, Pitch = 0.5f }, 4f);
            Assert.IsTrue(_plane.IsAlive);
            Assert.Greater(_plane.Position.y, liftoffHeight + 20f, "climbing away");
            yield return Fly(new FlightInput { Throttle = 1f }, _gear.RetractTime + 1f);
            Assert.AreNotEqual(GearState.Locked, _gear.State, "gear folds away after the climb");
        }

        [UnityTest]
        public IEnumerator HardSink_Bounces_WithBodyDamage()
        {
            StartApproach(14f);
            var body = _health.GetPart(PlanePartType.Body);
            yield return FlyToTouchdown(-10f, throttle: -1f);
            var td = _plane.LastTouchdown;
            Assert.AreEqual(TouchdownResult.Bounced, td.Result, $"fault {td.Fault}, sink {td.SinkRate:0.0}");
            Assert.AreEqual(TouchdownFault.HardSink, td.Fault);
            Assert.IsTrue(_plane.IsAlive);
            Assert.IsFalse(_plane.IsGrounded);
            Assert.AreEqual(body.MaxHealth * (1f - _plane.LandingRules.bounceDamage), body.Health, 0.01f);
            Assert.IsNull(_health.LastAttacker, "a bounce is self-inflicted: no kill credit");

            yield return Fly(default, 0.3f);
            Assert.Greater(_plane.Velocity.y, 0f, "hops back up");
        }

        [UnityTest]
        public IEnumerator TooFast_Bounces()
        {
            StartApproach(10f);
            yield return FlyToTouchdown(-2f, throttle: 1f);
            var td = _plane.LastTouchdown;
            Assert.Greater(td.Speed, _plane.LandingRules.MaxTouchdownSpeed(_plane.Stats.minSpeed));
            Assert.AreEqual(TouchdownResult.Bounced, td.Result, $"fault {td.Fault}");
            Assert.AreEqual(TouchdownFault.TooFast, td.Fault);
            Assert.IsTrue(_plane.IsAlive);
        }

        [UnityTest]
        public IEnumerator GearNotLocked_BellyLanding_Crashes()
        {
            StartApproach(5f, noseDown: 3f, gearDown: false);
            yield return FlyToTouchdown(-4f, throttle: -1f);
            Assert.IsFalse(_plane.IsAlive);
            Assert.AreEqual(DeathCause.Collision, _plane.LastDeathCause);
            Assert.AreEqual(TouchdownResult.Crashed, _plane.LastTouchdown.Result);
            Assert.AreEqual(TouchdownFault.GearNotLocked, _plane.LastTouchdown.Fault);
        }

        [UnityTest]
        public IEnumerator SteepBank_Crashes_BadAngle()
        {
            StartApproach(6f, noseDown: 3f, bank: 30f);
            yield return FlyToTouchdown(-4f, throttle: -1f, bank: 30f);
            Assert.IsFalse(_plane.IsAlive);
            Assert.AreEqual(TouchdownFault.BadAngle, _plane.LastTouchdown.Fault,
                $"roll {_plane.LastTouchdown.Roll:0}, pitch {_plane.LastTouchdown.Pitch:0}");
        }
    }
}

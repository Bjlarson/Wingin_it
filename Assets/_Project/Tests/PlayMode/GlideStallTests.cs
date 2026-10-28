using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Low power in the FlightTest scene: the idle glide, the flare, the stall, and a flared landing.</summary>
    public class GlideStallTests
    {
        PlaneController _plane;
        FlightStats _stats;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _stats = _plane.Stats;
        }

        float Pitch => RunwayLanding.Pitch(_plane);

        /// <summary>Level, high over the scene, at <paramref name="throttle"/>.</summary>
        void StartHigh(float throttle)
        {
            _plane.Respawn(_plane.Position + Vector3.up * 400f, Quaternion.LookRotation(Vector3.ProjectOnPlane(_plane.Rotation * Vector3.forward, Vector3.up)), throttle);
        }

        [UnityTest]
        public IEnumerator IdleThrottle_GlidesDownToJustUnderTheStall_EngineStillTurning()
        {
            StartHigh(_stats.cruiseThrottle);
            float startY = _plane.Position.y;
            yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = -1f }, 15f);
            Assert.IsTrue(_plane.IsAlive);
            Assert.AreEqual(0f, _plane.Throttle, "holding throttle down idles");

            float lowest = float.MaxValue, highest = 0f;
            for (float t = 0f; t < 5f; t += Time.fixedDeltaTime)
            {
                yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = -1f }, Time.fixedDeltaTime);
                lowest = Mathf.Min(lowest, _plane.Speed);
                highest = Mathf.Max(highest, _plane.Speed);
            }
            Debug.Log($"[Glide] idle speed {lowest:0.0}..{highest:0.0} (stall {_stats.stallSpeed}), lost {startY - _plane.Position.y:0} m");
            Assert.IsTrue(_plane.IsAlive);
            Assert.Greater(lowest, _stats.stallSpeed * 0.7f, "the idling engine keeps it moving");
            Assert.Less(lowest, _stats.stallSpeed, "settles just under the stall");
            Assert.Less(highest, _stats.minSpeed, "idle doesn't hold flying speed");
            Assert.Greater(startY - _plane.Position.y, 20f, "an idle glide loses height");
        }

        [UnityTest]
        public IEnumerator IdleThrottle_GlidesFartherThanTheOldFullDrag()
        {
            StartHigh(_stats.cruiseThrottle);
            while (_plane.Throttle > 0f) // the lever takes a moment to come down
                yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = -1f }, Time.fixedDeltaTime);
            float start = _plane.Speed;
            yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = -1f }, 2f);
            float lost = start - _plane.Speed;
            Debug.Log($"[Glide] idle lost {lost:0.0} m/s in 2 s from {start:0.0}");
            Assert.Less(lost, _stats.deceleration * 2f * 0.5f, "slows gently at idle instead of braking");
            Assert.Greater(lost, 0f);
        }

        [UnityTest]
        public IEnumerator Flare_AtLowPower_CostsSpeedAndHeight()
        {
            // Descending at the lowest flying power, as on a final approach.
            Vector3 start = _plane.Position + Vector3.up * 300f;
            Vector3 flat = Vector3.ProjectOnPlane(_plane.Rotation * Vector3.forward, Vector3.up);
            _plane.Respawn(start, Quaternion.LookRotation(flat) * Quaternion.Euler(3f, 0f, 0f), ThrottleCurve.IdleBand);
            yield return RunwayLanding.Fly(_plane, default, 0.5f);
            float speed0 = _plane.Speed, y0 = _plane.Position.y, highest = y0;

            // Idle, and hold the nose a few degrees up: the descent stops while the speed bleeds, then the plane settles.
            for (float t = 0f; t < 5f; t += Time.fixedDeltaTime)
            {
                float pitch = Mathf.Clamp((6f - Pitch) * 0.08f, -1f, 1f);
                yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = -1f, Pitch = pitch }, Time.fixedDeltaTime);
                highest = Mathf.Max(highest, _plane.Position.y);
            }
            Debug.Log($"[Glide] flare: speed {speed0:0.0} -> {_plane.Speed:0.0}, height {_plane.Position.y - y0:+0.0;-0.0} m (peak {highest - y0:+0.0} m), pitch {Pitch:0}");
            Assert.IsTrue(_plane.IsAlive);
            Assert.Less(_plane.Speed, speed0 - 4f, "a flare bleeds speed");
            Assert.Less(_plane.Position.y, y0 - 1f, "and the plane comes down");
            Assert.Less(highest - y0, 3f, "no ballooning back up on a flare");
        }

        [UnityTest]
        public IEnumerator Stall_HoldingBackStick_TheNoseDrops()
        {
            StartHigh(0f); // idle speed: already under the stall
            float y0 = _plane.Position.y, peak = Pitch;
            for (float t = 0f; t < 5f; t += Time.fixedDeltaTime)
            {
                yield return RunwayLanding.Fly(_plane, new FlightInput { Throttle = -1f, Pitch = 1f }, Time.fixedDeltaTime);
                peak = Mathf.Max(peak, Pitch);
            }
            Debug.Log($"[Glide] stall: peak pitch {peak:0}, end pitch {Pitch:0}, speed {_plane.Speed:0.0}, lost {y0 - _plane.Position.y:0} m");
            Assert.IsTrue(_plane.IsAlive);
            Assert.Less(peak, 60f, "a stalled wing can't haul the nose way up");
            Assert.Less(Pitch, peak - 10f, "the nose drops despite full back stick");
            Assert.Greater(y0 - _plane.Position.y, 10f, "a stall loses height");
        }

        [UnityTest]
        public IEnumerator FlaredLanding_IdleAndNoseUp_TouchesDownSlowerThanTheApproach()
        {
            var home = Airfield.NearestFriendly(_plane.transform.position, _plane.Team);
            const float distance = 500f, slope = 3f;
            Vector3 start = home.transform.TransformPoint(
                new Vector3(0f, distance * Mathf.Tan(slope * Mathf.Deg2Rad), -home.RunwaySize.y * 0.5f - distance));
            _plane.Respawn(start, Quaternion.LookRotation(home.RunwayDirection) * Quaternion.Euler(slope, 0f, 0f), ThrottleCurve.IdleBand);
            var gear = _plane.GetComponent<LandingGear>();
            gear.Snap(true);
            float approachSpeed = _plane.Speed;

            // Down the slope at the lowest flying power, then idle and ease the nose up over the last few metres.
            for (float t = 0f; t < 25f && _plane.IsAlive && !_plane.HasTouchedDown; t += Time.fixedDeltaTime)
            {
                bool flare = gear.HeightAboveGround < 6f;
                float speed = Mathf.Max(1f, _plane.Speed);
                float wanted = flare ? 6f : Mathf.Asin(-approachSpeed * Mathf.Sin(slope * Mathf.Deg2Rad) / speed) * Mathf.Rad2Deg;
                _plane.SetInput(new FlightInput
                {
                    Pitch = Mathf.Clamp((wanted - Pitch) * 0.08f, -1f, 1f),
                    Throttle = flare ? -1f : 0f,
                });
                yield return new WaitForFixedUpdate();
            }
            var td = _plane.LastTouchdown;
            Debug.Log($"[Glide] flared landing: {td.Result} {td.Fault}, speed {approachSpeed:0.0} -> {td.Speed:0.0}, sink {td.SinkRate:0.0}, pitch {td.Pitch:0.0}");
            Assert.IsTrue(_plane.HasTouchedDown, "never touched down");
            Assert.AreEqual(TouchdownResult.Landed, td.Result, $"fault {td.Fault}");
            Assert.Less(td.Speed, approachSpeed - 2f, "the flare bled off speed");
        }
    }
}

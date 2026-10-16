using System.Collections;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Flies a plane down to a runway and rolls it out, for the landing and repair scene tests.</summary>
    public static class RunwayLanding
    {
        public static float Pitch(PlaneController plane) =>
            Mathf.Asin(Mathf.Clamp((plane.Rotation * Vector3.forward).y, -1f, 1f)) * Mathf.Rad2Deg;

        public static float Bank(PlaneController plane)
        {
            Quaternion r = plane.Rotation;
            return Mathf.Atan2((r * Vector3.right).y, (r * Vector3.up).y) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// Flies a steady descent at <paramref name="verticalSpeed"/> (m/s, − = down) and <paramref name="bank"/>, holding
        /// the throttle input, until the first touchdown, a crash, or <paramref name="timeout"/>.
        /// </summary>
        public static IEnumerator FlyToTouchdown(PlaneController plane, float verticalSpeed, float throttle, float bank = 0f, float timeout = 12f)
        {
            for (float t = 0f; t < timeout && plane.IsAlive && !plane.HasTouchedDown; t += Time.fixedDeltaTime)
            {
                float speed = Mathf.Max(1f, plane.Speed);
                float pathAngle = Mathf.Asin(Mathf.Clamp(verticalSpeed / speed, -1f, 1f)) * Mathf.Rad2Deg;
                plane.SetInput(new FlightInput
                {
                    Pitch = Mathf.Clamp((pathAngle - Pitch(plane)) * 0.08f, -1f, 1f),
                    Roll = bank == 0f ? 0f : Mathf.Clamp((Bank(plane) - bank) * 0.05f, -1f, 1f),
                    Throttle = throttle,
                });
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(plane.HasTouchedDown, "the plane never touched down");
        }

        public static IEnumerator Fly(PlaneController plane, FlightInput input, float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.fixedDeltaTime)
            {
                plane.SetInput(input);
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// A by-the-book landing, like dev key L: 500 m before the threshold on a 3° glide slope at idle (class min
        /// speed), gear locked, touching down near the start of the runway.
        /// </summary>
        public static IEnumerator FinalApproachAndLand(PlaneController plane, Airfield home)
        {
            const float distance = 500f, slope = 3f;
            Vector3 start = home.transform.TransformPoint(
                new Vector3(0f, distance * Mathf.Tan(slope * Mathf.Deg2Rad), -home.RunwaySize.y * 0.5f - distance));
            plane.Respawn(start, Quaternion.LookRotation(home.RunwayDirection) * Quaternion.Euler(slope, 0f, 0f), throttle: 0f);
            plane.GetComponent<LandingGear>().Snap(true);
            yield return FlyToTouchdown(plane, -plane.Speed * Mathf.Sin(slope * Mathf.Deg2Rad), throttle: -1f, timeout: 20f);
            Assert.AreEqual(TouchdownResult.Landed, plane.LastTouchdown.Result, $"fault {plane.LastTouchdown.Fault}");
            Assert.Less(home.transform.InverseTransformPoint(plane.Position).z, -100f, "touched down on the near half");
        }

        /// <summary>Rolls hands-off (no throttle input) until the plane stops or <paramref name="timeout"/> runs out.</summary>
        public static IEnumerator RollToAStop(PlaneController plane, float timeout = 25f)
        {
            for (float t = 0f; t < timeout && plane.IsAlive && plane.Speed > 0f; t += Time.fixedDeltaTime)
                yield return Fly(plane, default, Time.fixedDeltaTime);
            Assert.IsTrue(plane.IsAlive);
            Assert.AreEqual(0f, plane.Speed, "never stopped");
        }
    }
}

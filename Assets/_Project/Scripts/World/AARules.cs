using UnityEngine;

namespace WingingIt.World
{
    /// <summary>Pure AA gun rules: who's hostile, when a turret may fire, and how accurate it is. EditMode-tested.</summary>
    public static class AARules
    {
        /// <summary>A gun on <paramref name="gunTeam"/> shoots planes on any other team. Neutral guns shoot nobody.</summary>
        public static bool IsHostile(int gunTeam, int planeTeam) => gunTeam != Airfield.Neutral && planeTeam != gunTeam;

        /// <summary>Fires once it has tracked the target for the lock-on delay and its barrels are on the lead point.</summary>
        public static bool CanFire(float trackTime, float aimError, AAStats s) =>
            trackTime >= s.acquireTime && aimError <= s.fireCone;

        /// <summary>Spread (deg) after tracking one target for <paramref name="trackTime"/>: tightens the longer it lingers.</summary>
        public static float Spread(float trackTime, AAStats s) =>
            Mathf.Lerp(s.startSpread, s.settledSpread, Mathf.Clamp01((trackTime - s.acquireTime) / s.settleTime));

        /// <summary>How fast (deg/s) the line from a gun to a target swings. Faster than the traverse rate = outrunning it.</summary>
        public static float LineOfSightRate(Vector3 gun, Vector3 target, Vector3 targetVelocity)
        {
            Vector3 r = target - gun;
            float sqr = r.sqrMagnitude;
            return sqr < 1e-4f ? 0f : Vector3.Cross(r, targetVelocity).magnitude / sqr * Mathf.Rad2Deg;
        }
    }
}

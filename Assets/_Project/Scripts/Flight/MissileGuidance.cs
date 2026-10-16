using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>Homing math for missiles. Pure functions, so the guidance can be tested without a scene.</summary>
    public static class MissileGuidance
    {
        /// <summary>
        /// Lead pursuit: turns <paramref name="forward"/> toward where the target will be when a missile at
        /// <paramref name="speed"/> could reach it (or straight at it if it can't), by at most
        /// <paramref name="turnRate"/> deg/s over <paramref name="dt"/>.
        /// </summary>
        public static Vector3 Steer(Vector3 forward, Vector3 position, float speed, Vector3 targetPosition,
            Vector3 targetVelocity, float turnRate, float dt)
        {
            Vector3 aim = targetPosition;
            if (Ballistics.TryInterceptTime(targetPosition - position, targetVelocity, speed, out float time))
                aim += targetVelocity * time;
            Vector3 toAim = aim - position;
            if (toAim.sqrMagnitude < 1e-6f)
                return forward;
            return Vector3.RotateTowards(forward, toAim.normalized, turnRate * Mathf.Deg2Rad * dt, 0f).normalized;
        }

        /// <summary>True if <paramref name="point"/> is within <paramref name="halfAngle"/> deg of <paramref name="forward"/> seen from <paramref name="origin"/>.</summary>
        public static bool InCone(Vector3 origin, Vector3 forward, Vector3 point, float halfAngle)
        {
            Vector3 to = point - origin;
            return to.sqrMagnitude > 1e-6f && Vector3.Angle(forward, to) <= halfAngle;
        }

        /// <summary>
        /// Closest distance between <paramref name="point"/> and the segment from <paramref name="a"/> to
        /// <paramref name="b"/>; <paramref name="t"/> is how far along the segment (0..1) the closest point lies.
        /// </summary>
        public static float DistanceToSegment(Vector3 point, Vector3 a, Vector3 b, out float t)
        {
            Vector3 ab = b - a;
            float lengthSq = ab.sqrMagnitude;
            t = lengthSq > 1e-8f ? Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSq) : 0f;
            return Vector3.Distance(point, a + ab * t);
        }
    }
}

using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>Lead math for straight-flying bullets. Shared by the HUD pipper and, later, bot aiming.</summary>
    public static class Ballistics
    {
        /// <summary>
        /// Time for a bullet fired now at <paramref name="bulletSpeed"/> (relative to the shooter) to meet a target
        /// that is <paramref name="relativePosition"/> away and moving at <paramref name="relativeVelocity"/>
        /// (both relative to the shooter). False if the bullet can never catch it.
        /// </summary>
        public static bool TryInterceptTime(Vector3 relativePosition, Vector3 relativeVelocity, float bulletSpeed, out float time)
        {
            // |r + w t| = s t  →  (w·w − s²) t² + 2 (r·w) t + r·r = 0
            float a = Vector3.Dot(relativeVelocity, relativeVelocity) - bulletSpeed * bulletSpeed;
            float b = 2f * Vector3.Dot(relativePosition, relativeVelocity);
            float c = Vector3.Dot(relativePosition, relativePosition);
            time = 0f;

            if (Mathf.Abs(a) < 1e-4f)
            {
                if (b >= 0f)
                    return false;
                time = -c / b;
                return true;
            }

            float disc = b * b - 4f * a * c;
            if (disc < 0f)
                return false;
            float sqrt = Mathf.Sqrt(disc);
            float t1 = (-b - sqrt) / (2f * a);
            float t2 = (-b + sqrt) / (2f * a);
            float t = t1 > 0f && (t2 <= 0f || t1 < t2) ? t1 : t2;
            if (t <= 0f)
                return false;
            time = t;
            return true;
        }

        /// <summary>
        /// Where to point the nose so bullets meet the target: the target's position pushed ahead by its velocity
        /// relative to the shooter. False if out of reach within <paramref name="maxTime"/>.
        /// </summary>
        public static bool TryLeadPoint(Vector3 shooterPosition, Vector3 shooterVelocity, Vector3 targetPosition,
            Vector3 targetVelocity, float bulletSpeed, float maxTime, out Vector3 leadPoint, out float time)
        {
            Vector3 w = targetVelocity - shooterVelocity;
            leadPoint = targetPosition;
            if (!TryInterceptTime(targetPosition - shooterPosition, w, bulletSpeed, out time) || time > maxTime)
                return false;
            leadPoint = targetPosition + w * time;
            return true;
        }
    }
}

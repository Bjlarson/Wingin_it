using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>Blast helpers shared by rockets and missiles.</summary>
    public static class Explosions
    {
        static readonly Collider[] Buffer = new Collider[64];

        /// <summary>
        /// Adds every plane with a hitbox inside the blast to <paramref name="results"/>, mapped to the distance from
        /// <paramref name="point"/> to its nearest hitbox (keeping any smaller distance already there). Planes under
        /// <paramref name="ignore"/> (the shooter) are skipped.
        /// </summary>
        public static void FindPlanesInRadius(Vector3 point, float radius, Transform ignore, Dictionary<PlaneHealth, float> results)
        {
            if (radius <= 0f)
                return;
            int count = Physics.OverlapSphereNonAlloc(point, radius, Buffer, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = Buffer[i];
                if ((ignore != null && col.transform.IsChildOf(ignore)) || !PlanePart.TryGetFromCollider(col, out _))
                    continue;
                var health = col.GetComponentInParent<PlaneHealth>();
                if (health == null)
                    continue;
                float d = Vector3.Distance(point, col.ClosestPoint(point));
                if (!results.TryGetValue(health, out float best) || d < best)
                    results[health] = d;
            }
        }

        /// <summary>
        /// Splash damage <paramref name="distance"/> m from the center: full at the center, falling linearly to
        /// <paramref name="falloffMin"/> × damage at the edge, zero outside.
        /// </summary>
        public static float SplashDamageAt(float damage, float radius, float falloffMin, float distance)
        {
            if (damage <= 0f || distance > radius)
                return 0f;
            float t = radius > 0f ? Mathf.Clamp01(distance / radius) : 0f;
            return damage * Mathf.Lerp(1f, falloffMin, t);
        }
    }
}

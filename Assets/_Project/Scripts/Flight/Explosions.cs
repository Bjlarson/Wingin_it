using System.Collections.Generic;
using UnityEngine;
using WingingIt.Effects;

namespace WingingIt.Flight
{
    /// <summary>Blast helpers shared by rockets, missiles and bombs, and the impact effects every weapon plays.</summary>
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

        /// <summary>
        /// The blast effect for ordnance bursting at <paramref name="point"/> on <paramref name="surface"/> (null: in the air,
        /// a plane: in the air too), sized to its splash radius: a fireball in the air, a fireball with dust and flying dirt
        /// on the ground, a splash on the sea.
        /// </summary>
        public static void PlayBlast(Vector3 point, Collider surface, float splashRadius)
        {
            if (surface == null || PlanePart.TryGetFromCollider(surface, out _))
            {
                Vfx.Play(VfxKind.ExplosionAir, point, splashRadius / 7f);
            }
            else if (PlaneController.IsWater(surface))
            {
                Vfx.Play(VfxKind.WaterSplash, point, splashRadius / 14f);
                Vfx.Play(VfxKind.ExplosionAir, point, splashRadius / 14f);
            }
            else
            {
                PlayGround(point, EffectsSurface.Classify(surface, point), splashRadius / 12f);
            }
        }

        /// <summary>A ground blast whose dust and dirt take the colour of <paramref name="ground"/> (sand on a beach, soil on grass).</summary>
        public static void PlayGround(Vector3 point, GroundSurface ground, float scale)
        {
            var effect = Vfx.Play(VfxKind.ExplosionGround, point, scale);
            if (effect == null || ground is GroundSurface.None or GroundSurface.Water)
                return;
            Color color = EffectsSurface.Color(ground);
            effect.SetColor("Dust", color);
            effect.SetColor("Dirt", VfxEffect.Shade(color, 0.75f));
        }

        /// <summary>A bullet or shell hitting <paramref name="hit"/>: sparks off a plane, a glance off the repair bubble, dust off the ground, spray off the sea.</summary>
        public static void PlayBulletHit(RaycastHit hit, PlaneHealth plane, float scale = 1f)
        {
            if (plane != null)
            {
                // The repair bubble turns hits away entirely; the Armor Shield ripples on its own (PlaneAbility).
                Vfx.Play(plane.IsInvulnerable ? VfxKind.ShieldHit : VfxKind.BulletHitPlane, hit.point, scale);
            }
            else if (PlaneController.IsWater(hit.collider))
            {
                Vfx.Play(VfxKind.BulletHitWater, hit.point, scale);
            }
            else
            {
                var effect = Vfx.Play(VfxKind.BulletHitGround, hit.point, Quaternion.FromToRotation(Vector3.up, hit.normal), scale);
                var ground = effect != null ? EffectsSurface.Classify(hit.collider, hit.point) : GroundSurface.None;
                if (ground != GroundSurface.None)
                    effect.SetColor("Dust", EffectsSurface.Color(ground));
            }
        }
    }
}

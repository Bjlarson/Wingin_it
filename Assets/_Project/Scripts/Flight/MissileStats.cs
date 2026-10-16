using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Homing missile tuning for one aircraft class. Keeping a plane near the nose builds a lock; a locked missile
    /// steers toward it at a limited turn rate. The target shakes it off by turning hard for a moment once it's
    /// close, or by getting outside its seeker cone; either way the missile loses it for good and flies on unguided.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Missile Stats", fileName = "Missiles")]
    public class MissileStats : ScriptableObject
    {
        [Header("Ammo")]
        [Tooltip("Missiles carried from spawn. Restocked on respawn (and later on repair or from supply drops).")]
        [Min(1)] public int ammo = 2;
        [Tooltip("Seconds after a launch before the next missile can fire.")]
        [Min(0.05f)] public float launchCooldown = 1f;

        [Header("Lock-on")]
        [Tooltip("Farthest a lock can start or hold (m).")]
        [Min(1f)] public float lockRange = 900f;
        [Tooltip("A target must be within this angle (deg) of the nose to start locking.")]
        [Range(1f, 45f)] public float lockConeAngle = 10f;
        [Tooltip("Once locking, the target can drift this far (deg) off the nose before the lock breaks.")]
        [Range(1f, 60f)] public float lockHoldAngle = 18f;
        [Tooltip("Seconds of keeping the target in the cone to lock.")]
        [Min(0.05f)] public float lockTime = 1.5f;

        [Header("Flight")]
        [Tooltip("Top speed (m/s). Missiles leave at the plane's speed and accelerate to it.")]
        [Min(1f)] public float missileSpeed = 240f;
        [Tooltip("Motor acceleration (m/s²).")]
        [Min(0f)] public float acceleration = 150f;
        [Tooltip("Max turn rate (deg/s). A plane turning faster than this (relative to the missile) can make it miss.")]
        [Min(0f)] public float turnRate = 32f;
        [Tooltip("Seeker cone half-angle (deg). A target outside it is lost for good and the missile flies on unguided.")]
        [Range(1f, 90f)] public float seekerAngle = 50f;
        [Tooltip("Seconds after launch before the missile steers and its proximity fuse arms.")]
        [Min(0f)] public float armDelay = 0.3f;
        [Tooltip("Seconds before a missile self-destructs (it still explodes).")]
        [Min(0.05f)] public float lifetime = 6f;

        [Header("Evasion")]
        [Tooltip("A target whose flight path turns at least this fast (deg/s) is turning hard enough to shake the missile.")]
        [Min(0f)] public float evadeTurnRate = 40f;
        [Tooltip("Seconds of hard turning (while the missile is within evade range) to break its lock.")]
        [Min(0.05f)] public float evadeTime = 0.6f;
        [Tooltip("Hard turns only count once the missile is this close (m): turn too early and it just follows.")]
        [Min(0f)] public float evadeRange = 400f;

        [Header("Fuse")]
        [Tooltip("Explodes when it passes this close (m) to its target's center.")]
        [Min(0f)] public float proximityRadius = 6f;
        [Tooltip("Missiles are swept as spheres this big (m).")]
        [Range(0f, 2f)] public float missileRadius = 0.4f;

        [Header("Damage")]
        [Tooltip("HP removed from the part a missile hits directly.")]
        [Min(0f)] public float directDamage = 40f;
        [Tooltip("Body HP removed from every plane at the center of the blast (the direct-hit plane included).")]
        [Min(0f)] public float splashDamage = 35f;
        [Tooltip("Blast radius (m). Planes whose nearest hitbox is inside it take splash damage on the body.")]
        [Min(0f)] public float splashRadius = 12f;
        [Tooltip("Fraction of the splash damage still dealt at the edge of the blast (falls off linearly).")]
        [Range(0f, 1f)] public float splashFalloffMin = 0.3f;

        [Header("Visuals")]
        [Min(0.1f)] public float missileLength = 2.4f;
        [Min(0.01f)] public float missileWidth = 0.35f;

        /// <summary>Body damage from a blast whose center is <paramref name="distance"/> m from the plane's nearest hitbox.</summary>
        public float SplashDamageAt(float distance) =>
            Explosions.SplashDamageAt(splashDamage, splashRadius, splashFalloffMin, distance);
    }
}

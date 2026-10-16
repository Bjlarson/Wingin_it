using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Rocket tuning for one aircraft class. Rockets are unguided, fire in salvos, and have limited ammo that is
    /// restocked on respawn (and, later, at airfields). A rocket damages the part it hits and splashes the body of
    /// every plane near the blast.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Rocket Stats", fileName = "Rockets")]
    public class RocketStats : ScriptableObject
    {
        [Header("Ammo")]
        [Tooltip("Rockets carried from spawn. Restocked on respawn (and later on repair).")]
        [Min(1)] public int ammo = 8;
        [Tooltip("Rockets fired per press. Holding the button keeps firing salvos.")]
        [Min(1)] public int rocketsPerSalvo = 2;
        [Tooltip("Seconds between rockets within one salvo.")]
        [Min(0f)] public float salvoInterval = 0.1f;
        [Tooltip("Seconds after a salvo starts before the next one can.")]
        [Min(0.05f)] public float salvoCooldown = 1f;

        [Header("Damage")]
        [Tooltip("HP removed from the part a rocket hits directly.")]
        [Min(0f)] public float directDamage = 30f;
        [Tooltip("Body HP removed from every plane at the center of the blast (the direct-hit plane included).")]
        [Min(0f)] public float splashDamage = 10f;
        [Tooltip("Blast radius (m). Planes whose nearest hitbox is inside it take splash damage on the body.")]
        [Min(0f)] public float splashRadius = 10f;
        [Tooltip("Fraction of the splash damage still dealt at the edge of the blast (falls off linearly).")]
        [Range(0f, 1f)] public float splashFalloffMin = 0.3f;

        [Header("Ballistics")]
        [Tooltip("Launch speed (m/s), added to the plane's own velocity. Rockets fly straight (no drop).")]
        [Min(1f)] public float rocketSpeed = 320f;
        [Tooltip("Seconds before a rocket self-destructs (it still explodes). Range = speed × lifetime.")]
        [Min(0.05f)] public float rocketLifetime = 2.5f;
        [Tooltip("Random cone half-angle (deg) per rocket.")]
        [Range(0f, 5f)] public float spreadAngle = 0.6f;
        [Tooltip("Rockets are swept as spheres this big (m).")]
        [Range(0f, 2f)] public float rocketRadius = 0.5f;

        [Header("Visuals")]
        [Min(0.1f)] public float rocketLength = 1.6f;
        [Min(0.01f)] public float rocketWidth = 0.3f;

        public float Range => rocketSpeed * rocketLifetime;

        /// <summary>Body damage from a blast whose center is <paramref name="distance"/> m from the plane's nearest hitbox.</summary>
        public float SplashDamageAt(float distance) =>
            Explosions.SplashDamageAt(splashDamage, splashRadius, splashFalloffMin, distance);
    }
}

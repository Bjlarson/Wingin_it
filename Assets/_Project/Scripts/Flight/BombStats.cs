using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Bomb tuning for one aircraft class. Bombs drop from a bay under the fuselage, keep the plane's velocity and fall
    /// under gravity (no drag, so the impact preview is exact). They hit ground targets very hard and planes only a
    /// little. Limited ammo, restocked on respawn (and, later, at airfields).
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Bomb Stats", fileName = "Bombs")]
    public class BombStats : ScriptableObject
    {
        [Header("Ammo")]
        [Tooltip("Bombs carried from spawn. Restocked on respawn (and later on repair).")]
        [Min(1)] public int ammo = 4;
        [Tooltip("Seconds between drops. Holding the button keeps dropping one bomb per interval.")]
        [Min(0.05f)] public float dropInterval = 0.5f;

        [Header("Damage vs ground targets")]
        [Tooltip("Damage to a ground target at the center of the blast.")]
        [Min(0f)] public float groundDamage = 150f;

        [Header("Damage vs planes")]
        [Tooltip("HP removed from the part a bomb hits directly (rare: bombs aren't for dogfighting).")]
        [Min(0f)] public float directDamage = 40f;
        [Tooltip("Body HP removed from every plane at the center of the blast.")]
        [Min(0f)] public float planeSplashDamage = 15f;

        [Header("Blast")]
        [Tooltip("Blast radius (m), for planes and ground targets alike.")]
        [Min(0f)] public float splashRadius = 18f;
        [Tooltip("Fraction of the damage still dealt at the edge of the blast (falls off linearly).")]
        [Range(0f, 1f)] public float splashFalloffMin = 0.3f;

        [Header("Ballistics")]
        [Tooltip("Downward acceleration (m/s²). Above real gravity so bombs land quickly enough to aim.")]
        [Min(0.1f)] public float gravity = 12f;
        [Tooltip("Small push straight down at release (m/s), so a bomb clears the plane even in a climb.")]
        [Min(0f)] public float ejectSpeed = 3f;
        [Tooltip("Seconds before an airborne bomb is removed (it never explodes in the air).")]
        [Min(1f)] public float lifetime = 20f;
        [Tooltip("Bombs are swept as spheres this big (m).")]
        [Range(0f, 2f)] public float bombRadius = 0.4f;

        [Header("Visuals")]
        [Min(0.1f)] public float bombLength = 1.4f;
        [Min(0.01f)] public float bombWidth = 0.45f;

        public float GroundDamageAt(float distance) =>
            Explosions.SplashDamageAt(groundDamage, splashRadius, splashFalloffMin, distance);

        public float PlaneSplashDamageAt(float distance) =>
            Explosions.SplashDamageAt(planeSplashDamage, splashRadius, splashFalloffMin, distance);

        /// <summary>Position after <paramref name="time"/> s of free fall from <paramref name="start"/>.</summary>
        public Vector3 PositionAt(Vector3 start, Vector3 velocity, float time) =>
            start + velocity * time + 0.5f * gravity * time * time * Vector3.down;

        public Vector3 VelocityAt(Vector3 velocity, float time) => velocity + gravity * time * Vector3.down;
    }
}

using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>Machine gun tuning for one aircraft class. Guns never run out of ammo; heat limits them instead.</summary>
    [CreateAssetMenu(menuName = "Winging It/Gun Stats", fileName = "Guns")]
    public class GunStats : ScriptableObject
    {
        [Header("Damage")]
        [Tooltip("HP removed from the part a bullet hits.")]
        [Min(0f)] public float damage = 2f;
        [Tooltip("Per gun. Each wing carries half the guns, so a lost wing halves the total.")]
        [Min(0.1f)] public float roundsPerSecondPerGun = 6f;

        [Header("Ballistics")]
        [Tooltip("Muzzle speed (m/s), added to the plane's own velocity. Bullets fly straight (no drop).")]
        [Min(1f)] public float bulletSpeed = 550f;
        [Tooltip("Seconds before a bullet disappears. Range = speed × lifetime.")]
        [Min(0.05f)] public float bulletLifetime = 1.5f;
        [Tooltip("Wing guns are angled in to meet on the nose line this far ahead (m).")]
        [Min(10f)] public float convergenceDistance = 350f;
        [Tooltip("Random cone half-angle (deg) per shot.")]
        [Range(0f, 5f)] public float spreadAngle = 0.35f;
        [Tooltip("Bullets are swept as spheres this big (m) so near-misses on thin parts still count.")]
        [Range(0f, 1f)] public float bulletRadius = 0.3f;

        [Header("Overheat")]
        [Tooltip("Heat gained per second of firing (1 = overheated). 0.25 = 4 s of continuous fire.")]
        [Min(0.01f)] public float heatPerSecond = 0.25f;
        [Tooltip("Heat lost per second while not firing.")]
        [Min(0.01f)] public float coolPerSecond = 0.4f;
        [Tooltip("Seconds after releasing the trigger before cooling starts.")]
        [Min(0f)] public float coolDelay = 0.3f;
        [Tooltip("Once overheated, the guns stay locked until heat drops to this.")]
        [Range(0f, 1f)] public float overheatRecoverAt = 0.3f;

        [Header("Jams (Critical body only)")]
        [Tooltip("Chance per second of firing that the guns jam while the body is Critical.")]
        [Min(0f)] public float criticalJamChancePerSecond = 0.3f;
        [Tooltip("Seconds a jam lasts.")]
        [Min(0f)] public float jamDuration = 1.2f;

        [Header("Tracers")]
        [Min(0.1f)] public float tracerLength = 6f;
        [Min(0.01f)] public float tracerWidth = 0.25f;

        public float Range => bulletSpeed * bulletLifetime;
    }
}

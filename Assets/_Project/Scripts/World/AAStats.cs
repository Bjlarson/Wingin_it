using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// Tuning for a home airfield's AA guns. They're a deterrent, not a wall: a turret needs time to settle on a
    /// target and can't turn faster than <see cref="traverseRate"/>, so lingering over the field gets punished while
    /// a fast, low pass can still get through.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/AA Stats", fileName = "AAGuns")]
    public class AAStats : ScriptableObject
    {
        [Header("Targeting")]
        [Tooltip("Engagement range from the turret (m).")]
        [Min(10f)] public float range = 800f;
        [Tooltip("Fastest the turret can swing (deg/s). Targets crossing faster than this outrun it.")]
        [Min(1f)] public float traverseRate = 45f;
        [Tooltip("Seconds of tracking a new target before the first shot (lock-on delay).")]
        [Min(0f)] public float acquireTime = 1f;
        [Tooltip("Only fires while the barrels are within this many degrees of the lead point.")]
        [Range(0.1f, 30f)] public float fireCone = 3f;
        [Tooltip("Seconds between scans for a new target when it has none.")]
        [Min(0.05f)] public float retargetInterval = 0.5f;

        [Header("Fire")]
        [Min(0f)] public float damage = 2f;
        [Min(0.1f)] public float roundsPerSecond = 4f;
        [Tooltip("Shell speed (m/s). Shells fly straight (no drop), like plane bullets.")]
        [Min(1f)] public float shellSpeed = 650f;
        [Tooltip("Seconds before a shell disappears. Keep speed × lifetime a bit past the range.")]
        [Min(0.05f)] public float shellLifetime = 1.4f;
        [Range(0f, 2f)] public float shellRadius = 0.5f;

        [Header("Accuracy")]
        [Tooltip("Random cone half-angle (deg) right after acquiring a target.")]
        [Range(0f, 10f)] public float startSpread = 4f;
        [Tooltip("Cone half-angle (deg) once the turret has tracked the same target for the settle time.")]
        [Range(0f, 10f)] public float settledSpread = 0.9f;
        [Tooltip("Seconds of firing at the same target to tighten from start to settled spread.")]
        [Min(0.01f)] public float settleTime = 4f;

        [Header("Tracers")]
        [Min(0.1f)] public float tracerLength = 8f;
        [Min(0.01f)] public float tracerWidth = 0.35f;
    }
}

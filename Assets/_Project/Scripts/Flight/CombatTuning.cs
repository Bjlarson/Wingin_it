using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Playtest A/B for "too hard to shoot people" (Design/feedback/playtest-2026-10-27.md, D): every plane's part HP
    /// scaled down (10-100%), and gun bullets that hit a plane from further off (a bigger hitbox for shooting only: the colliders,
    /// visuals and ground collisions are untouched). Set from the Combat test setting; logs each kill's time-to-kill
    /// (first hit this life to death) so the variants can be compared.
    /// </summary>
    public static class CombatTuning
    {
        /// <summary>Extra reach (m) of a gun bullet against plane parts: about ×1.5 on a fuselage or wing's thickness.</summary>
        public const float BigHitboxPad = 0.75f;

        /// <summary>Multiplies every part's max HP (applies from the next respawn or repair).</summary>
        public static float HealthScale { get; set; } = 1f;
        /// <summary>Added to a gun bullet's radius when it's tested against planes.</summary>
        public static float HitboxPad { get; set; }

        /// <summary>The variant in force, for the log: "BASE", "HP 60%", "BIG HITBOX" or both.</summary>
        public static string Label => HealthScale < 1f && HitboxPad > 0f ? $"HP {HealthScale * 100f:0}% + BIG HITBOX"
            : HealthScale < 1f ? $"HP {HealthScale * 100f:0}%" : HitboxPad > 0f ? "BIG HITBOX" : "BASE";

        public static int Kills { get; private set; }
        public static float AverageTimeToKill => Kills > 0 ? s_totalTimeToKill / Kills : 0f;
        static float s_totalTimeToKill;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => ResetStats();

        public static void ResetStats()
        {
            Kills = 0;
            s_totalTimeToKill = 0f;
        }

        /// <summary>A plane shot down <paramref name="seconds"/> after it was first hit this life.</summary>
        public static void RecordKill(string victim, float seconds)
        {
            Kills++;
            s_totalTimeToKill += seconds;
            Debug.Log($"[TTK] {Label}: {victim} down {seconds:0.0} s after the first hit (avg {AverageTimeToKill:0.0} s over {Kills})");
        }
    }
}

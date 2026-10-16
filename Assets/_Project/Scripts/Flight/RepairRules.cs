using System;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>How long a repair takes. The worst of body HP and lost parts decides it.</summary>
    public enum RepairTier
    {
        /// <summary>Body Healthy (60%+) and nothing lost, including a plane that only needs ammo.</summary>
        Light,
        /// <summary>Body Damaged (25–59%), a stabilizer lost, or the engine dead.</summary>
        Heavy,
        /// <summary>Body Critical (under 25%) or a wing lost.</summary>
        Critical,
    }

    /// <summary>
    /// Repair tiers and their fixed times (5 / 10 / 15 s). Body HP maps straight from <see cref="BodyDamageState"/>,
    /// so the tier thresholds always match the damage states the pilot sees. Pure data + math, so it's testable
    /// and can run on the server unchanged.
    /// </summary>
    [Serializable]
    public class RepairRules
    {
        [Tooltip("Seconds for a Light repair (body 60%+, nothing lost).")]
        [Min(0f)] public float lightTime = 5f;
        [Tooltip("Seconds for a Heavy repair (body 25–59%, a stabilizer lost, or engine dead).")]
        [Min(0f)] public float heavyTime = 10f;
        [Tooltip("Seconds for a Critical repair (body under 25% or a wing lost).")]
        [Min(0f)] public float criticalTime = 15f;
        [Tooltip("A grounded plane slower than this (m/s) counts as stopped and starts repairing.")]
        [Min(0f)] public float stoppedSpeed = 0.5f;

        static readonly int WingBits = PartFlightEffects.Bit(PlanePartType.WingLeft) | PartFlightEffects.Bit(PlanePartType.WingRight);
        static readonly int HeavyBits = PartFlightEffects.Bit(PlanePartType.HorizontalStabilizer)
            | PartFlightEffects.Bit(PlanePartType.VerticalStabilizer) | PartFlightEffects.Bit(PlanePartType.Engine);

        /// <summary>The worst of the body state and the lost parts (<see cref="PlaneHealth.LostPartsMask"/>).</summary>
        public static RepairTier GetTier(BodyDamageState body, int lostPartsMask)
        {
            if (body >= BodyDamageState.Critical || (lostPartsMask & WingBits) != 0)
                return RepairTier.Critical;
            if (body == BodyDamageState.Damaged || (lostPartsMask & HeavyBits) != 0)
                return RepairTier.Heavy;
            return RepairTier.Light;
        }

        public float GetDuration(RepairTier tier) => tier switch
        {
            RepairTier.Critical => criticalTime,
            RepairTier.Heavy => heavyTime,
            _ => lightTime,
        };
    }
}

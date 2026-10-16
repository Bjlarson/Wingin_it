using System;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Turns the set of lost parts into flight modifiers. Effects are rebuilt from scratch from a
    /// lost-part bitmask (bit = <see cref="PlanePartType"/>), so combinations stay consistent and a
    /// server can later sync just the mask. Multipliers stack, so two lost parts compound.
    /// </summary>
    [Serializable]
    public class PartFlightEffects
    {
        [Header("One wing lost")]
        [Tooltip("Constant roll toward the missing wing, as a fraction of the class roll rate.")]
        [Range(0f, 1f)] public float wingRollBias = 0.35f;
        [Range(0f, 1f)] public float wingLift = 0.6f;
        [Range(0f, 1f)] public float wingRollRate = 0.75f;
        [Range(0f, 1f)] public float wingRollStability = 0.4f;

        [Header("Horizontal stabilizer lost")]
        [Range(0f, 1f)] public float hStabPitchRate = 0.45f;
        [Range(0f, 1f)] public float hStabPitchStability = 0f;
        [Tooltip("deg/s of random pitch shake (porpoising).")]
        public float hStabPitchWobble = 40f;

        [Header("Vertical stabilizer lost")]
        [Range(0f, 1f)] public float vStabYawRate = 0.35f;
        [Range(0f, 1f)] public float vStabRollStability = 0.7f;
        [Tooltip("Lower = the plane slides through turns (sloppy).")]
        [Range(0f, 1f)] public float vStabGrip = 0.6f;
        [Tooltip("deg/s of random yaw shake (fishtailing).")]
        public float vStabYawWobble = 30f;

        [Header("Combinations")]
        [Tooltip("Both stabilizers gone: no roll auto-leveling at all.")]
        [Range(0f, 1f)] public float bothStabsRollStability = 0f;
        [Tooltip("A wing plus any stabilizer: the roll bias is multiplied by this (a near-uncontrollable spiral).")]
        public float wingAndStabRollBias = 1.5f;
        [Tooltip("Both wings gone: lift multiplier on top of the per-wing loss. 0 = it falls.")]
        [Range(0f, 1f)] public float bothWingsLift = 0f;

        public static int Bit(PlanePartType type) => 1 << (int)type;

        public static bool Has(int lostMask, PlanePartType type) => (lostMask & Bit(type)) != 0;

        /// <summary>Replaces every modifier from <paramref name="source"/> with the effects of <paramref name="lostMask"/>.</summary>
        public void Apply(StatModifierStack stack, int lostMask, float baseRollRate, object source)
        {
            stack.RemoveAllFromSource(source);

            bool left = Has(lostMask, PlanePartType.WingLeft);
            bool right = Has(lostMask, PlanePartType.WingRight);
            bool hStab = Has(lostMask, PlanePartType.HorizontalStabilizer);
            bool vStab = Has(lostMask, PlanePartType.VerticalStabilizer);

            if (left || right)
            {
                // Rolls toward the missing side (+ = right). Both gone cancel out; that plane is falling anyway.
                float side = (right ? 1f : 0f) - (left ? 1f : 0f);
                float bias = side * wingRollBias * baseRollRate;
                if (hStab || vStab)
                    bias *= wingAndStabRollBias;
                if (bias != 0f)
                    stack.Add(FlightStat.RollBias, ModifierOp.Add, bias, source);

                foreach (bool lost in new[] { left, right })
                {
                    if (!lost)
                        continue;
                    stack.Add(FlightStat.Lift, ModifierOp.Multiply, wingLift, source);
                    stack.Add(FlightStat.RollRate, ModifierOp.Multiply, wingRollRate, source);
                    stack.Add(FlightStat.RollStability, ModifierOp.Multiply, wingRollStability, source);
                }
                if (left && right)
                    stack.Add(FlightStat.Lift, ModifierOp.Multiply, bothWingsLift, source);
            }

            if (hStab)
            {
                stack.Add(FlightStat.PitchRate, ModifierOp.Multiply, hStabPitchRate, source);
                stack.Add(FlightStat.PitchStability, ModifierOp.Multiply, hStabPitchStability, source);
                stack.Add(FlightStat.PitchWobble, ModifierOp.Add, hStabPitchWobble, source);
            }

            if (vStab)
            {
                stack.Add(FlightStat.YawRate, ModifierOp.Multiply, vStabYawRate, source);
                stack.Add(FlightStat.RollStability, ModifierOp.Multiply, vStabRollStability, source);
                stack.Add(FlightStat.Grip, ModifierOp.Multiply, vStabGrip, source);
                stack.Add(FlightStat.YawWobble, ModifierOp.Add, vStabYawWobble, source);
            }

            if (hStab && vStab)
                stack.Add(FlightStat.RollStability, ModifierOp.Multiply, bothStabsRollStability, source);

            if (Has(lostMask, PlanePartType.Engine))
                stack.Add(FlightStat.Thrust, ModifierOp.Multiply, 0f, source);
        }
    }
}

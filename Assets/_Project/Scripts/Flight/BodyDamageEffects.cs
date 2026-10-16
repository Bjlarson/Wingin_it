using System;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>The plane's overall condition, set by body (fuselage) HP.</summary>
    public enum BodyDamageState
    {
        Healthy,
        Damaged,
        Critical,
        Destroyed,
    }

    /// <summary>
    /// Body HP thresholds and the flight penalties of each <see cref="BodyDamageState"/>. Like
    /// <see cref="PartFlightEffects"/>, the modifiers are rebuilt from the state alone, so they stack
    /// cleanly on top of lost parts and a server only needs to sync body HP.
    /// </summary>
    [Serializable]
    public class BodyDamageEffects
    {
        [Header("Thresholds (fraction of body HP)")]
        [Tooltip("Below this the plane is Damaged.")]
        [Range(0f, 1f)] public float damagedBelow = 0.6f;
        [Tooltip("Below this the plane is Critical.")]
        [Range(0f, 1f)] public float criticalBelow = 0.25f;

        [Header("Damaged")]
        [Tooltip("Top speed multiplier.")]
        [Range(0f, 1f)] public float damagedMaxSpeed = 0.85f;

        [Header("Critical")]
        [Tooltip("Top speed multiplier.")]
        [Range(0f, 1f)] public float criticalMaxSpeed = 0.85f;
        [Tooltip("Climb speed multiplier.")]
        [Range(0f, 1f)] public float criticalClimb = 0.7f;
        [Tooltip("How much thrust the engine loses in each sputter (0..1).")]
        [Range(0f, 1f)] public float criticalSputter = 0.5f;

        public BodyDamageState GetState(float normalizedBodyHealth)
        {
            if (normalizedBodyHealth <= 0f)
                return BodyDamageState.Destroyed;
            if (normalizedBodyHealth < criticalBelow)
                return BodyDamageState.Critical;
            if (normalizedBodyHealth < damagedBelow)
                return BodyDamageState.Damaged;
            return BodyDamageState.Healthy;
        }

        /// <summary>Replaces every modifier from <paramref name="source"/> with the effects of <paramref name="state"/>.</summary>
        public void Apply(StatModifierStack stack, BodyDamageState state, object source)
        {
            stack.RemoveAllFromSource(source);
            switch (state)
            {
                case BodyDamageState.Damaged:
                    stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, damagedMaxSpeed, source);
                    break;
                case BodyDamageState.Critical:
                    stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, criticalMaxSpeed, source);
                    stack.Add(FlightStat.Climb, ModifierOp.Multiply, criticalClimb, source);
                    stack.Add(FlightStat.EngineSputter, ModifierOp.Add, criticalSputter, source);
                    break;
            }
        }
    }
}

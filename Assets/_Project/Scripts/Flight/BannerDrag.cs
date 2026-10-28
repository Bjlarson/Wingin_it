using System;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// What towing a banner does to the carrier (Design/01-Game-Design/match-modes.md#capture-the-banner-prototype): about
    /// −25% top speed and −30% climb and acceleration. Boost, Afterburner and repair are shut off through
    /// <see cref="PlaneController.CarryingBanner"/>; Barrel Roll and Armor Shield still work. One modifier group under
    /// <see cref="Source"/>, so it comes off as a unit when the banner is dropped, captured or returned.
    /// </summary>
    [Serializable]
    public class BannerDrag
    {
        /// <summary>The modifier source for every banner drag (removed as a group).</summary>
        public static readonly object Source = new();

        [Tooltip("Top speed multiplier while towing.")]
        [Range(0.3f, 1f)] public float maxSpeed = 0.75f;
        [Tooltip("Climb speed multiplier while towing.")]
        [Range(0.3f, 1f)] public float climb = 0.7f;
        [Tooltip("Acceleration multiplier while towing.")]
        [Range(0.3f, 1f)] public float acceleration = 0.7f;

        /// <summary>Puts the drag on <paramref name="stack"/> (replacing any already there).</summary>
        public void Apply(StatModifierStack stack)
        {
            stack.RemoveAllFromSource(Source);
            stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, maxSpeed, Source);
            stack.Add(FlightStat.Climb, ModifierOp.Multiply, climb, Source);
            stack.Add(FlightStat.Acceleration, ModifierOp.Multiply, acceleration, Source);
        }

        public static void Remove(StatModifierStack stack) => stack.RemoveAllFromSource(Source);

        /// <summary>
        /// The carrier's speed after a step that went from <paramref name="before"/> to <paramref name="after"/>: nothing
        /// wins the drag back (full throttle, a dive), so it never ends above <paramref name="cap"/> (the dragged top
        /// speed) unless it was already faster, and then it only bleeds toward the cap at <paramref name="bleed"/> m/s².
        /// </summary>
        public static float CapSpeed(float before, float after, float cap, float bleed, float dt)
        {
            if (after <= cap)
                return after;
            return Mathf.Max(cap, Mathf.Min(after, before) - bleed * dt);
        }
    }
}

using System;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>One plane in a ram, as <see cref="RamRules.Resolve"/> sees it.</summary>
    public readonly struct Rammer
    {
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly Vector3 Velocity;
        /// <summary><see cref="FlightStats.ramMass"/>: heavier deals more and takes less.</summary>
        public readonly float Mass;
        /// <summary>Multiplies the damage this plane deals (the Ram Boost).</summary>
        public readonly float Dealt;

        public Rammer(Vector3 position, Vector3 forward, Vector3 velocity, float mass = 1f, float dealt = 1f)
        {
            Position = position;
            Forward = forward;
            Velocity = velocity;
            Mass = mass;
            Dealt = dealt;
        }
    }

    /// <summary>What one plane-on-plane contact does to each plane (see <see cref="RamRules.Resolve"/>).</summary>
    public readonly struct RamOutcome
    {
        /// <summary>m/s the two planes were closing at.</summary>
        public readonly float ClosingSpeed;
        public readonly float DamageToA;
        public readonly float DamageToB;
        /// <summary>Velocity change for A and B (pushed apart along the way they met).</summary>
        public readonly Vector3 KnockA;
        public readonly Vector3 KnockB;

        public RamOutcome(float closingSpeed, float damageToA, float damageToB, Vector3 knockA, Vector3 knockB)
        {
            ClosingSpeed = closingSpeed;
            DamageToA = damageToA;
            DamageToB = damageToB;
            KnockA = knockA;
            KnockB = knockB;
        }

        public bool IsBump => DamageToA <= 0f && DamageToB <= 0f;
    }

    /// <summary>
    /// Ram damage (Ram Royale, see Design/01-Game-Design/match-modes.md#ram-royale-prototype): two planes that touch both
    /// take damage from the closing speed. A plane that hits another from behind, above, below or the side (out of the
    /// other's sight) takes only <see cref="hitterShare"/> of it; a head-on hit hurts both fully. Heavier classes
    /// (<see cref="FlightStats.ramMass"/>) deal more and take less. Pure, so it's EditMode-testable.
    /// </summary>
    [Serializable]
    public class RamRules
    {
        [Tooltip("Damage per m/s of closing speed, between two planes of the same mass taking the full hit. Head-on at cruise (~200 m/s) kills both.")]
        [Min(0f)] public float damagePerSpeed = 1.2f;
        [Tooltip("Closing slower than this (m/s) is a harmless bump.")]
        [Min(0f)] public float minClosingSpeed = 8f;
        [Tooltip("Fraction of the hit taken by a plane that rams another from behind, above or the side.")]
        [Range(0f, 1f)] public float hitterShare = 0.3f;
        [Tooltip("How far in front of the other plane's nose (cosine) you have to be to take the full hit.")]
        [Range(0.05f, 1f)] public float headOnCone = 0.7f;
        [Tooltip("Total push apart, as a fraction of the closing speed, split by mass.")]
        [Min(0f)] public float knockFraction = 0.25f;
        [Tooltip("Cap on the total push apart (m/s).")]
        [Min(0f)] public float maxKnock = 30f;
        [Tooltip("Seconds two planes that just rammed pass through each other, so one contact counts once.")]
        [Min(0f)] public float ignoreTime = 1f;

        /// <summary>Plane A meets plane B. Damage is before each plane's own multipliers (Armor Shield).</summary>
        public RamOutcome Resolve(in Rammer a, in Rammer b)
        {
            Vector3 relative = a.Velocity - b.Velocity;
            float closing = relative.magnitude;
            if (!(closing >= 1e-4f))
                return default; // not closing (or a bad velocity)

            float massA = Mathf.Max(0.01f, a.Mass), massB = Mathf.Max(0.01f, b.Mass);
            float total = massA + massB;
            Vector3 dir = relative / closing; // the way A is moving into B
            float knock = Mathf.Min(closing * knockFraction, maxKnock);
            Vector3 knockA = -dir * knock * (massB / total);
            Vector3 knockB = dir * knock * (massA / total);
            if (closing < minClosingSpeed)
                return new RamOutcome(closing, 0f, 0f, knockA, knockB);

            // A takes the full hit only if it's in front of B's nose (B flew into it), else just its share.
            float takenA = Taken(a.Position - b.Position, b.Forward);
            float takenB = Taken(b.Position - a.Position, a.Forward);
            float hit = damagePerSpeed * closing;
            float toA = hit * takenA * (2f * massB / total) * Mathf.Max(0f, b.Dealt);
            float toB = hit * takenB * (2f * massA / total) * Mathf.Max(0f, a.Dealt);
            return new RamOutcome(closing, toA, toB, knockA, knockB);
        }

        /// <summary>The fraction of the hit a plane at <paramref name="offset"/> from the other plane takes.</summary>
        float Taken(Vector3 offset, Vector3 otherForward)
        {
            float ahead = offset.sqrMagnitude > 1e-6f && otherForward.sqrMagnitude > 1e-6f
                ? Vector3.Dot(offset.normalized, otherForward.normalized) : 1f;
            return Mathf.Lerp(hitterShare, 1f, Mathf.InverseLerp(0f, headOnCone, ahead));
        }

        /// <summary>
        /// The part a ram damages: a wing or stabilizer takes it (and may come off); anything else (body, engine, nose)
        /// goes to the body.
        /// </summary>
        public static PlanePartType DamagedPart(PlanePartType? touched) => touched switch
        {
            PlanePartType.WingLeft or PlanePartType.WingRight
                or PlanePartType.HorizontalStabilizer or PlanePartType.VerticalStabilizer => touched.Value,
            _ => PlanePartType.Body,
        };
    }
}

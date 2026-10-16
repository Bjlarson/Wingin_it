using System;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>Where a plane is relative to a runway's glide path. Distances in meters.</summary>
    public readonly struct ApproachGuide
    {
        /// <summary>Lined up with the runway, heading down it, inside the approach cone and not far above the path.</summary>
        public readonly bool OnApproach;
        /// <summary>Along the runway to the aim point (+ = still before it, − = past it, over the runway).</summary>
        public readonly float Distance;
        /// <summary>Sideways from the centerline (+ = right of it, looking down the runway).</summary>
        public readonly float Lateral;
        /// <summary>Height of the glide path here, above the runway.</summary>
        public readonly float TargetHeight;
        /// <summary>Height above (+) or below (−) the glide path.</summary>
        public readonly float HeightError;
        /// <summary>How far off the path (each way) still counts as on it here.</summary>
        public readonly float Tolerance;

        public ApproachGuide(bool onApproach, float distance, float lateral, float targetHeight, float heightError, float tolerance)
        {
            OnApproach = onApproach;
            Distance = distance;
            Lateral = lateral;
            TargetHeight = targetHeight;
            HeightError = heightError;
            Tolerance = tolerance;
        }

        public bool IsHigh => HeightError > Tolerance;
        public bool IsLow => HeightError < -Tolerance;
        public bool IsOffCenter => Mathf.Abs(Lateral) > Tolerance;
        public bool OnPath => OnApproach && !IsHigh && !IsLow && !IsOffCenter;
    }

    /// <summary>
    /// A runway's glide path: a straight line at <see cref="angle"/>° down to an aim point
    /// <see cref="aimDistance"/> m past the threshold, for the landing-assist guide. Works in runway-local space (the
    /// runway along +Z, centered on the origin, surface at y = 0). Pure math, so it's testable.
    /// </summary>
    [Serializable]
    public class GlideSlope
    {
        [Tooltip("Glide path angle above the runway (deg).")]
        public float angle = 3f;
        [Tooltip("Aim point: this far past the runway threshold (m).")]
        public float aimDistance = 60f;
        [Tooltip("The guide shows from this far out (m, along the runway from the aim point).")]
        public float range = 3000f;
        [Tooltip("Half-angle of the approach cone around the centerline (deg).")]
        public float coneHalfAngle = 30f;
        [Tooltip("Must be heading within this many degrees of the runway direction.")]
        public float maxHeadingError = 60f;
        [Tooltip("Higher than this above the path doesn't count as an approach (just overflying).")]
        public float maxHeightAbovePath = 150f;
        [Tooltip("On-path tolerance at the aim point (m), growing by toleranceGrowth per meter out.")]
        public float baseTolerance = 6f;
        public float toleranceGrowth = 0.025f;

        /// <summary>The aim point's Z in runway-local space.</summary>
        public float AimZ(float runwayLength) => -runwayLength * 0.5f + aimDistance;

        /// <summary>Glide path height <paramref name="distance"/> m before the aim point (0 at and past it).</summary>
        public float HeightAt(float distance) => Mathf.Max(0f, distance) * Mathf.Tan(angle * Mathf.Deg2Rad);

        /// <summary>The runway-local point on the glide path <paramref name="distance"/> m before the aim point.</summary>
        public Vector3 PathPoint(float distance, float runwayLength) =>
            new Vector3(0f, HeightAt(distance), AimZ(runwayLength) - distance);

        public float ToleranceAt(float distance) => baseTolerance + Mathf.Max(0f, distance) * toleranceGrowth;

        /// <summary>
        /// Judges a plane at runway-local <paramref name="position"/> moving at <paramref name="velocity"/> against the
        /// glide path of a runway <paramref name="runwaySize"/> (width, length) m.
        /// </summary>
        public ApproachGuide Evaluate(Vector3 position, Vector3 velocity, Vector2 runwaySize)
        {
            float distance = AimZ(runwaySize.y) - position.z;
            float target = HeightAt(distance);
            float error = position.y - target;
            float tolerance = ToleranceAt(distance);

            // Before the aim point: inside a cone widening from the runway edges. Past it: over the runway.
            float pastAim = runwaySize.y - aimDistance;
            bool inRange = distance <= range && distance >= -pastAim;
            float halfWidth = runwaySize.x * 0.5f + Mathf.Max(0f, distance) * Mathf.Tan(coneHalfAngle * Mathf.Deg2Rad);
            bool inCone = Mathf.Abs(position.x) <= halfWidth;
            var flat = new Vector2(velocity.x, velocity.z);
            bool headingDown = flat.sqrMagnitude > 1f
                && Vector2.Angle(flat, Vector2.up) <= maxHeadingError;
            bool notOverflying = error <= maxHeightAbovePath && position.y > -1f;

            return new ApproachGuide(inRange && inCone && headingDown && notOverflying,
                distance, position.x, target, error, tolerance);
        }
    }
}

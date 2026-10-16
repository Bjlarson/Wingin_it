using System;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Runway auto-braking: after landing at idle, the plane rolls to a stop on its own, at a friendly repair zone if
    /// one is ahead. It keeps its speed (or taxis up to <see cref="taxiSpeed"/>) until it's time to brake, then brakes
    /// along a steady-deceleration curve that ends at the stop point. With no stop point (a zone behind it, or a runway
    /// that isn't ours) it just brakes to a stop. Pure math, so it's testable and can run on the server unchanged.
    /// </summary>
    [Serializable]
    public class AutoBrake
    {
        [Tooltip("Deceleration of the braking curve (m/s²): the stop point is reached at 0 m/s braking this hard.")]
        public float brakeDeceleration = 8f;
        [Tooltip("Hardest the brakes can pull to catch up with the curve (m/s²), e.g. after landing long.")]
        public float maxDeceleration = 14f;
        [Tooltip("A plane slower than this taxis up to it while the stop point is still far (needs a working engine).")]
        public float taxiSpeed = 15f;
        [Tooltip("Acceleration while taxiing up to taxi speed (m/s²).")]
        public float taxiAcceleration = 4f;

        /// <summary>The fastest a plane can go at <paramref name="distance"/> m from the stop point and still stop on it.</summary>
        public float StoppingSpeed(float distance) => Mathf.Sqrt(2f * brakeDeceleration * Mathf.Max(0f, distance));

        /// <summary>
        /// The speed after one step of <paramref name="dt"/> s. <paramref name="distanceToStop"/> is how far ahead the
        /// stop point is (≤ 0 = reached or passed it), or null for "stop wherever you can". <paramref name="canTaxi"/>
        /// is false with a dead engine: the plane can coast but not speed up.
        /// </summary>
        public float NextSpeed(float speed, float? distanceToStop, bool canTaxi, float dt)
        {
            if (distanceToStop is not float distance)
                return Mathf.MoveTowards(speed, 0f, brakeDeceleration * dt);
            if (distance <= 0f)
                return Mathf.MoveTowards(speed, 0f, maxDeceleration * dt);

            float limit = StoppingSpeed(distance);
            if (speed > limit)
                return Mathf.MoveTowards(speed, limit, maxDeceleration * dt);
            float taxi = Mathf.Min(taxiSpeed, limit);
            if (speed < taxi && canTaxi)
                return Mathf.MoveTowards(speed, taxi, taxiAcceleration * dt);
            return speed; // roll on until the braking curve catches up
        }
    }
}

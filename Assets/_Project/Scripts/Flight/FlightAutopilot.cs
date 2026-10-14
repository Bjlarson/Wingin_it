using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Turns "fly toward this point" into stick inputs. Used by mouse-aim, the map boundary
    /// turn-back, and later by bots, so they all fly with the same controls as a player.
    /// </summary>
    public static class FlightAutopilot
    {
        /// <param name="sensitivity">Stick gain; higher = more aggressive corrections.</param>
        /// <param name="aggressiveTurnAngle">Off-target angle (deg) above which the plane banks into the turn; below it, it levels its wings.</param>
        public static void SteerTowards(Vector3 position, Quaternion rotation, Vector3 targetPoint,
            float sensitivity, float aggressiveTurnAngle,
            out float pitch, out float yaw, out float roll)
        {
            Vector3 toTarget = targetPoint - position;
            if (toTarget.sqrMagnitude < 0.0001f)
            {
                pitch = yaw = roll = 0f;
                return;
            }

            Vector3 local = Quaternion.Inverse(rotation) * toTarget.normalized;
            float angleOff = Vector3.Angle(Vector3.forward, local);

            yaw = Mathf.Clamp(local.x * sensitivity, -1f, 1f);
            pitch = Mathf.Clamp(local.y * sensitivity, -1f, 1f);

            // Target behind us: pull hard and let the roll put the target overhead.
            if (local.z < 0f)
                pitch = local.y >= -0.1f ? 1f : -1f;

            // Far off target: bank toward it. Close to target: level the wings.
            float aggressiveRoll = Mathf.Clamp(local.x * sensitivity, -1f, 1f);
            Vector3 right = rotation * Vector3.right;
            float wingsLevelRoll = Mathf.Clamp(right.y * sensitivity, -1f, 1f);
            float t = Mathf.InverseLerp(0f, aggressiveTurnAngle, angleOff);
            roll = Mathf.Lerp(wingsLevelRoll, aggressiveRoll, t);
        }
    }
}

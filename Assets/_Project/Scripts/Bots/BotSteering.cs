using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Bots
{
    /// <summary>
    /// The pure math behind <see cref="BotPilot"/>: where to point the nose and how to work the throttle.
    /// No scene access, so it's EditMode-tested.
    /// </summary>
    public static class BotSteering
    {
        /// <summary>
        /// The throttle input (-1..1 rate, like a player holding W/S) that moves the throttle toward the
        /// setting that flies at <paramref name="targetSpeed"/>. In the air the throttle picks a speed between
        /// min and max speed; on the wheels it's a fraction of max speed (see <c>PlaneController</c>).
        /// </summary>
        /// <param name="thrust">The plane's thrust multiplier (0 with a dead engine: nothing to command).</param>
        /// <param name="gain">How hard to push the lever per unit of throttle error.</param>
        public static float ThrottleCommand(float currentThrottle, float targetSpeed, float minSpeed, float maxSpeed,
            float thrust, bool grounded, float gain = 8f)
        {
            if (thrust <= 0.001f)
                return 0f;
            float speed = targetSpeed / thrust;
            float wanted = grounded
                ? Mathf.Clamp01(speed / Mathf.Max(1f, maxSpeed))
                : Mathf.Max(ThrottleCurve.IdleBand, Mathf.InverseLerp(minSpeed, maxSpeed, speed)); // bots never idle in the air
            return Mathf.Clamp((wanted - currentThrottle) * gain, -1f, 1f);
        }

        /// <summary>
        /// Where to aim to meet a target flying at <paramref name="targetVelocity"/>: its position after the time
        /// it takes to close the gap at our speed, capped at <paramref name="maxLeadTime"/> seconds so a target
        /// that's far away or faster than us doesn't send us chasing a point over the horizon.
        /// </summary>
        public static Vector3 InterceptPoint(Vector3 position, float speed, Vector3 targetPosition, Vector3 targetVelocity,
            float maxLeadTime = 3f)
        {
            float distance = Vector3.Distance(position, targetPosition);
            float time = Mathf.Min(distance / Mathf.Max(1f, speed), maxLeadTime);
            return targetPosition + targetVelocity * time;
        }

        /// <summary>
        /// Where to fly to <em>hit</em> a target flying at <paramref name="targetVelocity"/> (a ram, not a gun shot):
        /// the point where a straight line flown at <paramref name="speed"/> meets the target's straight path, at the
        /// soonest time that works, capped at <paramref name="maxTime"/> seconds. A target we can't catch gets the
        /// capped <see cref="InterceptPoint"/> instead.
        /// </summary>
        public static Vector3 CollisionPoint(Vector3 position, float speed, Vector3 targetPosition, Vector3 targetVelocity,
            float maxTime = 6f)
        {
            // |r + v t| = s t  →  (v·v − s²) t² + 2 (r·v) t + r·r = 0
            Vector3 r = targetPosition - position;
            float a = Vector3.Dot(targetVelocity, targetVelocity) - speed * speed;
            float b = 2f * Vector3.Dot(r, targetVelocity);
            float c = Vector3.Dot(r, r);
            float t = -1f;
            if (Mathf.Abs(a) < 1e-3f)
            {
                if (b < 0f)
                    t = -c / b;
            }
            else
            {
                float disc = b * b - 4f * a * c;
                if (disc >= 0f)
                {
                    float root = Mathf.Sqrt(disc);
                    float t1 = (-b - root) / (2f * a), t2 = (-b + root) / (2f * a);
                    t = t1 > 0f && (t1 < t2 || t2 <= 0f) ? t1 : t2;
                }
            }
            if (t <= 0f)
                return InterceptPoint(position, speed, targetPosition, targetVelocity, maxTime);
            return targetPosition + targetVelocity * Mathf.Min(t, maxTime);
        }

        /// <summary>
        /// A formation slot: <paramref name="offset"/> in the leader's level frame (x right, y up, z forward,
        /// so a bank doesn't swing the wingman up or down).
        /// </summary>
        public static Vector3 SlotPosition(Vector3 leaderPosition, Quaternion leaderRotation, Vector3 offset)
        {
            Vector3 flat = Vector3.ProjectOnPlane(leaderRotation * Vector3.forward, Vector3.up);
            Quaternion level = flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat) : Quaternion.identity;
            return leaderPosition + level * offset;
        }

        /// <summary>
        /// Speed to hold a formation slot: the leader's speed, plus <paramref name="gain"/> m/s per metre we're
        /// behind the slot (minus if ahead), along the leader's heading.
        /// </summary>
        public static float SlotSpeed(Vector3 position, Vector3 slot, Vector3 leaderForward, float leaderSpeed,
            float gain = 0.15f, float maxCorrection = 40f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(leaderForward, Vector3.up);
            float behind = flat.sqrMagnitude > 1e-4f ? Vector3.Dot(slot - position, flat.normalized) : 0f;
            return leaderSpeed + Mathf.Clamp(behind * gain, -maxCorrection, maxCorrection);
        }

        /// <summary>
        /// A steering point on a circle around <paramref name="center"/>, <paramref name="leadAngle"/> degrees
        /// ahead of the plane, at <paramref name="altitude"/>. Steering at it flies the circle.
        /// </summary>
        public static Vector3 OrbitPoint(Vector3 position, Vector3 forward, Vector3 center, float radius, float altitude,
            bool clockwise, float leadAngle = 25f)
        {
            Vector3 fromCenter = position - center;
            fromCenter.y = 0f;
            if (fromCenter.sqrMagnitude < 1f)
                fromCenter = forward;
            fromCenter.y = 0f;
            if (fromCenter.sqrMagnitude < 1e-4f)
                fromCenter = Vector3.forward;

            // A positive angle around +Y is clockwise seen from above.
            float angle = clockwise ? leadAngle : -leadAngle;
            Vector3 onCircle = Quaternion.AngleAxis(angle, Vector3.up) * fromCenter.normalized * radius;
            return new Vector3(center.x + onCircle.x, altitude, center.z + onCircle.z);
        }

        /// <summary>
        /// Where to aim on final, in runway-local space (runway along +Z, surface at y = 0): the point on the glide path
        /// <paramref name="lead"/> m further along than a plane <paramref name="distance"/> m before the aim point. Past the
        /// aim point the line carries on below the runway at the same angle, so steering at it holds a steady descent
        /// right into the touchdown instead of levelling off over the runway. Off the centerline, it turns the plane in.
        /// </summary>
        /// <param name="aimZ">The aim point's local Z (<c>GlideSlope.AimZ</c>).</param>
        public static Vector3 GlideCarrot(float distance, float lead, float angle, float aimZ)
        {
            float d = distance - lead;
            return new Vector3(0f, d * Mathf.Tan(angle * Mathf.Deg2Rad), aimZ - d);
        }

        /// <summary>
        /// A steering point that turns toward <paramref name="turnTo"/> but climbs or descends toward
        /// <paramref name="descendTo"/>: the heading of one with the flight-path angle of the other.
        /// </summary>
        public static Vector3 SplitAim(Vector3 position, Vector3 turnTo, Vector3 descendTo)
        {
            Vector3 toFar = descendTo - position;
            Vector3 flatFar = Vector3.ProjectOnPlane(toFar, Vector3.up);
            Vector3 flatNear = Vector3.ProjectOnPlane(turnTo - position, Vector3.up);
            if (flatNear.sqrMagnitude < 1e-4f || flatFar.sqrMagnitude < 1e-4f)
                return descendTo;
            return position + flatNear.normalized * flatFar.magnitude + Vector3.up * toFar.y;
        }

        /// <summary>
        /// Where to point the nose so the <em>flight path</em> goes to <paramref name="point"/>: the direction to it,
        /// shifted by however far the velocity sags (or slips) off the nose. With no difference it's the point itself.
        /// </summary>
        public static Vector3 FlightPathAim(Vector3 position, Vector3 forward, Vector3 velocity, Vector3 point,
            float maxCorrection = 0.35f)
        {
            Vector3 to = point - position;
            float distance = to.magnitude;
            if (distance < 1e-3f || velocity.sqrMagnitude < 1f)
                return point;
            Vector3 offset = Vector3.ClampMagnitude(forward.normalized - velocity.normalized, maxCorrection);
            return position + (to / distance + offset).normalized * distance;
        }

        /// <summary>
        /// Pursue speed over the target's: proportional to how far beyond <paramref name="gap"/> we are, capped at
        /// <paramref name="maxOvertake"/> either way, so the bot closes fast from long range and eases off at the gap.
        /// </summary>
        public static float Overtake(float distance, float gap, float perMeter, float maxOvertake) =>
            Mathf.Clamp((distance - gap) * perMeter, -maxOvertake * 0.5f, maxOvertake);

        /// <summary>
        /// Limits how fast a stick axis can move (full travel in 2 / <paramref name="rate"/> seconds), so a bot
        /// flies with a hand on the stick instead of snapping between extremes.
        /// </summary>
        public static float Slew(float current, float target, float rate, float dt) =>
            rate <= 0f ? target : Mathf.MoveTowards(current, target, rate * dt);

        /// <summary>
        /// A slowly wandering aim error for time <paramref name="time"/>: x (yaw) and y (pitch), each in -1..1, smooth
        /// over a couple of seconds. <paramref name="seed"/> gives each bot its own wander.
        /// </summary>
        public static Vector2 AimWander(float seed, float time, float rate = 0.4f)
        {
            float t = time * rate;
            // Perlin noise mostly stays within 0.2..0.8: stretch it to fill -1..1.
            float x = Mathf.Clamp((Mathf.PerlinNoise(seed, t) - 0.5f) * 3f, -1f, 1f);
            float y = Mathf.Clamp((Mathf.PerlinNoise(seed + 37.1f, t + 11.3f) - 0.5f) * 3f, -1f, 1f);
            return new Vector2(x, y);
        }

        /// <summary>
        /// <paramref name="aimPoint"/> moved <paramref name="yaw"/> degrees sideways (about <paramref name="up"/>) and
        /// <paramref name="pitch"/> degrees up, as seen from <paramref name="position"/>, at the same distance.
        /// </summary>
        public static Vector3 OffsetAim(Vector3 position, Vector3 aimPoint, Vector3 up, float yaw, float pitch)
        {
            Vector3 to = aimPoint - position;
            if (to.sqrMagnitude < 1e-4f)
                return aimPoint;
            Vector3 right = Vector3.Cross(up, to);
            if (right.sqrMagnitude < 1e-6f)
                right = Vector3.Cross(Vector3.forward, to);
            Quaternion turn = Quaternion.AngleAxis(yaw, up) * Quaternion.AngleAxis(-pitch, right.normalized);
            return position + turn * to;
        }
    }
}

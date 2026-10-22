using System.Collections.Generic;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Bots
{
    /// <summary>
    /// The pure math behind <see cref="BotBrain"/>'s decisions: how to rank targets, when a gun or rocket shot is lined
    /// up, how to break from a missile and where to pull up away from terrain. No scene access, so it's EditMode-tested.
    /// </summary>
    /// <summary>Why a landing was abandoned (see <see cref="BotSenses.ShouldGoAround"/>).</summary>
    public enum GoAroundReason
    {
        None,
        /// <summary>Another plane is on the runway.</summary>
        RunwayBusy,
        /// <summary>Never got (or didn't stay) lined up on the glide path.</summary>
        OffApproach,
        /// <summary>Would touch down far past the aim point.</summary>
        Overshot,
        /// <summary>About to touch down beside the runway.</summary>
        OffCenter,
    }

    public static class BotSenses
    {
        /// <summary>
        /// How attractive a target is (lower is better): its distance, stretched up to 2× for a target straight behind
        /// us, so a bot prefers the plane in front of it over a slightly closer one it would have to turn around for.
        /// </summary>
        public static float TargetScore(Vector3 position, Vector3 forward, Vector3 targetPosition)
        {
            Vector3 to = targetPosition - position;
            float angle = to.sqrMagnitude > 1e-4f ? Vector3.Angle(forward, to) : 0f;
            return to.magnitude * (1f + angle / 180f);
        }

        /// <summary>
        /// <see cref="TargetScore(Vector3, Vector3, Vector3)"/>, also weighing how easy the shot is: a target showing us
        /// its tail scores down to 0.7× (it isn't turning to meet us), one coming head-on up to 1.3×, and a damaged one
        /// down to 0.6× at no body HP left, so bots go for the kills they can actually finish.
        /// </summary>
        public static float TargetScore(Vector3 position, Vector3 forward, Vector3 targetPosition, Vector3 targetForward,
                                        float targetBodyHealth)
        {
            Vector3 to = targetPosition - position;
            float tail = to.sqrMagnitude > 1e-4f ? Vector3.Dot(targetForward.normalized, to.normalized) : 0f;
            return TargetScore(position, forward, targetPosition) * (1f - 0.3f * tail) *
                   (0.6f + 0.4f * Mathf.Clamp01(targetBodyHealth));
        }

        /// <summary>
        /// Gun trigger check: the lead point is within <paramref name="maxRange"/>, within <paramref name="tolerance"/>
        /// degrees of the nose, and the bullets would pass within <paramref name="missDistance"/> m of it (so the cone
        /// narrows with range instead of spraying at long range).
        /// </summary>
        public static bool IsGunShot(Vector3 position, Vector3 forward, Vector3 lead, float tolerance, float missDistance,
                                     float maxRange)
        {
            Vector3 to = lead - position;
            float distance = to.magnitude;
            if (distance < 1e-2f || distance > maxRange)
                return false;
            float angle = Vector3.Angle(forward, to);
            return angle <= tolerance && distance * Mathf.Sin(angle * Mathf.Deg2Rad) <= missDistance;
        }

        /// <summary>True if <paramref name="point"/> is within <paramref name="tolerance"/> degrees of the nose.</summary>
        public static bool IsAimedAt(Vector3 position, Vector3 forward, Vector3 point, float tolerance)
        {
            Vector3 to = point - position;
            return to.sqrMagnitude > 1e-4f && Vector3.Angle(forward, to) <= tolerance;
        }

        /// <summary>
        /// Trigger discipline for the guns: fire while the heat is below <paramref name="stopAt"/>, then hold off until
        /// it cools to <paramref name="resumeAt"/>, so a bot never runs them into an overheat lockout.
        /// </summary>
        public static bool GunsCool(float heat, bool wasFiring, float stopAt = 0.85f, float resumeAt = 0.45f) =>
            wasFiring ? heat < stopAt : heat < resumeAt;

        /// <summary>
        /// Which way to break from a missile: +1 right, -1 left. Into the turn the plane is already banked into, so the
        /// break starts at once; wings level, away from the side the missile is coming from.
        /// </summary>
        public static float BreakSide(Vector3 right, Vector3 position, Vector3 missilePosition)
        {
            if (Mathf.Abs(right.y) > 0.17f) // banked over 10°: keep turning that way
                return right.y < 0f ? 1f : -1f;
            return Vector3.Dot(missilePosition - position, right) > 0f ? -1f : 1f;
        }

        /// <summary>
        /// A steering point <paramref name="angle"/> degrees off the (level) nose toward <paramref name="side"/>, at our
        /// altitude. It moves with the plane, so steering at it is a sustained maximum-rate turn: what shakes a missile.
        /// </summary>
        public static Vector3 BreakTurnPoint(Vector3 position, Vector3 forward, float side, float angle = 100f,
            float distance = 400f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            return position + Quaternion.AngleAxis(angle * Mathf.Sign(side), Vector3.up) * flat.normalized * distance;
        }

        /// <summary>
        /// True if the plane is about to hit the ground: below <paramref name="minClearance"/> metres and not climbing,
        /// or low enough that its sink rate reaches the ground within <paramref name="reactionTime"/> seconds.
        /// </summary>
        public static bool TooLow(float heightAboveGround, float verticalSpeed, float minClearance, float reactionTime)
        {
            if (heightAboveGround < minClearance && verticalSpeed <= 1f)
                return true;
            return verticalSpeed < 0f && heightAboveGround + verticalSpeed * reactionTime < minClearance * 0.5f;
        }

        /// <summary>
        /// Whether a bot flying at <paramref name="velocity"/> should turn back from a hard arena edge (no autopilot
        /// turn-back there): it's heading outward and will be within <paramref name="margin"/> of the edge (or past it)
        /// in <paramref name="lookahead"/> seconds.
        /// </summary>
        public static bool ShouldTurnFromEdge(Vector3 position, Vector3 velocity, Vector3 center, float radius,
            float margin = 100f, float lookahead = 3f)
        {
            Vector3 radial = position - center;
            radial.y = 0f;
            Vector3 flatVelocity = new Vector3(velocity.x, 0f, velocity.z);
            if (Vector3.Dot(radial, flatVelocity) <= 0f)
                return false; // heading in (or straight along the edge)
            Vector3 ahead = radial + flatVelocity * lookahead;
            return ahead.magnitude > radius - margin;
        }

        /// <summary>
        /// Where to circle <paramref name="point"/> at <paramref name="orbitRadius"/> without the circle reaching within
        /// <paramref name="margin"/> of the play area's edge (out there the boundary turn-back would take the stick, and it
        /// doesn't watch the ground). The point itself if it's far enough in, else pulled straight in toward the middle.
        /// </summary>
        public static Vector3 OrbitInside(Vector3 point, float orbitRadius, Vector3 center, float radius, float margin)
        {
            Vector3 radial = point - center;
            radial.y = 0f;
            float max = Mathf.Max(0f, radius - margin - orbitRadius);
            if (radial.magnitude <= max)
                return point;
            Vector3 inside = center + radial.normalized * max;
            return new Vector3(inside.x, point.y, inside.z);
        }

        /// <summary>
        /// Where to steer to pull away from an obstacle: up at about <paramref name="climbAngle"/> degrees, and turned
        /// away from it along its surface normal (a wall turns us aside, flat ground just pulls the nose up).
        /// </summary>
        public static Vector3 EscapePoint(Vector3 position, Vector3 forward, Vector3 obstacleNormal, float climbAngle = 40f,
            float distance = 300f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 1e-4f)
                flat = Vector3.forward;
            flat.Normalize();
            Vector3 away = Vector3.ProjectOnPlane(obstacleNormal, Vector3.up);
            // A wall we're flying straight at turns us off to the side it leans toward (or right if dead-on).
            if (away.sqrMagnitude > 0.04f)
            {
                away.Normalize();
                if (Vector3.Dot(away, flat) < -0.95f)
                    away = Vector3.Cross(Vector3.up, flat);
                flat = (flat + away).normalized;
            }
            Vector3 dir = flat * Mathf.Cos(climbAngle * Mathf.Deg2Rad) + Vector3.up * Mathf.Sin(climbAngle * Mathf.Deg2Rad);
            return position + dir * distance;
        }

        /// <summary>
        /// Whether another plane will pass within <paramref name="radius"/> metres in the next <paramref name="horizon"/>
        /// seconds, flying straight. <paramref name="miss"/> is where it will be relative to us at the closest point
        /// (pointing from us to it; steer the other way).
        /// </summary>
        public static bool OnCollisionCourse(Vector3 relativePosition, Vector3 relativeVelocity, float horizon, float radius,
            out Vector3 miss)
        {
            float closing = relativeVelocity.sqrMagnitude;
            float t = closing > 1e-4f ? Mathf.Clamp(-Vector3.Dot(relativePosition, relativeVelocity) / closing, 0f, horizon) : 0f;
            miss = relativePosition + relativeVelocity * t;
            return miss.sqrMagnitude < radius * radius;
        }

        /// <summary>Seconds until two planes flying straight are closest (0 if they're already opening).</summary>
        public static float TimeToClosest(Vector3 relativePosition, Vector3 relativeVelocity)
        {
            float closing = relativeVelocity.sqrMagnitude;
            return closing > 1e-4f ? Mathf.Max(0f, -Vector3.Dot(relativePosition, relativeVelocity) / closing) : 0f;
        }

        /// <summary>
        /// The other plane (at <paramref name="relativePosition"/> from us) is flying at us with at least
        /// <paramref name="minSpeed"/> m/s of its own speed: lining up on us, not one we're catching up with.
        /// </summary>
        public static bool IsComingAt(Vector3 relativePosition, Vector3 otherVelocity, float minSpeed = 20f) =>
            relativePosition.sqrMagnitude > 1e-4f && Vector3.Dot(otherVelocity, -relativePosition.normalized) >= minSpeed;

        /// <summary>
        /// Ram Royale: let a plane coming at us hit us (rather than break away) only if we'd survive it and come off no
        /// worse: <paramref name="damageToUs"/> under our <paramref name="bodyHealth"/> and no more than we deal.
        /// </summary>
        public static bool RamWorthTaking(float damageToUs, float damageToThem, float bodyHealth) =>
            damageToUs < bodyHealth && damageToUs <= damageToThem;

        /// <summary>
        /// Where to steer to get clear of a plane that will pass at <paramref name="miss"/> (relative to us): straight
        /// ahead, offset away from it. Dead-on, it breaks right, like pilots do: two planes head-on doing the same
        /// pass left side to left side.
        /// </summary>
        public static Vector3 SidestepPoint(Vector3 position, Vector3 forward, Vector3 miss, float distance = 300f)
        {
            Vector3 away = -Vector3.ProjectOnPlane(miss, forward);
            if (away.sqrMagnitude < 1f)
                away = Vector3.Cross(Vector3.up, forward);
            if (away.sqrMagnitude < 1e-4f)
                away = Vector3.right;
            return position + forward.normalized * distance + away.normalized * distance * 0.6f;
        }

        // ------------------------------------------------------------------ retreat to repair

        /// <summary>
        /// Time to go home: any part lost (a wing, a stabilizer or the engine) or the body below
        /// <paramref name="retreatHealth"/> (0..1).
        /// </summary>
        public static bool NeedsRepair(float bodyHealth, int lostPartsMask, float retreatHealth) =>
            lostPartsMask != 0 || bodyHealth < retreatHealth;

        /// <summary>
        /// Where to join an approach lane from <paramref name="position"/>: the first point after the closest spot on it
        /// (its entry, from before the lane). Close to the end it's never the final approach fix itself but the point
        /// before it, so the plane flies that last leg and arrives lined up rather than from any old direction.
        /// <paramref name="cost"/> is the trip: across to the lane, then the rest of it.
        /// </summary>
        /// <param name="minFinalLeg">Closer than this (m, along the lane) to the fix: go back for the point before it.</param>
        public static int LaneJoinIndex(IReadOnlyList<Vector3> path, Vector3 position, out float cost, float minFinalLeg = 400f)
        {
            cost = 0f;
            if (path.Count < 2)
                return 0;
            float off = ApproachLane.FlatDistanceToPath(path, position, out float along);
            float length = ApproachLane.PathLength(path);
            cost = off + Mathf.Max(0f, length - along);
            int index = along <= 1e-3f ? 0 : ApproachLane.NextIndex(path, along);
            if (index == path.Count - 1 && along > length - minFinalLeg)
                index = path.Count - 2;
            return index;
        }

        /// <summary>
        /// Picks a lane from their trip lengths (<paramref name="costs"/>): any within <paramref name="slack"/>× of the
        /// shortest, chosen by <paramref name="random01"/> (0..1), so a chaser can't always guess which way a bot goes
        /// home. -1 for no lanes.
        /// </summary>
        public static int PickLane(IReadOnlyList<float> costs, float random01, float slack = 1.3f)
        {
            if (costs.Count == 0)
                return -1;
            float best = float.MaxValue;
            foreach (float c in costs)
                best = Mathf.Min(best, c);
            int candidates = 0;
            foreach (float c in costs)
                if (c <= best * slack)
                    candidates++;
            int pick = Mathf.Min(candidates - 1, (int)(Mathf.Clamp01(random01) * candidates));
            for (int i = 0; i < costs.Count; i++)
                if (costs[i] <= best * slack && pick-- == 0)
                    return i;
            return 0;
        }

        /// <summary>
        /// Abandon the landing and try again: the runway is taken while we're still well out, we've been off the glide
        /// path's approach for too long (blown through it, overshot the runway, or pulled off by a missile break), the
        /// touchdown would be way past the aim point, or we're about to touch down beside the runway.
        /// </summary>
        /// <param name="distance">Along the runway to the aim point (<c>ApproachGuide.Distance</c>; negative past it).</param>
        /// <param name="lateral">Off the centerline (<c>ApproachGuide.Lateral</c>).</param>
        public static GoAroundReason ShouldGoAround(bool onApproach, float offApproachTime, float distance, float lateral,
            float runwayHalfWidth, bool runwayBusy, float maxOffApproachTime = 4f, float maxOvershoot = 250f)
        {
            if (runwayBusy && distance > 300f)
                return GoAroundReason.RunwayBusy;
            if (!onApproach && offApproachTime > maxOffApproachTime)
                return GoAroundReason.OffApproach;
            if (distance < -maxOvershoot)
                return GoAroundReason.Overshot;
            return distance < 150f && Mathf.Abs(lateral) > runwayHalfWidth + 10f ? GoAroundReason.OffCenter : GoAroundReason.None;
        }
    }
}

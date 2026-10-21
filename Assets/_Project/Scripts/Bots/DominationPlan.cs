using System.Collections.Generic;
using UnityEngine;
using WingingIt.Match;

namespace WingingIt.Bots
{
    /// <summary>A bot's part in Airfield Domination (Design/02-Mechanics/bots.md#airfield-domination).</summary>
    public enum DominationRole
    {
        /// <summary>Normal bot: patrols and fights. Every bot outside a Domination match.</summary>
        None,
        /// <summary>Lands on its field to take it.</summary>
        Capturer,
        /// <summary>Circles over its field, fighting what comes near and strafing enemies parked on it.</summary>
        Cover,
    }

    /// <summary>What a bot is doing about the capture fields right now.</summary>
    public enum DominationDuty
    {
        /// <summary>Nothing field-related: patrol and fight as usual.</summary>
        None,
        /// <summary>Fly an approach lane into the field, land and sit in its zone until it's ours.</summary>
        Capture,
        /// <summary>Circle over the field, fighting enemies near it.</summary>
        Cover,
        /// <summary>Bomb the control tower of an enemy field (a plane with bombs left).</summary>
        BombTower,
    }

    /// <summary>The pure decisions behind the Domination bots, so they can be tested without a scene.</summary>
    public static class DominationPlan
    {
        /// <summary>A covering bot circles this high (m) above its field...</summary>
        public const float CoverHeight = 250f;
        /// <summary>...at this radius (m).</summary>
        public const float CoverRadius = 600f;
        /// <summary>A covering bot only takes on enemies this close (m) to its field.</summary>
        public const float CoverLeash = 1500f;
        /// <summary>A bomb run is flown level this high (m) above the field.</summary>
        public const float BombHeight = 120f;
        /// <summary>Drop when the predicted impact is this close (m, flat) to the tower: inside the 18 m blast, near full damage.</summary>
        public const float BombAimRadius = 10f;
        /// <summary>Past the tower, fly on this far (m) before turning back for another pass, so the next one is lined up.</summary>
        public const float BombRunOut = 1000f;
        /// <summary>Re-pick the field this often (s): progress changes every frame, the plan shouldn't.</summary>
        public const float ReplanInterval = 2f;

        static readonly DominationRole[] s_roles = { DominationRole.Capturer, DominationRole.Cover };

        /// <summary>The role of the <paramref name="slot"/>th bot on a team: capturer, cover, capturer... (4 and 3 in 7).</summary>
        public static DominationRole RoleForSlot(int slot) => s_roles[((slot % s_roles.Length) + s_roles.Length) % s_roles.Length];

        /// <summary>
        /// How much a field is worth sending bots to, for <paramref name="team"/>: our own field under attack (an enemy
        /// capturing or contesting it) most, then a neutral field or an enemy field whose tower is down, then a defended
        /// enemy field, and least our own quiet field (a little cover).
        /// </summary>
        public static float FieldValue(int team, FieldBoard.Field field)
        {
            if (field.Owner == team)
                return field.Contested || (field.CapturingTeam >= 0 && field.CapturingTeam != team && field.Progress > 0f) ? 4f : 1f;
            if (field.Owner == FieldBoard.Neutral)
                return 3f;
            return field.TowerUp ? 2f : 3f;
        }

        /// <summary>
        /// The field the <paramref name="slot"/>th bot goes to: slots spread over the fields in proportion to
        /// <paramref name="values"/> (a low-discrepancy walk, so 7 slots split close to the shares). -1 with no fields.
        /// </summary>
        public static int PickField(IReadOnlyList<float> values, int slot)
        {
            float total = 0f;
            foreach (float v in values)
                total += Mathf.Max(0f, v);
            if (total <= 0f)
                return values.Count > 0 ? ((slot % values.Count) + values.Count) % values.Count : -1;

            float u = Mathf.Repeat(slot * 0.618034f + 0.3f, 1f) * total;
            for (int i = 0; i < values.Count; i++)
            {
                u -= Mathf.Max(0f, values[i]);
                if (u < 0f)
                    return i;
            }
            return values.Count - 1;
        }

        /// <summary>
        /// The defended enemy field (owned by another team, tower standing) nearest <paramref name="position"/> for a
        /// bomber, or -1.
        /// </summary>
        public static int BombTarget(int team, IReadOnlyList<FieldBoard.Field> fields, IReadOnlyList<Vector3> positions, Vector3 position)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < fields.Count; i++)
            {
                var f = fields[i];
                if (f.Owner == team || f.Owner == FieldBoard.Neutral || !f.TowerUp)
                    continue;
                Vector3 d = positions[i] - position;
                d.y = 0f;
                if (d.sqrMagnitude < bestSqr)
                {
                    bestSqr = d.sqrMagnitude;
                    best = i;
                }
            }
            return best;
        }

        /// <summary>What a bot with <paramref name="role"/> should do about a field owned by <paramref name="owner"/>.</summary>
        public static DominationDuty Decide(DominationRole role, int team, int owner)
        {
            if (role == DominationRole.None)
                return DominationDuty.None;
            return role == DominationRole.Capturer && owner != team ? DominationDuty.Capture : DominationDuty.Cover;
        }

        /// <summary>A capturer parked on the field takes off again: it's ours now (or can't be taken), or an enemy landed too.</summary>
        public static bool ShouldLeaveField(bool stillCapturable, bool contested) => !stillCapturable || contested;

        /// <summary>A bomb released now lands on the tower: the predicted impact is on it, or within <see cref="BombAimRadius"/> of its foot.</summary>
        public static bool ShouldDrop(Vector3 impact, bool hitsTower, Vector3 tower) =>
            hitsTower || FlatDistance(impact, tower) <= BombAimRadius;

        /// <summary>
        /// Where a bomber at <paramref name="position"/> flying along <paramref name="velocity"/> heads: level at
        /// <see cref="BombHeight"/> over the tower, straight at it. Once past it (it's behind us and close) it's
        /// <paramref name="extending"/>: it flies on away from the tower until <see cref="BombRunOut"/> out, then turns back in.
        /// </summary>
        public static Vector3 BombRunPoint(Vector3 tower, float groundY, Vector3 position, Vector3 velocity, ref bool extending)
        {
            Vector3 flatVel = new Vector3(velocity.x, 0f, velocity.z);
            Vector3 toTower = tower - position;
            toTower.y = 0f;
            float distance = toTower.magnitude;
            bool passed = flatVel.sqrMagnitude > 1f && Vector3.Dot(flatVel, toTower) < 0f;
            if (!extending && passed && distance < BombRunOut)
                extending = true;
            else if (extending && distance >= BombRunOut)
                extending = false;

            Vector3 away = distance > 1f ? -toTower / distance : (flatVel.sqrMagnitude > 1f ? flatVel.normalized : Vector3.forward);
            Vector3 point = extending ? tower + away * (BombRunOut + 300f) : tower;
            point.y = groundY + BombHeight;
            return point;
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
    }
}

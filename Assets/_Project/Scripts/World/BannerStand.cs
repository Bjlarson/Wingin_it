using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A team's banner stand for Capture the Banner (Design/01-Game-Design/match-modes.md#capture-the-banner-prototype):
    /// a gate of two tall poles past the far end of its home runway (so a grab run isn't straight through the field's AA),
    /// with the banner hanging between them. The poles have no colliders: planes fly through them.
    /// This transform is the middle of the gate, on the ground. The grab / capture zone is a cylinder round it
    /// (<see cref="ZoneRadius"/> wide, the poles at its edge, counting only below <see cref="ZoneHeight"/> above the
    /// ground); the rules that use it are
    /// <see cref="Match.BannerBoard"/>, which the level's stands feed through <see cref="Position"/>.
    /// </summary>
    public class BannerStand : MonoBehaviour
    {
        static readonly List<BannerStand> s_all = new();

        /// <summary>Every enabled banner stand in the loaded scenes.</summary>
        public static IReadOnlyList<BannerStand> All => s_all;

        [Tooltip("The team whose banner this is (and whose home it stands at).")]
        [SerializeField] int team;
        [Tooltip("Horizontal radius of the grab / capture zone (m).")]
        [SerializeField, Min(1f)] float zoneRadius = 20f;
        [Tooltip("The zone only counts below this height above the ground (m).")]
        [SerializeField, Min(1f)] float zoneHeight = 40f;
        [Tooltip("Where the banner hangs from while it's home: the middle of the line between the poles.")]
        [SerializeField] Transform flagMount;
        [Tooltip("The cloth that flies from the pole; the towed banner takes it along while it's carried. Empty: one is made at runtime.")]
        [SerializeField] Transform cloth;

        public int Team => team;
        public Transform Cloth => cloth;
        public float ZoneRadius => zoneRadius;
        public float ZoneHeight => zoneHeight;
        public Transform FlagMount => flagMount;
        /// <summary>The middle of the gate, on the ground: what the banner rules take as the stand's place.</summary>
        public Vector3 Position => transform.position;

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        public void Configure(int owner, float radius, float height, Transform mount, Transform flagCloth = null)
        {
            team = owner;
            zoneRadius = radius;
            zoneHeight = height;
            flagMount = mount;
            cloth = flagCloth;
        }

        /// <summary><paramref name="team"/>'s stand, or null.</summary>
        public static BannerStand Of(int team)
        {
            foreach (var stand in s_all)
                if (stand.team == team)
                    return stand;
            return null;
        }

        /// <summary>The stands' positions indexed by team (for <see cref="Match.BannerMode.SetStands"/>), or null if a team has none.</summary>
        public static Vector3[] PositionsByTeam(int teamCount)
        {
            var positions = new Vector3[teamCount];
            for (int t = 0; t < teamCount; t++)
            {
                var stand = Of(t);
                if (stand == null)
                    return null;
                positions[t] = stand.Position;
            }
            return positions;
        }

        void OnDrawGizmos()
        {
            Gizmos.color = team == 0 ? new Color(0.3f, 0.6f, 1f) : new Color(1f, 0.45f, 0.3f);
            Vector3 foot = transform.position;
            const int steps = 32;
            for (int i = 0; i < steps; i++)
            {
                float a0 = i * Mathf.PI * 2f / steps, a1 = (i + 1) * Mathf.PI * 2f / steps;
                var p0 = new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * zoneRadius;
                var p1 = new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * zoneRadius;
                Gizmos.DrawLine(foot + p0, foot + p1);
                Gizmos.DrawLine(foot + p0 + Vector3.up * zoneHeight, foot + p1 + Vector3.up * zoneHeight);
            }
            Gizmos.DrawLine(foot, foot + Vector3.up * zoneHeight);
        }
    }
}

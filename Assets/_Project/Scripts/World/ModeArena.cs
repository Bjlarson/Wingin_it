using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>The pure rules behind <see cref="ModeArena"/>'s air start: a ring of slots facing in, filled spread out.</summary>
    public static class ArenaRules
    {
        /// <summary>
        /// The <paramref name="slot"/>th of <paramref name="count"/> starting slots evenly round a ring of
        /// <paramref name="radius"/> about <paramref name="center"/> (XZ), at world height <paramref name="altitude"/>,
        /// level and facing the middle. Slot 0 is due north of the center; slots go clockwise seen from above.
        /// </summary>
        public static Pose RingSlot(Vector3 center, float radius, float altitude, int slot, int count)
        {
            count = Mathf.Max(1, count);
            Vector3 outward = Quaternion.Euler(0f, slot * 360f / count, 0f) * Vector3.forward;
            var position = new Vector3(center.x, altitude, center.z) + outward * radius;
            return new Pose(position, Quaternion.LookRotation(-outward));
        }

        /// <summary>
        /// The slot (index into <paramref name="slots"/>) furthest from every plane already in the air
        /// (<paramref name="occupied"/>), so a field of any size spreads round the whole ring: the first plane takes slot
        /// 0, the second the far side, and so on. Ties go to the lower index. -1 if there are no slots.
        /// </summary>
        public static int SpreadSlot(IReadOnlyList<Vector3> slots, IReadOnlyList<Vector3> occupied)
        {
            int best = -1;
            float bestSqr = -1f;
            for (int i = 0; i < slots.Count; i++)
            {
                float nearest = float.MaxValue;
                foreach (var p in occupied)
                    nearest = Mathf.Min(nearest, (p - slots[i]).sqrMagnitude);
                if (nearest > bestSqr + 1e-3f)
                {
                    best = i;
                    bestSqr = nearest;
                }
            }
            return best;
        }
    }

    /// <summary>
    /// One game mode's arena on a level (Design/01-Game-Design/match-modes.md#arenas-built-2026-10-19): its play area
    /// (center = this transform, radius, ceiling, soft turn-back or hard edge), which airfields stay open, where planes
    /// start (the usual airfields or a ring in the air facing in) and the bots' patrol points. The level carries the
    /// geometry, the mode only names it: <see cref="For"/> finds the arena whose <see cref="ModeId"/> matches the mode
    /// being played, and a mode with none plays the level as built. The match applies it when the match starts
    /// (<see cref="Apply"/>) and puts the level back when it's dropped (<see cref="Restore"/>).
    /// </summary>
    public class ModeArena : MonoBehaviour
    {
        static readonly List<ModeArena> s_all = new();

        [Tooltip("The GameModeInfo.Id of the mode this arena is for.")]
        [SerializeField] string modeId;

        [Header("Play area")]
        [Tooltip("Horizontal radius (m) round this transform.")]
        [SerializeField, Min(50f)] float radius = 700f;
        [Tooltip("Distance inside the radius where the edge warning starts.")]
        [SerializeField, Min(0f)] float warningMargin = 150f;
        [Tooltip("World-space altitude limit.")]
        [SerializeField] float ceiling = 450f;
        [Tooltip("Past the edge is out (after the boundary's grace), instead of the soft turn-back.")]
        [SerializeField] bool hardEdge = true;

        [Header("Airfields")]
        [Tooltip("Every airfield on the level is closed for the match: no repairs, no airfield spawns, no AA.")]
        [SerializeField] bool closeAllAirfields;
        [Tooltip("Airfields closed for the match (when not closing all of them).")]
        [SerializeField] Airfield[] closedAirfields = { };

        [Header("Start")]
        [Tooltip("Planes start (and respawn) on a ring in the air facing in, instead of at their airfields.")]
        [SerializeField] bool airStart = true;
        [Tooltip("Radius (m) of the starting ring.")]
        [SerializeField, Min(0f)] float startRingRadius = 550f;
        [Tooltip("World height of the starting ring.")]
        [SerializeField] float startAltitude = 220f;
        [Tooltip("Slots round the ring (at least the most planes the mode has).")]
        [SerializeField, Min(1)] int startSlots = 16;

        [Header("Bots")]
        [Tooltip("Where bots patrol when nothing's in sight (world points). Empty keeps the level's fight areas.")]
        [SerializeField] Vector3[] fightAreas = { };

        readonly List<Airfield> _closed = new();
        FlightBoundary _boundary;

        public string ModeId => modeId;
        public float Radius => radius;
        public float Ceiling => ceiling;
        public bool HardEdge => hardEdge;
        public bool AirStart => airStart;
        public float StartRingRadius => startRingRadius;
        public float StartAltitude => startAltitude;
        public IReadOnlyList<Vector3> FightAreas => fightAreas;
        /// <summary>Its play area and airfields are in force (between <see cref="Apply"/> and <see cref="Restore"/>).</summary>
        public bool IsApplied { get; private set; }

        public void Configure(string mode, float areaRadius, float areaWarningMargin, float areaCeiling, bool areaHardEdge)
        {
            modeId = mode;
            radius = areaRadius;
            warningMargin = areaWarningMargin;
            ceiling = areaCeiling;
            hardEdge = areaHardEdge;
        }

        public void ConfigureAirfields(bool closeAll, Airfield[] closed = null)
        {
            closeAllAirfields = closeAll;
            closedAirfields = closed ?? new Airfield[0];
        }

        public void ConfigureStart(bool inTheAir, float ringRadius, float altitude, int slots)
        {
            airStart = inTheAir;
            startRingRadius = ringRadius;
            startAltitude = altitude;
            startSlots = slots;
        }

        public void ConfigureFightAreas(Vector3[] areas) => fightAreas = areas ?? new Vector3[0];

        void OnEnable() => s_all.Add(this);

        void OnDisable()
        {
            s_all.Remove(this);
            Restore();
        }

        /// <summary>The loaded level's arena for the mode <paramref name="id"/>, or null (play the level as built).</summary>
        public static ModeArena For(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            foreach (var arena in s_all)
                if (arena.modeId == id)
                    return arena;
            return null;
        }

        /// <summary>Swaps in this arena's play area and closes its airfields.</summary>
        public void Apply()
        {
            if (IsApplied)
                return;
            IsApplied = true;
            _boundary = FlightBoundary.Active;
            if (_boundary != null)
                _boundary.UseArea(transform.position, radius, warningMargin, ceiling, hardEdge);
            _closed.Clear();
            var fields = closeAllAirfields ? (IEnumerable<Airfield>)Airfield.All : closedAirfields;
            foreach (var field in fields)
                if (field != null && field.IsOpen)
                    _closed.Add(field);
            foreach (var field in _closed)
                field.SetOpen(false);
        }

        /// <summary>Puts the level's own play area back and reopens the airfields it closed.</summary>
        public void Restore()
        {
            if (!IsApplied)
                return;
            IsApplied = false;
            if (_boundary != null)
                _boundary.RestoreArea();
            _boundary = null;
            foreach (var field in _closed)
                if (field != null)
                    field.SetOpen(true);
            _closed.Clear();
        }

        /// <summary>
        /// Where <paramref name="plane"/> starts in an air-start arena: the ring slot furthest from every other live
        /// plane (see <see cref="ArenaRules.SpreadSlot"/>). False for an arena that keeps the airfield spawns.
        /// </summary>
        public bool TryGetSpawnPose(PlaneController plane, out Pose pose)
        {
            pose = default;
            if (!airStart)
                return false;
            var slots = new Vector3[startSlots];
            var poses = new Pose[startSlots];
            for (int i = 0; i < startSlots; i++)
            {
                poses[i] = ArenaRules.RingSlot(transform.position, startRingRadius, startAltitude, i, startSlots);
                slots[i] = poses[i].position;
            }
            var occupied = new List<Vector3>();
            foreach (var other in PlaneController.All)
                if (other != plane && other.IsAlive)
                    occupied.Add(other.Position);
            pose = poses[Mathf.Max(0, ArenaRules.SpreadSlot(slots, occupied))];
            return true;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = hardEdge ? Color.red : Color.yellow;
            DrawCircle(transform.position, radius);
            DrawCircle(new Vector3(transform.position.x, ceiling, transform.position.z), radius);
            if (airStart)
            {
                Gizmos.color = Color.cyan;
                DrawCircle(new Vector3(transform.position.x, startAltitude, transform.position.z), startRingRadius);
            }
        }

        static void DrawCircle(Vector3 center, float r)
        {
            const int segments = 64;
            Vector3 prev = center + new Vector3(0f, 0f, r);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Sin(a) * r, 0f, Mathf.Cos(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

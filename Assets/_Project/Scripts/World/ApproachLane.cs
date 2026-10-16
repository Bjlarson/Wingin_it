using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// One route into an airfield (island-map.md: "each airfield should have 2–3 approach routes, so retreating
    /// players can't be trivially intercepted"). A polyline of world points from where the lane leaves the fighting to
    /// its airfield's final approach fix, a point on the glide path that every lane into that field ends on. Bots will
    /// fly these to retreat; the debug HUD names the lane you're on and points at its next waypoint.
    /// </summary>
    public class ApproachLane : MonoBehaviour
    {
        static readonly List<ApproachLane> s_all = new();

        /// <summary>Every enabled lane in the loaded scenes.</summary>
        public static IReadOnlyList<ApproachLane> All => s_all;

        [SerializeField] string displayName = "Approach Lane";
        [SerializeField] Airfield airfield;
        [Tooltip("World points from the lane's entry to the final approach fix (last), at the altitude to fly them.")]
        [SerializeField] Vector3[] points = System.Array.Empty<Vector3>();
        [Tooltip("Within this far (m, horizontally) of the lane counts as on it.")]
        [SerializeField] float halfWidth = 150f;

        public string DisplayName => displayName;
        public Airfield Airfield => airfield;
        public IReadOnlyList<Vector3> Points => points;
        public float HalfWidth => halfWidth;
        public Vector3 Entry => points.Length > 0 ? points[0] : transform.position;
        /// <summary>Where the lane joins the glide path: the same point for every lane into this airfield.</summary>
        public Vector3 FinalFix => points.Length > 0 ? points[^1] : transform.position;
        public float Length => PathLength(points);

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        public void Configure(string name, Airfield field, Vector3[] path, float width)
        {
            displayName = name;
            airfield = field;
            points = path;
            halfWidth = width;
        }

        /// <summary>Horizontal distance from the lane; <paramref name="along"/> is how far down it that closest point is.</summary>
        public float DistanceFrom(Vector3 position, out float along) => FlatDistanceToPath(points, position, out along);

        public bool Contains(Vector3 position) => DistanceFrom(position, out _) <= halfWidth;

        /// <summary>Meters left to fly to the final approach fix from <paramref name="position"/> (projected onto the lane).</summary>
        public float RemainingDistance(Vector3 position)
        {
            DistanceFrom(position, out float along);
            return Mathf.Max(0f, Length - along);
        }

        /// <summary>
        /// The waypoint to steer for from <paramref name="position"/>: the first point further down the lane than
        /// where we are, plus <paramref name="lookAhead"/> m so a plane passing a point turns for the next one in time.
        /// </summary>
        public Vector3 NextWaypoint(Vector3 position, float lookAhead = 100f)
        {
            DistanceFrom(position, out float along);
            return points[NextIndex(points, along + lookAhead)];
        }

        /// <summary>The lane into a field friendly to <paramref name="team"/> that <paramref name="position"/> is on (the closest), or null.</summary>
        public static ApproachLane FriendlyLaneAt(Vector3 position, int team)
        {
            ApproachLane best = null;
            float bestDistance = float.MaxValue;
            foreach (var lane in s_all)
            {
                if (lane.airfield == null || !lane.airfield.IsFriendlyTo(team))
                    continue;
                float d = lane.DistanceFrom(position, out _);
                if (d <= lane.halfWidth && d < bestDistance)
                {
                    bestDistance = d;
                    best = lane;
                }
            }
            return best;
        }

        /// <summary>Every lane into <paramref name="field"/>.</summary>
        public static List<ApproachLane> Into(Airfield field)
        {
            var lanes = new List<ApproachLane>();
            foreach (var lane in s_all)
                if (lane.airfield == field)
                    lanes.Add(lane);
            return lanes;
        }

        // ------------------------------------------------------------------ polyline math (pure, EditMode-tested)

        public static float PathLength(IReadOnlyList<Vector3> path)
        {
            float length = 0f;
            for (int i = 1; i < path.Count; i++)
                length += Vector3.Distance(path[i - 1], path[i]);
            return length;
        }

        /// <summary>
        /// Horizontal (XZ) distance from <paramref name="p"/> to the polyline <paramref name="path"/>, and how far along
        /// the path (3D length from its start) the closest point is. Infinity for an empty path.
        /// </summary>
        public static float FlatDistanceToPath(IReadOnlyList<Vector3> path, Vector3 p, out float along)
        {
            along = 0f;
            if (path.Count == 0)
                return float.PositiveInfinity;
            if (path.Count == 1)
                return Flat(path[0] - p).magnitude;

            float best = float.MaxValue, walked = 0f;
            var q = new Vector2(p.x, p.z);
            for (int i = 1; i < path.Count; i++)
            {
                Vector3 a = path[i - 1], b = path[i];
                Vector2 a2 = new Vector2(a.x, a.z), ab = new Vector2(b.x, b.z) - a2;
                float t = ab.sqrMagnitude > 1e-6f ? Mathf.Clamp01(Vector2.Dot(q - a2, ab) / ab.sqrMagnitude) : 0f;
                float d = Vector2.Distance(q, a2 + ab * t);
                float segment = Vector3.Distance(a, b);
                if (d < best)
                {
                    best = d;
                    along = walked + segment * t;
                }
                walked += segment;
            }
            return best;
        }

        /// <summary>Index of the first point more than <paramref name="along"/> m down the path (the last point once past it).</summary>
        public static int NextIndex(IReadOnlyList<Vector3> path, float along)
        {
            float walked = 0f;
            for (int i = 1; i < path.Count; i++)
            {
                walked += Vector3.Distance(path[i - 1], path[i]);
                if (walked > along)
                    return i;
            }
            return Mathf.Max(0, path.Count - 1);
        }

        static Vector2 Flat(Vector3 v) => new Vector2(v.x, v.z);

        void OnDrawGizmos()
        {
            if (points.Length < 2)
                return;
            Gizmos.color = new Color(0.3f, 1f, 0.6f);
            for (int i = 1; i < points.Length; i++)
                Gizmos.DrawLine(points[i - 1], points[i]);
            foreach (var p in points)
                Gizmos.DrawWireSphere(p, 12f);
        }
    }
}

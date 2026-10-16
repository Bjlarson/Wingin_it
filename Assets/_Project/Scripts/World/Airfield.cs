using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A runway with a repair zone and spawn points. The runway runs along the local Z axis, centered on this
    /// transform, and planes take off and land heading +Z. Home airfields belong to a team; neutral ones
    /// (<see cref="Neutral"/>) belong to nobody until captured.
    /// </summary>
    public class Airfield : MonoBehaviour
    {
        public const int Neutral = -1;
        /// <summary>Planes that can wait on the runway at once: rows of three, side by side (see <see cref="RunwaySpawnSlot"/>).</summary>
        public const int RunwaySpawnSlots = 9;
        /// <summary>Gap between runway spawn lanes (m): a Heavy's wingspan plus room to spare.</summary>
        public const float RunwayLaneSpacing = 13f;
        /// <summary>Gap between runway spawn rows (m), further down the runway.</summary>
        public const float RunwayRowSpacing = 50f;

        static readonly List<Airfield> s_all = new();

        /// <summary>Every enabled airfield in the loaded scenes.</summary>
        public static IReadOnlyList<Airfield> All => s_all;

        [SerializeField] string displayName = "Home Airfield";
        [Tooltip("Owning team, or -1 for a neutral airfield.")]
        [SerializeField] int team;
        [Tooltip("A team's starting airfield: gets AA guns and can't be captured.")]
        [SerializeField] bool isHome = true;
        [Tooltip("Runway width (x) and length (y) in meters.")]
        [SerializeField] Vector2 runwaySize = new Vector2(40f, 600f);
        [Tooltip("Where planes appear after dying: in the air on the approach, heading down the runway.")]
        [SerializeField] Transform airSpawn;
        [Tooltip("The start of the runway, for a takeoff spawn (levels pick air or runway spawns in LevelSettings).")]
        [SerializeField] Transform runwaySpawn;
        [SerializeField] RepairZone repairZone;
        [Tooltip("The landing approach the landing-assist guide shows.")]
        [SerializeField] GlideSlope glideSlope = new();

        public string DisplayName => displayName;
        public int Team => team;
        public bool IsHome => isHome;
        public bool IsNeutral => team == Neutral;
        public Vector2 RunwaySize => runwaySize;
        public Transform AirSpawn => airSpawn;
        public Transform RunwaySpawn => runwaySpawn;
        public RepairZone RepairZone => repairZone;
        public GlideSlope GlideSlope => glideSlope;
        /// <summary>The landing and takeoff heading.</summary>
        public Vector3 RunwayDirection => transform.forward;

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        public void Configure(string name, int owner, bool home, Vector2 size, Transform air, Transform runway, RepairZone zone)
        {
            displayName = name;
            team = owner;
            isHome = home;
            runwaySize = size;
            airSpawn = air;
            runwaySpawn = runway;
            repairZone = zone;
        }

        /// <summary>
        /// Where the <paramref name="slot"/>th plane waiting to take off is parked: the runway spawn, then side by side
        /// across the runway (center, left, right), then the next row further down it. All face down the runway. Slots
        /// past <see cref="RunwaySpawnSlots"/> wrap around.
        /// </summary>
        public Pose RunwaySpawnSlot(int slot)
        {
            Transform spawn = runwaySpawn != null ? runwaySpawn : transform;
            slot = ((slot % RunwaySpawnSlots) + RunwaySpawnSlots) % RunwaySpawnSlots;
            int lane = slot % 3, row = slot / 3;
            float x = lane == 0 ? 0f : lane == 1 ? -RunwayLaneSpacing : RunwayLaneSpacing;
            Vector3 offset = transform.right * x + RunwayDirection * (row * RunwayRowSpacing);
            return new Pose(spawn.position + offset, Quaternion.LookRotation(RunwayDirection));
        }

        /// <summary>Where a plane at <paramref name="position"/> flying at <paramref name="velocity"/> is relative to this runway's glide path.</summary>
        public ApproachGuide GetApproach(Vector3 position, Vector3 velocity) =>
            glideSlope.Evaluate(transform.InverseTransformPoint(position), transform.InverseTransformDirection(velocity), runwaySize);

        /// <summary>The world point on the glide path <paramref name="distance"/> m before the aim point (the aim point itself at 0).</summary>
        public Vector3 GlidePathPoint(float distance) => transform.TransformPoint(glideSlope.PathPoint(distance, runwaySize.y));

        /// <summary>Planes on this team can land here to repair. Neutral airfields serve nobody until captured.</summary>
        public bool IsFriendlyTo(int planeTeam) => !IsNeutral && team == planeTeam;

        /// <summary>True if <paramref name="position"/> is over the runway and no more than <paramref name="maxHeight"/> above it.</summary>
        public bool IsOverRunway(Vector3 position, float maxHeight = 10f)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= runwaySize.x * 0.5f
                && Mathf.Abs(local.z) <= runwaySize.y * 0.5f
                && local.y >= -1f && local.y <= maxHeight;
        }

        /// <summary>The airfield whose runway <paramref name="position"/> is over (see <see cref="IsOverRunway"/>), or null.</summary>
        public static Airfield RunwayUnder(Vector3 position, float maxHeight = 10f)
        {
            foreach (var field in s_all)
                if (field.IsOverRunway(position, maxHeight))
                    return field;
            return null;
        }

        /// <summary>The closest airfield <paramref name="planeTeam"/> can repair at, or null if there is none.</summary>
        public static Airfield NearestFriendly(Vector3 position, int planeTeam)
        {
            Airfield best = null;
            float bestSqr = float.MaxValue;
            foreach (var field in s_all)
            {
                if (!field.IsFriendlyTo(planeTeam))
                    continue;
                float sqr = (field.transform.position - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = field;
                }
            }
            return best;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = IsNeutral ? Color.gray : new Color(0.3f, 0.6f, 1f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(runwaySize.x, 0.5f, runwaySize.y));
            Gizmos.DrawLine(new Vector3(0f, 1f, runwaySize.y * 0.3f), new Vector3(0f, 1f, runwaySize.y * 0.5f));
        }
    }
}

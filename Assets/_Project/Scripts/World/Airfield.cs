using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A runway with a repair zone and spawn points. The runway runs along the local Z axis, centered on this
    /// transform, and planes take off and land heading +Z. Home airfields belong to a team; neutral ones
    /// (<see cref="Neutral"/>) belong to nobody until captured. A match mode's arena can close a field for the match
    /// (<see cref="IsOpen"/>): it stays as scenery, but nobody repairs or spawns there, its AA is off and the HUD drops it.
    /// </summary>
    public class Airfield : MonoBehaviour
    {
        public const int Neutral = -1;
        /// <summary>Planes that can wait for takeoff at once, in a file beside the runway (see <see cref="RunwaySpawnSlot"/>).</summary>
        public const int RunwaySpawnSlots = 9;
        /// <summary>How far (m) off the runway's left edge the takeoff file's centreline is: a Heavy's half span and room to spare.</summary>
        public const float RunwayHoldOffset = 16f;
        /// <summary>Gap (m) between planes in the takeoff file, further down the runway.</summary>
        public const float RunwaySlotSpacing = 30f;
        /// <summary>Planes that can appear over the field at once: rows of three in the air (see <see cref="AirSpawnSlot"/>).</summary>
        public const int AirSpawnSlots = 9;
        /// <summary>Gap between air spawn lanes (m), wide enough that nobody spawns in a neighbor's wake.</summary>
        public const float AirLaneSpacing = 40f;
        /// <summary>Each air spawn row is this far (m) behind the one before it...</summary>
        public const float AirRowSpacing = 60f;
        /// <summary>...and this much (m) higher, so a row never flies into the one ahead.</summary>
        public const float AirRowStep = 10f;

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
        [Tooltip("Where a plane parks inside the hangar, nose toward the doors (the main menu backdrop).")]
        [SerializeField] Transform hangarSpot;
        [Tooltip("The landing approach the landing-assist guide shows.")]
        [SerializeField] GlideSlope glideSlope = new();
        [Tooltip("Landable from either end: the glide path, guide and lanes mirror at the far end. Home fields are one-way.")]
        [SerializeField] bool twoWay;

        /// <summary>In use this match. A closed field (<see cref="SetOpen"/>) is only scenery.</summary>
        public bool IsOpen { get; private set; } = true;
        /// <summary>Its control tower is bombed out (<see cref="FieldDefenses"/>): no repair and no AA until it rebuilds.</summary>
        public bool TowerDown { get; private set; }
        /// <summary>A capture field in play (Airfield Domination): planes of other teams land on it to take it.</summary>
        public bool IsCapturable { get; private set; }

        public string DisplayName => displayName;
        public int Team => team;
        public bool IsHome => isHome;
        public bool IsNeutral => team == Neutral;
        public Vector2 RunwaySize => runwaySize;
        public Transform AirSpawn => airSpawn;
        public Transform RunwaySpawn => runwaySpawn;
        public RepairZone RepairZone => repairZone;
        public Transform HangarSpot => hangarSpot;
        public GlideSlope GlideSlope => glideSlope;
        public bool IsTwoWay => twoWay;
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

        /// <summary>Names a field spawned from a map layout and gives it its owner (before it's enabled).</summary>
        public void SetIdentity(string name, int owner, bool home)
        {
            displayName = name;
            team = owner;
            isHome = home;
        }

        public void SetTwoWay(bool value) => twoWay = value;

        public void SetHangarSpot(Transform spot) => hangarSpot = spot;

        /// <summary>Opens or closes the field for the match (see <see cref="IsOpen"/>).</summary>
        public void SetOpen(bool open) => IsOpen = open;

        /// <summary>Hands the field to <paramref name="owner"/> (or <see cref="Neutral"/>): a capture. Home fields can't change hands.</summary>
        public void SetTeam(int owner)
        {
            if (!isHome)
                team = owner;
        }

        /// <summary>Set by the field's <see cref="FieldDefenses"/> while its tower is a wreck.</summary>
        public void SetTowerDown(bool down) => TowerDown = down;

        /// <summary>Makes the field one to capture this match (see <see cref="IsCapturable"/>), or not.</summary>
        public void SetCapturable(bool capturable) => IsCapturable = capturable && !isHome;

        /// <summary><paramref name="planeTeam"/> can land here to take it: an open capture field that isn't already theirs.</summary>
        public bool IsCapturableBy(int planeTeam) => IsOpen && IsCapturable && team != planeTeam;

        /// <summary>
        /// The fields <paramref name="planeTeam"/> can respawn at (see <see cref="IsFriendlyTo"/>): its home first, then
        /// the rest by name. Usually just home; owned capture fields join in Airfield Domination.
        /// </summary>
        public static List<Airfield> SpawnFieldsFor(int planeTeam)
        {
            var fields = new List<Airfield>();
            foreach (var field in s_all)
                if (field.IsFriendlyTo(planeTeam) && (field.airSpawn != null || field.runwaySpawn != null))
                    fields.Add(field);
            fields.Sort((a, b) => a.isHome != b.isHome ? (a.isHome ? -1 : 1) : string.CompareOrdinal(a.displayName, b.displayName));
            return fields;
        }

        /// <summary><paramref name="team"/>'s home airfield, or null.</summary>
        public static Airfield HomeOf(int team)
        {
            foreach (var field in s_all)
                if (field.isHome && field.team == team)
                    return field;
            return null;
        }

        /// <summary>
        /// Where the <paramref name="slot"/>th plane waiting to take off is parked: on the flat ground just off the
        /// runway's left edge (the side without buildings), level with the runway spawn, then one after another further
        /// down. All face down the runway, so a plane rolls out and turns onto it (or lifts off beside it), and nobody
        /// waits on the strip planes are landing on. Slots past <see cref="RunwaySpawnSlots"/> wrap around.
        /// </summary>
        public Pose RunwaySpawnSlot(int slot)
        {
            Transform spawn = runwaySpawn != null ? runwaySpawn : transform;
            slot = ((slot % RunwaySpawnSlots) + RunwaySpawnSlots) % RunwaySpawnSlots;
            Vector3 local = transform.InverseTransformPoint(spawn.position);
            float x = -(runwaySize.x * 0.5f + RunwayHoldOffset) - local.x;
            Vector3 offset = transform.right * x + RunwayDirection * (slot * RunwaySlotSpacing);
            return new Pose(spawn.position + offset, Quaternion.LookRotation(RunwayDirection));
        }

        /// <summary>
        /// Where the <paramref name="slot"/>th plane appearing in the air is put: the air spawn, then beside it (left,
        /// right), then the next row behind and a little above. All face the air spawn's heading. Slots past
        /// <see cref="AirSpawnSlots"/> wrap around.
        /// </summary>
        public Pose AirSpawnSlot(int slot)
        {
            Transform spawn = airSpawn != null ? airSpawn : transform;
            slot = ((slot % AirSpawnSlots) + AirSpawnSlots) % AirSpawnSlots;
            int lane = slot % 3, row = slot / 3;
            float x = lane == 0 ? 0f : lane == 1 ? -AirLaneSpacing : AirLaneSpacing;
            Vector3 offset = spawn.right * x - spawn.forward * (row * AirRowSpacing) + Vector3.up * (row * AirRowStep);
            return new Pose(spawn.position + offset, spawn.rotation);
        }

        /// <summary>
        /// Which end a plane at <paramref name="position"/> flying at <paramref name="velocity"/> is landing at: false =
        /// the normal end (heading <see cref="RunwayDirection"/>), true = the far end of a <see cref="IsTwoWay"/> runway
        /// (heading the other way). Decided by heading, or by which end it is nearer when nearly stopped. Always false
        /// on a one-way field.
        /// </summary>
        public bool ApproachesFromReverseEnd(Vector3 position, Vector3 velocity)
        {
            if (!twoWay)
                return false;
            Vector3 flat = new Vector3(velocity.x, 0f, velocity.z);
            if (flat.sqrMagnitude > 1f)
                return Vector3.Dot(flat, RunwayDirection) < 0f;
            return transform.InverseTransformPoint(position).z > 0f;
        }

        /// <summary>The landing and takeoff heading at the normal (false) or far (true) end.</summary>
        public Vector3 RunwayDirectionFor(bool reverseEnd) => reverseEnd ? -RunwayDirection : RunwayDirection;

        /// <summary>Right of the runway looking down it from the normal (false) or far (true) end.</summary>
        public Vector3 RunwayRightFor(bool reverseEnd) => reverseEnd ? -transform.right : transform.right;

        // Runway-local point or direction as seen from the far end: turned half a circle about the vertical axis.
        static Vector3 Flip(Vector3 local, bool reverseEnd) => reverseEnd ? new Vector3(-local.x, local.y, -local.z) : local;

        /// <summary>Where a plane at <paramref name="position"/> flying at <paramref name="velocity"/> is relative to this runway's glide path (at the end it is heading for, see <see cref="ApproachesFromReverseEnd"/>).</summary>
        public ApproachGuide GetApproach(Vector3 position, Vector3 velocity) =>
            GetApproach(position, velocity, ApproachesFromReverseEnd(position, velocity));

        /// <summary>The same, judged against the glide path of one chosen end.</summary>
        public ApproachGuide GetApproach(Vector3 position, Vector3 velocity, bool reverseEnd) =>
            glideSlope.Evaluate(Flip(transform.InverseTransformPoint(position), reverseEnd),
                Flip(transform.InverseTransformDirection(velocity), reverseEnd), runwaySize);

        /// <summary>The world point on the glide path <paramref name="distance"/> m before the aim point (the aim point itself at 0), at the normal end or, with <paramref name="reverseEnd"/>, the far one.</summary>
        public Vector3 GlidePathPoint(float distance, bool reverseEnd = false) =>
            transform.TransformPoint(Flip(glideSlope.PathPoint(distance, runwaySize.y), reverseEnd));

        /// <summary>
        /// Planes on this team can land here to repair. Neutral airfields serve nobody until captured, closed ones and
        /// ones with their tower down nobody at all.
        /// </summary>
        public bool IsFriendlyTo(int planeTeam) => IsOpen && !TowerDown && !IsNeutral && team == planeTeam;

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

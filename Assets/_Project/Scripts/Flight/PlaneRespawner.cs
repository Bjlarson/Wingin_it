using System;
using System.Collections;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// After any death (crash or shot down), waits <see cref="RespawnDelay"/> and puts the plane back at the spawn
    /// point, or at the nearest friendly airfield if no spawn point is set. The level's
    /// <see cref="LevelSettings.CurrentSpawnMode"/> picks how: in the air on the approach, or parked on the runway in
    /// the first free takeoff slot. Air spawns also take the first free slot, so a team respawning together doesn't
    /// appear inside itself. In <see cref="SpawnMode.Runway"/> levels a plane with no spawn point also starts
    /// the level on the runway.
    /// </summary>
    /// <summary>
    /// Rules a match mode puts on every respawn (see <see cref="PlaneRespawner.Rules"/>): whether a dead plane comes
    /// back at all (lives), where, and what it may carry once it does (loadout limits).
    /// </summary>
    public interface ISpawnRules
    {
        /// <summary>False keeps a dead plane out (no lives left). Asked when its respawn timer runs out.</summary>
        bool CanRespawn(PlaneController plane);

        /// <summary>A spawn pose of the mode's own (an air ring, an owned field); false for the usual one.</summary>
        bool TryGetSpawnPose(PlaneController plane, Vector3 deathPosition, out Pose pose, out bool onRunway);

        /// <summary>The plane was just put back (respawn or match start): the place to apply loadout limits.</summary>
        void OnSpawned(PlaneController plane);
    }

    [RequireComponent(typeof(PlaneController))]
    public class PlaneRespawner : MonoBehaviour
    {
        [Tooltip("Leave empty to spawn at the nearest friendly airfield. A set spawn point is always an air spawn.")]
        [SerializeField] Transform spawnPoint;
        [Tooltip("Seconds between dying and reappearing. Covers the explosion and the falling wreck.")]
        [SerializeField] float respawnDelay = 5f;

        /// <summary>A takeoff slot with another live plane closer than this (m, ignoring height) is taken.</summary>
        const float SlotClearance = 10f;
        /// <summary>An air spawn slot with another live plane closer than this (m) is taken.</summary>
        const float AirSlotClearance = 25f;

        PlaneController _plane;

        /// <summary>The running match mode's spawn rules, or null for the usual ones (free flight, tests).</summary>
        public static ISpawnRules Rules { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Rules = null;

        public float RespawnDelay => respawnDelay;
        public float RespawnTimeLeft { get; private set; }

        /// <summary>Raised just before the plane is put back, so a pending class change can apply first.</summary>
        public event Action<PlaneRespawner> Spawning;

        public void SetSpawnPoint(Transform point) => spawnPoint = point;

        void Awake() => _plane = GetComponent<PlaneController>();
        void OnEnable() => _plane.Crashed += OnCrashed;
        void OnDisable() => _plane.Crashed -= OnCrashed;

        void Start()
        {
            if (spawnPoint == null && LevelSettings.CurrentSpawnMode == SpawnMode.Runway)
                SpawnNow();
        }

        void OnCrashed(PlaneController plane, Vector3 point) => StartCoroutine(RespawnAfterDelay());

        /// <summary>Drops a respawn that's still counting down, for when something else puts the plane back in the air first.</summary>
        public void CancelPendingRespawn()
        {
            StopAllCoroutines();
            RespawnTimeLeft = 0f;
        }

        IEnumerator RespawnAfterDelay()
        {
            RespawnTimeLeft = respawnDelay;
            while (RespawnTimeLeft > 0f)
            {
                RespawnTimeLeft -= Time.deltaTime;
                yield return null;
            }
            RespawnTimeLeft = 0f;
            if (Rules == null || Rules.CanRespawn(_plane))
                SpawnNow();
        }

        /// <summary>Spawns the plane right away, where and how <see cref="SpawnPoseFor"/> says.</summary>
        public void SpawnNow()
        {
            CancelPendingRespawn();
            Spawning?.Invoke(this);
            Pose pose = SpawnPoseFor(_plane.Position, out bool onRunway);
            if (onRunway)
                _plane.SpawnOnGround(pose.position, pose.rotation);
            else
                _plane.Respawn(pose.position, pose.rotation);
            Rules?.OnSpawned(_plane);
        }

        /// <summary>Where the plane will reappear if it dies at <paramref name="deathPosition"/>; <paramref name="onRunway"/> = parked for takeoff.</summary>
        public Pose SpawnPoseFor(Vector3 deathPosition, out bool onRunway)
        {
            onRunway = false;
            if (Rules != null && Rules.TryGetSpawnPose(_plane, deathPosition, out var modePose, out onRunway))
                return modePose;
            if (spawnPoint != null)
                return new Pose(spawnPoint.position, spawnPoint.rotation);
            return SpawnPoseAt(Airfield.NearestFriendly(deathPosition, _plane.Team), out onRunway);
        }

        /// <summary>The usual spawn at <paramref name="field"/> (a free air or takeoff slot, per the level); where the plane is if null.</summary>
        public Pose SpawnPoseAt(Airfield field, out bool onRunway)
        {
            onRunway = false;
            if (field == null)
                return new Pose(transform.position, transform.rotation);
            if (LevelSettings.CurrentSpawnMode == SpawnMode.Runway && field.RunwaySpawn != null)
            {
                onRunway = true;
                return FreeRunwaySlot(field);
            }
            return field.AirSpawn != null ? FreeAirSlot(field) : new Pose(transform.position, transform.rotation);
        }

        /// <summary>The first takeoff slot on <paramref name="field"/>'s runway that no other live plane is sitting on (the first one if all are).</summary>
        Pose FreeRunwaySlot(Airfield field)
        {
            for (int slot = 0; slot < Airfield.RunwaySpawnSlots; slot++)
            {
                Pose pose = field.RunwaySpawnSlot(slot);
                if (!IsSlotTaken(pose.position))
                    return pose;
            }
            return field.RunwaySpawnSlot(0);
        }

        /// <summary>The first air spawn slot over <paramref name="field"/> with no other live plane in it (the first one if all are).</summary>
        Pose FreeAirSlot(Airfield field)
        {
            for (int slot = 0; slot < Airfield.AirSpawnSlots; slot++)
            {
                Pose pose = field.AirSpawnSlot(slot);
                if (!IsAirSlotTaken(pose.position))
                    return pose;
            }
            return field.AirSpawnSlot(0);
        }

        bool IsAirSlotTaken(Vector3 position)
        {
            foreach (var other in PlaneController.All)
                if (other != _plane && other.IsAlive && (other.Position - position).sqrMagnitude < AirSlotClearance * AirSlotClearance)
                    return true;
            return false;
        }

        bool IsSlotTaken(Vector3 position)
        {
            foreach (var other in PlaneController.All)
            {
                if (other == _plane || !other.IsAlive)
                    continue;
                Vector3 d = other.Position - position;
                if (Mathf.Abs(d.y) < 15f && new Vector2(d.x, d.z).sqrMagnitude < SlotClearance * SlotClearance)
                    return true;
            }
            return false;
        }
    }
}

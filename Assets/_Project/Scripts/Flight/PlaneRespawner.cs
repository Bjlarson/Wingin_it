using System.Collections;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// After any death (crash or shot down), waits <see cref="RespawnDelay"/> and puts the plane back at the spawn
    /// point, or at the nearest friendly airfield if no spawn point is set. The level's
    /// <see cref="LevelSettings.CurrentSpawnMode"/> picks how: in the air on the approach, or parked on the runway in
    /// the first free takeoff slot. In <see cref="SpawnMode.Runway"/> levels a plane with no spawn point also starts
    /// the level on the runway.
    /// </summary>
    [RequireComponent(typeof(PlaneController))]
    public class PlaneRespawner : MonoBehaviour
    {
        [Tooltip("Leave empty to spawn at the nearest friendly airfield. A set spawn point is always an air spawn.")]
        [SerializeField] Transform spawnPoint;
        [Tooltip("Seconds between dying and reappearing. Covers the explosion and the falling wreck.")]
        [SerializeField] float respawnDelay = 5f;

        /// <summary>A takeoff slot with another live plane closer than this (m, ignoring height) is taken.</summary>
        const float SlotClearance = 10f;

        PlaneController _plane;

        public float RespawnDelay => respawnDelay;
        public float RespawnTimeLeft { get; private set; }

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
            SpawnNow();
        }

        /// <summary>Spawns the plane right away, where and how <see cref="SpawnPoseFor"/> says.</summary>
        public void SpawnNow()
        {
            CancelPendingRespawn();
            Pose pose = SpawnPoseFor(_plane.Position, out bool onRunway);
            if (onRunway)
                _plane.SpawnOnGround(pose.position, pose.rotation);
            else
                _plane.Respawn(pose.position, pose.rotation);
        }

        /// <summary>Where the plane will reappear if it dies at <paramref name="deathPosition"/>; <paramref name="onRunway"/> = parked for takeoff.</summary>
        public Pose SpawnPoseFor(Vector3 deathPosition, out bool onRunway)
        {
            onRunway = false;
            if (spawnPoint != null)
                return new Pose(spawnPoint.position, spawnPoint.rotation);
            var field = Airfield.NearestFriendly(deathPosition, _plane.Team);
            if (field == null)
                return new Pose(transform.position, transform.rotation);
            if (LevelSettings.CurrentSpawnMode == SpawnMode.Runway && field.RunwaySpawn != null)
            {
                onRunway = true;
                return FreeRunwaySlot(field);
            }
            return field.AirSpawn != null ? new Pose(field.AirSpawn.position, field.AirSpawn.rotation)
                : new Pose(transform.position, transform.rotation);
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

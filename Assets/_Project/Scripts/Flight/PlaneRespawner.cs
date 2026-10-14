using System.Collections;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>After a crash, waits a moment and puts the plane back in the air at the spawn point.</summary>
    [RequireComponent(typeof(PlaneController))]
    public class PlaneRespawner : MonoBehaviour
    {
        [SerializeField] Transform spawnPoint;
        [SerializeField] float respawnDelay = 2.5f;

        PlaneController _plane;

        public float RespawnTimeLeft { get; private set; }

        public void SetSpawnPoint(Transform point) => spawnPoint = point;

        void Awake() => _plane = GetComponent<PlaneController>();
        void OnEnable() => _plane.Crashed += OnCrashed;
        void OnDisable() => _plane.Crashed -= OnCrashed;

        void OnCrashed(PlaneController plane, Vector3 point) => StartCoroutine(RespawnAfterDelay());

        IEnumerator RespawnAfterDelay()
        {
            RespawnTimeLeft = respawnDelay;
            while (RespawnTimeLeft > 0f)
            {
                RespawnTimeLeft -= Time.deltaTime;
                yield return null;
            }
            RespawnTimeLeft = 0f;

            Transform p = spawnPoint != null ? spawnPoint : transform;
            _plane.Respawn(p.position, p.rotation);
        }
    }
}

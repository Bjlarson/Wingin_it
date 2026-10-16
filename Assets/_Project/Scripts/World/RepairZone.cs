using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// The marked area on an airfield where a landed, stopped plane gets repaired. A box centered on this
    /// transform: <see cref="size"/> wide (x) and long (z), reaching <see cref="height"/> above the surface.
    /// The repair itself (tiers, timer) is <see cref="WingingIt.Flight.PlaneRepair"/>; this only answers "is it inside?".
    /// </summary>
    public class RepairZone : MonoBehaviour
    {
        [Tooltip("Zone width (x) and length (y) in meters.")]
        [SerializeField] Vector2 size = new Vector2(30f, 200f);
        [Tooltip("How far above the surface still counts as inside (a parked plane's pivot sits a few meters up).")]
        [SerializeField] float height = 8f;

        Airfield _airfield;

        public Vector2 Size => size;
        public float Height => height;

        /// <summary>The airfield this zone belongs to (its parent), or null for a standalone zone.</summary>
        public Airfield Airfield => _airfield != null ? _airfield : _airfield = GetComponentInParent<Airfield>();

        public void Configure(Vector2 zoneSize, float zoneHeight)
        {
            size = zoneSize;
            height = zoneHeight;
        }

        public bool Contains(Vector3 position)
        {
            Vector3 local = transform.InverseTransformPoint(position);
            return Mathf.Abs(local.x) <= size.x * 0.5f
                && Mathf.Abs(local.z) <= size.y * 0.5f
                && local.y >= -1f && local.y <= height;
        }

        void OnDrawGizmos()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 0.8f, 0.1f);
            Gizmos.DrawWireCube(new Vector3(0f, height * 0.5f, 0f), new Vector3(size.x, height, size.y));
        }
    }
}

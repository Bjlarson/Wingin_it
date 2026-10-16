using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A puffy cloud made of overlapping ellipsoid puffs, used as cover (<see cref="CloudCover"/>). The puffs are the
    /// cover volume and match the rendered spheres. Only the bank's position counts: don't rotate or scale it.
    /// </summary>
    public class CloudBank : MonoBehaviour
    {
        static readonly List<CloudBank> s_all = new();

        /// <summary>Every enabled cloud bank in the loaded scenes.</summary>
        public static IReadOnlyList<CloudBank> All => s_all;

        [SerializeField] CloudPuff[] puffs = System.Array.Empty<CloudPuff>();

        Vector3 _boundsOffset;
        float _boundsRadius;

        public IReadOnlyList<CloudPuff> Puffs => puffs;
        /// <summary>A sphere around every puff, for quick rejection.</summary>
        public Vector3 BoundsCenter => transform.position + _boundsOffset;
        public float BoundsRadius => _boundsRadius;

        void OnEnable()
        {
            RecalculateBounds();
            s_all.Add(this);
        }

        void OnDisable() => s_all.Remove(this);

        public void Configure(CloudPuff[] cloudPuffs)
        {
            puffs = cloudPuffs ?? System.Array.Empty<CloudPuff>();
            RecalculateBounds();
        }

        void RecalculateBounds()
        {
            if (puffs.Length == 0)
            {
                _boundsOffset = Vector3.zero;
                _boundsRadius = 0f;
                return;
            }
            Vector3 min = puffs[0].center - puffs[0].radii, max = puffs[0].center + puffs[0].radii;
            foreach (var p in puffs)
            {
                min = Vector3.Min(min, p.center - p.radii);
                max = Vector3.Max(max, p.center + p.radii);
            }
            _boundsOffset = (min + max) * 0.5f;
            _boundsRadius = 0f;
            foreach (var p in puffs)
            {
                float extent = Mathf.Max(p.radii.x, Mathf.Max(p.radii.y, p.radii.z));
                _boundsRadius = Mathf.Max(_boundsRadius, Vector3.Distance(p.center, _boundsOffset) + extent);
            }
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 1f, 1f, 0.6f);
            foreach (var p in puffs)
            {
                Gizmos.matrix = Matrix4x4.TRS(transform.position + p.center, Quaternion.identity, p.radii);
                Gizmos.DrawWireSphere(Vector3.zero, 1f);
            }
            Gizmos.matrix = Matrix4x4.identity;
        }
    }
}

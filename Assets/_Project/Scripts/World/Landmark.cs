using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A big, far-visible structure that names a region of the map for navigation (island-map.md: "each region needs
    /// a big landmark visible from far away"). Its beacon blinks so it reads at range and in fog.
    /// </summary>
    public class Landmark : MonoBehaviour
    {
        static readonly List<Landmark> s_all = new();

        /// <summary>Every enabled landmark in the loaded scenes.</summary>
        public static IReadOnlyList<Landmark> All => s_all;

        [SerializeField] string displayName = "Landmark";
        [Tooltip("The map region it stands in: an area name from the map's MapRegions (City, Mountains...).")]
        [SerializeField] string region;
        [Tooltip("The highest point, where the beacon sits.")]
        [SerializeField] Transform top;
        [SerializeField] Renderer beacon;
        [Tooltip("Beacon blinks per second.")]
        [SerializeField] float blinkRate = 0.75f;

        public string DisplayName => displayName;
        public string Region => region;
        public Vector3 TopPosition => top != null ? top.position : transform.position;

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        public void Configure(string name, string owner, Transform topPoint, Renderer beaconRenderer)
        {
            displayName = name;
            region = owner;
            top = topPoint;
            beacon = beaconRenderer;
        }

        void Update()
        {
            if (beacon != null)
                beacon.enabled = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;
        }

        /// <summary>The landmark closest to <paramref name="position"/> on the map (horizontal distance), or null.</summary>
        public static Landmark Nearest(Vector3 position)
        {
            Landmark best = null;
            float bestSq = float.MaxValue;
            foreach (var l in s_all)
            {
                Vector3 d = l.transform.position - position;
                float sq = d.x * d.x + d.z * d.z;
                if (sq < bestSq)
                {
                    bestSq = sq;
                    best = l;
                }
            }
            return best;
        }
    }
}

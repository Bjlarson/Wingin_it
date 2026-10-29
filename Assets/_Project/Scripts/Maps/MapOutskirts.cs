using System;
using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>
    /// The land (or sea) past a map's edge, so the world seems to go on: one coarse terrain round the map that mirrors
    /// the map's own ground just past its edge, then fades into far hills (or open sea with the odd island) made from
    /// noise. Render only: no collider, no grass, trees only near the edge. Built by the editor's
    /// <c>MapOutskirtsBuilder</c>; the map's fog and sky hide where it ends.
    /// </summary>
    [Serializable]
    public class MapOutskirts
    {
        [Tooltip("Build the outskirts terrain round the map.")]
        public bool enabled;
        [Tooltip("How far past the map's edge it reaches (m). Past the fog's end nothing shows anyway.")]
        public float reach = 6000f;
        [Tooltip("Over this many meters past the edge, the mirrored map ground fades into the far hills.")]
        public float blend = 1500f;
        [Tooltip("The far hills' height above the sea (m): valleys .. ridges.")]
        public Vector2 height = new(40f, 600f);
        [Tooltip("Wavelength of the far hills (m).")]
        public float featureSize = 2500f;
        [Tooltip("0: rolling hills; 1: sharp ridges.")]
        [Range(0f, 1f)] public float ridged = 0.5f;
        [Tooltip("How much of the far ground stands above the sea: 1 all land, ~0.2 a sea with scattered islands.")]
        [Range(0f, 1f)] public float land = 1f;
        [Tooltip("No far land closer than this to the map's edge (m), so an island map's sea stays open near it.")]
        public float landClearance;
        [Tooltip("Trees grow this far past the edge (m), by the map's own rules; 0 for none.")]
        public float treeBand = 1500f;
        [Tooltip("Fraction of the trees the map's rules would grow there.")]
        [Range(0f, 1f)] public float treeDensity = 0.6f;
    }
}

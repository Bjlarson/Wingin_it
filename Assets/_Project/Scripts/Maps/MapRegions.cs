using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Maps
{
    public enum RegionShape { Ellipse, Box }

    /// <summary>
    /// A named area of a map (City, Mountains, Desert...): an ellipse or an axis-aligned box in world XZ. Areas are
    /// checked in list order, so a small area listed first wins over a big one it sits inside.
    /// </summary>
    [Serializable]
    public class RegionArea
    {
        public string name;
        public RegionShape shape;
        public Vector2 center;
        [Tooltip("Radii for an ellipse, half width and depth for a box.")]
        public Vector2 size;
        [Tooltip("Only counts over land: over the sea (canals, bays) the point falls through to the next area or the sea.")]
        public bool landOnly = true;

        public bool Contains(Vector2 xz)
        {
            Vector2 d = xz - center;
            if (shape == RegionShape.Box)
                return Mathf.Abs(d.x) <= size.x && Mathf.Abs(d.y) <= size.y;
            if (size.x <= 0f || size.y <= 0f)
                return false;
            float u = d.x / size.x, v = d.y / size.y;
            return u * u + v * v <= 1f;
        }
    }

    public enum LandmarkKind { Spire, SummitRadar, Pyramid }

    /// <summary>
    /// A big landmark to steer by, naming the region it marks. The Spire stands on the ground at <see cref="xz"/>, the
    /// Pyramid sits on the ground there, and the radar goes on the highest ground inside the box of half size
    /// <see cref="searchHalfSize"/> round <see cref="xz"/>.
    /// </summary>
    [Serializable]
    public class LandmarkAnchor
    {
        public LandmarkKind kind;
        public Vector2 xz;
        [Tooltip("Summit radar: the box to find the highest ground in.")]
        public Vector2 searchHalfSize;
        [Tooltip("A MapRegions area name.")]
        public string region;
    }

    /// <summary>A map's named areas plus what to call the sea and everything else.</summary>
    [Serializable]
    public class MapRegions
    {
        public List<RegionArea> areas = new();
        [Tooltip("Open water no area claims.")]
        public string sea = "Sea";
        [Tooltip("Land no area claims.")]
        public string fallback = "Open";
        [Tooltip("Ground lower than this above the sea counts as sea.")]
        public float seaDepth = 1f;

        /// <summary>
        /// The region at <paramref name="xz"/>. Pass the ground's height above the sea to tell land from water; leave
        /// it out to judge by the areas alone (as if over land).
        /// </summary>
        public string RegionAt(Vector2 xz, float groundAboveSea = float.PositiveInfinity)
        {
            bool water = groundAboveSea < seaDepth;
            foreach (var a in areas)
                if ((!water || !a.landOnly) && a.Contains(xz))
                    return a.name;
            return water ? sea : fallback;
        }

        public RegionArea Area(string name) => areas.Find(a => a.name == name);
    }
}

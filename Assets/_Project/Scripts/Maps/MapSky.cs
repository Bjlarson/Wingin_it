using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>A cloud bank to hide in: center (x, z), horizontal radius, center altitude and the seed for its puffs.</summary>
    [Serializable]
    public struct CloudBankAnchor
    {
        public Vector2 xz;
        public float radius;
        [Tooltip("Center altitude; raised if needed so the underside clears the ground.")]
        public float altitude;
        public int seed;
        public CloudBankAnchor(Vector2 xz, float radius, float altitude, int seed)
        {
            this.xz = xz;
            this.radius = radius;
            this.altitude = altitude;
            this.seed = seed;
        }
    }

    /// <summary>A map's light and air: sun, fog, skybox and its cloud banks.</summary>
    [Serializable]
    public class MapSky
    {
        [Tooltip("Procedural sky material; empty = the shared greybox sky.")]
        public Material skybox;
        public Vector2 sunAngles = new(48f, -35f);
        public float sunIntensity = 1.4f;
        public Color fogColor = new(0.72f, 0.82f, 0.92f);
        public float fogStart = 900f, fogEnd = 6500f;
        public List<CloudBankAnchor> clouds = new();
        [Tooltip("Also builds each bank mirrored through the map's center, so both teams get the same cover.")]
        public bool mirrorClouds = true;
        [Tooltip("Vertical radius of a bank's core puff; 70 m gives ~140 m of cloud to hide in.")]
        public float cloudCoreHalfHeight = 70f;
        [Tooltip("A bank's underside stays at least this far above anything solid below it.")]
        public float cloudClearance = 80f;

        /// <summary>Every bank built, mirrors included, in build order (each bank then its mirror).</summary>
        public IEnumerable<CloudBankAnchor> AllClouds()
        {
            foreach (var c in clouds)
            {
                yield return c;
                if (mirrorClouds)
                    yield return new CloudBankAnchor(-c.xz, c.radius, c.altitude, c.seed);
            }
        }
    }

    /// <summary>How the HUD's minimap shows a map.</summary>
    [Serializable]
    public class MapMinimap
    {
        [Tooltip("A picture to use instead of photographing the level from above at match start; north up, square, covering the boundary times the margin.")]
        public Texture2D image;
        [Tooltip("Fills the map past the photo and round its corners when turned.")]
        public Color sea = new(0.12f, 0.3f, 0.42f);
        [Tooltip("How far past the flight boundary the map shows, as a multiple of its radius.")]
        public float margin = 1.06f;
    }
}

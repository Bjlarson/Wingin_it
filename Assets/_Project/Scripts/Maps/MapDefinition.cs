using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>
    /// A map's ground: a heightmap plus the numbers that turn it into a terrain (size, height range, sea level) and the
    /// rules that paint and plant it. One asset per map; the editor tool <c>Winging It/Map/Build Terrain From Heightmap</c>
    /// reads it. World position of a sample: x, z centered on the origin; y = <see cref="baseHeight"/> + value × <see cref="heightRange"/>.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Map Definition", fileName = "Map")]
    public class MapDefinition : ScriptableObject
    {
        [Tooltip("The name players see.")]
        public string displayName = "Map";
        [Tooltip("16-bit little-endian RAW (.bytes), square, 2^n + 1 samples a side (1025, 2049...).")]
        public TextAsset heightmapRaw;
        [Tooltip("Used only when there is no RAW file: a readable square grayscale image (8-bit, so coarse).")]
        public Texture2D heightmapImage;
        [Tooltip("Terrain width and depth in meters, centered on the origin.")]
        public float worldSize = 5600f;
        [Tooltip("World height of a black (0) sample.")]
        public float baseHeight = -40f;
        [Tooltip("Meters from black to white.")]
        public float heightRange = 800f;
        [Tooltip("World height of the sea surface; the ground rules count height above this.")]
        public float seaLevel;
        [Tooltip("Radius of the circular flight boundary round the middle, in meters; the ocean, shore map and buoy ring follow the map's own size.")]
        public float boundaryRadius = 2600f;
        [Tooltip("Flight ceiling in meters above the sea.")]
        public float ceiling = 1500f;
        [Tooltip("Seeds the tree and grass scatter, so a rebuild gives the same map.")]
        public int seed = 777;
        public TerrainRules rules = TerrainRules.Default;
        [Tooltip("Airfields, lanes, banner stands, arenas and what each mode uses, as anchor data.")]
        public MapLayout layout = new();
        [Tooltip("Named areas (City, Mountains...) for the debug HUD, surveys and callouts.")]
        public MapRegions regions = new();
        [Tooltip("Landmarks a heightmap map's scene builds (the Island places its own).")]
        public List<LandmarkAnchor> landmarks = new();
        public MapMinimap minimap = new();
        [Tooltip("Sun, fog, skybox and cloud banks.")]
        public MapSky sky = new();

        /// <summary>The heightmap to build from (RAW first), or null when neither is set.</summary>
        public Heightmap LoadHeightmap()
        {
            if (heightmapRaw != null)
                return Heightmap.FromR16(heightmapRaw.bytes);
            return heightmapImage != null ? Heightmap.FromTexture(heightmapImage) : null;
        }

        /// <summary>World ground height at x, z (the sea floor's edge value outside the map).</summary>
        public float GroundHeight(Heightmap map, float x, float z) =>
            WorldHeight(map.Sample(x / worldSize + 0.5f, z / worldSize + 0.5f));

        /// <summary>World height of a normalized sample value.</summary>
        public float WorldHeight(float value) => baseHeight + value * heightRange;

        /// <summary>The 0..1 value a world height is stored as (clamped to the range).</summary>
        public float ToValue(float worldHeight) => Mathf.Clamp01((worldHeight - baseHeight) / heightRange);

        /// <summary>World position (x, z) of a normalized map position.</summary>
        public Vector2 WorldXZ(float nx, float nz) => new((nx - 0.5f) * worldSize, (nz - 0.5f) * worldSize);

        /// <summary>The terrain's south-west corner, where the terrain object sits.</summary>
        public Vector3 TerrainOrigin => new(-worldSize * 0.5f, baseHeight, -worldSize * 0.5f);
    }
}

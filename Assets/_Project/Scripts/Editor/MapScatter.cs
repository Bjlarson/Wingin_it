using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// What a map's ground looks like and grows, spot by spot: the splat weights and what tree or tuft stands there.
    /// A heightmap map answers from its <see cref="Maps.TerrainRules"/> (<see cref="MapTerrainBuilder"/>); the Island
    /// from its regions. <c>h</c> is meters above the sea, <c>steep</c> the slope in degrees.
    /// </summary>
    internal interface ITerrainPainter
    {
        /// <summary>Splat weights (they sum to 1): grass, sand, rock, snow in <c>layers</c>, and the paved city in <c>paved</c>.</summary>
        (Vector4 layers, float paved) Layers(float x, float z, float h, float steep);

        /// <summary>What grows at a spot (a <see cref="MapScatter.TreeKinds"/> name and its scale range), or null for nothing.</summary>
        (string kind, float minScale, float maxScale)? PickTree(float x, float z, float h, float steep, double roll);

        /// <summary>Grass tufts (0 none, 1 sparse, 2 thick) and flowers at a detail cell. Draws from <paramref name="rng"/> only as it needs.</summary>
        (int grass, bool flowers) Detail(float x, float z, float h, float steep, System.Random rng);

        /// <summary>The world XZ rectangle trees are scattered over (a jittered grid from its corner).</summary>
        Rect TreeArea { get; }
    }

    /// <summary>
    /// Paints a terrain's layers and scatters its trees, rocks, grass and flowers by an <see cref="ITerrainPainter"/>,
    /// from a seed: trees from <c>seed</c>, details from <c>seed + 1</c>. Shared by every map, the Island included.
    /// </summary>
    internal static class MapScatter
    {
        const int SplatResolution = 512;
        const int DetailResolution = 1024;
        /// <summary>Trees and rocks go on a jittered grid this far apart; each cell grows at most one.</summary>
        internal const float TreeSpacing = 24f;

        /// <summary>The nature pack's terrain trees (props without colliders), in prototype order.</summary>
        internal static readonly string[] TreeKinds =
        {
            "TreeOak", "TreeOakTall", "TreePine", "TreePineTall", "TreePineSnow", "TreePalm", "TreePalmBent", "TreeDead",
            "Bush", "BushDry", "Rock", "RockTall", "RockFlat", "RockSnow", "RockDesert", "CliffRock", "Cactus", "CactusSmall",
            "BeachRocks", "Driftwood",
        };

        /// <summary>Height above the sea at a normalized terrain spot.</summary>
        static float AboveSea(TerrainData data, Vector3 origin, float seaLevel, float nx, float nz) =>
            data.GetInterpolatedHeight(nx, nz) + origin.y - seaLevel;

        /// <summary>The five layers' weights (<see cref="MapTerrainBuilder.StandardLayers"/> order) over the whole terrain.</summary>
        public static void PaintLayers(TerrainData data, Vector3 origin, float seaLevel, ITerrainPainter painter)
        {
            int res = SplatResolution;
            float size = data.size.x;
            data.alphamapResolution = res;
            var maps = new float[res, res, 5];
            for (int iz = 0; iz < res; iz++)
            {
                float nz = (iz + 0.5f) / res;
                float z = origin.z + nz * size;
                for (int ix = 0; ix < res; ix++)
                {
                    float nx = (ix + 0.5f) / res;
                    float x = origin.x + nx * size;
                    var (w, paved) = painter.Layers(x, z, AboveSea(data, origin, seaLevel, nx, nz), data.GetSteepness(nx, nz));
                    maps[iz, ix, 0] = w.x;
                    maps[iz, ix, 1] = w.y;
                    maps[iz, ix, 2] = w.z;
                    maps[iz, ix, 3] = w.w;
                    maps[iz, ix, 4] = paved;
                }
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>Scatters the nature pack's trees, bushes and rocks as terrain trees (no colliders).</summary>
        public static void PlantTrees(TerrainData data, Vector3 origin, float seaLevel, int seed, ITerrainPainter painter, NatureModels nature)
        {
            var prototypes = new TreePrototype[TreeKinds.Length];
            for (int i = 0; i < TreeKinds.Length; i++)
                prototypes[i] = new TreePrototype { prefab = nature.Prefab(TreeKinds[i]) };
            data.treePrototypes = prototypes;

            var rng = new System.Random(seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float size = data.size.x;
            Rect area = painter.TreeArea;
            var trees = new List<TreeInstance>();
            for (float cz = area.yMin; cz <= area.yMax; cz += TreeSpacing)
            for (float cx = area.xMin; cx <= area.xMax; cx += TreeSpacing)
            {
                float x = cx + Range(-0.45f, 0.45f) * TreeSpacing, z = cz + Range(-0.45f, 0.45f) * TreeSpacing;
                float nx = (x - origin.x) / size, nz = (z - origin.z) / size;
                if (nx < 0f || nx > 1f || nz < 0f || nz > 1f)
                    continue;
                double roll = rng.NextDouble();
                var pick = painter.PickTree(x, z, AboveSea(data, origin, seaLevel, nx, nz), data.GetSteepness(nx, nz), roll);
                if (pick == null)
                    continue;
                var (kind, minScale, maxScale) = pick.Value;
                float scale = Range(minScale, maxScale);
                trees.Add(new TreeInstance
                {
                    position = new Vector3(nx, 0f, nz),
                    prototypeIndex = System.Array.IndexOf(TreeKinds, kind),
                    widthScale = scale,
                    heightScale = scale * Range(0.9f, 1.1f),
                    rotation = Range(0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            data.SetTreeInstances(trees.ToArray(), snapToHeightmap: true);
        }

        /// <summary>Grass tufts and flowers as instanced terrain details.</summary>
        public static void PlantDetails(TerrainData data, Vector3 origin, float seaLevel, int seed, ITerrainPainter painter, NatureModels nature)
        {
            DetailPrototype Detail(string kind, float min, float max) => new()
            {
                prototype = nature.Prefab(kind, castShadows: false, lod: false),
                usePrototypeMesh = true,
                useInstancing = true,
                renderMode = DetailRenderMode.VertexLit,
                minWidth = min, maxWidth = max, minHeight = min, maxHeight = max,
                noiseSpread = 0.3f,
                healthyColor = Color.white, dryColor = Color.white,
            };
            data.detailPrototypes = new[] { Detail("GrassTuft", 0.8f, 1.6f), Detail("Flowers", 0.8f, 1.3f) };
            data.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            data.SetDetailResolution(DetailResolution, 32);

            var rng = new System.Random(seed + 1);
            var grass = new int[DetailResolution, DetailResolution];
            var flowers = new int[DetailResolution, DetailResolution];
            float cell = data.size.x / DetailResolution;
            for (int iz = 0; iz < DetailResolution; iz++)
            for (int ix = 0; ix < DetailResolution; ix++)
            {
                float x = origin.x + (ix + 0.5f) * cell, z = origin.z + (iz + 0.5f) * cell;
                float nx = (ix + 0.5f) / DetailResolution, nz = (iz + 0.5f) / DetailResolution;
                var (g, f) = painter.Detail(x, z, AboveSea(data, origin, seaLevel, nx, nz), data.GetSteepness(nx, nz), rng);
                grass[iz, ix] = g;
                flowers[iz, ix] = f ? 1 : 0;
            }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, flowers);
        }
    }
}

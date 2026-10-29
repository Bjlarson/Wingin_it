using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// The land past a heightmap map's edge (<see cref="MapOutskirts"/>): one coarse terrain, 513 samples a side, centered
    /// on the map and reaching <see cref="MapOutskirts.reach"/> past it. The map's square is cut out of it (terrain holes,
    /// so nothing is drawn twice) and so is any cell deep under the sea. Just past the edge its ground is the map's own,
    /// mirrored, so ridges and valleys run on unbroken; further out that fades into noise hills (or open sea with islands).
    /// Painted by the map's rules on the first four standard layers (one terrain pass), trees near the edge only, no
    /// grass and no collider: the flight boundary keeps every plane inside.
    /// </summary>
    internal static class MapOutskirtsBuilder
    {
        const int Resolution = 513;
        const int SplatResolution = 512;
        /// <summary>Just inside the map's edge the outskirts sit this far under its lowest ground, so they never poke through.</summary>
        const float Tuck = 4f;

        /// <summary>Builds the outskirts terrain under <paramref name="parent"/>, or nothing when the map has none.</summary>
        public static Terrain Build(MapDefinition map, Transform parent, NatureModels nature)
        {
            var o = map.outskirts;
            if (o == null || !o.enabled)
                return null;
            var ground = MapTerrainBuilder.LoadGround(map);
            float half = map.worldSize * 0.5f;
            float size = map.worldSize + 2f * o.reach;
            float cell = size / (Resolution - 1);
            var origin = new Vector3(-size * 0.5f, map.baseHeight, -size * 0.5f);

            var heights = new float[Resolution, Resolution];
            for (int iz = 0; iz < Resolution; iz++)
            for (int ix = 0; ix < Resolution; ix++)
            {
                float x = origin.x + ix * cell, z = origin.z + iz * cell;
                heights[iz, ix] = map.ToValue(Height(map, ground, x, z, half, cell));
            }

            string path = $"{MapTerrainBuilder.TerrainFolder}/{map.name}_Outskirts.asset";
            B.EnsureFolder(MapTerrainBuilder.TerrainFolder);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(path);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, path);
            }
            data.heightmapResolution = Resolution;
            data.size = new Vector3(size, map.heightRange, size);
            data.SetHeights(0, 0, heights);
            data.SetHoles(0, 0, Holes(map, heights, origin, cell, half));

            // Four layers, so one terrain pass: no paving out here, and on a land map the sand's slot holds the far woods.
            var standard = MapTerrainBuilder.StandardLayers();
            bool woods = o.land >= 1f;
            data.terrainLayers = new[] { standard[0], woods ? WoodsLayer() : standard[1], standard[2], standard[3] };
            Paint(map, data, origin, half, woods);
            PlantTrees(map, data, origin, half, nature);
            data.detailPrototypes = System.Array.Empty<DetailPrototype>();
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Outskirts";
            Object.DestroyImmediate(go.GetComponent<TerrainCollider>());
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = B.Materials.TerrainMat("Env_Terrain");
            terrain.heightmapPixelError = 10f; // seen from at least the boundary, through haze
            terrain.basemapDistance = 1200f;
            terrain.drawInstanced = true;
            terrain.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            terrain.treeDistance = 1500f; // the band past the edge, seen from inside the boundary
            terrain.treeBillboardDistance = 1500f;
            terrain.detailObjectDistance = 0f;
            AssetDatabase.SaveAssets();
            return terrain;
        }

        /// <summary>
        /// World height at (x, z): under the map, a little below its lowest ground nearby (the map's terrain covers it);
        /// past its edge, the map's ground mirrored across the edge, fading into <see cref="FarHeight"/>.
        /// </summary>
        static float Height(MapDefinition map, Heightmap ground, float x, float z, float half, float cell)
        {
            float outside = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - half;
            if (outside < 0f)
            {
                float low = float.MaxValue;
                for (int dz = -1; dz <= 1; dz++)
                for (int dx = -1; dx <= 1; dx++)
                    low = Mathf.Min(low, map.GroundHeight(ground, x + dx * cell, z + dz * cell));
                return low - Tuck;
            }
            float mirrored = map.GroundHeight(ground, Fold(x, half), Fold(z, half));
            float t = TerrainRules.Smooth(0f, map.outskirts.blend, outside);
            return Mathf.Lerp(mirrored, FarHeight(map, x, z, outside), t);
        }

        /// <summary>Reflects a coordinate back into -half..half, as if the map were mirrored across each edge in turn.</summary>
        static float Fold(float v, float half)
        {
            float period = 4f * half;
            float u = Mathf.Repeat(v + half, period);
            return (u > 2f * half ? period - u : u) - half;
        }

        /// <summary>The far hills: rolling or ridged noise between the outskirts' heights, with seabed where it isn't land.</summary>
        static float FarHeight(MapDefinition map, float x, float z, float outside)
        {
            var o = map.outskirts;
            float seed = map.seed * 0.0173f;
            float s = o.featureSize;
            float rolling = 0f, ridges = 0f, amp = 1f, total = 0f;
            for (int octave = 0; octave < 5; octave++)
            {
                float f = (1 << octave) / s;
                float n = Mathf.PerlinNoise(x * f + seed + octave * 7.31f, z * f - seed + octave * 3.17f);
                rolling += n * amp;
                float r = 1f - Mathf.Abs(2f * n - 1f);
                ridges += r * r * amp;
                total += amp;
                amp *= 0.5f;
            }
            float shape = Mathf.Lerp(rolling, ridges, o.ridged) / total;
            float hill = map.seaLevel + Mathf.Lerp(o.height.x, o.height.y, Mathf.Clamp01(shape * 1.25f - 0.1f));
            if (o.land >= 1f)
                return hill;

            float mask = Mathf.PerlinNoise(x / (s * 1.7f) - seed * 1.9f, z / (s * 1.7f) + seed * 2.3f) * 0.75f
                + Mathf.PerlinNoise(x / (s * 0.6f) + 91.7f, z / (s * 0.6f) - 13.1f) * 0.25f;
            float landness = TerrainRules.Smooth(1f - o.land, 1f - o.land + 0.08f, mask)
                * TerrainRules.Smooth(o.landClearance, o.landClearance + o.blend, outside);
            return Mathf.Lerp(map.baseHeight, hill, landness);
        }

        /// <summary>
        /// Which cells to draw (true) or cut out: the map's square, less a one-cell overlap round its rim, and any cell
        /// whose corners are all well under the sea.
        /// </summary>
        static bool[,] Holes(MapDefinition map, float[,] heights, Vector3 origin, float cell, float half)
        {
            int n = Resolution - 1;
            var solid = new bool[n, n];
            float under = map.ToValue(map.seaLevel - 3f);
            float inner = half - cell;
            for (int iz = 0; iz < n; iz++)
            for (int ix = 0; ix < n; ix++)
            {
                float x0 = origin.x + ix * cell, z0 = origin.z + iz * cell;
                bool insideMap = x0 >= -inner && x0 + cell <= inner && z0 >= -inner && z0 + cell <= inner;
                bool drowned = heights[iz, ix] < under && heights[iz, ix + 1] < under
                    && heights[iz + 1, ix] < under && heights[iz + 1, ix + 1] < under;
                solid[iz, ix] = !insideMap && !drowned;
            }
            return solid;
        }

        /// <summary>
        /// The far woods: the grass texture darkened to a forest green (its own PNG, so the terrain's distant base map
        /// shows it too), standing in for the trees past <see cref="MapOutskirts.treeBand"/>, so the far hills don't read
        /// as bare lawn. The texture is made once (delete it to remake); the layer is made or updated in place.
        /// </summary>
        internal static TerrainLayer WoodsLayer()
        {
            var grass = MapTerrainBuilder.Layer(TerrainTextures.Kind.Grass, 80f);
            string texturePath = $"{TerrainTextures.Folder}/FarWoods.png";
            var albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (albedo == null)
            {
                var source = new Texture2D(2, 2);
                source.LoadImage(System.IO.File.ReadAllBytes(AssetDatabase.GetAssetPath(grass.diffuseTexture)));
                var pixels = source.GetPixels32();
                for (int i = 0; i < pixels.Length; i++)
                    pixels[i] = new Color32((byte)(pixels[i].r * 0.42f), (byte)(pixels[i].g * 0.58f), (byte)(pixels[i].b * 0.46f), 255);
                source.SetPixels32(pixels);
                System.IO.File.WriteAllBytes(texturePath, source.EncodeToPNG());
                Object.DestroyImmediate(source);
                AssetDatabase.ImportAsset(texturePath);
                albedo = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            }

            string path = $"{MapTerrainBuilder.TerrainFolder}/FarWoods.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            layer.diffuseTexture = albedo;
            layer.normalMapTexture = grass.normalMapTexture;
            layer.tileSize = grass.tileSize;
            layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly;
            layer.smoothness = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>
        /// Grass, sand, rock and snow by the map's own rules; with <paramref name="woods"/>, the sand counts as grass and
        /// the grass turns to far woods in big patches, more of it the further out (near the edge the real trees stand).
        /// </summary>
        static void Paint(MapDefinition map, TerrainData data, Vector3 origin, float half, bool woods)
        {
            int res = SplatResolution;
            float size = data.size.x;
            data.alphamapResolution = res;
            var maps = new float[res, res, 4];
            for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                float nx = (ix + 0.5f) / res, nz = (iz + 0.5f) / res;
                float h = data.GetInterpolatedHeight(nx, nz) + origin.y - map.seaLevel;
                var w = map.rules.Layers(h, data.GetSteepness(nx, nz));
                if (woods)
                {
                    float x = origin.x + nx * size, z = origin.z + nz * size;
                    float outside = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - half;
                    float wooded = (0.2f + 0.75f * TerrainRules.Smooth(0.4f, 0.58f, MapTerrainBuilder.Noise(x, z, 900f, map.seed * 0.031f)))
                        * TerrainRules.Smooth(0f, map.outskirts.treeBand, outside);
                    float grass = w.x + w.y;
                    w = new Vector4(grass * (1f - wooded), grass * wooded, w.z, w.w);
                }
                maps[iz, ix, 0] = w.x;
                maps[iz, ix, 1] = w.y;
                maps[iz, ix, 2] = w.z;
                maps[iz, ix, 3] = w.w;
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>
        /// The map's rules' trees in a band <see cref="MapOutskirts.treeBand"/> m wide past its edge, thinned to
        /// <see cref="MapOutskirts.treeDensity"/>, so the woods at the edge carry on and then give way to the haze.
        /// </summary>
        static void PlantTrees(MapDefinition map, TerrainData data, Vector3 origin, float half, NatureModels nature)
        {
            var o = map.outskirts;
            if (nature == null || o.treeBand <= 0f)
            {
                data.treeInstances = System.Array.Empty<TreeInstance>();
                data.treePrototypes = System.Array.Empty<TreePrototype>();
                return;
            }
            var prototypes = new TreePrototype[MapScatter.TreeKinds.Length];
            for (int i = 0; i < prototypes.Length; i++)
                prototypes[i] = new TreePrototype { prefab = nature.Prefab(MapScatter.TreeKinds[i]) };
            data.treePrototypes = prototypes;

            var rng = new System.Random(map.seed + 7);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float spacing = map.rules.treeSpacing > 0f ? map.rules.treeSpacing : MapScatter.TreeSpacing;
            float size = data.size.x, reach = half + o.treeBand;
            var trees = new System.Collections.Generic.List<TreeInstance>();
            for (float cz = -reach; cz <= reach; cz += spacing)
            for (float cx = -reach; cx <= reach; cx += spacing)
            {
                float x = cx + Range(-0.45f, 0.45f) * spacing, z = cz + Range(-0.45f, 0.45f) * spacing;
                double roll = rng.NextDouble(), keep = rng.NextDouble(), scaleRoll = rng.NextDouble();
                float outside = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z)) - half;
                // Fewer the further out: the band thins into the haze rather than stopping at a line.
                if (outside < 0f || keep > o.treeDensity * (1f - TerrainRules.Smooth(o.treeBand * 0.5f, o.treeBand, outside)))
                    continue;
                float nx = (x - origin.x) / size, nz = (z - origin.z) / size;
                float h = data.GetInterpolatedHeight(nx, nz) + origin.y - map.seaLevel;
                var pick = map.rules.PickTree(h, data.GetSteepness(nx, nz), (float)roll,
                    MapTerrainBuilder.Noise(x, z, 220f, map.seed * 0.013f));
                if (pick == null)
                    continue;
                var (kind, minScale, maxScale) = pick.Value;
                float scale = Mathf.Lerp(minScale, maxScale, (float)scaleRoll);
                trees.Add(new TreeInstance
                {
                    position = new Vector3(nx, 0f, nz),
                    prototypeIndex = System.Array.IndexOf(MapScatter.TreeKinds, kind),
                    widthScale = scale,
                    heightScale = scale * Range(0.9f, 1.1f),
                    rotation = Range(0f, Mathf.PI * 2f),
                    color = Color.white,
                    lightmapColor = Color.white,
                });
            }
            data.SetTreeInstances(trees.ToArray(), snapToHeightmap: true);
        }
    }
}

using System.IO;
using UnityEditor;
using UnityEngine;
using WingingIt.World;
using WingingIt.Maps;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Builds a terrain from a <see cref="MapDefinition"/>'s heightmap: the heights, the four layers painted by height
    /// and slope, and trees and grass scattered by the same rules (<see cref="TerrainRules"/>), all from the map's seed.
    /// No Blender round trip and no hand-painted splat. <see cref="BuildTerrain"/> is the shared core the Island's own
    /// terrain goes through too, painted by its regions instead of the rules. <b>Bake Island Heightmap</b> writes today's
    /// Island ground through the heightmap path, to check the tool against the terrain the Island scene builds itself.
    /// </summary>
    public static class MapTerrainBuilder
    {
        const string MapsFolder = B.Root + "/Maps";
        /// <summary>Where every map's terrain data and layers, the sea and the generated meshes are saved.</summary>
        internal const string TerrainFolder = B.Root + "/Terrain";
        const float TreeDistance = 2500f, DetailDistance = 200f;
        /// <summary>The flat pad under every airfield (local half extents: across, along the runway), blending into the land over <see cref="PadBlend"/> m.</summary>
        public static readonly Vector2 PadHalfSize = new(130f, 400f);
        public const float PadBlend = 250f;
        /// <summary>No trees or grass this close to a pad's flat part (m).</summary>
        const float PadClearing = 40f;

        [MenuItem("Winging It/Map/Build Terrain From Heightmap")]
        static void BuildSelected()
        {
            var map = Selection.activeObject as MapDefinition;
            var parent = new GameObject("World").transform;
            Undo.RegisterCreatedObjectUndo(parent.gameObject, "Build map terrain");
            Build(map, parent, NatureModels.Load());
            Selection.activeTransform = parent;
        }

        [MenuItem("Winging It/Map/Build Terrain From Heightmap", true)]
        static bool CanBuildSelected() => Selection.activeObject is MapDefinition;

        /// <summary>The selected map's sun, fog and skybox on the open scene, and its cloud banks over the ground already in it.</summary>
        [MenuItem("Winging It/Map/Build Sky From Definition")]
        static void BuildSkySelected()
        {
            var map = (MapDefinition)Selection.activeObject;
            Physics.SyncTransforms();
            MapSkyBuilder.ApplyAtmosphere(map.sky);
            var clouds = MapSkyBuilder.BuildClouds(null, map.sky, NatureModels.Load());
            Undo.RegisterCreatedObjectUndo(clouds.gameObject, "Build map sky");
            Selection.activeTransform = clouds;
        }

        [MenuItem("Winging It/Map/Build Sky From Definition", true)]
        static bool CanBuildSkySelected() => Selection.activeObject is MapDefinition;

        [MenuItem("Winging It/Map/Bake Island Heightmap")]
        static void BakeIslandFromMenu() => Selection.activeObject = BakeIsland();

        /// <summary>The path a map's heightmap is saved to: <c>Maps/&lt;name&gt;/&lt;name&gt;_height.bytes</c>.</summary>
        static string HeightmapPath(string name) => $"{MapsFolder}/{name}/{name}_height.bytes";

        /// <summary>
        /// Samples the Island's <see cref="IslandSceneBuilder.Height"/> over its 5.6 km terrain at the 1025-sample grid it
        /// uses today and saves it as <c>Maps/Island</c>, with a <see cref="MapDefinition"/> of the same size and range.
        /// </summary>
        public static MapDefinition BakeIsland()
        {
            B.EnsureFolder(MapsFolder);
            B.EnsureFolder($"{MapsFolder}/Island");
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinition>($"{MapsFolder}/Island/Island.asset");
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MapDefinition>();
                AssetDatabase.CreateAsset(definition, $"{MapsFolder}/Island/Island.asset");
            }
            definition.displayName = "The Island";
            definition.worldSize = 5600f;
            definition.baseHeight = -40f;
            definition.heightRange = 800f;
            definition.seaLevel = 0f;
            definition.seed = 777;
            definition.boundaryRadius = IslandSceneBuilder.BoundaryRadius;
            definition.ceiling = IslandSceneBuilder.Ceiling;

            const int res = 1025;
            var map = new Heightmap(res);
            for (int iz = 0; iz < res; iz++)
            for (int ix = 0; ix < res; ix++)
            {
                Vector2 p = definition.WorldXZ(ix / (float)(res - 1), iz / (float)(res - 1));
                map.Values[iz, ix] = definition.ToValue(IslandSceneBuilder.Height(p.x, p.y));
            }

            string path = HeightmapPath("Island");
            File.WriteAllBytes(path, map.ToR16());
            AssetDatabase.ImportAsset(path);
            definition.heightmapRaw = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Baked the Island heightmap: {path} ({res}x{res}, {File.ReadAllBytes(path).Length / 1024} KB)");
            return definition;
        }

        /// <summary>
        /// Writes the map's <see cref="TerrainData"/> asset (<c>Terrain/&lt;name&gt;_MapTerrain.asset</c>, reused on a rebuild) and
        /// adds a terrain object for it under <paramref name="parent"/>. <paramref name="nature"/> may be null for bare ground.
        /// </summary>
        public static Terrain Build(MapDefinition map, Transform parent, bool withNature) =>
            Build(map, parent, withNature ? NatureModels.Load() : null);

        internal static Terrain Build(MapDefinition map, Transform parent, NatureModels nature)
        {
            var heightmap = LoadGround(map);
            if (heightmap == null)
                throw new System.InvalidOperationException($"{map.name} has no heightmap: set the RAW file or an image.");
            if (!Heightmap.IsTerrainResolution(heightmap.Resolution))
                throw new System.InvalidOperationException(
                    $"{map.name}'s heightmap is {heightmap.Resolution} samples a side; a terrain needs 2^n + 1 (513, 1025, 2049...).");

            var terrain = BuildTerrain($"{TerrainFolder}/{map.name}_MapTerrain.asset", $"{map.name} Terrain", parent,
                map.TerrainOrigin, new Vector3(map.worldSize, map.heightRange, map.worldSize), heightmap.Values,
                map.seaLevel, map.seed, new RulesPainter(map), nature);
            AssetDatabase.SaveAssets();
            return terrain;
        }

        /// <summary>
        /// The terrain every map stands on: writes <paramref name="heights"/> (0..1 of <paramref name="size"/>.y, 2^n + 1
        /// a side) to the <see cref="TerrainData"/> at <paramref name="dataPath"/> (reused on a rebuild), paints the
        /// <see cref="StandardLayers"/> and scatters nature as <paramref name="painter"/> says (<see cref="MapScatter"/>),
        /// and adds the terrain object at <paramref name="origin"/> under <paramref name="parent"/>. Bare ground when
        /// <paramref name="nature"/> is null.
        /// </summary>
        internal static Terrain BuildTerrain(string dataPath, string name, Transform parent, Vector3 origin, Vector3 size,
            float[,] heights, float seaLevel, int seed, ITerrainPainter painter, NatureModels nature)
        {
            B.EnsureFolder(TerrainFolder);
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(dataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, dataPath);
            }
            data.heightmapResolution = heights.GetLength(0);
            data.size = size;
            data.SetHeights(0, 0, heights);

            data.terrainLayers = StandardLayers();
            MapScatter.PaintLayers(data, origin, seaLevel, painter);
            if (nature != null)
            {
                MapScatter.PlantTrees(data, origin, seaLevel, seed, painter, nature);
                MapScatter.PlantDetails(data, origin, seaLevel, seed, painter, nature);
            }
            else
            {
                data.treeInstances = System.Array.Empty<TreeInstance>();
                data.treePrototypes = System.Array.Empty<TreePrototype>();
                data.detailPrototypes = System.Array.Empty<DetailPrototype>();
            }
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            var material = B.Materials.TerrainMat("Env_Terrain");
            material.EnableKeyword("_NORMALMAP"); // the layers have normal maps (the terrain inspector would set this)
            EditorUtility.SetDirty(material);
            terrain.materialTemplate = material;
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 2500f;
            terrain.drawInstanced = true;
            terrain.treeDistance = TreeDistance;
            terrain.treeBillboardDistance = TreeDistance; // mesh trees have no billboards: draw them as meshes all the way
            terrain.detailObjectDistance = DetailDistance;
            terrain.detailObjectDensity = 1f;
            return terrain;
        }

        /// <summary>Every map's five layers, in splat order: grass, sand, rock, snow, paved.</summary>
        internal static TerrainLayer[] StandardLayers() => new[]
        {
            Layer(TerrainTextures.Kind.Grass, 80f),
            Layer(TerrainTextures.Kind.Sand, 70f),
            Layer(TerrainTextures.Kind.Rock, 50f),
            Layer(TerrainTextures.Kind.Snow, 60f, smoothness: 0.25f),
            Layer(TerrainTextures.Kind.Paved, 48f),
        };

        /// <summary>A terrain layer on <see cref="TerrainTextures"/>' albedo and normal map, made or updated in place.</summary>
        internal static TerrainLayer Layer(TerrainTextures.Kind kind, float tileSize, float smoothness = 0f)
        {
            string path = $"{TerrainFolder}/{kind}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }
            var (albedo, normal) = TerrainTextures.Load(kind);
            layer.diffuseTexture = albedo;
            layer.normalMapTexture = normal;
            layer.normalScale = 1f;
            layer.tileSize = new Vector2(tileSize, tileSize);
            layer.smoothnessSource = TerrainLayerSmoothnessSource.ConstantOnly; // not the albedo's alpha
            layer.smoothness = smoothness;
            layer.metallic = 0f;
            EditorUtility.SetDirty(layer);
            return layer;
        }

        /// <summary>
        /// The sea round the map's terrain (ocean box, wave surface, shore map baked from the heightmap) and the flight
        /// boundary at the map's radius and ceiling, with its buoy ring (the markers pack's, or greybox) when <paramref name="buoys"/>.
        /// </summary>
        public static FlightBoundary BuildSeaAndBoundary(MapDefinition map, Transform parent, bool buoys = true)
        {
            var heightmap = LoadGround(map);
            MapOceanBuilder.Build(parent, map.worldSize, (x, z) => map.GroundHeight(heightmap, x, z) - map.seaLevel);
            return MapBoundaryBuilder.Build(parent, map.boundaryRadius, map.ceiling, buoys, MarkerModels.Load());
        }

        /// <summary>The map's heightmap with a flat pad cut under each of its layout's airfields, or null when it has none.</summary>
        public static Heightmap LoadGround(MapDefinition map)
        {
            var heightmap = map.LoadHeightmap();
            if (heightmap != null)
                FlattenPads(map, heightmap);
            return heightmap;
        }

        /// <summary>
        /// Levels the ground under each airfield to its runway's elevation: flat across <see cref="PadHalfSize"/> in the
        /// field's frame, easing back into the land over <see cref="PadBlend"/> m.
        /// </summary>
        static void FlattenPads(MapDefinition map, Heightmap heightmap)
        {
            int res = heightmap.Resolution;
            float cell = map.worldSize / (res - 1);
            float reach = PadHalfSize.magnitude + PadBlend;
            foreach (var field in map.layout.airfields)
            {
                float target = map.ToValue(field.elevation);
                int ix0 = Mathf.Max(0, Mathf.FloorToInt((field.position.x - reach) / cell + (res - 1) * 0.5f));
                int ix1 = Mathf.Min(res - 1, Mathf.CeilToInt((field.position.x + reach) / cell + (res - 1) * 0.5f));
                int iz0 = Mathf.Max(0, Mathf.FloorToInt((field.position.y - reach) / cell + (res - 1) * 0.5f));
                int iz1 = Mathf.Min(res - 1, Mathf.CeilToInt((field.position.y + reach) / cell + (res - 1) * 0.5f));
                for (int iz = iz0; iz <= iz1; iz++)
                for (int ix = ix0; ix <= ix1; ix++)
                {
                    Vector2 p = map.WorldXZ(ix / (float)(res - 1), iz / (float)(res - 1));
                    float t = 1f - Mathf.Clamp01(PadOutside(field, p) / PadBlend);
                    t = t * t * (3f - 2f * t);
                    if (t > 0f)
                        heightmap.Values[iz, ix] = Mathf.Lerp(heightmap.Values[iz, ix], target, t);
                }
            }
        }

        /// <summary>How far (m) <paramref name="p"/> is outside an airfield's flat pad; 0 on it.</summary>
        static float PadOutside(AirfieldAnchor field, Vector2 p)
        {
            Vector3 l = Quaternion.Euler(0f, -field.yaw, 0f) * new Vector3(p.x - field.position.x, 0f, p.y - field.position.y);
            return Vector2.Max(new Vector2(Mathf.Abs(l.x), Mathf.Abs(l.z)) - PadHalfSize, Vector2.zero).magnitude;
        }

        /// <summary>True on or next to (within <see cref="PadClearing"/> m of) any of the map's airfield pads.</summary>
        static bool NearPad(MapDefinition map, float x, float z)
        {
            foreach (var field in map.layout.airfields)
                if (PadOutside(field, new Vector2(x, z)) < PadClearing)
                    return true;
            return false;
        }

        static float Noise(float x, float z, float scale, float seed) =>
            Mathf.PerlinNoise(x / scale + seed, z / scale - seed) * 0.65f + Mathf.PerlinNoise(x * 2f / scale + seed + 17.3f, z * 2f / scale - seed + 31.7f) * 0.35f;

        /// <summary>A heightmap map's ground: painted and grown by its <see cref="TerrainRules"/> alone, edge to edge.</summary>
        sealed class RulesPainter : ITerrainPainter
        {
            readonly MapDefinition _map;

            public RulesPainter(MapDefinition map) => _map = map;

            public Rect TreeArea => new(-_map.worldSize * 0.5f, -_map.worldSize * 0.5f, _map.worldSize, _map.worldSize);

            public (Vector4 layers, float paved) Layers(float x, float z, float h, float steep) => (_map.rules.Layers(h, steep), 0f);

            public (string kind, float minScale, float maxScale)? PickTree(float x, float z, float h, float steep, double roll) =>
                NearPad(_map, x, z) ? null : _map.rules.PickTree(h, steep, (float)roll, Noise(x, z, 220f, _map.seed * 0.013f));

            public (int grass, bool flowers) Detail(float x, float z, float h, float steep, System.Random rng)
            {
                var detail = _map.rules.Detail(h, steep, Noise(x, z, 120f, _map.seed * 0.029f), (float)rng.NextDouble());
                return NearPad(_map, x, z) ? (0, false) : detail; // still rolls, so the scatter elsewhere stays the same
            }
        }
    }
}

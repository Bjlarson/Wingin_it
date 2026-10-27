using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WingingIt.Maps;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Builds each map as its own scene, <c>Scenes/Maps/Map_&lt;name&gt;.unity</c>, holding only what every mode needs:
    /// sun, fog and sky, the terrain, the sea and flight boundary, and the clouds, under a <see cref="MapScene"/> that
    /// names the definition. No runways, banner stands, AA, arenas, player or match: those come from the map's layout
    /// for the chosen mode. The Island adds its city, canal, bridges, mesas and landmarks
    /// (<see cref="IslandSceneBuilder.BuildMapScene"/>); a heightmap map is its terrain alone.
    /// </summary>
    public static class MapSceneBuilder
    {
        public const string ScenesFolder = B.Root + "/Scenes/Maps";
        const string MapsFolder = B.Root + "/Maps";

        /// <summary>The maps decided on for the game (2026-10-26), by definition name.</summary>
        public static readonly string[] GameMaps = { "Island", "BroadReach", "ShatteredArchipelago" };

        public static string ScenePath(string mapName) => $"{ScenesFolder}/Map_{mapName}.unity";

        public static MapDefinition LoadDefinition(string mapName) =>
            AssetDatabase.LoadAssetAtPath<MapDefinition>($"{MapsFolder}/{mapName}/{mapName}.asset");

        [MenuItem("Winging It/Map/Build Map Scene")]
        static void BuildSelected()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Build((MapDefinition)Selection.activeObject);
        }

        [MenuItem("Winging It/Map/Build Map Scene", true)]
        static bool CanBuildSelected() => Selection.activeObject is MapDefinition;

        [MenuItem("Winging It/Map/Build All Map Scenes")]
        static void BuildAllFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                BuildAll();
        }

        /// <summary>
        /// Builds every map in <see cref="GameMaps"/> (a few minutes: the Island's city and each terrain's scatter), then
        /// reopens the scene that was open before.
        /// </summary>
        public static void BuildAll()
        {
            string open = SceneManager.GetActiveScene().path;
            foreach (string name in GameMaps)
                Build(LoadDefinition(name) ?? throw new System.InvalidOperationException($"No map definition for {name}."));
            if (!string.IsNullOrEmpty(open))
                EditorSceneManager.OpenScene(open);
        }

        /// <summary>Builds and saves one map's scene, replacing the open scene.</summary>
        public static void Build(MapDefinition map)
        {
            if (map.name == "Island")
            {
                IslandSceneBuilder.BuildMapScene(map);
                return;
            }
            string path = AssetDatabase.GetAssetPath(map);
            Scene scene;
            (scene, map) = NewMapScene(map);
            var sky = map.sky;
            var world = new GameObject("World").transform;
            var terrain = MapTerrainBuilder.Build(map, world, NatureModels.Load());
            map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path); // the terrain's asset saves can unload it
            MapTerrainBuilder.BuildSeaAndBoundary(map, world);
            BuildLandmarks(map, world, terrain);
            Physics.SyncTransforms();
            MapSkyBuilder.BuildClouds(world, sky, NatureModels.Load());
            Save(scene, path);
        }

        /// <summary>The definition's <see cref="MapDefinition.landmarks"/> on its terrain (none: no Landmarks group).</summary>
        static void BuildLandmarks(MapDefinition map, Transform world, Terrain terrain)
        {
            if (map.landmarks.Count == 0)
                return;
            var landmarks = new MapLandmarkBuilder(world, new B.Materials(), NatureModels.Load(), LandmarkModels.Load());
            // The lowest ground under the Spire's 70 m podium, so on a slope it sinks in rather than floats.
            float Ground(Vector2 xz)
            {
                float low = float.MaxValue;
                for (float dz = -35f; dz <= 35f; dz += 17.5f)
                for (float dx = -35f; dx <= 35f; dx += 17.5f)
                    low = Mathf.Min(low, terrain.SampleHeight(new Vector3(xz.x + dx, 0f, xz.y + dz)));
                return low + terrain.transform.position.y;
            }
            foreach (var l in map.landmarks)
                switch (l.kind)
                {
                    case LandmarkKind.Spire: landmarks.Spire(new Vector3(l.xz.x, Ground(l.xz), l.xz.y), l.region); break;
                    case LandmarkKind.SummitRadar: landmarks.SummitRadar(MapLandmarkBuilder.HighestPoint(terrain, l.xz, l.searchHalfSize), l.region); break;
                    case LandmarkKind.Pyramid: landmarks.Pyramid(terrain, l.xz, l.region); break;
                }
            landmarks.Finish();
        }

        /// <summary>
        /// A new scene with the map's atmosphere, its sun and its <see cref="MapScene"/> marker, and no camera. Returns
        /// the definition loaded again: opening a scene unloads unused assets, which destroys the one passed in.
        /// </summary>
        internal static (Scene scene, MapDefinition map) NewMapScene(MapDefinition map)
        {
            string path = AssetDatabase.GetAssetPath(map);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            map = AssetDatabase.LoadAssetAtPath<MapDefinition>(path);
            // The camera comes with the player (or the menu), never with the map.
            foreach (var camera in Object.FindObjectsByType<Camera>())
                Object.DestroyImmediate(camera.gameObject);
            MapSkyBuilder.ApplyAtmosphere(map.sky);
            new GameObject("Map").AddComponent<MapScene>().Configure(map);
            return (scene, map);
        }

        /// <summary>
        /// Saves a map scene and lists it in Build Settings after the scenes already there. Takes the definition's path,
        /// not the asset: the builders' asset imports can unload it, so it is loaded again and set on the marker here.
        /// </summary>
        internal static void Save(Scene scene, string definitionPath)
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(definitionPath);
            foreach (var root in scene.GetRootGameObjects())
                if (root.TryGetComponent(out MapScene marker))
                    marker.Configure(map);
            B.EnsureFolder(ScenesFolder);
            string path = ScenePath(map.name);
            EditorSceneManager.SaveScene(scene, path);
            B.AddSceneToBuildSettings(path, first: false);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {path}");
        }
    }
}

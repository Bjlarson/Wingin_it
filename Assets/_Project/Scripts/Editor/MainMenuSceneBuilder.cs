using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Builds <c>Scenes/MainMenu.unity</c>, the scene a build opens: the match flow and its screens (menu, game mode,
    /// class select, settings), the menu camera, the player's plane with its chase camera and HUD, the bot roster and
    /// the mode piece spawner, and nothing from any map. The menu's backdrop is its own hangar on an apron
    /// (<see cref="MenuStage"/>). Picking a class loads the map scene beside it (<see cref="MapLoader"/>) and spawns the
    /// mode's pieces there; leaving the match reloads this scene, which unloads the map.
    /// </summary>
    public static class MainMenuSceneBuilder
    {
        public const string ScenePath = B.Root + "/Scenes/MainMenu.unity";
        /// <summary>The map a match loads until the setup has a map select.</summary>
        const string DefaultMap = "Map_IslandV2";

        const float ApronSize = 400f;
        const float FieldSize = 6000f;

        [MenuItem("Winging It/Build Main Menu Scene")]
        static void BuildFromMenu()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                Build();
        }

        public static void Build()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath) == null
                || AssetDatabase.LoadAssetAtPath<GameObject>(B.AirfieldPrefabPath) == null)
                B.BuildAll();

            // Before the class stats: these save assets, and saved (clean) class assets would be unloaded by NewScene.
            ModePieceKitBuilder.Ensure();
            var classes = B.CreateClassStats();
            var m = new B.Materials();
            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            MapSkyBuilder.ApplyAtmosphere(new MapSky { skybox = MapSkyBuilder.GreyboxSky() });
            new GameObject("Level Settings").AddComponent<LevelSettings>().SpawnMode = SpawnMode.Air;

            var stage = BuildStage(m);
            B.BuildPlayer(scene, planePrefab, classes, stage.HangarSpot);
            var roster = IslandSceneBuilder.BuildBotRoster(planePrefab, classes);
            var pieces = new GameObject("Mode Pieces").AddComponent<ModePieceBuilder>();
            pieces.Configure(null, AssetDatabase.LoadAssetAtPath<ModePieceKit>(ModePieceKitBuilder.KitPath)); // the map comes with its scene
            var loader = new GameObject("Map Loader").AddComponent<MapLoader>();
            loader.Configure(DefaultMap, stage);
            IslandSceneBuilder.BuildMatch(roster, classes, pieces, loader);

            EditorSceneManager.SaveScene(scene, ScenePath);
            B.AddSceneToBuildSettings(ScenePath, first: true); // the scene a build opens
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {ScenePath}");
        }

        /// <summary>
        /// The menu's backdrop: the airfield hangar (the same model and interior as a home field's) on a concrete apron
        /// in a grass field, lit by the scene's sun, which goes under the stage so it hides with it once a map is in.
        /// </summary>
        static MenuStage BuildStage(B.Materials m)
        {
            var root = new GameObject("Menu Stage");
            var stage = root.AddComponent<MenuStage>();
            B.Box("Apron", root.transform, new Vector3(0f, -0.5f, 0f), new Vector3(ApronSize, 1f, ApronSize), m.Concrete, true);
            B.Box("Field", root.transform, new Vector3(0f, -0.55f, 0f), new Vector3(FieldSize, 1f, FieldSize), m.Ground, false);
            var models = AirfieldModels.Load();
            var spot = models != null
                ? B.BuildHangar(root.transform, Vector3.zero, models)
                : B.BuildHangar(root.transform, Vector3.zero, m);
            stage.Configure(spot);
            if (RenderSettings.sun != null)
                RenderSettings.sun.transform.SetParent(root.transform, true);
            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
            return stage;
        }
    }
}

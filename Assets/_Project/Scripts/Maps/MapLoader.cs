using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace WingingIt.Maps
{
    /// <summary>
    /// Loads a map scene (<c>Scenes/Maps/Map_&lt;name&gt;</c>) beside the main menu scene, which stays loaded and keeps
    /// the match, the player, the HUD and the cameras. The map becomes the active scene, so its sun, fog and sky take
    /// over, and the <see cref="MenuStage"/> is hidden. Leaving the match reloads the menu scene on its own, which
    /// unloads the map with it.
    /// </summary>
    public class MapLoader : MonoBehaviour
    {
        [Tooltip("The map scene loaded when none is named (scene name, listed in Build Settings). The match names the picked one.")]
        [SerializeField] string defaultMap = "Map_Island";
        [Tooltip("The menu's backdrop, hidden once a map is in.")]
        [SerializeField] MenuStage stage;

        Scene _loaded;

        public string DefaultMap => defaultMap;
        /// <summary>A map scene is loading right now.</summary>
        public bool IsLoading { get; private set; }
        /// <summary>0..1 while loading.</summary>
        public float Progress { get; private set; }
        /// <summary>The scene name of the map loading or loaded.</summary>
        public string MapName { get; private set; }
        /// <summary>The definition of the loaded map, or null before one is in.</summary>
        public MapDefinition Map { get; private set; }
        public bool IsLoaded => Map != null && _loaded.IsValid() && _loaded.isLoaded;

        public void Configure(string map, MenuStage menuStage)
        {
            defaultMap = map;
            stage = menuStage;
        }

        /// <summary>
        /// Loads <paramref name="mapScene"/> (the default map if empty) additively over several frames; then makes it
        /// active, hides the menu stage and calls <paramref name="done"/> with its definition. Runs with the game frozen.
        /// </summary>
        public IEnumerator Load(string mapScene, Action<MapDefinition> done)
        {
            if (IsLoading)
                yield break;
            MapName = string.IsNullOrEmpty(mapScene) ? defaultMap : mapScene;
            IsLoading = true;
            Progress = 0f;
            var op = SceneManager.LoadSceneAsync(MapName, LoadSceneMode.Additive);
            if (op == null)
            {
                IsLoading = false;
                Debug.LogError($"[Winging It] Map scene {MapName} is not in Build Settings.");
                yield break;
            }
            while (!op.isDone)
            {
                Progress = Mathf.Clamp01(op.progress / 0.9f);
                yield return null;
            }
            _loaded = SceneManager.GetSceneByName(MapName);
            SceneManager.SetActiveScene(_loaded);
            Map = null;
            foreach (var root in _loaded.GetRootGameObjects())
                if (root.TryGetComponent(out MapScene marker))
                    Map = marker.Definition;
            if (Map == null)
                Debug.LogError($"[Winging It] {MapName} has no MapScene marker.");
            if (stage != null)
                stage.Show(false);
            Progress = 1f;
            IsLoading = false;
            done?.Invoke(Map);
        }
    }
}

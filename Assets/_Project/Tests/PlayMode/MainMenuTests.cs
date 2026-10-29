using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.Maps;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// The main menu scene (<c>MainMenu.unity</c>, the scene a build opens): the menus run over its own hangar with no map
    /// loaded; picking a class loads the map scene beside it and spawns the mode's pieces; leaving reloads the menu
    /// scene, which unloads the map.
    /// </summary>
    public class MainMenuTests
    {
        const float LoadTimeout = 90f;

        MatchManager _match;
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameSettings.UseForTests(new GameSettings(false, null)); // defaults, nothing saved
            yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(_match, "MainMenu scene has no MatchManager");
            _player = _match.Player;
            _match.SelectMap(0); // the map pick outlives a reload: start each test on the Island
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        IEnumerator PickClassAndLoad()
        {
            _match.OpenClassSelect(); // already there after Play Again
            _match.SelectClass(0);
            Assert.IsTrue(_match.IsLoadingMap, "the map loads behind the loading screen");
            Assert.AreEqual(MatchPhase.ClassSelect, _match.Phase, "no countdown until the map is in");
            float t = Time.realtimeSinceStartup;
            while (_match.IsLoadingMap && Time.realtimeSinceStartup - t < LoadTimeout)
                yield return null;
            Assert.IsFalse(_match.IsLoadingMap, "the map loaded in time");
        }

        [UnityTest]
        public IEnumerator MenuHasNoMap()
        {
            Assert.AreEqual(MatchPhase.Menu, _match.Phase);
            Assert.IsNull(MapScene.Current, "no map scene loaded");
            Assert.IsNull(Terrain.activeTerrain, "no terrain");
            Assert.IsEmpty(Airfield.All, "no airfields");
            Assert.IsNotNull(MenuStage.Current, "the menu's own hangar");
            Assert.AreSame(MenuStage.Current.HangarSpot, _match.HangarSpot);
            Assert.Less(Vector3.Distance(_player.Position, _match.HangarSpot.position), 3f, "the plane is parked in the hangar");
            Assert.AreEqual(0, SceneManager.GetActiveScene().buildIndex, "the first scene in Build Settings");
            yield return null;
        }

        [UnityTest]
        public IEnumerator PickingAClassLoadsTheMapAndStarts()
        {
            yield return PickClassAndLoad();
            var map = MapScene.Current;
            Assert.IsNotNull(map, "the map scene is loaded");
            Assert.AreEqual("IslandV2", map.Definition.name, "the first map card");
            Assert.AreEqual(map.gameObject.scene, SceneManager.GetActiveScene(), "the map's sky and fog are in force");
            Assert.AreEqual(2, SceneManager.sceneCount, "the menu scene stays loaded beside it");
            Assert.IsNull(MenuStage.Current, "the menu stage hides");
            Assert.IsNotNull(Terrain.activeTerrain);
            Assert.AreSame(map.Definition, _match.Pieces.Map, "the pieces spawn from the loaded map");
            Assert.IsTrue(_match.Pieces.Airfields.ContainsKey("West Home Airfield"), "Teams spawns the home fields");
            Assert.Greater(_match.Pieces.FightAreas.Count, 0, "the bots get the map's fight areas");
            Assert.AreEqual(MatchPhase.Countdown, _match.Phase);
            var home = Airfield.HomeOf(_player.Team);
            Assert.IsNotNull(home);
            Assert.Less(Vector3.Distance(_player.Position, home.AirSpawn.position), 50f, "the player starts over home");
        }

        [UnityTest]
        public IEnumerator LoadingTheMapFromTheMenu_StaysInTheMenu_ParkedAtHome_ThenPicksStartAtOnce()
        {
            yield return _match.LoadMap(MapTestScene.Island);
            Assert.AreEqual(MatchPhase.Menu, _match.Phase, "still in the menu");
            Assert.IsNotNull(MapScene.Current, "the map scene is loaded");
            Assert.AreEqual(MapTestScene.Island, SceneManager.GetActiveScene().name);
            Assert.IsTrue(_match.Pieces.Airfields.ContainsKey("West Home Airfield"), "the mode's pieces spawned");
            var home = Airfield.HomeOf(_player.Team);
            Assert.IsNotNull(home);
            Assert.AreSame(home.HangarSpot, _match.HangarSpot, "the stage is gone: the home field's hangar is the backdrop");
            Assert.Less(Vector3.Distance(_player.Position, home.HangarSpot.position), 3f, "parked in it");

            _match.OpenClassSelect();
            _match.SelectClass(0);
            Assert.IsFalse(_match.IsLoadingMap, "the map is already in");
            Assert.AreEqual(MatchPhase.Countdown, _match.Phase);
        }

        [UnityTest]
        public IEnumerator PickingAMap_LoadsThatMap_AndPlayAgainKeepsIt()
        {
            int archipelago = MapInfo.IndexOf("Map_ShatteredArchipelago");
            _match.SelectMap(archipelago);
            Assert.AreEqual(archipelago, _match.MapIndex);
            yield return PickClassAndLoad();
            Assert.AreEqual("ShatteredArchipelago", MapScene.Current.Definition.name, "the picked map loaded");
            Assert.AreSame(MapScene.Current.Definition, _match.Pieces.Map, "its layout spawned the pieces");
            Assert.AreEqual(MatchPhase.Countdown, _match.Phase);
            _match.SelectMap(0);
            Assert.AreEqual(archipelago, _match.MapIndex, "no switching once the map is in");

            _match.PlayAgain();
            yield return null;
            float t = Time.realtimeSinceStartup;
            while ((SceneManager.sceneCount > 1 || Object.FindAnyObjectByType<MatchManager>() == null)
                   && Time.realtimeSinceStartup - t < 10f)
                yield return null;
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            Assert.AreEqual(MatchPhase.ClassSelect, _match.Phase, "Play Again opens on class select");
            Assert.AreEqual(archipelago, _match.MapIndex, "on the same map");
            yield return PickClassAndLoad();
            Assert.AreEqual("ShatteredArchipelago", MapScene.Current.Definition.name);
        }

        [UnityTest]
        public IEnumerator LeavingUnloadsTheMap()
        {
            yield return PickClassAndLoad();
            _match.ReturnToMainMenu();
            yield return null;
            float t = Time.realtimeSinceStartup;
            while ((SceneManager.sceneCount > 1 || Object.FindAnyObjectByType<MatchManager>() == null)
                   && Time.realtimeSinceStartup - t < 10f)
                yield return null;
            yield return null;
            Assert.AreEqual(1, SceneManager.sceneCount, "only the menu scene");
            Assert.IsNull(MapScene.Current, "the map is unloaded");
            var match = Object.FindAnyObjectByType<MatchManager>();
            Assert.AreEqual(MatchPhase.Menu, match.Phase);
            Assert.IsNotNull(MenuStage.Current, "back in the menu hangar");
        }

        [UnityTest]
        public IEnumerator PilotScreen_CameraStaysInTheHangar_InSightOfThePilotAndCanopy()
        {
            var menuCamera = Object.FindAnyObjectByType<CameraSystem.MenuCamera>();
            var pilot = MenuPilot.Current;
            Assert.IsNotNull(menuCamera);
            Assert.IsNotNull(pilot, "the menu pilot");
            menuCamera.ShowPilot(true);
            pilot.Showcase = true;
            menuCamera.Skip();
            yield return null;
            yield return null;

            var eye = Camera.main.transform.position;
            foreach (float up in new[] { 1.5f, 8f }) // the pilot's chest, the canopy
            {
                var from = pilot.Position + Vector3.up * up;
                Assert.IsFalse(Physics.Linecast(from, eye, out var hit),
                    $"the view {up} m up the pilot is blocked by {(hit.collider != null ? hit.collider.name : "")} (camera at {eye})");
            }
        }

        [UnityTest]
        public IEnumerator RamRoyaleGetsItsArenaFromTheLoadedMap()
        {
            _match.SetMode(new RamRoyaleMode());
            Assert.IsNull(ModeArena.For("RamRoyale"), "no map, no arena yet");
            yield return PickClassAndLoad();
            Assert.IsNotNull(ModeArena.For("RamRoyale"), "the arena spawned from the map's layout");
            Assert.AreSame(ModeArena.For("RamRoyale"), _match.Arena, "and is in force");
        }
    }
}

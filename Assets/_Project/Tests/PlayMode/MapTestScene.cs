using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using WingingIt.Flight;
using WingingIt.Maps;
using WingingIt.Match;

namespace WingingIt.Tests
{
    /// <summary>
    /// Loads a map the way the game does: the main menu scene (match, player, HUD, bots), then the map scene beside it
    /// through its <see cref="MapLoader"/>, with the mode's pieces spawned from the map's layout. The match stays in the
    /// menu with the plane parked at home, so a class pick starts at once.
    /// </summary>
    public static class MapTestScene
    {
        /// <summary>The first, hand-built Island: the tests' level (left out of Build Settings; the editor loads it by path).</summary>
        public const string Island = "Map_Island";
        public const string IslandV2 = "Map_IslandV2";
        public const string BroadReach = "Map_BroadReach";
        public const string ShatteredArchipelago = "Map_ShatteredArchipelago";
        public const string MountainRange = "Map_MountainRange";

        public static MatchManager Match { get; private set; }
        public static PlaneController Player { get; private set; }

        public static IEnumerator Load(string map = Island)
        {
            yield return SceneManager.LoadSceneAsync("MainMenu", LoadSceneMode.Single);
            yield return null;
            Match = Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(Match, "MainMenu scene has no MatchManager");
            Player = Match.Player;
            Assert.IsNotNull(Player, "MainMenu scene has no player plane");

            yield return Match.LoadMap(map);
            Assert.IsTrue(Match.MapLoader.IsLoaded, $"{map} loaded");
            Assert.AreEqual(map, SceneManager.GetActiveScene().name, "the map is the active scene");
            yield return Settle();
        }

        /// <summary>
        /// Waits (up to a few seconds) for several short frames in a row: the frames right after the map scene loads or the
        /// pieces spawn can hitch, and a test that samples once a frame (a grab zone, a towed cable) can miss it.
        /// </summary>
        public static IEnumerator Settle()
        {
            const float Steady = 1f / 20f;
            float t = Time.realtimeSinceStartup;
            int calm = 0;
            while (calm < 5 && Time.realtimeSinceStartup - t < 5f)
            {
                yield return null;
                calm = Time.unscaledDeltaTime < Steady ? calm + 1 : 0;
            }
        }
    }
}

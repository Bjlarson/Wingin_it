using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WingingIt.Bots;
using WingingIt.Maps;
using WingingIt.Match;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// Each game map is its own scene (<c>Scenes/Maps/Map_&lt;name&gt;</c>) with only what every mode needs, built by
    /// <c>Winging It/Map/Build All Map Scenes</c>. Opens each one beside the open scene and closes it again.
    /// </summary>
    public class MapSceneTests
    {
        /// <summary>The shipped maps, in map select order.</summary>
        static readonly string[] GameMaps = { "IslandV2", "BroadReach", "ShatteredArchipelago", "MountainRange" };
        /// <summary>Every map scene: the shipped ones and the tests' Island.</summary>
        static readonly string[] Maps = { "IslandV2", "BroadReach", "ShatteredArchipelago", "MountainRange", "Island" };

        static string Path(string map) => $"Assets/_Project/Scenes/Maps/Map_{map}.unity";

        static T[] InScene<T>(Scene scene) where T : Component
        {
            var found = new System.Collections.Generic.List<T>();
            foreach (var root in scene.GetRootGameObjects())
                found.AddRange(root.GetComponentsInChildren<T>(true));
            return found.ToArray();
        }

        static void WithScene(string map, System.Action<Scene> check)
        {
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<SceneAsset>(Path(map)), $"{Path(map)} is not built");
            var scene = EditorSceneManager.OpenScene(Path(map), OpenSceneMode.Additive);
            try { check(scene); }
            finally { EditorSceneManager.CloseScene(scene, true); }
        }

        [TestCaseSource(nameof(Maps))]
        public void HoldsTheSharedWorld(string map)
        {
            WithScene(map, scene =>
            {
                var marker = InScene<MapScene>(scene);
                Assert.AreEqual(1, marker.Length, "one MapScene marker");
                Assert.IsNotNull(marker[0].Definition, "the marker names its definition");
                Assert.AreEqual(map, marker[0].Definition.name);
                Assert.GreaterOrEqual(InScene<Terrain>(scene).Length, 1, "terrain");
                Assert.AreEqual(1, InScene<OceanSurface>(scene).Length, "sea");
                Assert.AreEqual(1, InScene<FlightBoundary>(scene).Length, "flight boundary");
                Assert.AreEqual(1, InScene<Light>(scene).Length, "one sun");
            });
        }

        [TestCaseSource(nameof(Maps))]
        public void HasNoModePieces(string map)
        {
            WithScene(map, scene =>
            {
                Assert.IsEmpty(InScene<Airfield>(scene), "airfields come from the layout");
                Assert.IsEmpty(InScene<ApproachLane>(scene), "lanes come with the airfields");
                Assert.IsEmpty(InScene<FieldDefenses>(scene), "AA comes from the layout");
                Assert.IsEmpty(InScene<BannerStand>(scene), "banner stands come from the layout");
                Assert.IsEmpty(InScene<ModeArena>(scene), "arenas come from the layout");
                Assert.IsEmpty(InScene<MatchManager>(scene), "the match is not part of a map");
                Assert.IsEmpty(InScene<BotRoster>(scene), "bots are not part of a map");
                Assert.IsEmpty(InScene<LevelSettings>(scene), "spawn settings are not part of a map");
                Assert.IsEmpty(InScene<Camera>(scene), "the camera comes with the player or the menu");
            });
        }

        static readonly string[] NewMaps = { "IslandV2", "BroadReach", "ShatteredArchipelago", "MountainRange" };

        [TestCaseSource(nameof(NewMaps))]
        public void NewMapHasFlatPadsAndLandmarks(string mapName)
        {
            WithScene(mapName, scene =>
            {
                var terrain = InScene<Terrain>(scene)[0];
                var map = InScene<MapScene>(scene)[0].Definition;
                foreach (var field in map.layout.airfields)
                {
                    // The runway and its apron (the pad less a margin) stand on ground level with the runway.
                    var rotation = Quaternion.Euler(0f, field.yaw, 0f);
                    for (float v = -350f; v <= 350f; v += 50f)
                    for (float u = -100f; u <= 100f; u += 50f)
                    {
                        Vector3 p = new Vector3(field.position.x, 0f, field.position.y) + rotation * new Vector3(u, 0f, v);
                        float ground = terrain.SampleHeight(p) + terrain.transform.position.y;
                        Assert.AreEqual(field.elevation, ground, 0.6f, $"{field.name} pad at ({p.x:F0}, {p.z:F0})");
                    }
                }
                Assert.AreEqual(map.landmarks.Count, InScene<Landmark>(scene).Length, "the definition's landmarks");
                Assert.Greater(map.sky.clouds.Count, 0, "cloud banks");
            });
        }

        [Test]
        public void IslandMapKeepsItsScenery()
        {
            WithScene("Island", scene =>
            {
                var world = System.Array.Find(scene.GetRootGameObjects(), g => g.name == "World");
                Assert.IsNotNull(world);
                Assert.IsNotNull(world.transform.Find("Clouds"), "clouds");
                Assert.Greater(world.transform.childCount, 6, "city, canal, bridges, mesas and landmarks under World");
            });
        }

        [Test]
        public void MapSelect_HasACardPerMap_EachInBuildSettings()
        {
            Assert.AreEqual(GameMaps.Length, MapInfo.All.Count, "a map select card per game map");
            for (int i = 0; i < GameMaps.Length; i++)
            {
                var card = MapInfo.All[i];
                Assert.AreEqual($"Map_{GameMaps[i]}", card.Scene);
                Assert.AreEqual(i, MapInfo.IndexOf(card.Scene));
                Assert.IsFalse(string.IsNullOrEmpty(card.Name) || string.IsNullOrEmpty(card.Pitch), $"{card.Scene}: name and pitch");
                Assert.IsTrue(System.Array.Exists(EditorBuildSettings.scenes, s => s.enabled && s.path == Path(GameMaps[i])),
                    $"{card.Scene} is in Build Settings, so the match can load it");
            }
            Assert.AreEqual(-1, MapInfo.IndexOf("Island"), "only map scenes");
        }

        [Test]
        public void TestIsland_HasNoCard_AndIsLeftOutOfTheRelease()
        {
            Assert.AreEqual(-1, MapInfo.IndexOf("Map_Island"), "no map select card");
            foreach (var path in new[] { Path("Island"), "Assets/_Project/Scenes/Island.unity" })
                Assert.IsFalse(System.Array.Exists(EditorBuildSettings.scenes, s => s.path == path), $"{path} is not in Build Settings");
        }

        [Test]
        public void MountainRange_IsSnowy()
        {
            WithScene("MountainRange", scene =>
            {
                var map = InScene<MapScene>(scene)[0].Definition;
                Assert.IsTrue(map.rules.winter, "winter rules: no grass, snowy woods");
                Assert.AreEqual(1, InScene<Snowfall>(scene).Length, "snow falls");
                var terrain = InScene<Terrain>(scene)[0].terrainData;
                // The snow layer (splat 3) covers most of the ground; grass (splat 0) none of it.
                var splat = terrain.GetAlphamaps(0, 0, terrain.alphamapWidth, terrain.alphamapHeight);
                double grass = 0, snow = 0;
                for (int z = 0; z < splat.GetLength(0); z += 8)
                for (int x = 0; x < splat.GetLength(1); x += 8)
                {
                    grass += splat[z, x, 0];
                    snow += splat[z, x, 3];
                }
                Assert.Greater(snow, 4 * grass + 1, $"snow {snow:F0} vs grass {grass:F0}");
                int snowyPines = 0;
                foreach (var tree in terrain.treeInstances)
                {
                    string kind = terrain.treePrototypes[tree.prototypeIndex].prefab.name;
                    Assert.IsFalse(kind.StartsWith("TreeOak") || kind.StartsWith("TreePalm"), $"a {kind} in the snow");
                    if (kind == "TreePineSnow")
                        snowyPines++;
                }
                Assert.Greater(snowyPines, 1000, "snowy pine woods");
            });
            WithScene("BroadReach", scene => Assert.IsEmpty(InScene<Snowfall>(scene), "only the winter map snows"));
        }
    }
}

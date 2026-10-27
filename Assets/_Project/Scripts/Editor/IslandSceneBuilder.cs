using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Bots;
using WingingIt.CameraSystem;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Maps;
using WingingIt.Match;
using WingingIt.UI;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Generates the greybox Island map (island-map.md): one terrain with a flat central city cut by a canal and two
    /// bridges, a mountain range in the north-west with a canyon through it, a desert of dunes and mesas in the east,
    /// grassland between them, and the ocean all around. Two home airfields (west = team 0, east = team 1) and a
    /// neutral one south of the city sit on flattened pads. The player spawns over the west home airfield.
    /// Reuses the plane and airfield prefabs, class assets and materials from <see cref="FlightTestSceneBuilder"/>
    /// (building them first if they're missing). The terrain, its layers and the scene are regenerated every time.
    /// What any map has goes through the shared map builders (terrain and scatter, sea, boundary, landmarks, sky, mode
    /// pieces); what only the Island has is here: its shape and regions (<c>IslandSceneBuilder.Terrain.cs</c>), its
    /// city, canal and bridges (<c>IslandSceneBuilder.City.cs</c>), and its layout and definition.
    /// </summary>
    public static partial class IslandSceneBuilder
    {
        const string ScenePath = B.Root + "/Scenes/Island.unity";
        const string TerrainFolder = MapTerrainBuilder.TerrainFolder;
        const string TerrainDataPath = TerrainFolder + "/IslandTerrain.asset";
        const string IslandDefinitionPath = B.Root + "/Maps/Island/Island.asset";

        // Scale (island-map.md): a Fighter cruises at ~85 m/s, so the 4.1 km east-west crossing takes ~48 s and the
        // boundary circle ~61 s. The sea is world y = 0; lowland, the city and the airfields sit at LandHeight.
        static readonly Vector2 IslandHalfSize = new Vector2(2050f, 1350f);
        const float LandHeight = 6f;
        const float SeaFloor = -30f;
        internal const float BoundaryRadius = 2600f;
        internal const float Ceiling = 1500f;

        /// <summary>The Island's terrain square, for the shared ocean builder.</summary>
        static float MapSize() => TerrainSize;

        // Terrain: 5.6 km square centered on the island, ~5.5 m per height sample, world y -40 .. 760.
        const float TerrainSize = 5600f;
        const float TerrainBase = -40f;
        const float TerrainHeight = 800f;
        const int HeightmapResolution = 1025;

        // Regions
        static readonly Vector2 CityCenter = new Vector2(0f, 250f);
        const float CityRadius = 650f;
        static readonly Vector2 MountainCenter = new Vector2(-900f, 450f);
        static readonly Vector2 MountainRadii = new Vector2(700f, 650f);
        static readonly Vector2 CanyonStart = new Vector2(-1500f, 150f), CanyonEnd = new Vector2(-350f, 500f);
        const float DesertStartX = 450f, DesertFullX = 850f;
        /// <summary>Flat-topped desert buttes: center, top radius, height above the dunes.</summary>
        static readonly (Vector2 center, float radius, float height)[] Mesas =
        {
            (new Vector2(1150f, 350f), 130f, 120f),
            (new Vector2(1150f, -620f), 100f, 90f),
            (new Vector2(750f, -230f), 80f, 140f),
        };
        // The canal runs from the middle of the city north to the sea, between quay walls 120 m apart (their face is
        // CanalHalfWidth from the middle, with a promenade behind: the city pack's Quay). The bridges carry two streets.
        const float CanalStartZ = 50f;
        const float CanalHalfWidth = 60f;
        static readonly float[] BridgeZ = { 200f, 560f };

        // Landmarks, one per region (island-map.md). The Spire takes a whole city block as its plaza; the summit
        // radar goes on the highest mountain peak; the lighthouse stands on a rocky islet off the south coast.
        // Fairness (island-map.md, Retreat times): the Pyramid mirrors the summit radar (found at about (-804, 794)) so
        // each team's home-region landmark is the same ~13 s from its home field and outside the other team's AA, and
        // the lighthouse sits on the center line, equally far from both home fields.
        static readonly Vector2 SpirePosition = new Vector2(-230f, 260f);
        static readonly Vector2 PyramidPosition = new Vector2(804f, 794f);
        static readonly Vector2 IsletPosition = new Vector2(0f, -1950f);
        const float IsletRadius = 90f, IsletHeight = 24f;

        /// <summary>
        /// The three airfields. Yaw 0 = landing heading north. Home fields are point-symmetric about the center. Central
        /// sits close enough to the city that, once captured, every fight area is within ~20 s of a friendly field.
        /// </summary>
        static readonly (string name, int team, bool home, Vector2 position, float yaw)[] Airfields =
        {
            ("West Home Airfield", 0, true, new Vector2(-1600f, 0f), 0f),
            ("East Home Airfield", 1, true, new Vector2(1600f, 0f), 180f),
            ("Central Airfield", Airfield.Neutral, false, new Vector2(0f, -700f), 90f),
            // Airfield Domination's mirrored pair of capture fields: equally far from both homes, one-way, landing north
            // off the south sea (the other end faces the south coast hills).
            ("South West Airfield", Airfield.Neutral, false, new Vector2(-750f, -900f), 0f),
            ("South East Airfield", Airfield.Neutral, false, new Vector2(750f, -900f), 0f),
        };
        /// <summary>
        /// Where bots patrol when nothing's in sight (the layout's fight areas, placed by <c>ModePieceBuilder</c>): world XZ
        /// and the height to fly above the highest thing nearby. Mirrored across x = 0, so both teams' areas are
        /// the same distances from home: their own home-region landmark, the city, Central, the lighthouse, the enemy's.
        /// </summary>
        static readonly (string name, Vector2 xz, float agl)[] FightAreas =
        {
            ("City", new Vector2(0f, 300f), 150f),
            ("Summit Radar", new Vector2(-600f, 600f), 200f),
            ("The Pyramid", new Vector2(600f, 600f), 200f),
            ("Central Airfield", new Vector2(0f, -700f), 250f),
            ("Lighthouse Rock", new Vector2(0f, -1700f), 250f),
        };

        // Flattened pad around each airfield (local half extents), blending into the land around it: every map's.
        static readonly Vector2 PadHalfSize = MapTerrainBuilder.PadHalfSize;
        const float PadBlend = MapTerrainBuilder.PadBlend;

        /// <summary>
        /// East Home's stand-in for the Canyon Run: the same circuit, but its first leg weaves low between the desert
        /// mesas instead of through a canyon. Its points mirror the canyon's, nudged clear of the mesas.
        /// </summary>
        static readonly (Vector2 xz, float agl)[] EastDuneRun =
        {
            (new Vector2(350f, -500f), 50f), (new Vector2(800f, -450f), 50f), (new Vector2(1150f, -300f), 50f),
            (new Vector2(1500f, -150f), 50f), (new Vector2(1950f, -120f), 60f), (new Vector2(1980f, 450f), 60f),
            (new Vector2(1950f, 1150f), 50f),
        };

        /// <summary>
        /// East Home's Grassland lane: the mirror of West's runs into the side of the Pyramid, so it passes south of
        /// it instead, between the Pyramid and the northern mesa, starting a little further east to keep the length.
        /// </summary>
        static readonly (Vector2 xz, float agl)[] EastGrassland =
        {
            (new Vector2(640f, 520f), 60f), (new Vector2(1050f, 600f), 60f), (new Vector2(1250f, 1150f), 50f),
        };

        /// <summary>
        /// Approach lanes into West Home (island-map.md, Approach lanes): world XZ waypoints and the height to fly each
        /// one above the highest thing near it. Each lane ends on the field's final approach fix on the glide path
        /// (<see cref="ModePieceBuilder"/> builds them from the layout). East Home gets the same lanes mirrored
        /// through the island center (<see cref="MirrorLane"/>), so both teams' routes are the same length, except where
        /// the terrain isn't a mirror image: those East lanes have their own points (<c>east</c>), kept the same length.
        /// Central's are <see cref="CentralLane"/>.
        /// </summary>
        static readonly (string name, string eastName, Color color, (Vector2 xz, float agl)[] points, (Vector2 xz, float agl)[] east)[] WestLanes =
        {
            // From the city through the mountain canyon, out over the sea west of the runway, then a left downwind.
            ("Canyon Run", "Dune Run", new Color(0.95f, 0.35f, 0.9f), new[]
            {
                (new Vector2(-350f, 500f), 50f), (new Vector2(-733f, 383f), 50f), (new Vector2(-1117f, 267f), 50f),
                (new Vector2(-1500f, 150f), 50f), (new Vector2(-1950f, 120f), 60f), (new Vector2(-1980f, -450f), 60f),
                (new Vector2(-1950f, -1150f), 50f),
            }, EastDuneRun),
            // From the ocean, low along the south coast, then a straight-in final.
            ("South Coast", "North Shore", new Color(0.2f, 0.85f, 1f), new[]
            {
                (new Vector2(-250f, -1500f), 60f), (new Vector2(-1100f, -1620f), 60f), (new Vector2(-1600f, -1420f), 50f),
            }, null),
            // From the south of the city and Central, low over the grassland, then a right base.
            ("Grassland", "Grassland", new Color(0.6f, 1f, 0.2f), new[]
            {
                (new Vector2(-550f, -450f), 60f), (new Vector2(-950f, -800f), 60f), (new Vector2(-1250f, -1150f), 50f),
            }, EastGrassland),
        };

        /// <summary>
        /// Central lands from either end, so it has one lane into each: the same route from the south sea, mirrored across
        /// x = 0 for the east end, so both teams (and both ends) get identical approaches that stay clear of both homes'
        /// AA. The west one ends on the west end's glide path, heading east.
        /// </summary>
        static readonly (Vector2 xz, float agl)[] CentralLane =
        {
            (new Vector2(-300f, -1500f), 60f), (new Vector2(-900f, -1500f), 60f), (new Vector2(-1300f, -1250f), 60f),
            (new Vector2(-1350f, -850f), 60f), (new Vector2(-1150f, -720f), 50f),
        };

        /// <summary>
        /// The south-west capture field's two lanes, both from the open sea south of the island to its final fix (the
        /// south-east field's are the x-mirror). They stay clear of the lighthouse and of Central's and the homes' lanes.
        /// </summary>
        static readonly (string name, (Vector2 xz, float agl)[] points)[] SouthFieldLanes =
        {
            ("Open Sea", new[] { (new Vector2(-300f, -2100f), 70f), (new Vector2(-700f, -2100f), 60f) }),
            ("Coast Run", new[] { (new Vector2(-1300f, -1550f), 70f), (new Vector2(-1150f, -1800f), 70f), (new Vector2(-1000f, -1950f), 60f), (new Vector2(-800f, -2000f), 60f) }),
        };

        /// <summary>
        /// Cloud banks for cover (island-map.md, Clouds): the west half of the map; each is mirrored through the island
        /// center for the east team, like the approach lanes. Center (x, z), horizontal radius, center altitude, seed.
        /// Kept out of home AA reach, clear of landmark tops and over the fight areas.
        /// </summary>
        static readonly (Vector2 xz, float radius, float altitude, int seed)[] CloudBanks =
        {
            (new Vector2(-450f, 650f), 210f, 470f, 11),    // between the city and the mountains, over the canyon's end
            (new Vector2(-150f, -500f), 190f, 440f, 23),   // south city edge, toward Central (mirror: north city, off the sight line to the Pyramid)
            (new Vector2(-500f, -1000f), 220f, 420f, 37),  // south coast, west of Central
            (new Vector2(-1150f, 1200f), 200f, 500f, 41),  // north coast, behind the mountains
            (new Vector2(-1800f, 1250f), 230f, 430f, 53),  // north-west sea
            (new Vector2(-700f, -1500f), 240f, 410f, 67),  // south sea, toward the lighthouse
        };
        /// <summary>Puff heights (vertical radius of the core puff). 70 m gives ~140 m of cloud to hide in.</summary>
        const float CloudCoreHalfHeight = 70f;
        /// <summary>A cloud's underside stays at least this far above anything solid below it.</summary>
        const float CloudClearance = 80f;

        [MenuItem("Winging It/Build Island Scene")]
        static void BuildFromMenu()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild Island",
                    $"This regenerates {ScenePath} and {TerrainDataPath}. Prefabs, class assets and materials are kept.",
                    "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            BuildAll();
        }

        public static void BuildAll()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath) == null
                || AssetDatabase.LoadAssetAtPath<GameObject>(B.AirfieldPrefabPath) == null)
                B.BuildAll();
            B.EnsureFolder(TerrainFolder);

            // Before the class stats: these save assets, and saved (clean) class assets would be unloaded by NewScene.
            WriteIslandDefinition(); // the layout the mode pieces spawn from
            ModePieceKitBuilder.Ensure();
            var classes = B.CreateClassStats();
            var m = new B.Materials();
            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            MapSkyBuilder.ApplyAtmosphere(IslandSky());
            new GameObject("Level Settings").AddComponent<LevelSettings>().SpawnMode = SpawnMode.Air;

            var nature = NatureModels.Load();
            var markers = MarkerModels.Load();
            var (world, _) = BuildWorld(m, nature, markers);

            // Airfields, lanes, banner stands and arenas spawn from the Island's layout when a mode is picked
            // (ModePieceBuilder). Spawn them all here for a moment, so the clouds and the bots' patrol heights clear them
            // as before and the player starts over West Home, then take them out again: none of them is saved in the scene.
            var pieces = new GameObject("Mode Pieces").AddComponent<ModePieceBuilder>();
            pieces.Configure(AssetDatabase.LoadAssetAtPath<MapDefinition>(IslandDefinitionPath),
                AssetDatabase.LoadAssetAtPath<ModePieceKit>(ModePieceKitBuilder.KitPath));
            pieces.BuildEverything();
            Physics.SyncTransforms();
            MapSkyBuilder.BuildClouds(world, IslandSky(), nature);
            var playerSpawn = pieces.Airfields["West Home Airfield"].AirSpawn;

            B.BuildPlayer(scene, planePrefab, classes, playerSpawn);
            var roster = BuildBotRoster(planePrefab, classes);
            pieces.Clear();
            while (pieces.transform.childCount > 0)
                Object.DestroyImmediate(pieces.transform.GetChild(0).gameObject); // the empty groups: they're made again at runtime
            BuildMatch(roster, classes, pieces);

            EditorSceneManager.SaveScene(scene, ScenePath);
            B.AddSceneToBuildSettings(ScenePath, first: false); // the tests' level; a build opens MainMenu
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {ScenePath}");
        }

        /// <summary>
        /// What every mode on the Island shares, under a new "World": the terrain, mesas, sea, bridges, city, canal,
        /// landmarks and the flight boundary with its buoys. No airfields, lanes, stands, arenas or clouds.
        /// </summary>
        static (Transform world, Terrain terrain) BuildWorld(B.Materials m, NatureModels nature, MarkerModels markers)
        {
            var city = CityModels.Load();
            var world = new GameObject("World").transform;
            var terrain = BuildTerrain(world, nature);
            BuildMesas(world, nature);
            MapOceanBuilder.Build(world, MapSize(), Height);
            var bridges = BuildBridges(world, m, city);
            BuildCity(world, m, city, nature, bridges);
            BuildCanal(world, city);
            BuildLandmarks(world, terrain, m, nature, LandmarkModels.Load());
            MapBoundaryBuilder.Build(world, BoundaryRadius, Ceiling, true, markers);
            return (world, terrain);
        }

        /// <summary>
        /// The Island as a map scene (<c>Scenes/Maps/Map_Island</c>, see <see cref="MapSceneBuilder"/>): the shared world
        /// and its clouds, with none of the mode pieces <see cref="BuildAll"/> adds. The game loads it from the main menu
        /// scene (<see cref="MainMenuSceneBuilder"/>), and so do the PlayMode tests (<c>MapTestScene</c>); nothing loads
        /// <c>Island.unity</c> any more.
        /// </summary>
        public static void BuildMapScene(Maps.MapDefinition definition)
        {
            B.EnsureFolder(TerrainFolder);
            string path = AssetDatabase.GetAssetPath(definition);
            var (scene, _) = MapSceneBuilder.NewMapScene(definition);
            var nature = NatureModels.Load();
            var (world, _) = BuildWorld(new B.Materials(), nature, MarkerModels.Load());
            Physics.SyncTransforms();
            MapSkyBuilder.BuildClouds(world, IslandSky(), nature);
            MapSceneBuilder.Save(scene, path);
        }

        // ------------------------------------------------------------------ landmarks

        /// <summary>
        /// One big landmark per region (<see cref="MapLandmarkBuilder"/>): the Spire on its city plaza, the radar on the
        /// highest mountain peak, the Pyramid in the desert and the lighthouse on its islet.
        /// </summary>
        static void BuildLandmarks(Transform parent, Terrain terrain, B.Materials m, NatureModels nature, LandmarkModels models)
        {
            var landmarks = new MapLandmarkBuilder(parent, m, nature, models);
            landmarks.Spire(new Vector3(SpirePosition.x, LandHeight, SpirePosition.y), "City");
            landmarks.SummitRadar(MapLandmarkBuilder.HighestPoint(terrain, MountainCenter, MountainRadii), "Mountains");
            landmarks.Pyramid(terrain, PyramidPosition, "Desert");
            landmarks.Lighthouse(IsletPosition, IsletHeight, "LighthouseSea");
            landmarks.Finish();
        }

        // ------------------------------------------------------------------ approach lanes

        /// <summary>A West Home lane point mirrored through the island center, for East Home.</summary>
        static (Vector2 xz, float agl)[] MirrorLane((Vector2 xz, float agl)[] points)
        {
            var mirrored = new (Vector2, float)[points.Length];
            for (int i = 0; i < points.Length; i++)
                mirrored[i] = (-points[i].xz, points[i].agl);
            return mirrored;
        }

        /// <summary>A lane point mirrored across x = 0 (Central's west lane for its east end).</summary>
        static (Vector2 xz, float agl)[] MirrorLaneX((Vector2 xz, float agl)[] points)
        {
            var mirrored = new (Vector2, float)[points.Length];
            for (int i = 0; i < points.Length; i++)
                mirrored[i] = (new Vector2(-points[i].xz.x, points[i].xz.y), points[i].agl);
            return mirrored;
        }

        // ------------------------------------------------------------------ mode arenas

        /// <summary>How far past a home runway's far end (m) the banner pole stands, clear of the field's AA corners.</summary>
        const float BannerStandDistance = 150f;
        /// <summary>Ram Royale's arena (match-modes.md): a small circle over the sea round Lighthouse Rock.</summary>
        const float RamArenaRadius = 700f, RamArenaCeiling = 450f, RamArenaWarning = 150f;
        const float RamStartRadius = 550f, RamStartAltitude = 220f;
        const float RamPatrolRadius = 350f, RamPatrolAltitude = 200f;

        // ------------------------------------------------------------------ map layout

        /// <summary>
        /// The Island's airfields, lanes, banner stands, Ram Royale arena and per-mode piece lists as anchor data
        /// (<see cref="Maps.MapLayout"/>), read from the same constants the scene is built from. Written to the
        /// Island's <c>MapDefinition</c> by <c>Winging It/Map/Write Island Definition</c>.
        /// </summary>
        internal static Maps.MapLayout BuildLayout()
        {
            var layout = new Maps.MapLayout();
            foreach (var (name, team, home, position, yaw) in Airfields)
                layout.airfields.Add(new Maps.AirfieldAnchor
                {
                    name = name, team = team, home = home, twoWay = name == "Central Airfield",
                    capturable = !home, position = position, elevation = LandHeight, yaw = yaw,
                });

            Maps.LayoutPoint[] Points((Vector2 xz, float agl)[] pts)
            {
                var result = new Maps.LayoutPoint[pts.Length];
                for (int i = 0; i < pts.Length; i++)
                    result[i] = new Maps.LayoutPoint(pts[i].xz, pts[i].agl);
                return result;
            }
            void Lane(string field, string name, Color color, (Vector2 xz, float agl)[] pts, bool reverseEnd = false) =>
                layout.lanes.Add(new Maps.LaneAnchor { airfield = field, name = name, color = color, points = Points(pts), reverseEnd = reverseEnd });

            foreach (var (name, eastName, color, points, eastPoints) in WestLanes)
            {
                Lane("West Home Airfield", name, color, points);
                Lane("East Home Airfield", eastName, color, eastPoints ?? MirrorLane(points));
            }
            var gold = new Color(1f, 0.8f, 0.2f);
            Lane("Central Airfield", "West Final", gold, CentralLane);
            Lane("Central Airfield", "East Final", gold, MirrorLaneX(CentralLane), reverseEnd: true);
            var orange = new Color(1f, 0.55f, 0.2f);
            foreach (var (name, points) in SouthFieldLanes)
            {
                Lane("South West Airfield", name, orange, points);
                Lane("South East Airfield", name, orange, MirrorLaneX(points));
            }

            layout.bannerStands.Add(new Maps.BannerStandAnchor { airfield = "West Home Airfield", distance = BannerStandDistance });
            layout.bannerStands.Add(new Maps.BannerStandAnchor { airfield = "East Home Airfield", distance = BannerStandDistance });

            var ramAreas = new List<Maps.LayoutPoint> { new(IsletPosition, RamPatrolAltitude) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * RamPatrolRadius;
                ramAreas.Add(new Maps.LayoutPoint(IsletPosition + new Vector2(offset.x, offset.z), RamPatrolAltitude));
            }
            layout.arenas.Add(new Maps.ArenaAnchor
            {
                modeId = "RamRoyale", center = IsletPosition, radius = RamArenaRadius, warning = RamArenaWarning,
                ceiling = RamArenaCeiling, hardEdge = true, closeAirfields = true, airStart = true,
                startRadius = RamStartRadius, startAltitude = RamStartAltitude, startSlots = 16,
                fightAreas = ramAreas.ToArray(),
            });

            foreach (var (name, xz, agl) in FightAreas)
                layout.fightAreas.Add(new Maps.FightAreaAnchor { name = name, point = new Maps.LayoutPoint(xz, agl) });

            var allFields = new string[Airfields.Length];
            for (int i = 0; i < allFields.Length; i++)
                allFields[i] = Airfields[i].name;
            // Every mode but Ram Royale plays the Island whole. Ram Royale's arena closes every field, but the home fields
            // stay: their hangars are the menu's backdrop while the menu shares the map's scene.
            layout.modes.Add(new Maps.ModeLayout { modeId = "Teams", airfields = allFields });
            layout.modes.Add(new Maps.ModeLayout { modeId = "Banner", airfields = allFields, bannerStands = true });
            layout.modes.Add(new Maps.ModeLayout { modeId = "Domination", airfields = allFields });
            layout.modes.Add(new Maps.ModeLayout { modeId = "RamRoyale", airfields = new[] { "West Home Airfield", "East Home Airfield" }, arena = "RamRoyale" });
            return layout;
        }

        /// <summary>
        /// The Island's regions as the map-review survey reads them: the lighthouse sea first (it counts over water),
        /// then the city (its canal is sea), the mountains, and the desert east of <see cref="DesertStartX"/>; the rest
        /// of the land is grassland.
        /// </summary>
        internal static Maps.MapRegions IslandRegions()
        {
            const float half = TerrainSize * 0.5f;
            var regions = new Maps.MapRegions { sea = "Sea", fallback = "Grassland" };
            regions.areas.Add(new Maps.RegionArea { name = "LighthouseSea", shape = Maps.RegionShape.Ellipse, center = new Vector2(0f, -1950f), size = new Vector2(700f, 700f), landOnly = false });
            regions.areas.Add(new Maps.RegionArea { name = "City", shape = Maps.RegionShape.Ellipse, center = CityCenter, size = new Vector2(CityRadius + 50f, CityRadius + 50f) });
            regions.areas.Add(new Maps.RegionArea { name = "Mountains", shape = Maps.RegionShape.Ellipse, center = MountainCenter, size = new Vector2(650f, 650f) });
            regions.areas.Add(new Maps.RegionArea { name = "Desert", shape = Maps.RegionShape.Box, center = new Vector2((DesertStartX + half) * 0.5f, 0f), size = new Vector2((half - DesertStartX) * 0.5f, half) });
            return regions;
        }

        /// <summary>The Island's sun, fog, sky and the <see cref="CloudBanks"/>, each mirrored for the east team.</summary>
        internal static Maps.MapSky IslandSky()
        {
            var sky = new Maps.MapSky
            {
                skybox = MapSkyBuilder.GreyboxSky(), fogStart = 900f, fogEnd = 6500f, mirrorClouds = true,
                cloudCoreHalfHeight = CloudCoreHalfHeight, cloudClearance = CloudClearance,
            };
            foreach (var (xz, radius, altitude, seed) in CloudBanks)
                sky.clouds.Add(new Maps.CloudBankAnchor(xz, radius, altitude, seed));
            return sky;
        }

        [MenuItem("Winging It/Map/Write Island Definition")]
        static void WriteIslandDefinition()
        {
            var def = AssetDatabase.LoadAssetAtPath<Maps.MapDefinition>(IslandDefinitionPath);
            if (def == null)
            {
                Debug.LogError("[Winging It] Island MapDefinition not found.");
                return;
            }
            def.layout = BuildLayout();
            def.regions = IslandRegions();
            def.sky = IslandSky();
            def.minimap = new Maps.MapMinimap();
            EditorUtility.SetDirty(def);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Island definition written: {def.layout.airfields.Count} airfields, {def.layout.lanes.Count} lanes, "
                + $"{def.regions.areas.Count} regions, {def.sky.clouds.Count} cloud banks, {def.layout.Validate().Count} problems");
        }

        /// <summary>
        /// Fills both teams to 8v8 with bots. Their patrols aren't baked in: the match hands them the layout's fight areas
        /// (<see cref="FightAreas"/> on the Island) for the mode it picks.
        /// </summary>
        internal static BotRoster BuildBotRoster(GameObject planePrefab, FlightStats[] classes)
        {
            var roster = new GameObject("Bot Roster").AddComponent<BotRoster>();
            roster.Configure(planePrefab, classes, new List<Vector3>());
            return roster;
        }

        /// <summary>
        /// The Teams match flow (menu → class select → 5:00 → scoreboard), its screens and the menu camera. The player's
        /// controls, dev tools, flight HUD and touch controls only run in play (controls and touch controls also stop
        /// while the in-match menu is open); the free class switcher only runs once dismissed. With a
        /// <paramref name="loader"/> (the main menu scene) the map scene loads at the class pick.
        /// </summary>
        internal static void BuildMatch(BotRoster roster, FlightStats[] classes, ModePieceBuilder pieces, MapLoader loader = null)
        {
            var player = GameObject.Find("Player Plane");
            var go = new GameObject("Match");
            var match = go.AddComponent<MatchManager>();
            B.SetField(match, "player", player.GetComponent<PlaneController>());
            B.SetField(match, "roster", roster);
            B.SetArray(match, "classes", classes);
            B.SetArray(match, "playOnlyBehaviours", new Behaviour[]
            {
                player.GetComponent<PlayerFlightInput>(), player.GetComponent<DamageTester>(),
            });
            B.SetArray(match, "sandboxOnlyBehaviours", new Behaviour[] { player.GetComponent<ClassSwitcher>() });
            B.SetField(match, "cameraRig", Object.FindAnyObjectByType<FlightCameraRig>());
            B.SetField(match, "pieces", pieces);
            B.SetField(match, "mapLoader", loader);
            var hud = GameObject.Find("Flight HUD");
            var debugHud = GameObject.Find("Flight Debug HUD");
            B.SetArray(match, "playOnlyObjects", new[] { hud, debugHud });
            B.SetArray(match, "controlObjects", new[] { GameObject.Find("Touch Controls") });
            B.SetField(hud.GetComponent<FlightHUD>(), "match", match);
            B.SetBool(debugHud.GetComponent<FlightDebugHUD>(), "showHelp", false); // the match starts clean: F1 shows it

            var menuCamera = go.AddComponent<MenuCamera>();
            B.SetField(menuCamera, "match", match);
            B.SetField(menuCamera, "view", Camera.main);
            var screens = go.AddComponent<MatchScreens>();
            B.SetField(screens, "match", match);
            B.SetField(screens, "menuCamera", menuCamera);
            B.SetField(screens, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(B.InputActionsPath));
        }
    }
}
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
    /// </summary>
    public static class IslandSceneBuilder
    {
        const string ScenePath = B.Root + "/Scenes/Island.unity";
        const string TerrainFolder = B.Root + "/Terrain";
        const string TerrainDataPath = TerrainFolder + "/IslandTerrain.asset";

        // Scale (island-map.md): a Fighter cruises at ~85 m/s, so the 4.1 km east-west crossing takes ~48 s and the
        // boundary circle ~61 s. The sea is world y = 0; lowland, the city and the airfields sit at LandHeight.
        static readonly Vector2 IslandHalfSize = new Vector2(2050f, 1350f);
        const float LandHeight = 6f;
        const float SeaFloor = -30f;
        const float BoundaryRadius = 2600f;
        const float Ceiling = 1500f;
        const float BuoySpacing = 150f;

        // Terrain: 5.6 km square centered on the island, ~5.5 m per height sample, world y -40 .. 760.
        const float TerrainSize = 5600f;
        const float TerrainBase = -40f;
        const float TerrainHeight = 800f;
        const int HeightmapResolution = 1025;
        const int SplatResolution = 512;

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
        const float PyramidBase = 280f, PyramidStepHeight = 26f;
        const int PyramidSteps = 8;
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
        /// Where bots patrol when nothing's in sight (<see cref="BotRoster"/>): world XZ and the height to fly above the
        /// highest thing within <see cref="FightAreaClearanceRadius"/>. Mirrored across x = 0, so both teams' areas are
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
        const float FightAreaClearanceRadius = 150f;

        // Flattened pad around each airfield (local half extents), blending into the land around it.
        static readonly Vector2 PadHalfSize = new Vector2(130f, 400f);
        const float PadBlend = 250f;

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
        /// one above the highest thing within <see cref="LaneClearanceRadius"/>. Each lane ends on the field's final
        /// approach fix, <see cref="FinalFixDistance"/> m out on the glide path. East Home gets the same lanes mirrored
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

        /// <summary>Where every lane joins its runway's glide path: inside the home AA umbrella, ~31 m above the runway.</summary>
        const float FinalFixDistance = 600f;
        /// <summary>A lane waypoint flies its height above the highest ground or building within this far of it.</summary>
        const float LaneClearanceRadius = 60f;
        const float LaneHalfWidth = 150f;
        /// <summary>Gate pylons stand this far either side of a lane waypoint.</summary>
        const float LaneGateHalfWidth = 40f;

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

            var classes = B.CreateClassStats();
            var m = new B.Materials();
            var planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.PrefabPath);
            var airfieldPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(B.AirfieldPrefabPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            B.SetUpAtmosphere(900f, 6500f);
            RenderSettings.skybox = Sky();
            new GameObject("Level Settings").AddComponent<LevelSettings>().SpawnMode = SpawnMode.Air;

            var nature = NatureModels.Load();
            var city = CityModels.Load();
            var markers = MarkerModels.Load();
            var landmarks = LandmarkModels.Load();
            var world = new GameObject("World").transform;
            var terrain = BuildTerrain(world, nature);
            BuildMesas(world, nature);
            BuildOcean(world);
            var bridges = BuildBridges(world, m, city);
            BuildCity(world, m, city, nature, bridges);
            BuildCanal(world, city);
            BuildLandmarks(world, terrain, m, nature, landmarks);

            var boundary = new GameObject("Flight Boundary").AddComponent<FlightBoundary>();
            boundary.transform.SetParent(world, false);
            boundary.radius = BoundaryRadius;
            boundary.ceiling = Ceiling;
            BuildBoundaryBuoys(world, boundary, markers);

            Transform playerSpawn = null;
            var group = new GameObject("Airfields").transform;
            group.SetParent(world, false);
            var fields = new Dictionary<string, Airfield>();
            foreach (var (name, team, home, position, yaw) in Airfields)
            {
                var go = (GameObject)PrefabUtility.InstantiatePrefab(airfieldPrefab, scene);
                go.name = name;
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(new Vector3(position.x, LandHeight, position.y), Quaternion.Euler(0f, yaw, 0f));
                var field = go.GetComponent<Airfield>();
                var so = new SerializedObject(field);
                so.FindProperty("displayName").stringValue = name;
                so.FindProperty("team").intValue = team;
                so.FindProperty("isHome").boolValue = home;
                so.FindProperty("twoWay").boolValue = name == "Central Airfield";
                so.ApplyModifiedPropertiesWithoutUndo();
                fields[name] = field;
                if (home && team == 0)
                    playerSpawn = field.AirSpawn;
                if (!home)
                    BuildFieldDefenses(field, LightAAStats());
            }
            BuildApproachLanes(world, fields["West Home Airfield"], fields["East Home Airfield"], fields["Central Airfield"],
                fields["South West Airfield"], fields["South East Airfield"], markers);
            BuildBannerStands(world, terrain, fields["West Home Airfield"], fields["East Home Airfield"]);
            BuildClouds(world, nature);
            BuildModeArenas(world);

            B.BuildPlayer(scene, planePrefab, classes, playerSpawn);
            var roster = BuildBotRoster(planePrefab, classes);
            BuildMatch(roster, classes);

            EditorSceneManager.SaveScene(scene, ScenePath);
            B.AddSceneToBuildSettings(ScenePath, first: true); // the scene a build opens: the main menu lives here
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {ScenePath}");
        }

        /// <summary>Control tower HP: about 2 bomb hits.</summary>
        const float TowerHealth = 300f, TowerRebuild = 45f;
        /// <summary>Light AA HP: one bomb, or a couple of rocket salvos.</summary>
        const float LightAAHealth = 120f, LightAARebuild = 30f;

        /// <summary>
        /// Turns a capture field's buildings into Airfield Domination's defenses (<see cref="FieldDefenses"/>): the
        /// tower and its cab become one bombable <see cref="GroundTarget"/>, and of the prefab's four AA guns the two on
        /// one diagonal stay, as destructible light AA; the other two go. Dormant until a Domination match.
        /// </summary>
        static void BuildFieldDefenses(Airfield field, AAStats lightAA)
        {
            PrefabUtility.UnpackPrefabInstance(field.gameObject, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
            string shortName = field.DisplayName.Replace(" Airfield", "");

            var buildings = field.transform.Find("Buildings");
            var tower = buildings.Find("Control Tower"); // the airfield pack's stands on its foot
            var cab = buildings.Find("Tower Cab");
            if (cab != null) // greybox: a shaft box and a cab box, grouped on the ground
            {
                var shaft = tower;
                tower = new GameObject("Control Tower").transform;
                tower.SetParent(buildings, false);
                tower.localPosition = new Vector3(shaft.localPosition.x, 0f, shaft.localPosition.z); // squashes toward the ground
                shaft.name = "Tower Shaft";
                shaft.SetParent(tower, true);
                cab.SetParent(tower, true);
            }
            var towerTarget = tower.gameObject.AddComponent<GroundTarget>();
            towerTarget.Configure($"{shortName} Tower", TowerHealth, tower.GetComponentsInChildren<Renderer>(), TowerRebuild);
            SetStatic(tower, false); // a wreck changes scale

            var aa = field.transform.Find("AA Guns");
            var kept = new List<AAGun>();
            foreach (var gun in aa.GetComponentsInChildren<AAGun>())
            {
                if (gun.name != "AA Gun 1" && gun.name != "AA Gun 4")
                {
                    Object.DestroyImmediate(gun.gameObject);
                    continue;
                }
                var so = new SerializedObject(gun);
                so.FindProperty("stats").objectReferenceValue = lightAA;
                so.ApplyModifiedPropertiesWithoutUndo();
                gun.gameObject.AddComponent<GroundTarget>().Configure($"{shortName} AA", LightAAHealth,
                    gun.GetComponentsInChildren<Renderer>(), LightAARebuild);
                SetStatic(gun.transform, false);
                kept.Add(gun);
            }
            field.gameObject.AddComponent<FieldDefenses>().Configure(towerTarget, kept.ToArray());
        }

        static void SetStatic(Transform root, bool value)
        {
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                t.gameObject.isStatic = value;
        }

        /// <summary>Capture fields' light AA: shorter reach, slower and less accurate than a home field's guns.</summary>
        static AAStats LightAAStats()
        {
            string path = $"{B.WeaponsFolder}/LightAAGuns.asset";
            var stats = AssetDatabase.LoadAssetAtPath<AAStats>(path);
            if (stats != null)
                return stats; // keep hand tuning
            stats = ScriptableObject.CreateInstance<AAStats>();
            stats.range = 600f;
            stats.traverseRate = 40f;
            stats.damage = 1.5f;
            stats.roundsPerSecond = 3f;
            stats.startSpread = 5f;
            stats.settledSpread = 1.4f;
            AssetDatabase.CreateAsset(stats, path);
            return stats;
        }

        /// <summary>The procedural sky with its lower half the fog color, so the sea fades into it with no seam at the horizon.</summary>
        static Material Sky()
        {
            string path = $"{B.MaterialsFolder}/Greybox_Sky.mat";
            var sky = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (sky != null)
                return sky;
            sky = new Material(Shader.Find("Skybox/Procedural"));
            sky.SetColor("_GroundColor", RenderSettings.fogColor);
            sky.SetFloat("_AtmosphereThickness", 0.9f);
            AssetDatabase.CreateAsset(sky, path);
            return sky;
        }

        // ------------------------------------------------------------------ shape

        static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Fractal Perlin noise in ~0..1. <paramref name="seed"/> offsets the sample so layers don't line up.</summary>
        static float Fbm(float x, float z, float scale, int octaves, float seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f, f = 1f / scale;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Mathf.PerlinNoise(x * f + seed + i * 17.3f, z * f - seed + i * 31.7f);
                norm += amp;
                amp *= 0.5f;
                f *= 2f;
            }
            return sum / norm;
        }

        /// <summary>Ridged fractal noise in 0..1: sharp crests and valleys, for the mountains.</summary>
        static float Ridged(float x, float z, float scale, int octaves, float seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f, f = 1f / scale;
            for (int i = 0; i < octaves; i++)
            {
                float n = 1f - Mathf.Abs(Mathf.PerlinNoise(x * f + seed + i * 13.1f, z * f + seed - i * 7.9f) * 2f - 1f);
                sum += amp * n * n;
                norm += amp;
                amp *= 0.5f;
                f *= 2f;
            }
            return sum / norm;
        }

        static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
            return Vector2.Distance(p, a + ab * t);
        }

        /// <summary>1 inland, 0 out at sea, with a wobbly coastline.</summary>
        static float LandMask(float x, float z)
        {
            float d = Mathf.Sqrt(Sq(x / IslandHalfSize.x) + Sq(z / IslandHalfSize.y));
            d += (Fbm(x, z, 900f, 3, 3.1f) - 0.5f) * 0.18f;
            return Smooth(1.02f, 0.94f, d);
        }

        static float Sq(float v) => v * v;

        static float CityMask(float x, float z) => Smooth(CityRadius + 50f, CityRadius - 40f, Vector2.Distance(new Vector2(x, z), CityCenter));

        static float DesertMask(float x, float z) => Smooth(DesertStartX, DesertFullX, x + (Fbm(x, z, 600f, 2, 9.4f) - 0.5f) * 300f);

        static float MountainMask(float x, float z)
        {
            float d = Mathf.Sqrt(Sq((x - MountainCenter.x) / MountainRadii.x) + Sq((z - MountainCenter.y) / MountainRadii.y));
            d += (Fbm(x, z, 500f, 3, 5.7f) - 0.5f) * 0.35f;
            return Smooth(1f, 0.55f, d);
        }

        static float CanalX(float z) => 70f * Mathf.Sin((z - CanalStartZ) / 260f);

        /// <summary>dx/dz of the canal's middle line.</summary>
        static float CanalSlope(float z) => 70f / 260f * Mathf.Cos((z - CanalStartZ) / 260f);

        /// <summary>Distance from the canal's center line (it ends in a rounded basin at <see cref="CanalStartZ"/>).</summary>
        static float CanalDistance(float x, float z) =>
            z >= CanalStartZ ? Mathf.Abs(x - CanalX(z)) : Vector2.Distance(new Vector2(x, z), new Vector2(CanalX(CanalStartZ), CanalStartZ));

        /// <summary>1 on an airfield pad, fading to 0 over <see cref="PadBlend"/> m.</summary>
        static float PadMask(float x, float z)
        {
            float best = 0f;
            foreach (var field in Airfields)
            {
                Vector2 local = Local(new Vector2(x, z), field.position, field.yaw);
                Vector2 outside = Vector2.Max(new Vector2(Mathf.Abs(local.x), Mathf.Abs(local.y)) - PadHalfSize, Vector2.zero);
                best = Mathf.Max(best, Smooth(PadBlend, 0f, outside.magnitude));
            }
            return best;
        }

        /// <summary>A world XZ point in an airfield's frame (x = right of the runway, y = down the runway).</summary>
        static Vector2 Local(Vector2 p, Vector2 origin, float yaw)
        {
            Vector3 l = Quaternion.Euler(0f, -yaw, 0f) * new Vector3(p.x - origin.x, 0f, p.y - origin.y);
            return new Vector2(l.x, l.z);
        }

        /// <summary>World-space ground height (m above the sea) at x, z.</summary>
        static float Height(float x, float z)
        {
            float land = LandMask(x, z);
            float desert = DesertMask(x, z);

            // Grassland: low rolling hills.
            float h = LandHeight + Fbm(x, z, 450f, 3, 1.3f) * 16f * (1f - desert);

            // Desert: wind-blown dune ridges, plus a few flat-topped mesas. The mesas themselves are meshes
            // (BuildMesas); the heightmap keeps a lower, narrower core inside each so no terrain pokes through its cliffs.
            float warp = Fbm(x, z, 400f, 2, 4.2f) * 6f;
            float dune = Mathf.Sin((x * 0.6f + z) / 70f + warp) * 0.5f + 0.5f;
            h += desert * dune * dune * 14f;
            foreach (var (center, radius, height) in Mesas)
            {
                float d = Vector2.Distance(new Vector2(x, z), center);
                h = Mathf.Max(h, LandHeight + (height - 3f) * Smooth(radius - 10f, radius - 40f, d));
            }

            // Mountains: ridged peaks up to ~650 m, with a canyon cut through toward the city.
            float mountains = MountainMask(x, z) * (60f + 600f * Mathf.Pow(Ridged(x, z, 700f, 5, 8.8f), 1.4f));
            float canyon = Smooth(160f, 55f, DistanceToSegment(new Vector2(x, z), CanyonStart, CanyonEnd));
            h += mountains * (1f - 0.97f * canyon);

            // City: flat ground, then the canal (below sea level, so the ocean fills it): flat banks, then a sheer cut
            // just behind the quay walls' face (BuildCanal), inside their promenade, so the walls hide it.
            h = Mathf.Lerp(h, LandHeight, CityMask(x, z));
            float canal = CanalDistance(x, z);
            h = Mathf.Lerp(h, LandHeight, Smooth(CanalHalfWidth + 50f, CanalHalfWidth + 20f, canal));
            h = Mathf.Lerp(h, -10f, Smooth(CanalHalfWidth + 10f, CanalHalfWidth + 3f, canal));

            // Coast: shelve down to the sea floor.
            h = Mathf.Lerp(SeaFloor, h, land);

            // The lighthouse islet: a mound just inside the islet mesh (BuildLighthouse), whose cliffs and grassy top
            // at IsletHeight are what you see and hit.
            float islet = Vector2.Distance(new Vector2(x, z), IsletPosition);
            h = Mathf.Max(h, Mathf.Lerp(SeaFloor, IsletHeight - 1.5f, Smooth(IsletRadius + 20f, IsletRadius - 40f, islet)));

            // Airfield pads last: dead flat at LandHeight.
            return Mathf.Lerp(h, LandHeight, PadMask(x, z));
        }

        // ------------------------------------------------------------------ terrain

        static Terrain BuildTerrain(Transform parent, NatureModels nature)
        {
            var data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data == null)
            {
                data = new TerrainData();
                AssetDatabase.CreateAsset(data, TerrainDataPath);
            }
            data.heightmapResolution = HeightmapResolution;
            data.size = new Vector3(TerrainSize, TerrainHeight, TerrainSize);

            Vector3 origin = new Vector3(-TerrainSize * 0.5f, TerrainBase, -TerrainSize * 0.5f);
            int res = data.heightmapResolution;
            var heights = new float[res, res];
            for (int iz = 0; iz < res; iz++)
            {
                float z = origin.z + iz / (float)(res - 1) * TerrainSize;
                for (int ix = 0; ix < res; ix++)
                {
                    float x = origin.x + ix / (float)(res - 1) * TerrainSize;
                    heights[iz, ix] = Mathf.Clamp01((Height(x, z) - TerrainBase) / TerrainHeight);
                }
            }
            data.SetHeights(0, 0, heights);

            data.terrainLayers = new[]
            {
                Layer(TerrainTextures.Kind.Grass, 80f),
                Layer(TerrainTextures.Kind.Sand, 70f),
                Layer(TerrainTextures.Kind.Rock, 50f),
                Layer(TerrainTextures.Kind.Snow, 60f, smoothness: 0.25f),
                Layer(TerrainTextures.Kind.Paved, 48f),
            };
            PaintLayers(data, origin);
            if (nature != null)
            {
                PlantTrees(data, origin, nature);
                PlantDetails(data, origin, nature);
            }
            else
            {
                data.treeInstances = System.Array.Empty<TreeInstance>();
                data.treePrototypes = System.Array.Empty<TreePrototype>();
                data.detailPrototypes = System.Array.Empty<DetailPrototype>();
            }
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Island Terrain";
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

        /// <summary>Splat weights: grass, sand (beaches, desert, seabed), rock (steep or high), snow (peaks), paved (city).</summary>
        static void PaintLayers(TerrainData data, Vector3 origin)
        {
            int res = SplatResolution;
            data.alphamapResolution = res;
            var maps = new float[res, res, 5];
            for (int iz = 0; iz < res; iz++)
            {
                float nz = (iz + 0.5f) / res;
                float z = origin.z + nz * TerrainSize;
                for (int ix = 0; ix < res; ix++)
                {
                    float nx = (ix + 0.5f) / res;
                    float x = origin.x + nx * TerrainSize;
                    float h = data.GetInterpolatedHeight(nx, nz) + TerrainBase;
                    float steep = data.GetSteepness(nx, nz);

                    float snow = Smooth(380f, 440f, h) * (1f - Smooth(40f, 55f, steep));
                    float rock = Mathf.Max(Smooth(22f, 34f, steep), Smooth(140f, 220f, h)) * (1f - snow);
                    float rest = 1f - snow - rock;
                    float paved = rest * CityMask(x, z) * (h > 1f ? 1f : 0f);
                    float beach = Smooth(0.85f, 0.6f, LandMask(x, z));
                    float sandMask = Mathf.Max(DesertMask(x, z), beach);
                    float sand = (rest - paved) * sandMask;
                    float grass = rest - paved - sand;

                    maps[iz, ix, 0] = grass;
                    maps[iz, ix, 1] = sand;
                    maps[iz, ix, 2] = rock;
                    maps[iz, ix, 3] = snow;
                    maps[iz, ix, 4] = paved;
                }
            }
            data.SetAlphamaps(0, 0, maps);
        }

        /// <summary>A terrain layer on <see cref="TerrainTextures"/>' albedo and normal map, made or updated in place.</summary>
        static TerrainLayer Layer(TerrainTextures.Kind kind, float tileSize, float smoothness = 0f)
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

        // ------------------------------------------------------------------ nature

        const float TreeDistance = 2500f, DetailDistance = 200f;
        /// <summary>Trees and rocks go on a jittered grid this far apart; each cell grows at most one.</summary>
        const float TreeSpacing = 24f;
        const int DetailResolution = 1024;

        /// <summary>The nature pack's terrain trees (props without colliders), in prototype order.</summary>
        static readonly string[] TreeKinds =
        {
            "TreeOak", "TreeOakTall", "TreePine", "TreePineTall", "TreePineSnow", "TreePalm", "TreePalmBent", "TreeDead",
            "Bush", "BushDry", "Rock", "RockTall", "RockFlat", "RockSnow", "RockDesert", "CliffRock", "Cactus", "CactusSmall",
            "BeachRocks", "Driftwood",
        };

        /// <summary>
        /// Nothing grows in the city, the canal, on or off the ends of a runway (planes come in low there), on the
        /// mesas, the islet or under the Pyramid: those are built, or meshes of their own.
        /// </summary>
        static bool KeepClear(float x, float z)
        {
            var p = new Vector2(x, z);
            if (CityMask(x, z) > 0.02f || CanalDistance(x, z) < 100f)
                return true;
            foreach (var field in Airfields)
            {
                Vector2 local = Local(p, field.position, field.yaw);
                if (Mathf.Abs(local.x) < 160f && Mathf.Abs(local.y) < 1300f)
                    return true;
            }
            foreach (var (center, radius, _) in Mesas)
                if (Vector2.Distance(p, center) < radius + 75f)
                    return true;
            return Vector2.Distance(p, PyramidPosition) < PyramidBase * 0.75f || Vector2.Distance(p, IsletPosition) < 150f;
        }

        /// <summary>What grows at a spot (a <see cref="TreeKinds"/> name and its scale range), or null for nothing.</summary>
        static (string kind, float minScale, float maxScale)? PickTree(float x, float z, float h, float steep, double roll)
        {
            if (h < 1.5f || KeepClear(x, z))
                return null;
            float desert = DesertMask(x, z), mountain = MountainMask(x, z);
            bool beach = h < 10f && LandMask(x, z) < 0.97f; // the low strip along the coast
            float grove = Fbm(x, z, 220f, 2, 12.3f); // clumps trees into woods with clearings between

            // Scales are toy-sized (about twice life) so woods and rocks still read from a few hundred metres up.
            if (steep > 58f) // the steepest faces: big boulders and the odd crag (the mountains are mostly 45-70 degrees)
                return roll < 0.12 ? ("Rock", 2f, 3.5f) : roll < 0.18 ? ("CliffRock", 1.2f, 1.8f) : null;
            if (h > 430f) // above the tree line
                return roll < 0.1 ? ("RockSnow", 1.8f, 3.5f) : null;
            if (h > 330f)
                return roll < 0.4 ? ("TreePineSnow", 1.5f, 2.1f) : roll < 0.46 ? ("RockSnow", 1.8f, 3f) : null;
            if (mountain > 0.35f && h > 50f)
            {
                double forest = 0.45 + 0.45 * Smooth(0.42f, 0.6f, grove);
                if (roll < forest)
                    return roll < forest * 0.6 ? ("TreePine", 1.5f, 2.2f) : ("TreePineTall", 1.5f, 2.2f);
                return roll < forest + 0.04 ? ("Rock", 1.6f, 3f) : roll < forest + 0.06 ? ("RockFlat", 1.6f, 2.6f) : null;
            }
            if (desert > 0.5f)
                return roll < 0.04 ? ("Cactus", 1.5f, 2.2f) : roll < 0.08 ? ("CactusSmall", 1.6f, 2.4f)
                    : roll < 0.14 ? ("BushDry", 1.5f, 2.5f) : roll < 0.17 ? ("RockDesert", 1.6f, 3.2f)
                    : roll < 0.185 ? ("TreeDead", 1.5f, 2.1f) : null;
            if (beach)
                return roll < 0.07 ? ("TreePalm", 1.5f, 2.1f) : roll < 0.12 ? ("TreePalmBent", 1.5f, 2.1f)
                    : roll < 0.15 ? ("Driftwood", 1.5f, 2.2f) : roll < 0.18 ? ("BeachRocks", 1.5f, 2.6f) : null;

            // Grassland: woods and copses, bushes at their edges, the odd boulder in the open.
            double wood = 0.75 * Smooth(0.47f, 0.62f, grove) + 0.03;
            if (roll < wood)
                return roll < wood * 0.6 ? ("TreeOak", 1.5f, 2.2f) : ("TreeOakTall", 1.5f, 2.1f);
            return roll < wood + 0.05 ? ("Bush", 1.5f, 2.5f) : roll < wood + 0.065 ? ("Rock", 1.4f, 2.6f)
                : roll < wood + 0.07 ? ("RockFlat", 1.4f, 2.5f) : null;
        }

        /// <summary>Scatters the nature pack's trees, bushes and rocks over the island as terrain trees (no colliders).</summary>
        static void PlantTrees(TerrainData data, Vector3 origin, NatureModels nature)
        {
            var prototypes = new TreePrototype[TreeKinds.Length];
            for (int i = 0; i < TreeKinds.Length; i++)
                prototypes[i] = new TreePrototype { prefab = nature.Prefab(TreeKinds[i]) };
            data.treePrototypes = prototypes;

            var rng = new System.Random(777);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var trees = new List<TreeInstance>();
            for (float cz = -IslandHalfSize.y - 100f; cz <= IslandHalfSize.y + 100f; cz += TreeSpacing)
            for (float cx = -IslandHalfSize.x - 100f; cx <= IslandHalfSize.x + 100f; cx += TreeSpacing)
            {
                float x = cx + Range(-0.45f, 0.45f) * TreeSpacing, z = cz + Range(-0.45f, 0.45f) * TreeSpacing;
                float nx = (x - origin.x) / TerrainSize, nz = (z - origin.z) / TerrainSize;
                float h = data.GetInterpolatedHeight(nx, nz) + TerrainBase;
                double roll = rng.NextDouble();
                var pick = PickTree(x, z, h, data.GetSteepness(nx, nz), roll);
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

        /// <summary>Grass tufts and flowers on the grassland (terrain details, instanced), none on pads or in the city.</summary>
        static void PlantDetails(TerrainData data, Vector3 origin, NatureModels nature)
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

            var rng = new System.Random(778);
            var grass = new int[DetailResolution, DetailResolution];
            var flowers = new int[DetailResolution, DetailResolution];
            float cell = TerrainSize / DetailResolution;
            for (int iz = 0; iz < DetailResolution; iz++)
            for (int ix = 0; ix < DetailResolution; ix++)
            {
                float x = origin.x + (ix + 0.5f) * cell, z = origin.z + (iz + 0.5f) * cell;
                if (Mathf.Abs(x) > IslandHalfSize.x + 100f || Mathf.Abs(z) > IslandHalfSize.y + 100f)
                    continue;
                float nx = (ix + 0.5f) / DetailResolution, nz = (iz + 0.5f) / DetailResolution;
                float h = data.GetInterpolatedHeight(nx, nz) + TerrainBase;
                if (h < 2f || h > 250f || PadMask(x, z) > 0.01f || CityMask(x, z) > 0.02f || CanalDistance(x, z) < 90f
                    || DesertMask(x, z) > 0.3f || LandMask(x, z) < 0.85f || data.GetSteepness(nx, nz) > 25f)
                    continue;
                float meadow = Fbm(x, z, 120f, 2, 21.7f);
                grass[iz, ix] = meadow > 0.45f ? 2 : rng.NextDouble() < 0.5 ? 1 : 0;
                flowers[iz, ix] = meadow > 0.55f && rng.NextDouble() < 0.25 ? 1 : 0;
            }
            data.SetDetailLayer(0, 0, 0, grass);
            data.SetDetailLayer(0, 0, 1, flowers);
        }

        /// <summary>The three desert mesas: hero meshes with matching colliders, standing on the lowland height.</summary>
        static void BuildMesas(Transform parent, NatureModels nature)
        {
            if (nature == null)
                return; // the heightmap's mesa cores stand in
            var group = new GameObject("Mesas").transform;
            group.SetParent(parent, false);
            for (int i = 0; i < Mesas.Length; i++)
                nature.Place($"Mesa{i + 1}", group, new Vector3(Mesas[i].center.x, LandHeight, Mesas[i].center.y), collider: true);
            group.gameObject.isStatic = true;
        }

        // ------------------------------------------------------------------ ocean

        /// <summary>
        /// A 30 km sea plane at y = 0 on the Water layer: solid (flying into it is a crash), but never landable
        /// (<see cref="PlaneController.IsWater"/>).
        /// </summary>
        static void BuildOcean(Transform parent)
        {
            var ocean = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ocean.name = "Ocean";
            ocean.layer = PlaneController.WaterLayer;
            ocean.transform.SetParent(parent, false);
            ocean.transform.localScale = new Vector3(3000f, 1f, 3000f); // 30 km: the edge is always lost in the fog
            Object.DestroyImmediate(ocean.GetComponent<MeshCollider>());
            var box = ocean.AddComponent<BoxCollider>(); // 10 m thick, top at the surface
            box.center = new Vector3(0f, -5f, 0f);
            box.size = new Vector3(10f, 10f, 10f);
            ocean.GetComponent<Renderer>().sharedMaterial = B.Materials.Mat("Greybox_Water", new Color(0.16f, 0.4f, 0.6f), smoothness: 0.85f);
            ocean.isStatic = true;
        }

        // ------------------------------------------------------------------ city

        const float CityPitch = 120f, CityBlockSize = 90f;

        /// <summary>
        /// A downtown grid: 120 m blocks with 30 m streets, towers tallest in the middle (~300 m) falling to ~40 m at
        /// the edge, some plazas left open, nothing on the canal banks or the central airfield's pad. With the city pack
        /// each lot gets one of its buildings (<see cref="CityModels.PlaceBuilding"/>), each block a sidewalk with
        /// lamps, each open block a plaza with trees, and the streets roads and traffic (<see cref="BuildStreets"/>);
        /// without it, boxes. No building stands in the way of a bridge or its ramps (<paramref name="bridges"/>).
        /// </summary>
        static void BuildCity(Transform parent, B.Materials m, CityModels city, NatureModels nature, List<Footprint> bridges)
        {
            var group = new GameObject("City").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(4242);
            var pick = new System.Random(4343); // which building stands on a lot: its own stream, so the lots don't move
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            const float half = CityBlockSize * 0.5f;
            int n = 0, skipped = 0;
            for (float bz = CityCenter.y - CityRadius; bz <= CityCenter.y + CityRadius; bz += CityPitch)
            for (float bx = CityCenter.x - CityRadius; bx <= CityCenter.x + CityRadius; bx += CityPitch)
            {
                // Blocks on a grid offset so the center falls on an intersection.
                float cx = bx + CityPitch * 0.5f, cz = bz + CityPitch * 0.5f;
                float r = Vector2.Distance(new Vector2(cx, cz), CityCenter);
                if (r > CityRadius - 40f || CanalDistance(cx, cz) < 125f || PadMask(cx, cz) > 0.01f)
                    continue;
                var center = new Vector3(cx, LandHeight, cz);
                bool paved = city != null && IsFlat(cx, cz, half, half);
                if (rng.NextDouble() < 0.1) // left open: a square
                {
                    if (paved)
                        BuildPlaza(group, city, nature, center, pick);
                    continue;
                }
                if (paved)
                    city.Place("CityBlock", group, center, collider: false).name = "Sidewalk";
                if (Vector2.Distance(new Vector2(cx, cz), SpirePosition) < 100f) // the Spire's plaza
                    continue;

                float core = Mathf.Lerp(300f, 40f, Smooth(0f, CityRadius, r));
                var lots = new List<(Vector2 center, float size)>();
                if (rng.NextDouble() < 0.4)
                    lots.Add((new Vector2(cx, cz), Range(45f, 75f)));
                else
                    foreach (float ox in new[] { -22.5f, 22.5f })
                    foreach (float oz in new[] { -22.5f, 22.5f })
                        if (rng.NextDouble() < 0.75)
                            lots.Add((new Vector2(cx + ox, cz + oz), Range(26f, 40f)));

                foreach (var (lot, size) in lots)
                {
                    float h = Mathf.Max(20f, core * Range(0.45f, 1.05f) * (size < 42f ? 0.7f : 1f));
                    float w = size, d = Mathf.Min(CityBlockSize, size * Range(0.8f, 1.2f));
                    if (Footprint.Any(bridges, lot, new Vector2(w, d) * 0.5f, 1f))
                    {
                        skipped++;
                        continue;
                    }
                    if (city != null)
                    {
                        var building = city.PlaceBuilding(group, new Vector3(lot.x, LandHeight, lot.y), w, d, h, pick);
                        building.name = $"Building {n++:000} ({building.name})";
                        continue;
                    }
                    B.Box($"Building {n++:000}", group, new Vector3(lot.x, LandHeight + h * 0.5f, lot.y),
                        new Vector3(w, h, d), rng.NextDouble() < 0.5 ? m.Concrete : m.ConcreteDark, true).gameObject.isStatic = true;
                }
            }
            if (skipped > 0)
                Debug.Log($"[Winging It] City: {skipped} lots left empty for the bridges and their ramps");
            if (city != null)
                BuildStreets(group, city, bridges);
        }

        /// <summary>
        /// Whether the ground is dead level at <see cref="LandHeight"/> over a <paramref name="halfX"/> by
        /// <paramref name="halfZ"/> rectangle (and a heightmap cell round it), so a sidewalk or road laid there doesn't
        /// float or sink into a bump: not on the city's ragged edge, a canal bank's cut or a hill.
        /// </summary>
        static bool IsFlat(float x, float z, float halfX, float halfZ)
        {
            const float margin = 6f, tolerance = 0.2f;
            for (int i = -2; i <= 2; i++)
            for (int j = -2; j <= 2; j++)
                if (Mathf.Abs(Height(x + i * (halfX + margin) * 0.5f, z + j * (halfZ + margin) * 0.5f) - LandHeight) > tolerance)
                    return false;
            return true;
        }

        /// <summary>An open block: the pack's plaza (lawns, a fountain) with a pair of oaks on each lawn.</summary>
        static void BuildPlaza(Transform parent, CityModels city, NatureModels nature, Vector3 center, System.Random rng)
        {
            city.Place("Plaza", parent, center, collider: false);
            if (nature == null)
                return;
            foreach (float sx in new[] { -23f, 23f })
            foreach (float sz in new[] { -23f, 23f })
                for (int k = 0; k < 2; k++)
                {
                    var offset = new Vector3(sx + (k == 0 ? -6f : 6f), 0.35f, sz + (k == 0 ? 5f : -5f));
                    var tree = nature.Place(rng.NextDouble() < 0.5 ? "TreeOak" : "TreeOakTall", parent, center + offset, collider: false);
                    tree.transform.rotation = Quaternion.Euler(0f, (float)rng.NextDouble() * 360f, 0f);
                    tree.transform.localScale = Vector3.one * (1.6f + (float)rng.NextDouble() * 0.4f); // toy-sized, like the island's
                }
        }

        /// <summary>
        /// Roads on every street between blocks, and a crossing wherever two meet, where the ground is flat (so not on
        /// the city's edge, a pad or past the canal's promenade), with traffic stopped in their lanes (none under a
        /// bridge ramp).
        /// </summary>
        static void BuildStreets(Transform parent, CityModels city, List<Footprint> bridges)
        {
            var group = new GameObject("Streets").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(4444);
            // Streets run down the gaps between blocks: x0 + i * pitch, z0 + j * pitch.
            float x0 = CityCenter.x - CityRadius, z0 = CityCenter.y - CityRadius;
            int lines = Mathf.FloorToInt(2f * CityRadius / CityPitch);
            const float roadHalfLength = (CityPitch - 30f) * 0.5f, roadHalfWidth = 15f;
            var crossings = new HashSet<Vector2Int>();
            for (int i = 1; i <= lines; i++)
            for (int j = 1; j <= lines; j++)
                foreach (bool alongX in new[] { true, false })
                {
                    // The road east of crossing (i, j), or north of it.
                    Vector2 mid = alongX
                        ? new Vector2(x0 + i * CityPitch + CityPitch * 0.5f, z0 + j * CityPitch)
                        : new Vector2(x0 + i * CityPitch, z0 + j * CityPitch + CityPitch * 0.5f);
                    if (!IsFlat(mid.x, mid.y, alongX ? roadHalfLength : roadHalfWidth, alongX ? roadHalfWidth : roadHalfLength)
                        || PadMask(mid.x, mid.y) > 0.01f)
                        continue;
                    float yaw = alongX ? 90f : 0f;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);
                    var center = new Vector3(mid.x, LandHeight, mid.y);
                    city.Place("Road", group, center, rotation, Vector3.one, null, castShadows: false);
                    crossings.Add(new Vector2Int(i, j));
                    crossings.Add(alongX ? new Vector2Int(i + 1, j) : new Vector2Int(i, j + 1));

                    // Traffic: up to three vehicles in the four lanes (right-hand: the +x lanes head +z), never two in a slot.
                    var slots = new List<int>();
                    for (int c = rng.Next(4); c > 0; c--)
                    {
                        int slot = rng.Next(20);
                        if (slots.Contains(slot))
                            continue;
                        slots.Add(slot);
                        float lane = (slot / 5) switch { 0 => -10f, 1 => -4.5f, 2 => 4.5f, _ => 10f };
                        var local = new Vector3(lane, 0.1f, -30f + (slot % 5) * 15f);
                        string kind = rng.NextDouble() < 0.12 ? "Bus" : CityModels.Cars[rng.Next(3)];
                        Vector3 at = center + rotation * local;
                        if (!Footprint.Any(bridges, new Vector2(at.x, at.z), Vector2.zero, kind == "Bus" ? 8f : 4f))
                            city.PlaceVehicle(kind, group, at, yaw + (lane > 0f ? 0f : 180f));
                    }
                }
            foreach (var c in crossings)
            {
                var p = new Vector2(x0 + c.x * CityPitch, z0 + c.y * CityPitch);
                if (IsFlat(p.x, p.y, roadHalfWidth, roadHalfWidth))
                    city.Place("Crossing", group, new Vector3(p.x, LandHeight, p.y), Quaternion.identity, Vector3.one, null, castShadows: false);
            }
        }

        /// <summary>The city pack's Bridge (build_city.py): deck top over the water, half its length, half its width.</summary>
        const float BridgeDeckTop = 35.5f, BridgeHalfSpan = 115f, BridgeHalfWidth = 12f;
        /// <summary>
        /// The approach ramps: each runs on from a deck end and is back on the street's middle line within
        /// <see cref="RampSwing"/> m (the bridges are square to the canal, so their ends sit off the street), then comes
        /// down at an even grade, easing in and out over <see cref="RampEase"/> m, to touch down at least
        /// <see cref="RampMinRun"/> m out and <see cref="RampCrossingGap"/> m short of a crossing.
        /// </summary>
        const float RampMinRun = 210f, RampSwing = 90f, RampEase = 40f, RampCrossingGap = 12f;
        /// <summary>Ramp deck (the bridge's section): half width at the top, depth, and how far its end sits above the road.</summary>
        const float RampHalfWidth = 9f, RampDepth = 3f, RampEndLift = 0.25f;
        const float RampPierSpacing = 24f;

        /// <summary>Ground a bridge or ramp covers: its middle line and half width. Nothing is built or parked there.</summary>
        readonly struct Footprint
        {
            readonly Vector2[] _line;
            readonly float _halfWidth;

            public Footprint(Vector2[] line, float halfWidth)
            {
                _line = line;
                _halfWidth = halfWidth;
            }

            /// <summary>Whether it comes within <paramref name="margin"/> of a rectangle (its middle and half size).</summary>
            public bool Overlaps(Vector2 center, Vector2 half, float margin)
            {
                for (int i = 0; i < _line.Length - 1; i++)
                {
                    Vector2 a = _line[i], b = _line[i + 1];
                    int steps = Mathf.Max(1, Mathf.CeilToInt(Vector2.Distance(a, b) / 2f));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector2 p = Vector2.Lerp(a, b, k / (float)steps) - center;
                        Vector2 outside = Vector2.Max(new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - half, Vector2.zero);
                        if (outside.magnitude < _halfWidth + margin)
                            return true;
                    }
                }
                return false;
            }

            public static bool Any(List<Footprint> footprints, Vector2 center, Vector2 half, float margin)
            {
                foreach (var f in footprints)
                    if (f.Overlaps(center, half, margin))
                        return true;
                return false;
            }
        }

        /// <summary>
        /// Two bridges over the canal, 34 m above the water, with 150 m between their towers to fly under: the city
        /// pack's cable-stayed bridge square across the canal with a ramp down to the street at each end
        /// (<see cref="BuildRamp"/>), or greybox boxes without it. Returns the ground they cover.
        /// </summary>
        static List<Footprint> BuildBridges(Transform parent, B.Materials m, CityModels city)
        {
            var group = new GameObject("Bridges").transform;
            group.SetParent(parent, false);
            var footprints = new List<Footprint>();
            var rampMeshes = new List<Mesh>();
            for (int i = 0; i < BridgeZ.Length; i++)
            {
                float z = BridgeZ[i], x = CanalX(z);
                if (city != null)
                {
                    // The deck runs along local x: turn it square to the canal.
                    float yaw = Mathf.Atan(CanalSlope(z)) * Mathf.Rad2Deg;
                    var rotation = Quaternion.Euler(0f, yaw, 0f);
                    var placed = city.Place("Bridge", group, new Vector3(x, 0f, z), rotation, Vector3.one, "BridgeCol");
                    placed.name = $"Bridge {i + 1}";
                    Vector3 axis = rotation * Vector3.right;
                    var origin = new Vector2(x, z);
                    var end = new Vector2(axis.x, axis.z) * BridgeHalfSpan;
                    footprints.Add(new Footprint(new[] { origin - end, origin + end }, BridgeHalfWidth));
                    foreach (float side in new[] { 1f, -1f })
                        footprints.Add(BuildRamp(placed.transform, $"Ramp {(side > 0f ? "East" : "West")}", origin + end * side,
                            new Vector2(axis.x, axis.z) * side, z, rampMeshes));
                    continue;
                }
                var bridge = new GameObject($"Bridge {i + 1}").transform;
                bridge.SetParent(group, false);
                bridge.localPosition = new Vector3(x, 0f, z);
                const float deck = 34f, span = 230f;
                B.Box("Deck", bridge, new Vector3(0f, deck, 0f), new Vector3(span, 3f, 18f), m.ConcreteDark, true);
                foreach (float side in new[] { -1f, 1f })
                {
                    B.Box("Tower", bridge, new Vector3(side * 75f, 45f, 0f), new Vector3(6f, 90f, 6f), m.Concrete, true);
                    B.Box("Ramp Pier", bridge, new Vector3(side * (span * 0.5f - 6f), deck * 0.5f, 0f), new Vector3(8f, deck, 14f), m.Concrete, true);
                    // Cables from the tower top to the deck ends and middle, for the silhouette (no collision).
                    foreach (float toward in new[] { 0f, side * span * 0.5f })
                    {
                        Vector3 top = new Vector3(side * 75f, 88f, 0f), end = new Vector3(toward, deck + 1.5f, 0f);
                        var cable = B.Box("Cable", bridge, (top + end) * 0.5f, new Vector3(0.6f, 0.6f, (end - top).magnitude), m.PlaneDark, false);
                        cable.transform.localRotation = Quaternion.LookRotation(end - top);
                    }
                }
                foreach (var t in bridge.GetComponentsInChildren<Transform>())
                    t.gameObject.isStatic = true;
            }
            SaveMeshes(RampMeshesPath, rampMeshes);
            return footprints;
        }

        const string RampMeshesPath = TerrainFolder + "/BridgeRamps.asset";

        /// <summary>
        /// One approach ramp, from a bridge deck's end (<paramref name="start"/>, heading <paramref name="heading"/>) down
        /// to the street along world z = <paramref name="streetZ"/>: the deck's girder section (asphalt, slate sides, a
        /// dashed middle line, white railings on posts) on concrete piers, which leave out the crossings so the streets
        /// across stay open underneath. A mesh in the city pack's style (<see cref="InkMesh"/>) with a collider of its
        /// girder and piers.
        /// </summary>
        static Footprint BuildRamp(Transform bridge, string name, Vector2 start, Vector2 heading, float streetZ, List<Mesh> meshes)
        {
            // Along the street u (out from the bridge), across it v (from its middle line): v eases from the deck end's
            // offset and heading to 0 over RampSwing (a cubic Hermite), then stays on the middle line.
            float dir = Mathf.Sign(heading.x);
            float v0 = start.y - streetZ, slope0 = heading.y / Mathf.Abs(heading.x);
            // Crossings are at x0 + k * pitch: touch down short of the first whose near edge lies far enough out.
            float x0 = CityCenter.x - CityRadius, run = float.MaxValue;
            for (int k = 0; k <= Mathf.CeilToInt(2f * CityRadius / CityPitch); k++)
            {
                float out_ = (x0 + k * CityPitch - dir * (15f + RampCrossingGap) - start.x) * dir;
                if (out_ >= RampMinRun)
                    run = Mathf.Min(run, out_);
            }
            float touchdown = start.x + run * dir;

            var line = new List<Vector3>(); // middle of the deck top, heights still to come
            for (float u = 0f; ; u = Mathf.Min(u + 3f, run))
            {
                float t = Mathf.Clamp01(u / RampSwing);
                float v = u < RampSwing
                    ? (2f * t * t * t - 3f * t * t + 1f) * v0 + (t * t * t - 2f * t * t + t) * RampSwing * slope0
                    : 0f;
                line.Add(new Vector3(start.x + u * dir, 0f, streetZ + v));
                if (u >= run)
                    break;
            }
            var arc = new float[line.Count];
            for (int i = 1; i < line.Count; i++)
                arc[i] = arc[i - 1] + Vector3.Distance(line[i], line[i - 1]);
            float length = arc[^1], fall = BridgeDeckTop - (LandHeight + RampEndLift);
            float grade = fall / (length - RampEase);
            float Top(float s)
            {
                if (s < RampEase)
                    return BridgeDeckTop - grade * s * s / (2f * RampEase);
                if (s > length - RampEase)
                    return LandHeight + RampEndLift + grade * Sq(length - s) / (2f * RampEase);
                return BridgeDeckTop - grade * (RampEase * 0.5f + s - RampEase);
            }
            for (int i = 0; i < line.Count; i++)
                line[i] = new Vector3(line[i].x, Top(arc[i]), line[i].z);

            var rights = new Vector3[line.Count];
            for (int i = 0; i < line.Count; i++)
            {
                Vector3 along = line[Mathf.Min(i + 1, line.Count - 1)] - line[Mathf.Max(i - 1, 0)];
                rights[i] = Vector3.Cross(Vector3.up, new Vector3(along.x, 0f, along.z)).normalized;
            }
            Vector3 At(int i, float across, float up) => line[i] + rights[i] * across + Vector3.up * up;

            var mesh = new InkMesh();
            // Girder: the bridge deck's section (top 18 m, chamfered down to 14 m), open where it meets the deck.
            var girder = new List<Vector3[]>();
            for (int i = 0; i < line.Count; i++)
                girder.Add(new[]
                {
                    At(i, RampHalfWidth, 0f), At(i, RampHalfWidth - 2f, -RampDepth), At(i, 2f - RampHalfWidth, -RampDepth),
                    At(i, -RampHalfWidth, 0f),
                });
            mesh.Solid(girder, (_, side) => side == 3 ? InkMesh.Asphalt : InkMesh.Slate, null, InkMesh.Slate, 0.25f, true);

            // Railings like the bridge's: a white rail on posts every 6 m along each edge.
            foreach (float across in new[] { -8.6f, 8.6f })
            {
                var rail = new List<Vector3[]>();
                for (int i = 0; i < line.Count; i++)
                    rail.Add(new[]
                    {
                        At(i, across + 0.175f, 0.85f), At(i, across + 0.175f, 1.35f), At(i, across - 0.175f, 1.35f),
                        At(i, across - 0.175f, 0.85f),
                    });
                mesh.Solid(rail, (_, _) => InkMesh.White, InkMesh.White, InkMesh.White, 0.06f, false);
            }
            for (int i = 0; i < line.Count; i++)
                if (i % 2 == 1)
                    foreach (float across in new[] { -8.6f, 8.6f })
                    {
                        Vector3 foot = At(i, across, 0f);
                        mesh.Box(foot, foot.y + 0.9f, Vector3.Cross(rights[i], Vector3.up), 0.24f, 0.24f, InkMesh.White, 0f, false);
                    }

            // Dashed middle line, 6 m on and 6 off (the bridge's are 5 and 5), laid along the curve.
            for (int i = 0; i + 2 < line.Count; i += 4)
            {
                const float lift = 0.1f, half = 0.25f;
                mesh.Decal(At(i, -half, lift), At(i, half, lift), At(i + 1, half, lift), At(i + 1, -half, lift), InkMesh.White);
                mesh.Decal(At(i + 1, -half, lift), At(i + 1, half, lift), At(i + 2, half, lift), At(i + 2, -half, lift), InkMesh.White);
            }

            // Piers wherever there's room under the girder, but never on a crossing.
            for (float s = RampPierSpacing * 0.66f; s < length; s += RampPierSpacing)
            {
                int i = System.Array.FindIndex(arc, a => a >= s);
                float underside = line[i].y - RampDepth;
                if (underside - LandHeight < 2f)
                    break;
                float nearest = x0 + Mathf.Round((line[i].x - x0) / CityPitch) * CityPitch;
                if (Mathf.Abs(line[i].x - nearest) < 15f + 3f && Mathf.Abs(line[i].z - streetZ) < 15f + 3f)
                    continue;
                Vector3 forward = Vector3.Cross(rights[i], Vector3.up);
                mesh.Box(new Vector3(line[i].x, LandHeight - 1f, line[i].z), underside + 0.3f, forward, 2.4f, 8f, InkMesh.Concrete, 0.15f, true);
            }

            var paint = mesh.ToMesh($"{bridge.name} {name}");
            var collider = mesh.ToCollider($"{bridge.name} {name} Col");
            meshes.Add(paint);
            meshes.Add(collider);
            var materials = new ClassModels.PlaneMaterials();
            var go = new GameObject(name);
            go.transform.SetParent(bridge, true);
            go.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // the mesh is in world space
            go.AddComponent<MeshFilter>().sharedMesh = paint;
            go.AddComponent<MeshRenderer>().sharedMaterials = new[] { materials.Paint, materials.Ink };
            go.AddComponent<MeshCollider>().sharedMesh = collider;
            go.isStatic = true;

            var footprint = new Vector2[line.Count];
            for (int i = 0; i < line.Count; i++)
                footprint[i] = new Vector2(line[i].x, line[i].z);
            Debug.Log($"[Winging It] {bridge.name} {name}: {length:0} m down to the street at x {touchdown:0}, {grade * 100f:0.0}% grade");
            return new Footprint(footprint, RampHalfWidth + 0.5f);
        }

        /// <summary>Generated meshes into one asset (replaced every build), so the scene references them instead of embedding them.</summary>
        static void SaveMeshes(string path, List<Mesh> meshes)
        {
            AssetDatabase.DeleteAsset(path);
            if (meshes.Count == 0)
                return;
            AssetDatabase.CreateAsset(meshes[0], path);
            for (int i = 1; i < meshes.Count; i++)
                AssetDatabase.AddObjectToAsset(meshes[i], path);
        }

        /// <summary>Quay pieces' depth below street level and promenade width (ArtSource/City/build_city.py).</summary>
        const float QuayDepth = 16f, QuayPromenade = 12f;

        /// <summary>
        /// Quay walls down both banks of the canal and round its basin (the city pack's Quay pieces, their face on the
        /// heightmap's cut <see cref="CanalHalfWidth"/> from the middle), as far as there's land behind them, and boats on
        /// the water. Without the pack the bare cut stands in.
        /// </summary>
        static void BuildCanal(Transform parent, CityModels city)
        {
            if (city == null)
                return;
            var group = new GameObject("Canal").transform;
            group.SetParent(parent, false);

            void Quay(string kind, float length, Vector3 position, Vector3 awayFromWater)
            {
                // Local +x looks away from the water, local +z runs along the bank.
                var rotation = Quaternion.LookRotation(Vector3.Cross(awayFromWater, Vector3.up));
                var go = city.Place(kind, group, position, rotation, Vector3.one, null);
                var box = go.AddComponent<BoxCollider>();
                box.center = new Vector3(QuayPromenade * 0.5f, -QuayDepth * 0.5f, 0f);
                box.size = new Vector3(QuayPromenade, QuayDepth, length);
            }

            // Down each bank, end to end along the line CanalHalfWidth from the middle, until the land runs out.
            const float piece = 20f;
            foreach (float side in new[] { -1f, 1f })
                for (float z = CanalStartZ; ;)
                {
                    float dz = piece / Mathf.Sqrt(1f + Sq(CanalSlope(z)));
                    float zm = z + dz * 0.5f;
                    if (Height(CanalX(zm) + side * (CanalHalfWidth + 25f), zm) < 2f)
                        break; // the coast
                    var across = new Vector3(1f, 0f, -CanalSlope(zm)).normalized * side;
                    Quay("Quay", piece, new Vector3(CanalX(zm) + side * CanalHalfWidth, LandHeight, zm), across);
                    z += dz;
                }

            // Round the basin at the south end: short pieces on the half circle.
            const float shortPiece = 7f;
            var basin = new Vector3(CanalX(CanalStartZ), LandHeight, CanalStartZ);
            int count = Mathf.CeilToInt(Mathf.PI * CanalHalfWidth / shortPiece);
            for (int k = 0; k < count; k++)
            {
                float a = Mathf.PI + (k + 0.5f) * Mathf.PI / count;
                var out_ = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Quay("QuayShort", shortPiece, basin + out_ * CanalHalfWidth, out_);
            }

            // Boats: tugs and water buses along the middle, heading up or down the canal.
            var rng = new System.Random(4545);
            int n = 0;
            foreach (float z in new[] { 130f, 320f, 430f, 680f, 860f })
            {
                float x = CanalX(z) + (n % 2 == 0 ? -18f : 18f);
                if (Height(x, z) > -5f)
                    continue;
                float yaw = Mathf.Atan(CanalSlope(z)) * Mathf.Rad2Deg + (n % 2 == 0 ? 180f : 0f);
                city.PlaceVehicle(rng.NextDouble() < 0.5 ? "Tugboat" : "Ferry", group, new Vector3(x, 0f, z), yaw);
                n++;
            }
        }

        // ------------------------------------------------------------------ boundary

        /// <summary>
        /// A ring of marker buoys on the sea at the boundary radius, ~150 m apart: the markers pack's buoy (orange float,
        /// cage tower, X topmark) or greybox cylinders, each with a red light that blinks in unison
        /// (<see cref="BoundaryBuoys"/>). No colliders: they mark the edge, they aren't in the way.
        /// </summary>
        static void BuildBoundaryBuoys(Transform parent, FlightBoundary boundary, MarkerModels markers)
        {
            var root = new GameObject("Boundary Buoys").transform;
            root.SetParent(parent, false);
            Material floatMat = null, mastMat = null, lightMat = null;
            if (markers == null)
            {
                floatMat = B.Materials.Mat("Greybox_BuoyFloat", new Color(1f, 0.45f, 0.08f));
                mastMat = B.Materials.Mat("Greybox_BuoyMast", new Color(0.92f, 0.92f, 0.9f));
                lightMat = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));
            }

            int count = Mathf.CeilToInt(2f * Mathf.PI * boundary.radius / BuoySpacing);
            var lights = new Renderer[count];
            Vector3 c = boundary.transform.position;
            for (int i = 0; i < count; i++)
            {
                float a = i * 360f / count;
                var buoy = new GameObject($"Buoy {i:000}").transform;
                buoy.SetParent(root, false);
                buoy.position = new Vector3(c.x, 0f, c.z) + Quaternion.Euler(0f, a, 0f) * Vector3.forward * boundary.radius;
                if (markers != null)
                {
                    lights[i] = markers.Buoy(buoy);
                    continue;
                }
                Cylinder("Float", buoy, new Vector3(0f, 3f, 0f), 7f, 8f, floatMat);
                Cylinder("Mast", buoy, new Vector3(0f, 16f, 0f), 1.4f, 20f, mastMat);
                lights[i] = Sphere("Light", buoy, new Vector3(0f, 28f, 0f), 5f, lightMat, false);
            }
            root.gameObject.AddComponent<BoundaryBuoys>().Configure(lights);
            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        static Renderer Cylinder(string name, Transform parent, Vector3 localPos, float diameter, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter); // the primitive is 2 units tall
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ------------------------------------------------------------------ landmarks

        /// <summary>
        /// One big landmark per region, each with a blinking beacon on top (<see cref="Landmark"/>): the landmarks pack's
        /// models when it's been generated, else greybox.
        /// </summary>
        static void BuildLandmarks(Transform parent, Terrain terrain, B.Materials m, NatureModels nature, LandmarkModels models)
        {
            var group = new GameObject("Landmarks").transform;
            group.SetParent(parent, false);
            var beacon = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));
            BuildSpire(group, m, beacon, models);
            BuildSummitRadar(group, terrain, m, beacon, models);
            BuildPyramid(group, terrain, beacon, models);
            BuildLighthouse(group, m, nature, models);
            foreach (var t in group.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = t.GetComponent<Spinner>() == null; // the radar antenna and lighthouse lens turn
        }

        /// <summary>A landmark's root, on the ground at <paramref name="ground"/>.</summary>
        static Transform NewLandmark(Transform parent, string name, Vector3 ground)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = ground;
            return root;
        }

        /// <summary>Makes <paramref name="root"/> a <see cref="Landmark"/>: a top marker at <paramref name="top"/> and the beacon it blinks.</summary>
        static void Light(Transform root, MapRegion region, Vector3 top, Renderer beacon)
        {
            var marker = new GameObject("Top").transform;
            marker.SetParent(root, false);
            marker.localPosition = top;
            root.gameObject.AddComponent<Landmark>().Configure(root.name, region, marker, beacon);
        }

        /// <summary>A collider-less beacon <paramref name="size"/> across at <paramref name="localPos"/>: the pack's gem, or a greybox sphere.</summary>
        static Renderer Beacon(Transform root, Vector3 localPos, float size, Material greybox, LandmarkModels models) =>
            models != null ? models.Beacon(root, localPos, size) : Sphere("Beacon", root, localPos, size, greybox, false);

        static Renderer Sphere(string name, Transform parent, Vector3 localPos, float diameter, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * diameter;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        /// <summary>City: a ~540 m stepped needle tower, nearly twice the height of downtown, on its own plaza.</summary>
        static void BuildSpire(Transform parent, B.Materials m, Material beacon, LandmarkModels models)
        {
            var root = NewLandmark(parent, "The Spire", new Vector3(SpirePosition.x, LandHeight, SpirePosition.y));
            var top = new Vector3(0f, 545f, 0f);
            if (models != null)
            {
                models.Body("Spire", root);
                top = models.Top("Spire");
            }
            else
            {
                var glass = B.Materials.Mat("Greybox_SpireGlass", new Color(0.42f, 0.56f, 0.7f), smoothness: 0.8f);
                B.Box("Podium", root, new Vector3(0f, 6f, 0f), new Vector3(70f, 12f, 70f), m.ConcreteDark, true);
                float y = 12f;
                foreach (var (width, height) in new[] { (44f, 220f), (34f, 120f), (24f, 80f), (14f, 40f) })
                {
                    B.Box("Shaft", root, new Vector3(0f, y + height * 0.5f, 0f), new Vector3(width, height, width), glass, true);
                    y += height;
                    B.Box("Band", root, new Vector3(0f, y - 2f, 0f), new Vector3(width + 4f, 4f, width + 4f), m.Concrete, true);
                }
                // Observation deck above the first stage: the widest part, so the silhouette reads as a tower, not a box.
                B.Box("Observation Deck", root, new Vector3(0f, 236f, 0f), new Vector3(62f, 8f, 62f), m.Concrete, true);
                B.Box("Needle", root, new Vector3(0f, y + 35f, 0f), new Vector3(2.5f, 70f, 2.5f), m.Concrete, true);
            }
            Light(root, MapRegion.City, top, Beacon(root, top, 9f, beacon, models));
        }

        /// <summary>Mountains: a radar station (mast + dome) on the highest peak, sitting on a plinth sunk into the rock.</summary>
        static void BuildSummitRadar(Transform parent, Terrain terrain, B.Materials m, Material beacon, LandmarkModels models)
        {
            Vector3 peak = Vector3.zero;
            float best = float.MinValue;
            for (float z = MountainCenter.y - MountainRadii.y; z <= MountainCenter.y + MountainRadii.y; z += 10f)
            for (float x = MountainCenter.x - MountainRadii.x; x <= MountainCenter.x + MountainRadii.x; x += 10f)
            {
                float h = terrain.SampleHeight(new Vector3(x, 0f, z)) + terrain.transform.position.y;
                if (h > best)
                {
                    best = h;
                    peak = new Vector3(x, h, z);
                }
            }

            var root = NewLandmark(parent, "Summit Radar", peak);
            var top = new Vector3(6f, 156f, -6f);
            if (models != null)
            {
                models.Body("SummitRadar", root);
                models.RadarAntenna(root);
                top = models.Top("SummitRadar");
            }
            else
            {
                var white = B.Materials.Mat("Greybox_RadarWhite", new Color(0.93f, 0.93f, 0.95f));
                B.Box("Plinth", root, new Vector3(0f, -12f, 0f), new Vector3(44f, 30f, 44f), m.ConcreteDark, true);
                Sphere("Radome", root, new Vector3(-10f, 14f, 6f), 24f, white, true);
                B.Box("Mast", root, new Vector3(6f, 78f, -6f), new Vector3(5f, 150f, 5f), m.PlaneDark, true);
                for (float h = 40f; h <= 140f; h += 50f)
                    B.Box("Mast Arm", root, new Vector3(6f, h, -6f), new Vector3(18f, 1.5f, 1.5f), m.PlaneDark, true);
            }
            Light(root, MapRegion.Mountains, top, Beacon(root, top, 7f, beacon, models));
        }

        /// <summary>Desert: a stepped pyramid, 280 m across and ~210 m tall, the biggest thing on the open sand.</summary>
        static void BuildPyramid(Transform parent, Terrain terrain, Material beacon, LandmarkModels models)
        {
            // Stand it on the lowest ground under its footprint and sink the bottom step in, so no edge floats.
            float ground = float.MaxValue;
            for (float dz = -PyramidBase * 0.5f; dz <= PyramidBase * 0.5f; dz += 20f)
            for (float dx = -PyramidBase * 0.5f; dx <= PyramidBase * 0.5f; dx += 20f)
                ground = Mathf.Min(ground, terrain.SampleHeight(new Vector3(PyramidPosition.x + dx, 0f, PyramidPosition.y + dz)) + terrain.transform.position.y);

            float height = PyramidSteps * PyramidStepHeight;
            var root = NewLandmark(parent, "The Pyramid", new Vector3(PyramidPosition.x, ground, PyramidPosition.y));
            var top = new Vector3(0f, height + 6f, 0f);
            if (models != null)
            {
                models.Body("Pyramid", root);
                top = models.Top("Pyramid");
            }
            else
            {
                var stone = B.Materials.Mat("Greybox_Sandstone", new Color(0.8f, 0.66f, 0.45f), B.Materials.CheckerTexture(), new Vector2(4f, 2f));
                const float sink = 12f;
                for (int i = 0; i < PyramidSteps; i++)
                {
                    float width = PyramidBase * (1f - i / (float)PyramidSteps);
                    float bottom = i == 0 ? -sink : i * PyramidStepHeight;
                    float stepTop = (i + 1) * PyramidStepHeight;
                    B.Box($"Step {i + 1}", root, new Vector3(0f, (bottom + stepTop) * 0.5f, 0f), new Vector3(width, stepTop - bottom, width), stone, true);
                }
            }
            Light(root, MapRegion.Desert, top, Beacon(root, top, 8f, beacon, models));
        }

        /// <summary>
        /// Ocean: a striped lighthouse on a rocky islet off the south coast. Its lamp is the beacon (the pack's lens
        /// turns round it). The islet is the nature pack's cliff mesh (with a matching collider), its grassy top level
        /// with the lighthouse's foot.
        /// </summary>
        static void BuildLighthouse(Transform parent, B.Materials m, NatureModels nature, LandmarkModels models)
        {
            var root = NewLandmark(parent, "Lighthouse Rock", new Vector3(IsletPosition.x, IsletHeight, IsletPosition.y));
            if (models != null)
            {
                models.Body("Lighthouse", root);
                models.LighthouseLens(root);
                var lamp = models.LighthouseLamp(root);
                Light(root, MapRegion.Ocean, lamp.transform.localPosition, lamp);
            }
            else
            {
                var red = B.Materials.Mat("Greybox_LighthouseRed", new Color(0.78f, 0.16f, 0.14f));
                B.Box("Base", root, new Vector3(0f, -2f, 0f), new Vector3(20f, 8f, 20f), m.ConcreteDark, true);
                float y = 2f;
                for (int i = 0; i < 4; i++)
                {
                    float width = 13f - i * 1.6f;
                    B.Box($"Tower {i + 1}", root, new Vector3(0f, y + 7.5f, 0f), new Vector3(width, 15f, width), i % 2 == 0 ? m.Concrete : red, true);
                    y += 15f;
                }
                B.Box("Gallery", root, new Vector3(0f, y + 1f, 0f), new Vector3(12f, 2f, 12f), m.PlaneDark, true);
                B.Box("Lamp Room", root, new Vector3(0f, y + 6f, 0f), new Vector3(6f, 8f, 6f), m.Glass, true);
                B.Box("Roof", root, new Vector3(0f, y + 11f, 0f), new Vector3(7f, 2f, 7f), red, true);
                B.Box("Keeper's House", root, new Vector3(16f, 3f, 6f), new Vector3(12f, 8f, 9f), m.Concrete, true);
                var lamp = B.Materials.UnlitMat("Greybox_LighthouseLamp", new Color(1f, 0.92f, 0.55f));
                var top = new Vector3(0f, y + 6f, 0f);
                Light(root, MapRegion.Ocean, top, Sphere("Beacon", root, top, 6f, lamp, false));
            }
            nature?.Place("Islet", root, new Vector3(IsletPosition.x, 0f, IsletPosition.y), collider: true);
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

        /// <summary>
        /// Three lanes into each home field (<see cref="WestLanes"/>, mirrored for the east), each a marked route of
        /// gate pylons (colored per lane, no collision) ending on the field's final approach fix. The pylons are the
        /// markers pack's (the marker on a lane-coloured glow material) or greybox poles with a sphere.
        /// </summary>
        static void BuildApproachLanes(Transform parent, Airfield west, Airfield east, Airfield central, Airfield southWest, Airfield southEast,
            MarkerModels markers)
        {
            Physics.SyncTransforms(); // the colliders built so far, so the clearance probe sees them
            var group = new GameObject("Approach Lanes").transform;
            group.SetParent(parent, false);
            var pole = markers == null ? B.Materials.Mat("Greybox_LanePole", new Color(0.85f, 0.85f, 0.82f)) : null;
            Material Gate(string name, Color color) =>
                markers != null ? MarkerModels.LaneMaterial(name, color) : B.Materials.UnlitMat($"Greybox_{name}", color);
            for (int i = 0; i < WestLanes.Length; i++)
            {
                var (name, eastName, color, points, eastPoints) = WestLanes[i];
                var mat = Gate($"Lane{i + 1}", color);
                BuildLane(group, west, name, points, mat, pole, markers);
                BuildLane(group, east, eastName, eastPoints ?? MirrorLane(points), mat, pole, markers);
            }
            var centralMat = Gate("LaneCentral", new Color(1f, 0.8f, 0.2f));
            BuildLane(group, central, "West Final", CentralLane, centralMat, pole, markers, reverseEnd: false);
            BuildLane(group, central, "East Final", MirrorLaneX(CentralLane), centralMat, pole, markers, reverseEnd: true);
            var southMat = Gate("LaneSouth", new Color(1f, 0.55f, 0.2f));
            foreach (var (name, points) in SouthFieldLanes)
            {
                BuildLane(group, southWest, name, points, southMat, pole, markers);
                BuildLane(group, southEast, name, MirrorLaneX(points), southMat, pole, markers);
            }
            foreach (var t in group.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        /// <summary>A lane point mirrored across x = 0 (Central's west lane for its east end).</summary>
        static (Vector2 xz, float agl)[] MirrorLaneX((Vector2 xz, float agl)[] points)
        {
            var mirrored = new (Vector2, float)[points.Length];
            for (int i = 0; i < points.Length; i++)
                mirrored[i] = (new Vector2(-points[i].xz.x, points[i].xz.y), points[i].agl);
            return mirrored;
        }

        static void BuildLane(Transform parent, Airfield field, string name, (Vector2 xz, float agl)[] points, Material gateMat, Material poleMat,
            MarkerModels markers, bool reverseEnd = false)
        {
            var root = new GameObject($"{field.DisplayName} - {name}").transform;
            root.SetParent(parent, false);
            var path = new Vector3[points.Length + 1];
            for (int i = 0; i < points.Length; i++)
            {
                var (xz, agl) = points[i];
                path[i] = new Vector3(xz.x, HighestPointNear(xz, LaneClearanceRadius) + agl, xz.y);
            }
            path[^1] = field.GlidePathPoint(FinalFixDistance, reverseEnd);
            root.position = path[0];
            root.gameObject.AddComponent<ApproachLane>().Configure(name, field, path, LaneHalfWidth);

            // A gate at every waypoint but the fix (the glide-path rings take over there): two pylons across the lane.
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 inDir = i > 0 ? path[i] - path[i - 1] : path[i + 1] - path[i];
                Vector3 dir = inDir.normalized + (path[i + 1] - path[i]).normalized;
                dir.y = 0f;
                Vector3 across = Vector3.Cross(Vector3.up, dir.normalized);
                foreach (float side in new[] { -1f, 1f })
                {
                    Vector3 top = path[i] + across * (side * LaneGateHalfWidth);
                    float ground = Mathf.Max(0f, HighestPointNear(new Vector2(top.x, top.z), 0f));
                    float height = Mathf.Max(1f, top.y - ground);
                    var gate = new GameObject(i == 0 ? "Entry Gate" : $"Gate {i}").transform;
                    gate.SetParent(root, false);
                    gate.position = new Vector3(top.x, ground, top.z);
                    if (markers != null)
                    {
                        markers.Pylon(gate, height, gateMat);
                        continue;
                    }
                    B.Box("Pole", gate, new Vector3(0f, height * 0.5f, 0f), new Vector3(1.5f, height, 1.5f), poleMat, false);
                    Sphere("Marker", gate, new Vector3(0f, height, 0f), 8f, gateMat, false);
                }
            }
        }

        // ------------------------------------------------------------------ clouds

        /// <summary>
        /// Puffy cloud banks (<see cref="CloudBank"/>): an ellipsoid core with puffs around and on top of it, rendered as
        /// collider-less white spheres that match the cover volume. Each bank and its mirror share one altitude (the higher
        /// of the two needed to clear the ground), so both teams' clouds are the same.
        /// </summary>
        static void BuildClouds(Transform parent, NatureModels nature)
        {
            var group = new GameObject("Clouds").transform;
            group.SetParent(parent, false);
            var mat = B.Materials.Mat("Greybox_Cloud", new Color(0.97f, 0.98f, 1f), smoothness: 0f);
            int n = 0;
            foreach (var (xz, radius, altitude, seed) in CloudBanks)
            {
                var puffs = CloudPuffs(radius, seed);
                float underside = 0f;
                foreach (var p in puffs)
                    underside = Mathf.Max(underside, p.radii.y - p.center.y);
                float y = altitude;
                foreach (var at in new[] { xz, -xz })
                    y = Mathf.Max(y, HighestPointNear(at, radius) + CloudClearance + underside);

                BuildCloudBank(group, $"Cloud {++n}", new Vector3(xz.x, y, xz.y), puffs, mat, nature);
                var mirrored = new CloudPuff[puffs.Length];
                for (int i = 0; i < puffs.Length; i++)
                    mirrored[i] = new CloudPuff(new Vector3(-puffs[i].center.x, puffs[i].center.y, -puffs[i].center.z), puffs[i].radii);
                BuildCloudBank(group, $"Cloud {++n}", new Vector3(-xz.x, y, -xz.y), mirrored, mat, nature);
            }
        }

        /// <summary>One bank's puffs, relative to its center: a big flat core, a ring of side puffs, and two on top.</summary>
        static CloudPuff[] CloudPuffs(float radius, int seed)
        {
            var rng = new System.Random(seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float h = CloudCoreHalfHeight;
            var puffs = new List<CloudPuff> { new(Vector3.zero, new Vector3(radius * 0.6f, h, radius * 0.6f)) };
            int ring = 6 + rng.Next(3);
            float start = Range(0f, Mathf.PI * 2f);
            for (int i = 0; i < ring; i++)
            {
                float a = start + i * Mathf.PI * 2f / ring + Range(-0.25f, 0.25f);
                float d = radius * Range(0.45f, 0.65f);
                float r = radius * Range(0.3f, 0.42f);
                puffs.Add(new CloudPuff(new Vector3(Mathf.Cos(a) * d, h * Range(-0.25f, 0.15f), Mathf.Sin(a) * d),
                    new Vector3(r, h * Range(0.6f, 0.85f), r * Range(0.85f, 1.15f))));
            }
            for (int i = 0; i < 2; i++)
            {
                float a = Range(0f, Mathf.PI * 2f);
                float d = radius * Range(0.1f, 0.3f);
                float r = radius * Range(0.3f, 0.4f);
                puffs.Add(new CloudPuff(new Vector3(Mathf.Cos(a) * d, h * Range(0.45f, 0.65f), Mathf.Sin(a) * d),
                    new Vector3(r, h * Range(0.55f, 0.7f), r)));
            }
            return puffs.ToArray();
        }

        /// <summary>
        /// One bank: a faceted puff mesh per cover puff (the nature pack's unit-radius CloudPuffA-C, scaled to its radii),
        /// or a greybox sphere when the pack hasn't been generated.
        /// </summary>
        static void BuildCloudBank(Transform parent, string name, Vector3 position, CloudPuff[] puffs, Material mat, NatureModels nature)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = position;
            for (int i = 0; i < puffs.Length; i++)
            {
                Renderer r;
                if (nature != null)
                {
                    var go = nature.Place($"CloudPuff{"ABC"[i % 3]}", root, position + puffs[i].center, collider: false, castShadows: false);
                    go.name = $"Puff {i + 1}";
                    go.transform.localScale = puffs[i].radii;
                    r = go.GetComponent<Renderer>();
                    r.sharedMaterial = nature.CloudMaterial; // unlit: the baked tones are the shading, so no grey undersides
                }
                else
                {
                    r = Sphere($"Puff {i + 1}", root, puffs[i].center, 1f, mat, false);
                    r.transform.localScale = puffs[i].radii * 2f;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }
                r.receiveShadows = false;
            }
            root.gameObject.AddComponent<CloudBank>().Configure(puffs);
            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        // ------------------------------------------------------------------ mode arenas

        /// <summary>How far past a home runway's far end (m) the banner pole stands, clear of the field's AA corners.</summary>
        const float BannerStandDistance = 150f;
        const float BannerPoleHeight = 45f;

        /// <summary>
        /// Capture the Banner's stands (match-modes.md): one pole past the far end of each home runway, in line with it.
        /// The grab zone (<see cref="BannerStand"/>) reaches 40 m up, so the pole stands taller; the banner flies from its top.
        /// </summary>
        static void BuildBannerStands(Transform parent, Terrain terrain, params Airfield[] homes)
        {
            var group = new GameObject("Banner Stands").transform;
            group.SetParent(parent, false);
            var pole = B.Materials.Mat("Greybox_BannerPole", new Color(0.82f, 0.82f, 0.85f));
            foreach (var home in homes)
            {
                Color color = home.Team == 0 ? new Color(0.35f, 0.6f, 1f) : new Color(1f, 0.38f, 0.32f);
                var cloth = B.Materials.UnlitMat($"Greybox_Banner_Team{home.Team}", color);

                Vector3 xz = home.transform.position + home.RunwayDirection * (home.RunwaySize.y * 0.5f + BannerStandDistance);
                float ground = terrain.SampleHeight(new Vector3(xz.x, 0f, xz.z)) + terrain.transform.position.y;

                var go = new GameObject($"{(home.Team == 0 ? "West" : "East")} Banner Stand");
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(new Vector3(xz.x, ground, xz.z), home.transform.rotation);
                var mount = new GameObject("Flag Mount").transform;
                mount.SetParent(go.transform, false);
                mount.localPosition = new Vector3(0f, BannerPoleHeight, 0f);
                B.Box("Pole", go.transform, new Vector3(0f, BannerPoleHeight * 0.5f, 0f), new Vector3(1.2f, BannerPoleHeight, 1.2f), pole, true);
                B.Box("Base", go.transform, new Vector3(0f, 1f, 0f), new Vector3(6f, 2f, 6f), pole, true);
                var flag = B.Box("Banner", go.transform, new Vector3(0f, BannerPoleHeight - 6f, 5f), new Vector3(0.3f, 10f, 10f), cloth, false);
                go.AddComponent<BannerStand>().Configure(home.Team, 20f, 40f, mount, flag.transform);
            }
        }

        /// <summary>Ram Royale's arena (match-modes.md): a small circle over the sea round Lighthouse Rock.</summary>
        const float RamArenaRadius = 700f, RamArenaCeiling = 450f, RamArenaWarning = 150f;
        const float RamStartRadius = 550f, RamStartAltitude = 220f;
        const float RamPatrolRadius = 350f, RamPatrolAltitude = 200f;

        /// <summary>
        /// The arenas modes play in instead of the whole Island (<see cref="ModeArena"/>). Teams, Capture the Banner and
        /// Airfield Domination use the Island as built, so only Ram Royale has one: ~700 m round the lighthouse, 450 m
        /// ceiling, a hard edge, every airfield closed, everyone starting on a ring in the air facing in, and the bots
        /// patrolling the lighthouse and four points round it.
        /// </summary>
        static void BuildModeArenas(Transform parent)
        {
            var group = new GameObject("Mode Arenas").transform;
            group.SetParent(parent, false);

            var ram = new GameObject("Ram Royale Arena").AddComponent<ModeArena>();
            ram.transform.SetParent(group, false);
            ram.transform.position = new Vector3(IsletPosition.x, 0f, IsletPosition.y);
            ram.Configure("RamRoyale", RamArenaRadius, RamArenaWarning, RamArenaCeiling, true);
            ram.ConfigureAirfields(true);
            ram.ConfigureStart(true, RamStartRadius, RamStartAltitude, 16);
            var areas = new List<Vector3> { new(IsletPosition.x, RamPatrolAltitude, IsletPosition.y) };
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * RamPatrolRadius;
                areas.Add(new Vector3(IsletPosition.x + offset.x, RamPatrolAltitude, IsletPosition.y + offset.z));
            }
            ram.ConfigureFightAreas(areas.ToArray());
        }

        /// <summary>Fills both teams to 8v8 with bots that patrol the <see cref="FightAreas"/>.</summary>
        static BotRoster BuildBotRoster(GameObject planePrefab, FlightStats[] classes)
        {
            var areas = new List<Vector3>();
            foreach (var (_, xz, agl) in FightAreas)
                areas.Add(new Vector3(xz.x, HighestPointNear(xz, FightAreaClearanceRadius) + agl, xz.y));
            var roster = new GameObject("Bot Roster").AddComponent<BotRoster>();
            roster.Configure(planePrefab, classes, areas);
            return roster;
        }

        /// <summary>
        /// The Teams match flow (menu → class select → 5:00 → scoreboard), its screens and the menu camera. The player's
        /// controls, dev tools, flight HUD and touch controls only run in play (controls and touch controls also stop
        /// while the in-match menu is open); the free class switcher only runs once dismissed.
        /// </summary>
        static void BuildMatch(BotRoster roster, FlightStats[] classes)
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

        /// <summary>The highest solid surface (ground, sea, building) at <paramref name="xz"/> or within <paramref name="radius"/> of it.</summary>
        static float HighestPointNear(Vector2 xz, float radius)
        {
            float best = 0f;
            for (int ring = 0; ring <= 2; ring++)
            {
                float r = radius * ring / 2f;
                int samples = ring == 0 ? 1 : 12;
                for (int k = 0; k < samples; k++)
                {
                    float a = k * Mathf.PI * 2f / samples;
                    var from = new Vector3(xz.x + Mathf.Cos(a) * r, 2000f, xz.y + Mathf.Sin(a) * r);
                    if (Physics.Raycast(from, Vector3.down, out var hit, 3000f, ~0, QueryTriggerInteraction.Ignore))
                        best = Mathf.Max(best, hit.point.y);
                }
            }
            return best;
        }
    }
}
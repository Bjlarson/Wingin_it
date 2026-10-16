using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WingingIt.Flight;
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
        // The canal runs from the middle of the city north to the sea.
        const float CanalStartZ = 50f;
        static readonly float[] BridgeZ = { 200f, 520f };

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
        };
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
        /// Central has none until capture exists.
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

            var world = new GameObject("World").transform;
            var terrain = BuildTerrain(world);
            BuildOcean(world);
            BuildCity(world, m);
            BuildBridges(world, m);
            BuildLandmarks(world, terrain, m);

            var boundary = new GameObject("Flight Boundary").AddComponent<FlightBoundary>();
            boundary.transform.SetParent(world, false);
            boundary.radius = BoundaryRadius;
            boundary.ceiling = Ceiling;
            BuildBoundaryBuoys(world, boundary);

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
                so.ApplyModifiedPropertiesWithoutUndo();
                fields[name] = field;
                if (home && team == 0)
                    playerSpawn = field.AirSpawn;
            }
            BuildApproachLanes(world, fields["West Home Airfield"], fields["East Home Airfield"]);
            BuildClouds(world);

            B.BuildPlayer(scene, planePrefab, classes, playerSpawn);

            EditorSceneManager.SaveScene(scene, ScenePath);
            B.AddSceneToBuildSettings(ScenePath, first: false);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {ScenePath}");
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

        static float CityMask(float x, float z) => Smooth(CityRadius + 50f, CityRadius - 100f, Vector2.Distance(new Vector2(x, z), CityCenter));

        static float DesertMask(float x, float z) => Smooth(DesertStartX, DesertFullX, x + (Fbm(x, z, 600f, 2, 9.4f) - 0.5f) * 300f);

        static float MountainMask(float x, float z)
        {
            float d = Mathf.Sqrt(Sq((x - MountainCenter.x) / MountainRadii.x) + Sq((z - MountainCenter.y) / MountainRadii.y));
            d += (Fbm(x, z, 500f, 3, 5.7f) - 0.5f) * 0.35f;
            return Smooth(1f, 0.55f, d);
        }

        static float CanalX(float z) => 70f * Mathf.Sin((z - CanalStartZ) / 260f);

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

            // Desert: wind-blown dune ridges, plus a few flat-topped mesas.
            float warp = Fbm(x, z, 400f, 2, 4.2f) * 6f;
            float dune = Mathf.Sin((x * 0.6f + z) / 70f + warp) * 0.5f + 0.5f;
            h += desert * dune * dune * 14f;
            foreach (var (center, radius, height) in Mesas)
            {
                float d = Vector2.Distance(new Vector2(x, z), center) + (Fbm(x, z, 60f, 2, 2.2f) - 0.5f) * 40f;
                h = Mathf.Max(h, LandHeight + height * Smooth(radius + 35f, radius - 5f, d));
            }

            // Mountains: ridged peaks up to ~650 m, with a canyon cut through toward the city.
            float mountains = MountainMask(x, z) * (60f + 600f * Mathf.Pow(Ridged(x, z, 700f, 5, 8.8f), 1.4f));
            float canyon = Smooth(160f, 55f, DistanceToSegment(new Vector2(x, z), CanyonStart, CanyonEnd));
            h += mountains * (1f - 0.97f * canyon);

            // City: flat ground, then the canal (below sea level, so the ocean fills it).
            h = Mathf.Lerp(h, LandHeight, CityMask(x, z));
            h = Mathf.Lerp(h, -10f, Smooth(75f, 40f, CanalDistance(x, z)));

            // Coast: shelve down to the sea floor.
            h = Mathf.Lerp(SeaFloor, h, land);

            // The lighthouse islet: a rocky lump with a flat top in the middle for the lighthouse.
            float islet = Vector2.Distance(new Vector2(x, z), IsletPosition);
            float rocks = (Fbm(x, z, 40f, 2, 6.6f) - 0.5f) * 14f * Smooth(30f, 80f, islet);
            h = Mathf.Max(h, Mathf.Lerp(SeaFloor, IsletHeight + rocks, Smooth(IsletRadius + 70f, IsletRadius - 30f, islet)));

            // Airfield pads last: dead flat at LandHeight.
            return Mathf.Lerp(h, LandHeight, PadMask(x, z));
        }

        // ------------------------------------------------------------------ terrain

        static Terrain BuildTerrain(Transform parent)
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
                Layer("Grass", new Color(0.47f, 0.64f, 0.36f), 40f),
                Layer("Sand", new Color(0.88f, 0.78f, 0.54f), 30f),
                Layer("Rock", new Color(0.52f, 0.47f, 0.42f), 25f),
                Layer("Snow", new Color(0.95f, 0.96f, 0.98f), 30f),
                Layer("Paved", new Color(0.55f, 0.55f, 0.57f), 60f),
            };
            PaintLayers(data, origin);
            EditorUtility.SetDirty(data);

            var go = Terrain.CreateTerrainGameObject(data);
            go.name = "Island Terrain";
            go.transform.SetParent(parent, false);
            go.transform.position = origin;
            go.isStatic = true;
            var terrain = go.GetComponent<Terrain>();
            terrain.materialTemplate = B.Materials.TerrainMat("Greybox_Terrain");
            terrain.heightmapPixelError = 4f;
            terrain.basemapDistance = 2500f;
            terrain.drawInstanced = true;
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

        static TerrainLayer Layer(string name, Color color, float tileSize)
        {
            string path = $"{TerrainFolder}/{name}.terrainlayer";
            var layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer != null)
                return layer;
            layer = new TerrainLayer
            {
                diffuseTexture = B.Materials.TintedChecker($"Terrain_{name}", color, matteAlpha: true),
                tileSize = new Vector2(tileSize, tileSize),
            };
            AssetDatabase.CreateAsset(layer, path);
            return layer;
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

        /// <summary>
        /// A downtown grid: 120 m blocks with 30 m streets, towers tallest in the middle (~300 m) falling to ~40 m at
        /// the edge, some plazas left open, nothing on the canal banks or the central airfield's pad.
        /// </summary>
        static void BuildCity(Transform parent, B.Materials m)
        {
            var group = new GameObject("City").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(4242);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            const float pitch = 120f, block = 90f;
            int n = 0;
            for (float bz = CityCenter.y - CityRadius; bz <= CityCenter.y + CityRadius; bz += pitch)
            for (float bx = CityCenter.x - CityRadius; bx <= CityCenter.x + CityRadius; bx += pitch)
            {
                // Blocks on a grid offset so the center falls on an intersection.
                float cx = bx + pitch * 0.5f, cz = bz + pitch * 0.5f;
                float r = Vector2.Distance(new Vector2(cx, cz), CityCenter);
                if (r > CityRadius - 40f || CanalDistance(cx, cz) < 125f || PadMask(cx, cz) > 0.01f || rng.NextDouble() < 0.1
                    || Vector2.Distance(new Vector2(cx, cz), SpirePosition) < 100f) // the Spire's plaza
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

                foreach (var (center, size) in lots)
                {
                    float h = Mathf.Max(20f, core * Range(0.45f, 1.05f) * (size < 42f ? 0.7f : 1f));
                    float w = size, d = Mathf.Min(block, size * Range(0.8f, 1.2f));
                    B.Box($"Building {n++:000}", group, new Vector3(center.x, LandHeight + h * 0.5f, center.y),
                        new Vector3(w, h, d), rng.NextDouble() < 0.5 ? m.Concrete : m.ConcreteDark, true).gameObject.isStatic = true;
                }
            }
        }

        /// <summary>Two bridges over the canal, 34 m above the water, with 150 m between their towers to fly under.</summary>
        static void BuildBridges(Transform parent, B.Materials m)
        {
            var group = new GameObject("Bridges").transform;
            group.SetParent(parent, false);
            for (int i = 0; i < BridgeZ.Length; i++)
            {
                float z = BridgeZ[i], x = CanalX(z);
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
        }

        // ------------------------------------------------------------------ boundary

        /// <summary>
        /// A ring of marker buoys on the sea at the boundary radius, ~150 m apart: orange floats with a mast and a red
        /// light that blinks in unison (<see cref="BoundaryBuoys"/>). No colliders: they mark the edge, they aren't in the way.
        /// </summary>
        static void BuildBoundaryBuoys(Transform parent, FlightBoundary boundary)
        {
            var root = new GameObject("Boundary Buoys").transform;
            root.SetParent(parent, false);
            var floatMat = B.Materials.Mat("Greybox_BuoyFloat", new Color(1f, 0.45f, 0.08f));
            var mastMat = B.Materials.Mat("Greybox_BuoyMast", new Color(0.92f, 0.92f, 0.9f));
            var lightMat = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));

            int count = Mathf.CeilToInt(2f * Mathf.PI * boundary.radius / BuoySpacing);
            var lights = new Renderer[count];
            Vector3 c = boundary.transform.position;
            for (int i = 0; i < count; i++)
            {
                float a = i * 360f / count;
                var buoy = new GameObject($"Buoy {i:000}").transform;
                buoy.SetParent(root, false);
                buoy.position = new Vector3(c.x, 0f, c.z) + Quaternion.Euler(0f, a, 0f) * Vector3.forward * boundary.radius;
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

        /// <summary>One big landmark per region, each with a blinking beacon on top (<see cref="Landmark"/>).</summary>
        static void BuildLandmarks(Transform parent, Terrain terrain, B.Materials m)
        {
            var group = new GameObject("Landmarks").transform;
            group.SetParent(parent, false);
            var beacon = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));
            BuildSpire(group, m, beacon);
            BuildSummitRadar(group, terrain, m, beacon);
            BuildPyramid(group, terrain, beacon);
            BuildLighthouse(group, m);
            foreach (var t in group.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        /// <summary>A landmark root at ground level, with a top marker and a collider-less beacon sphere there.</summary>
        static Transform NewLandmark(Transform parent, string name, MapRegion region, Vector3 ground, float topHeight, Material beaconMat, float beaconSize)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = ground;
            var top = new GameObject("Top").transform;
            top.SetParent(root, false);
            top.localPosition = new Vector3(0f, topHeight, 0f);
            var beacon = Sphere("Beacon", root, top.localPosition, beaconSize, beaconMat, false);
            root.gameObject.AddComponent<Landmark>().Configure(name, region, top, beacon);
            return root;
        }

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
        static void BuildSpire(Transform parent, B.Materials m, Material beacon)
        {
            var root = NewLandmark(parent, "The Spire", MapRegion.City, new Vector3(SpirePosition.x, LandHeight, SpirePosition.y), 545f, beacon, 9f);
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

        /// <summary>Mountains: a radar station (mast + dome) on the highest peak, sitting on a plinth sunk into the rock.</summary>
        static void BuildSummitRadar(Transform parent, Terrain terrain, B.Materials m, Material beacon)
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

            var root = NewLandmark(parent, "Summit Radar", MapRegion.Mountains, peak, 152f, beacon, 7f);
            var white = B.Materials.Mat("Greybox_RadarWhite", new Color(0.93f, 0.93f, 0.95f));
            B.Box("Plinth", root, new Vector3(0f, -12f, 0f), new Vector3(44f, 30f, 44f), m.ConcreteDark, true);
            Sphere("Radome", root, new Vector3(-10f, 14f, 6f), 24f, white, true);
            B.Box("Mast", root, new Vector3(6f, 78f, -6f), new Vector3(5f, 150f, 5f), m.PlaneDark, true);
            for (float h = 40f; h <= 140f; h += 50f)
                B.Box("Mast Arm", root, new Vector3(6f, h, -6f), new Vector3(18f, 1.5f, 1.5f), m.PlaneDark, true);
            root.Find("Top").localPosition = new Vector3(6f, 156f, -6f);
            root.Find("Beacon").localPosition = new Vector3(6f, 156f, -6f);
        }

        /// <summary>Desert: a stepped pyramid, 280 m across and ~210 m tall, the biggest thing on the open sand.</summary>
        static void BuildPyramid(Transform parent, Terrain terrain, Material beacon)
        {
            // Stand it on the lowest ground under its footprint and sink the bottom step in, so no edge floats.
            float ground = float.MaxValue;
            for (float dz = -PyramidBase * 0.5f; dz <= PyramidBase * 0.5f; dz += 20f)
            for (float dx = -PyramidBase * 0.5f; dx <= PyramidBase * 0.5f; dx += 20f)
                ground = Mathf.Min(ground, terrain.SampleHeight(new Vector3(PyramidPosition.x + dx, 0f, PyramidPosition.y + dz)) + terrain.transform.position.y);

            float height = PyramidSteps * PyramidStepHeight;
            var root = NewLandmark(parent, "The Pyramid", MapRegion.Desert, new Vector3(PyramidPosition.x, ground, PyramidPosition.y), height + 6f, beacon, 8f);
            var stone = B.Materials.Mat("Greybox_Sandstone", new Color(0.8f, 0.66f, 0.45f), B.Materials.CheckerTexture(), new Vector2(4f, 2f));
            const float sink = 12f;
            for (int i = 0; i < PyramidSteps; i++)
            {
                float width = PyramidBase * (1f - i / (float)PyramidSteps);
                float bottom = i == 0 ? -sink : i * PyramidStepHeight;
                float top = (i + 1) * PyramidStepHeight;
                B.Box($"Step {i + 1}", root, new Vector3(0f, (bottom + top) * 0.5f, 0f), new Vector3(width, top - bottom, width), stone, true);
            }
        }

        /// <summary>Ocean: a striped lighthouse on a rocky islet off the south coast. Its lamp is the beacon.</summary>
        static void BuildLighthouse(Transform parent, B.Materials m)
        {
            var lamp = B.Materials.UnlitMat("Greybox_LighthouseLamp", new Color(1f, 0.92f, 0.55f));
            var root = NewLandmark(parent, "Lighthouse Rock", MapRegion.Ocean, new Vector3(IsletPosition.x, IsletHeight, IsletPosition.y), 70f, lamp, 6f);
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
            root.Find("Top").localPosition = new Vector3(0f, y + 6f, 0f);
            root.Find("Beacon").localPosition = new Vector3(0f, y + 6f, 0f);
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
        /// gate pylons (colored per lane, no collision) ending on the field's final approach fix.
        /// </summary>
        static void BuildApproachLanes(Transform parent, Airfield west, Airfield east)
        {
            Physics.SyncTransforms(); // the colliders built so far, so the clearance probe sees them
            var group = new GameObject("Approach Lanes").transform;
            group.SetParent(parent, false);
            var pole = B.Materials.Mat("Greybox_LanePole", new Color(0.85f, 0.85f, 0.82f));
            for (int i = 0; i < WestLanes.Length; i++)
            {
                var (name, eastName, color, points, eastPoints) = WestLanes[i];
                var mat = B.Materials.UnlitMat($"Greybox_Lane{i + 1}", color);
                BuildLane(group, west, name, points, mat, pole);
                BuildLane(group, east, eastName, eastPoints ?? MirrorLane(points), mat, pole);
            }
            foreach (var t in group.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }

        static void BuildLane(Transform parent, Airfield field, string name, (Vector2 xz, float agl)[] points, Material gateMat, Material poleMat)
        {
            var root = new GameObject($"{field.DisplayName} - {name}").transform;
            root.SetParent(parent, false);
            var path = new Vector3[points.Length + 1];
            for (int i = 0; i < points.Length; i++)
            {
                var (xz, agl) = points[i];
                path[i] = new Vector3(xz.x, HighestPointNear(xz, LaneClearanceRadius) + agl, xz.y);
            }
            path[^1] = field.GlidePathPoint(FinalFixDistance);
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
        static void BuildClouds(Transform parent)
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

                BuildCloudBank(group, $"Cloud {++n}", new Vector3(xz.x, y, xz.y), puffs, mat);
                var mirrored = new CloudPuff[puffs.Length];
                for (int i = 0; i < puffs.Length; i++)
                    mirrored[i] = new CloudPuff(new Vector3(-puffs[i].center.x, puffs[i].center.y, -puffs[i].center.z), puffs[i].radii);
                BuildCloudBank(group, $"Cloud {++n}", new Vector3(-xz.x, y, -xz.y), mirrored, mat);
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

        static void BuildCloudBank(Transform parent, string name, Vector3 position, CloudPuff[] puffs, Material mat)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.position = position;
            for (int i = 0; i < puffs.Length; i++)
            {
                var r = Sphere($"Puff {i + 1}", root, puffs[i].center, 1f, mat, false);
                r.transform.localScale = puffs[i].radii * 2f;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            root.gameObject.AddComponent<CloudBank>().Configure(puffs);
            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
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
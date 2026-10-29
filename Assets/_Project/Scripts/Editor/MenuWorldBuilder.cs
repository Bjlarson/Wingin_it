using System.Collections.Generic;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// The world round the main menu's hangar (<see cref="MainMenuSceneBuilder"/>), so the view out of its doors is a real
    /// place: the rest of a home airfield laid out round the hangar as on a map (runway, apron, repair pad, tower, radar,
    /// windsock, AA guns, fuel and props, a fence along the far side), set in a green valley. One terrain with woods,
    /// grass and a couple of farms rises to snowy mountains out past the runway (the doors' view) and lower wooded hills
    /// behind, melting into the day sky's haze; a few clouds and balloons hang over it. Scenery only: no airfield
    /// component (the menu has none), colliders only on the ground (the parked plane stands on it) and the airfield's
    /// buildings, and what moves runs on unscaled time, since the menus freeze the game.
    /// </summary>
    internal static class MenuWorldBuilder
    {
        const string TerrainPath = MapTerrainBuilder.TerrainFolder + "/MainMenu_Terrain.asset";
        const int Seed = 4242;
        const int Resolution = 513;
        /// <summary>Terrain square (m), centred on the hangar: past the fog's end in every direction that isn't behind a ridge.</summary>
        const float Size = 8000f;
        /// <summary>The terrain's floor and height range (m); the valley floor is at 0, the airfield's level.</summary>
        const float Floor = -20f, HeightRange = 800f;
        /// <summary>The rules' sea level, low enough that the valley grows oaks and grass, not a beach.</summary>
        const float SeaLevel = -20f;

        /// <summary>The runway's centre, from the hangar (which is at the stage's origin, doors to -X).</summary>
        static readonly Vector3 Field = -B.HangarOffset;
        /// <summary>The flat ground round the airfield in its frame (x across, z along the runway), buildings and AA guns included.</summary>
        static readonly Vector2 PadMin = new(-180f, -460f), PadMax = new(230f, 460f);
        const float PadBlend = 300f;
        /// <summary>No trees this close to the flat ground (m).</summary>
        const float PadClearing = 50f;
        /// <summary>Real trees within this far of the runway (m); the far hills are wooded by texture (<see cref="MapOutskirtsBuilder.WoodsLayer"/>).</summary>
        const float TreeReach = 2200f;
        /// <summary>Grass tufts within this far of the runway (m): only the near meadows are ever seen close up.</summary>
        const float GrassReach = 900f;

        static readonly TerrainRules Rules = TerrainRules.Default;

        /// <summary>The farms and the hamlet out past the runway (centre, yaw), kept clear of terrain trees.</summary>
        static readonly (Vector2 at, float yaw)[] Farms = { (new(-720f, 70f), 12f), (new(-1060f, -390f), -28f) };
        static readonly Vector2 Hamlet = new(-1700f, -60f);
        static readonly Vector2[] Windmills = { new(-1350f, 290f), new(-860f, -720f) };

        /// <summary>A clear day: the maps' day sky, a little hazier so the mountains fade, and banks of cloud over the valley.</summary>
        public static MapSky Sky()
        {
            var sky = new MapSky { fogStart = 600f, fogEnd = 5500f, mirrorClouds = false, cloudCoreHalfHeight = 50f };
            sky.skybox = MapSkyBuilder.DaySky(sky.fogColor);
            foreach (var (xz, radius, altitude, seed) in new[]
            {
                (new Vector2(-3400f, -250f), 300f, 650f, 3),  // low over the ridge, in the doors' view
                (new Vector2(-2500f, 650f), 260f, 760f, 5),
                (new Vector2(-2000f, -1500f), 220f, 700f, 7),
                (new Vector2(1500f, -1600f), 240f, 650f, 9),  // behind the hangar
                (new Vector2(900f, 1900f), 260f, 720f, 13),
            })
                sky.clouds.Add(new CloudBankAnchor(xz, radius, altitude, seed));
            return sky;
        }

        /// <summary>Builds it all under <paramref name="stage"/>. Without the airfield pack the field stays bare grass.</summary>
        public static void Build(Transform stage, AirfieldModels airfield, MapSky sky)
        {
            var nature = NatureModels.Load();
            var terrain = BuildTerrain(stage, nature);
            if (airfield != null)
                BuildAirfield(stage, airfield);
            var prairie = MapPackModels.Load("Prairie");
            if (prairie != null)
                BuildCountryside(stage, terrain, prairie);
            MapSkyBuilder.BuildClouds(stage, sky, nature);
        }

        /// <summary>Clears the static flag under everything that moves (rotors, the beacon and dish, the sock, balloons).</summary>
        public static void Unfreeze(Transform stage)
        {
            var moving = new List<Component>();
            moving.AddRange(stage.GetComponentsInChildren<Spinner>(true));
            moving.AddRange(stage.GetComponentsInChildren<Windsock>(true));
            moving.AddRange(stage.GetComponentsInChildren<Bobber>(true));
            foreach (var c in moving)
                foreach (var t in c.GetComponentsInChildren<Transform>(true))
                    t.gameObject.isStatic = false;
        }

        // ------------------------------------------------------------------------------------------ ground

        static Terrain BuildTerrain(Transform stage, NatureModels nature)
        {
            var origin = new Vector3(-Size * 0.5f, Floor, -Size * 0.5f);
            float cell = Size / (Resolution - 1);
            var heights = new float[Resolution, Resolution];
            for (int iz = 0; iz < Resolution; iz++)
            for (int ix = 0; ix < Resolution; ix++)
                heights[iz, ix] = Mathf.Clamp01((Height(origin.x + ix * cell, origin.z + iz * cell) - Floor) / HeightRange);

            var terrain = MapTerrainBuilder.BuildTerrain(TerrainPath, "Valley", stage, origin, new Vector3(Size, HeightRange, Size),
                heights, SeaLevel, Seed, new Painter(), nature);
            // The beach's slot holds the far woods: the rules never paint sand this far above their sea.
            var layers = terrain.terrainData.terrainLayers;
            layers[1] = MapOutskirtsBuilder.WoodsLayer();
            terrain.terrainData.terrainLayers = layers;
            terrain.treeDistance = TreeReach + 800f;
            terrain.treeBillboardDistance = terrain.treeDistance;
            return terrain;
        }

        /// <summary>
        /// Ground height (m) at (x, z): flat round the airfield, gentle rolling meadows past it, and from 1.3 km out a
        /// range of ridges, high and snowy out past the runway (to -X, the doors' view), foothills the other ways.
        /// </summary>
        static float Height(float x, float z)
        {
            float outside = PadOutside(x, z);
            var rel = new Vector2(x - Field.x, z - Field.z);
            float r = rel.magnitude;
            float rolling = MapTerrainBuilder.Noise(x, z, 700f, Seed * 0.011f) * 35f * TerrainRules.Smooth(0f, PadBlend, outside);
            float west = TerrainRules.Smooth(-0.3f, 0.7f, -rel.x / Mathf.Max(1f, r));
            float range = TerrainRules.Smooth(1300f, 3200f, r) * Mathf.Lerp(0.35f, 1f, west);
            return rolling + range * (90f + 480f * Ridges(x, z));
        }

        /// <summary>Ridged noise 0..1: sharp crests with valleys between.</summary>
        static float Ridges(float x, float z)
        {
            float sum = 0f, amp = 1f, total = 0f;
            for (int octave = 0; octave < 5; octave++)
            {
                float f = (1 << octave) / 1500f;
                float n = Mathf.PerlinNoise(x * f + 31.7f + octave * 7.31f, z * f - 12.9f + octave * 3.17f);
                float ridge = 1f - Mathf.Abs(2f * n - 1f);
                sum += ridge * ridge * amp;
                total += amp;
                amp *= 0.5f;
            }
            return sum / total;
        }

        /// <summary>How far (m) (x, z) is outside the airfield's flat ground; 0 on it.</summary>
        static float PadOutside(float x, float z)
        {
            float lx = x - Field.x, lz = z - Field.z;
            float dx = Mathf.Max(PadMin.x - lx, 0f, lx - PadMax.x);
            float dz = Mathf.Max(PadMin.y - lz, 0f, lz - PadMax.y);
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>True on a farm's, the hamlet's or a windmill's ground, where the terrain grows nothing.</summary>
        static bool Claimed(float x, float z)
        {
            var p = new Vector2(x, z);
            foreach (var (at, _) in Farms)
                if (Vector2.Distance(p, at) < 210f) // the yard, its fields and the windbreak
                    return true;
            if (Vector2.Distance(p, Hamlet) < 110f)
                return true;
            foreach (var mill in Windmills)
                if (Vector2.Distance(p, mill) < 20f)
                    return true;
            return false;
        }

        /// <summary>A fixed 0..1 per spot, to thin the trees with distance without touching the scatter's own rolls.</summary>
        static float Hash(float x, float z) => Mathf.Repeat(Mathf.Sin(x * 12.9898f + z * 78.233f) * 43758.547f, 1f);

        /// <summary>The valley by the maps' rules, kept bare on the airfield and the farms, wooded by texture far out.</summary>
        sealed class Painter : ITerrainPainter
        {
            public Rect TreeArea => new(Field.x - TreeReach, Field.z - TreeReach, TreeReach * 2f, TreeReach * 2f);

            public (Vector4 layers, float paved) Layers(float x, float z, float h, float steep)
            {
                var w = Rules.Layers(h, steep);
                float r = new Vector2(x - Field.x, z - Field.z).magnitude;
                float wooded = TerrainRules.Smooth(TreeReach - 600f, TreeReach + 400f, r)
                    * (0.35f + 0.6f * TerrainRules.Smooth(0.42f, 0.6f, MapTerrainBuilder.Noise(x, z, 700f, Seed * 0.031f)));
                float grass = w.x + w.y;
                return (new Vector4(grass * (1f - wooded), grass * wooded, w.z, w.w), 0f);
            }

            public (string kind, float minScale, float maxScale)? PickTree(float x, float z, float h, float steep, double roll)
            {
                float r = new Vector2(x - Field.x, z - Field.z).magnitude;
                if (PadOutside(x, z) < PadClearing || Claimed(x, z) || r > TreeReach)
                    return null;
                // Thinner toward the far edge, where the woods texture takes over, and only copses in the meadows across
                // the runway, so the farms show through the doors.
                float hash = Hash(x, z);
                if (hash > 1f - 0.7f * TerrainRules.Smooth(TreeReach - 900f, TreeReach, r)
                    || x < Field.x && r < 800f && hash > 0.35f + 0.65f * TerrainRules.Smooth(500f, 800f, r))
                    return null;
                return Rules.PickTree(h, steep, (float)roll, MapTerrainBuilder.Noise(x, z, 220f, Seed * 0.013f));
            }

            public (int grass, bool flowers) Detail(float x, float z, float h, float steep, System.Random rng)
            {
                var detail = Rules.Detail(h, steep, MapTerrainBuilder.Noise(x, z, 120f, Seed * 0.029f), (float)rng.NextDouble());
                float r = new Vector2(x - Field.x, z - Field.z).magnitude;
                return PadOutside(x, z) < 6f || Claimed(x, z) || r > GrassReach ? (0, false) : detail;
            }
        }

        // ------------------------------------------------------------------------------------------ airfield

        /// <summary>
        /// The rest of the home field round the hangar, where a map's airfield has it: runway, apron and repair pad, the
        /// tower, radar, windsock, fuel and props (<see cref="B.DressAirfield"/>), AA guns at the corners (models only,
        /// pointing up the approach), and a fence along the far side of the runway.
        /// </summary>
        static void BuildAirfield(Transform stage, AirfieldModels models)
        {
            var field = new GameObject("Airfield").transform;
            field.SetParent(stage, false);
            field.localPosition = Field;
            float halfLength = B.RunwaySize.y * 0.5f, halfWidth = B.RunwaySize.x * 0.5f;
            const float surface = 0.1f;

            models.Child("Runway", field, Vector3.zero, castShadows: false);
            models.Child("Apron", field, Vector3.zero, castShadows: false);
            models.Child("RepairPad", field, new Vector3(0f, surface, 140f), castShadows: false);

            var buildings = new GameObject("Buildings").transform;
            buildings.SetParent(field, false);
            var moving = new List<Transform>();
            B.DressAirfield(buildings, models, halfWidth, halfLength, moving);
            foreach (var spinner in field.GetComponentsInChildren<Spinner>())
                B.SetBool(spinner, "unscaledTime", true);
            foreach (var sock in field.GetComponentsInChildren<Windsock>())
                B.SetBool(sock, "unscaledTime", true);

            var aa = new GameObject("AA Guns").transform;
            aa.SetParent(field, false);
            int n = 1;
            foreach (float z in new[] { -halfLength + 80f, halfLength - 80f })
                foreach (float x in new[] { -halfWidth - 50f, halfWidth + 90f })
                    AAGun(aa, $"AA Gun {n++}", new Vector3(x, 0f, z), models, yaw: z < 0f ? 200f : -20f);
        }

        /// <summary>An AA turret's models at rest: sandbags, head, cradle raised a little, both barrels.</summary>
        static void AAGun(Transform parent, string name, Vector3 localPos, AirfieldModels models, float yaw)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            models.Child("AAGunBase", root, Vector3.zero);
            var head = models.Mounted("AAGunHead", root, "AAGunBase").transform;
            head.localRotation = Quaternion.Euler(0f, yaw, 0f);
            var cradle = models.Mounted("AAGunCradle", head, "AAGunHead").transform;
            cradle.localRotation = Quaternion.Euler(-35f, 0f, 0f);
            models.Mounted("AAGunBarrelLeft", cradle, "AAGunCradle");
            models.Mounted("AAGunBarrelRight", cradle, "AAGunCradle");
        }

        // ------------------------------------------------------------------------------------------ countryside

        /// <summary>
        /// The prairie pack across the valley: a fence along the airfield's far side, two farms in their fields, a hamlet
        /// with its church under the mountains, windmills turning on the rises and balloons drifting over it all.
        /// </summary>
        static void BuildCountryside(Transform stage, Terrain terrain, MapPackModels pack)
        {
            var group = new GameObject("Countryside").transform;
            group.SetParent(stage, false);
            var rng = new System.Random(Seed);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            float Ground(Vector2 p) => terrain.SampleHeight(new Vector3(p.x, 0f, p.y)) + terrain.transform.position.y;
            float Lowest(Vector2 p, float radius)
            {
                float low = Ground(p);
                for (int k = 0; k < 8; k++)
                    low = Mathf.Min(low, Ground(p + new Vector2(Mathf.Cos(k * 0.785f), Mathf.Sin(k * 0.785f)) * radius));
                return low;
            }
            GameObject Put(string name, Vector2 p, float yaw, float sink = 0f, float scale = 1f) =>
                pack.Place(name, group, new Vector3(p.x, Lowest(p, pack.Radius(name) * scale * 0.6f) - sink, p.y),
                    Quaternion.Euler(0f, yaw, 0f), Vector3.one * scale, null);
            void Rotor(string rotorName, string bodyName, GameObject body, float speed)
            {
                var go = pack.Mounted(rotorName, body.transform, bodyName);
                go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var spinner = go.AddComponent<Spinner>();
                spinner.Configure(speed);
                B.SetBool(spinner, "unscaledTime", true);
            }
            void Lay(string name, Vector2 p, float yaw)
            {
                var nz = terrain.terrainData.GetInterpolatedNormal((p.x - terrain.transform.position.x) / Size,
                    (p.y - terrain.transform.position.z) / Size);
                var rotation = Quaternion.FromToRotation(Vector3.up, nz) * Quaternion.Euler(0f, yaw, 0f);
                pack.Place(name, group, new Vector3(p.x, Ground(p) - 0.1f, p.y), rotation, Vector3.one, null, castShadows: false);
            }
            Vector2 Turn(Vector2 v, float yaw)
            {
                float a = -yaw * Mathf.Deg2Rad;
                return new Vector2(v.x * Mathf.Cos(a) - v.y * Mathf.Sin(a), v.x * Mathf.Sin(a) + v.y * Mathf.Cos(a));
            }

            // The airfield's boundary fence, along the far side of the runway.
            float fenceX = Field.x + PadMin.x + 30f;
            for (float z = Field.z + PadMin.y + 20f; z <= Field.z + PadMax.y - 20f; z += 12f)
                Put("Fence", new Vector2(fenceX, z), 90f);

            string[] crops = { "CropWheat", "CropGreen", "CropPlowed" };
            foreach (var (f, yaw) in Farms)
            {
                Vector2 L(float x, float z) => f + Turn(new Vector2(x, z), yaw);
                Put("Barn", L(4f, 14f), yaw, sink: 0.3f);
                Put("Farmhouse", L(-26f, -8f), yaw + 90f, sink: 0.3f);
                Put("Silo", L(22f, 26f), yaw, sink: 0.5f);
                Put("Silo", L(22f, 37f), yaw, sink: 0.5f);
                Rotor("WindpumpRotor", "Windpump", Put("Windpump", L(26f, -16f), yaw + Range(-40f, 40f)), Range(90f, 150f));
                Put("HayStack", L(-6f, -18f), Range(0f, 360f));
                for (int i = 0; i < 3; i++)
                    Put("HayBale", L(10f + Range(-8f, 8f), -26f + Range(-6f, 6f)), Range(0f, 360f));
                for (int side = 0; side < 4; side++)
                    for (int k = 0; k < 7; k++)
                    {
                        if (side == 0 && k == 3)
                            continue; // the gate
                        float s = -36f + k * 12f;
                        var local = side switch { 0 => new Vector2(s, -42f), 1 => new Vector2(42f, s), 2 => new Vector2(s, 42f), _ => new Vector2(-42f, s) };
                        var p = L(local.x, local.y);
                        pack.Place("Fence", group, new Vector3(p.x, Ground(p) - 0.1f, p.y),
                            Quaternion.Euler(0f, yaw + (side % 2 == 0 ? 0f : 90f), 0f), Vector3.one, null);
                    }
                for (int i = 0; i < 5; i++)
                    Put("Cow", L(Range(-14f, 14f), -64f + Range(-12f, 12f)), Range(0f, 360f));
                // Fields front and back, and a windbreak of poplars down one side.
                for (int i = -3; i <= 3; i++)
                    foreach (int j in new[] { -3, 2, 3 })
                        Lay(crops[rng.Next(crops.Length)], L(i * 42f, j * 42f), yaw);
                for (float z = -100f; z <= 150f; z += 10f)
                    Put("TreePoplar", L(-150f, z), Range(0f, 360f), scale: Range(0.9f, 1.15f));
            }

            // The hamlet: a church, a water tower and houses round a green.
            Put("Church", Hamlet, 80f, sink: 0.3f);
            Put("WaterTower", Hamlet + new Vector2(34f, 10f), 80f);
            for (int i = 0; i < 6; i++)
            {
                var p = Hamlet + new Vector2(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f)) * 60f;
                var toGreen = Hamlet - p;
                Put("Farmhouse", p, Mathf.Atan2(toGreen.x, toGreen.y) * Mathf.Rad2Deg - 90f, sink: 0.3f);
            }

            foreach (var p in Windmills)
                Rotor("WindmillSails", "Windmill", Put("Windmill", p, Range(60f, 120f), sink: 0.4f), Range(28f, 40f));

            // Balloons drifting over the valley, in the doors' view.
            foreach (var (p, height) in new[] { (new Vector2(-1000f, 260f), 240f), (new Vector2(-1900f, -520f), 380f), (new Vector2(-600f, -900f), 300f) })
            {
                var go = pack.Place("HotAirBalloon", group, new Vector3(p.x, Ground(p) + height, p.y),
                    Quaternion.Euler(0f, Range(0f, 360f), 0f), Vector3.one * 1.4f, null);
                var bobber = go.AddComponent<Bobber>();
                bobber.Configure(3f, 1.5f, Range(8f, 12f), Range(90f, 160f), Range(300f, 480f));
                B.SetBool(bobber, "unscaledTime", true);
            }
        }
    }
}

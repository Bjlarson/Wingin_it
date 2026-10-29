using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using W = WingingIt.Editor.MapLayoutWriter;

namespace WingingIt.Editor
{
    /// <summary>
    /// Mountain Range (from <c>Design/05-Levels/Mountins.png</c>), the winter map: snowbound ridges to the edge of the
    /// map (peaks just over 1 km under a 1.1 km ceiling), valleys between them, snowy pine woods below the tree line and
    /// snow falling round the camera. No sea. The homes sit in valleys south-west and north-east at the same height
    /// (~605 m); the three capture fields stand on the line equally far from both: the Frozen Valley in the north-west,
    /// the Central Pass and the Ridge field south-east of it. Ram Royale gets a 700 m bowl over the Frozen Valley with
    /// the Spire inside. Written to the map's definition by <c>Winging It/Map/Write Mountain Range Definition</c>; the
    /// terrain levels a pad under each field (a notch cut into the slope) when built.
    /// </summary>
    public static class MountainRangeDefinition
    {
        const string Path = "Assets/_Project/Maps/MountainRange/MountainRange.asset";

        static readonly Color Magenta = new(0.95f, 0.35f, 0.9f), Cyan = new(0.2f, 0.85f, 1f), Lime = new(0.6f, 1f, 0.2f);
        static readonly Color Gold = new(1f, 0.8f, 0.2f), Orange = new(1f, 0.55f, 0.2f);

        /// <summary>The fog and the sky's lower half: a cold, pale blue-grey, so far ridges fade into the snow haze.</summary>
        static readonly Color SnowFog = new(0.8f, 0.85f, 0.9f);

        /// <summary>The Frozen Valley's floor, the Ram Royale bowl's middle and the Spire's spot.</summary>
        static readonly Vector2 ValleyFloor = new(-1100f, 300f);

        [MenuItem("Winging It/Map/Write Mountain Range Definition")]
        public static void Write()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Path);
            if (map == null)
            {
                Debug.LogError($"[Winging It] No map definition at {Path}.");
                return;
            }
            map.displayName = "Mountain Range";
            map.rules = WinterRules();
            map.layout = BuildLayout(map);
            map.regions = Regions();
            map.landmarks = new()
            {
                new LandmarkAnchor { kind = LandmarkKind.SummitRadar, xz = new Vector2(1000f, -700f), searchHalfSize = new Vector2(500f, 500f), region = "Summit Ridge" },
            };
            map.sky = Sky();
            map.outskirts = Outskirts();
            map.minimap = new MapMinimap { sea = new Color(0.78f, 0.82f, 0.88f) }; // snow runs to the edge
            W.Save(map);
        }

        /// <summary>
        /// Snow on everything but the steep faces (bare rock from ~34°), whatever the height; snowy pines in woods up to
        /// the tree line, snowy boulders above it; no grass.
        /// </summary>
        internal static TerrainRules WinterRules()
        {
            var rules = TerrainRules.Default;
            rules.rockSlope = new Vector2(24f, 36f);
            rules.rockHeight = new Vector2(5000f, 5100f); // never rock by height alone
            rules.snowHeight = new Vector2(-200f, -100f); // snow from the valley floors up
            rules.snowSlope = new Vector2(34f, 48f);
            rules.treeLine = 850f;
            rules.treeSpacing = 26f;
            rules.winter = true;
            return rules;
        }

        internal static MapLayout BuildLayout(MapDefinition map)
        {
            var ground = map.LoadHeightmap();
            var layout = new MapLayout();
            var west = W.Airfield(map, ground, "West Home Airfield", 0, true, false, false, new Vector2(-650f, -1290f), 255f);
            var east = W.Airfield(map, ground, "East Home Airfield", 1, true, false, false, new Vector2(790f, 1110f), 105f);
            var central = W.Airfield(map, ground, "Central Pass Airfield", Airfield.Neutral, false, true, false, new Vector2(-330f, 150f), 255f);
            var ridge = W.Airfield(map, ground, "Ridge Airfield", Airfield.Neutral, false, true, false, new Vector2(470f, -330f), 165f);
            var valley = W.Airfield(map, ground, "Frozen Valley Airfield", Airfield.Neutral, false, true, false, new Vector2(-1210f, 710f), 330f);
            layout.airfields.AddRange(new[] { west, east, central, ridge, valley });

            // Each home: straight down its valley, or round either shoulder of it. Short lanes: the map's edge is close.
            foreach (var home in new[] { west, east })
            {
                layout.lanes.Add(W.Lane(home, "Valley Run", Lime, false, (0f, -1500f, 70f), (0f, -1100f, 60f)));
                layout.lanes.Add(W.Lane(home, "Right Shoulder", Magenta, false, (700f, -1300f, 80f), (450f, -1100f, 70f), (200f, -1000f, 60f)));
                layout.lanes.Add(W.Lane(home, "Left Shoulder", Cyan, false, (-350f, -1200f, 80f), (-250f, -1080f, 70f), (-100f, -980f, 60f)));
            }
            // The capture fields are notches in the slopes: one way in each, from either side of the approach.
            foreach (var field in new[] { central, ridge, valley })
            {
                var color = field == central ? Gold : Orange;
                layout.lanes.Add(W.Lane(field, "Left Final", color, false, (-400f, -1500f, 70f), (-150f, -1100f, 60f)));
                layout.lanes.Add(W.Lane(field, "Right Final", color, false, (400f, -1500f, 70f), (150f, -1100f, 60f)));
            }

            layout.bannerStands.Add(new BannerStandAnchor { airfield = west.name, distance = 150f });
            layout.bannerStands.Add(new BannerStandAnchor { airfield = east.name, distance = 150f });

            // The Frozen Valley bowl, up to 1050 m: ridges up to 760 m wall it in; the air start ring (550 m out) clears
            // everything under 710 m.
            layout.arenas.Add(W.RamArena(ValleyFloor, radius: 700f, warning: 120f, ceiling: 1050f,
                startRadius: 550f, startAltitude: 800f, patrolRadius: 400f, patrolAltitude: 850f));

            foreach (var (name, xz, agl) in new[]
            {
                ("Central Pass", central.position, 250f), ("Ridge", ridge.position, 250f),
                ("Frozen Valley", valley.position, 250f), ("North Slopes", new Vector2(300f, 1300f), 200f),
                ("South Slopes", new Vector2(-300f, -1300f), 200f), ("Summit Ridge", new Vector2(1000f, -700f), 200f),
            })
                layout.fightAreas.Add(new FightAreaAnchor { name = name, point = new LayoutPoint(xz, agl) });

            string[] all = { west.name, east.name, central.name, ridge.name, valley.name };
            layout.modes.Add(new ModeLayout { modeId = "Teams", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "Banner", airfields = all, bannerStands = true });
            layout.modes.Add(new ModeLayout { modeId = "Domination", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "RamRoyale", airfields = new string[0], arena = "RamRoyale" });
            return layout;
        }

        /// <summary>The pass first, so it wins over the slopes round it.</summary>
        static MapRegions Regions()
        {
            var regions = new MapRegions { sea = "Frozen Lake", fallback = "High Country" };
            void Area(string name, RegionShape shape, Vector2 center, Vector2 size) =>
                regions.areas.Add(new RegionArea { name = name, shape = shape, center = center, size = size });
            Area("Central Pass", RegionShape.Ellipse, new Vector2(-100f, -50f), new Vector2(500f, 400f));
            Area("Frozen Valley", RegionShape.Box, new Vector2(-1150f, 500f), new Vector2(450f, 700f));
            Area("Summit Ridge", RegionShape.Box, new Vector2(1000f, -700f), new Vector2(600f, 600f));
            Area("North Slopes", RegionShape.Box, new Vector2(400f, 1350f), new Vector2(900f, 500f));
            Area("South Slopes", RegionShape.Box, new Vector2(-400f, -1350f), new Vector2(900f, 500f));
            return regions;
        }

        /// <summary>
        /// Winter light: a soft sun, a hazy pale sky, close snow fog (out to 3.4 km, which also hides the map's edge) and
        /// snow falling. Banks over the
        /// valleys, placed by hand rather than mirrored (a valley's mirror is a ridge); each home gets a bank ahead of it
        /// and one over its shoulder. The builder lifts any bank a ridge would touch.
        /// </summary>
        static MapSky Sky()
        {
            var sky = new MapSky
            {
                skybox = MapSkyBuilder.WinterSky(SnowFog), sunAngles = new Vector2(40f, -140f), sunIntensity = 1.15f, sunColor = new Color(0.9f, 0.95f, 1f),
                fogColor = SnowFog, fogStart = 300f, fogEnd = 3400f, mirrorClouds = false, snowfall = true,
            };
            foreach (var (xz, radius, altitude, seed) in new[]
            {
                (new Vector2(-1100f, 600f), 230f, 700f, 11),  // over the Frozen Valley
                (new Vector2(1450f, 450f), 230f, 650f, 23),   // the low ground east of the east home
                (new Vector2(150f, -1100f), 220f, 850f, 37),  // ahead of the west home
                (new Vector2(-50f, 1000f), 220f, 850f, 41),   // ahead of the east home
                (new Vector2(-1300f, -700f), 210f, 800f, 53), // the west home's shoulder
                (new Vector2(1200f, 1500f), 210f, 750f, 67),  // the east home's shoulder
            })
                sky.clouds.Add(new CloudBankAnchor(xz, radius, altitude, seed));
            return sky;
        }

        /// <summary>Ridge after snowy ridge into the snow haze; the fog ends 3.4 km out, so the outskirts needn't reach far.</summary>
        internal static MapOutskirts Outskirts() => new()
        {
            enabled = true, reach = 4500f, blend = 1400f, height = new Vector2(380f, 1000f), featureSize = 1800f, ridged = 0.85f,
            treeBand = 1200f, treeDensity = 0.45f,
        };
    }
}

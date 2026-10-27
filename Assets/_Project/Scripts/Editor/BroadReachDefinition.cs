using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using W = WingingIt.Editor.MapLayoutWriter;

namespace WingingIt.Editor
{
    /// <summary>
    /// Broad Reach (island-map.md, Map List): a long north-south valley between ridges, the West Plains beyond the
    /// western ridges and high ridges to the east. The homes sit at the valley's two ends; the three capture fields
    /// stand in a line across the middle, each as far from one home as from the other, so holding all three spans
    /// ~3 km. Ram Royale gets a 1 km circle round the Central Spur and its Spire. Written to the map's definition by
    /// <c>Winging It/Map/Write Broad Reach Definition</c>; the terrain levels a pad under each field when built.
    /// </summary>
    public static class BroadReachDefinition
    {
        const string Path = "Assets/_Project/Maps/BroadReach/BroadReach.asset";

        static readonly Color Magenta = new(0.95f, 0.35f, 0.9f), Cyan = new(0.2f, 0.85f, 1f), Lime = new(0.6f, 1f, 0.2f);
        static readonly Color Gold = new(1f, 0.8f, 0.2f), Orange = new(1f, 0.55f, 0.2f);

        /// <summary>The Central Spur: a rocky knoll in the middle of the valley, with the Spire on top.</summary>
        static readonly Vector2 SpurPosition = new(90f, -620f);

        [MenuItem("Winging It/Map/Write Broad Reach Definition")]
        public static void Write()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Path);
            if (map == null)
            {
                Debug.LogError($"[Winging It] No map definition at {Path}.");
                return;
            }
            map.layout = BuildLayout(map);
            map.regions = Regions();
            map.landmarks = new()
            {
                new LandmarkAnchor { kind = LandmarkKind.Spire, xz = SpurPosition, region = "Central Spur" },
                new LandmarkAnchor { kind = LandmarkKind.SummitRadar, xz = new Vector2(2800f, 0f), searchHalfSize = new Vector2(800f, 1800f), region = "East Ridges" },
                new LandmarkAnchor { kind = LandmarkKind.Pyramid, xz = new Vector2(-2750f, 1300f), region = "West Plains" },
            };
            map.sky = Sky();
            map.minimap = new MapMinimap { sea = new Color(0.22f, 0.32f, 0.2f) }; // land runs to the edge
            W.Save(map);
        }

        internal static MapLayout BuildLayout(MapDefinition map)
        {
            var ground = map.LoadHeightmap();
            var layout = new MapLayout();
            var south = W.Airfield(map, ground, "South Home Airfield", 0, true, false, false, new Vector2(400f, -2700f), 180f);
            var north = W.Airfield(map, ground, "North Home Airfield", 1, true, false, false, new Vector2(0f, 2300f), 0f);
            var central = W.Airfield(map, ground, "Central Airfield", Airfield.Neutral, false, true, true, new Vector2(800f, -150f), 0f);
            var gap = W.Airfield(map, ground, "Ridge Gap Airfield", Airfield.Neutral, false, true, true, new Vector2(-600f, -280f), 0f);
            var plain = W.Airfield(map, ground, "West Plain Airfield", Airfield.Neutral, false, true, true, new Vector2(-2400f, -400f), 165f);
            layout.airfields.AddRange(new[] { south, north, central, gap, plain });

            // Each home: straight up the valley from the middle, or swinging in over either side of it.
            foreach (var (home, rightName, leftName) in new[] { (south, "West Bend", "East Bend"), (north, "East Bend", "West Bend") })
            {
                layout.lanes.Add(W.Lane(home, "Valley Run", Lime, false, (0f, -2400f, 60f), (0f, -1600f, 50f)));
                layout.lanes.Add(W.Lane(home, rightName, Magenta, false, (1500f, -1900f, 70f), (1100f, -1500f, 60f), (500f, -1200f, 50f)));
                layout.lanes.Add(W.Lane(home, leftName, Cyan, false, (-1500f, -1900f, 70f), (-1100f, -1500f, 60f), (-500f, -1200f, 50f)));
            }
            // The capture fields land from either end, one lane into each, the same route turned round.
            layout.lanes.Add(W.Lane(central, "South Final", Gold, false, (-700f, -2000f, 60f), (-400f, -1400f, 50f)));
            layout.lanes.Add(W.Lane(central, "North Final", Gold, true, (700f, -2000f, 60f), (400f, -1400f, 50f)));
            layout.lanes.Add(W.Lane(gap, "South Final", Orange, false, (500f, -1900f, 60f), (200f, -1400f, 50f)));
            layout.lanes.Add(W.Lane(gap, "North Final", Orange, true, (500f, -1900f, 60f), (200f, -1400f, 50f)));
            layout.lanes.Add(W.Lane(plain, "North Final", Orange, false, (250f, -1800f, 60f), (100f, -1300f, 50f)));
            layout.lanes.Add(W.Lane(plain, "South Final", Orange, true, (250f, -1800f, 60f), (100f, -1300f, 50f)));

            layout.bannerStands.Add(new BannerStandAnchor { airfield = south.name, distance = 150f });
            layout.bannerStands.Add(new BannerStandAnchor { airfield = north.name, distance = 150f });

            // A bigger circle than the Island's (1 km, up to 750 m), with the Spur and its Spire to ram round.
            layout.arenas.Add(W.RamArena(new Vector2(200f, -500f), radius: 1000f, warning: 150f, ceiling: 750f,
                startRadius: 800f, startAltitude: 350f, patrolRadius: 500f, patrolAltitude: 300f));

            foreach (var (name, xz, agl) in new[]
            {
                ("Central Airfield", central.position, 250f), ("Ridge Gap", gap.position, 250f),
                ("West Plain", plain.position, 250f), ("Central Spur", SpurPosition, 300f),
                ("South Valley", new Vector2(500f, -1400f), 200f), ("North Valley", new Vector2(300f, 1100f), 200f),
            })
                layout.fightAreas.Add(new FightAreaAnchor { name = name, point = new LayoutPoint(xz, agl) });

            string[] all = { south.name, north.name, central.name, gap.name, plain.name };
            layout.modes.Add(new ModeLayout { modeId = "Teams", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "Banner", airfields = all, bannerStands = true });
            layout.modes.Add(new ModeLayout { modeId = "Domination", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "RamRoyale", airfields = new string[0], arena = "RamRoyale" });
            return layout;
        }

        /// <summary>Small areas first: the spur and the gap win over the valley and ridges round them.</summary>
        static MapRegions Regions()
        {
            var regions = new MapRegions { sea = "Sea", fallback = "South Valley" };
            void Area(string name, RegionShape shape, Vector2 center, Vector2 size) =>
                regions.areas.Add(new RegionArea { name = name, shape = shape, center = center, size = size });
            Area("Central Spur", RegionShape.Ellipse, SpurPosition, new Vector2(350f, 350f));
            Area("Ridge Gap", RegionShape.Ellipse, new Vector2(-650f, -300f), new Vector2(500f, 450f));
            Area("West Plains", RegionShape.Box, new Vector2(-3000f, 0f), new Vector2(1100f, 4100f));
            Area("West Ridges", RegionShape.Box, new Vector2(-1500f, 0f), new Vector2(400f, 4100f));
            Area("East Ridges", RegionShape.Box, new Vector2(2900f, 0f), new Vector2(1200f, 4100f));
            Area("North Valley", RegionShape.Box, new Vector2(0f, 2150f), new Vector2(1700f, 1950f));
            return regions;
        }

        /// <summary>
        /// Long sightlines (fog out to 9 km) and cloud banks over the valley and plains, each mirrored through the
        /// middle for the north team; banks over the ridges rise to clear them.
        /// </summary>
        static MapSky Sky()
        {
            var sky = new MapSky { skybox = MapSkyBuilder.GreyboxSky(), fogStart = 1200f, fogEnd = 9000f, mirrorClouds = true };
            foreach (var (xz, radius, altitude, seed) in new[]
            {
                (new Vector2(600f, -1100f), 260f, 520f, 11),   // over the south valley, between Central and the spur
                (new Vector2(-500f, -1300f), 240f, 560f, 23),  // south of the Ridge Gap
                (new Vector2(-1700f, -900f), 260f, 600f, 37),  // over the west ridges
                (new Vector2(1300f, -1900f), 230f, 500f, 41),  // the valley's east side, toward the south home
                (new Vector2(-1300f, -2300f), 250f, 620f, 53), // south-west, behind the south home
                (new Vector2(-2700f, -1500f), 260f, 520f, 67), // over the West Plains
            })
                sky.clouds.Add(new CloudBankAnchor(xz, radius, altitude, seed));
            return sky;
        }
    }
}

using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using W = WingingIt.Editor.MapLayoutWriter;

namespace WingingIt.Editor
{
    /// <summary>
    /// The Island (v2, from <c>Design/05-Levels/island.png</c>): one long island running north to south, split by a
    /// shallow strait. North of it a green plateau, south of it hills rising to sharp peaks (300 m). The island is too
    /// narrow for the homes, so they stand on strips built out of the sea off its west and east coasts, and the line
    /// equally far from both runs down the island's spine: the three capture fields sit on it, one on the plateau, one
    /// on a causeway across the strait, one in the southern hills. Ram Royale gets a 1 km circle over the strait.
    /// Written to the map's definition by <c>Winging It/Map/Write Island v2 Definition</c>; the terrain levels a pad
    /// under each field when built. Replaced the hand-built Island (now the tests' level) on 2026-10-27.
    /// </summary>
    public static class IslandV2Definition
    {
        const string Path = "Assets/_Project/Maps/IslandV2/IslandV2.asset";

        /// <summary>A strip's runway height off the coast or across the strait: clear of the waves, under the beach band's top.</summary>
        const float StripHeight = 8f;

        static readonly Color Magenta = new(0.95f, 0.35f, 0.9f), Cyan = new(0.2f, 0.85f, 1f), Lime = new(0.6f, 1f, 0.2f);
        static readonly Color Gold = new(1f, 0.8f, 0.2f), Orange = new(1f, 0.55f, 0.2f);

        [MenuItem("Winging It/Map/Write Island v2 Definition")]
        public static void Write()
        {
            var map = AssetDatabase.LoadAssetAtPath<MapDefinition>(Path);
            if (map == null)
            {
                Debug.LogError($"[Winging It] No map definition at {Path}.");
                return;
            }
            map.displayName = "The Island";
            map.layout = BuildLayout(map);
            map.regions = Regions();
            map.landmarks = new()
            {
                new LandmarkAnchor { kind = LandmarkKind.SummitRadar, xz = new Vector2(100f, -1800f), searchHalfSize = new Vector2(700f, 700f), region = "South Peaks" },
                new LandmarkAnchor { kind = LandmarkKind.Pyramid, xz = new Vector2(250f, 1300f), region = "North Plateau" },
            };
            map.sky = Sky();
            map.outskirts = Outskirts();
            map.minimap = new MapMinimap();
            W.Save(map);
        }

        internal static MapLayout BuildLayout(MapDefinition map)
        {
            var ground = map.LoadHeightmap();
            var layout = new MapLayout();
            AirfieldAnchor Field(string name, int team, bool home, bool capturable, bool twoWay, Vector2 position, float yaw)
            {
                // Built out of the sea where the pad is mostly water: never lower than a strip.
                var field = W.Airfield(map, ground, name, team, home, capturable, twoWay, position, yaw);
                field.elevation = Mathf.Max(field.elevation, StripHeight);
                return field;
            }
            var west = Field("West Home Airfield", 0, true, false, false, new Vector2(-1500f, -400f), 0f);
            var east = Field("East Home Airfield", 1, true, false, false, new Vector2(1800f, 400f), 180f);
            var central = Field("Central Airfield", Airfield.Neutral, false, true, true, new Vector2(100f, 225f), 0f);
            var north = Field("North Plateau Airfield", Airfield.Neutral, false, true, false, new Vector2(-350f, 2150f), 315f);
            var south = Field("South Hills Airfield", Airfield.Neutral, false, true, true, new Vector2(550f, -1550f), 285f);
            layout.airfields.AddRange(new[] { west, east, central, north, south });

            // Each home: straight in over the open sea, or swinging in from the island's side or from further out.
            foreach (var home in new[] { west, east })
            {
                layout.lanes.Add(W.Lane(home, "Open Sea", Lime, false, (0f, -1900f, 60f), (0f, -1300f, 50f)));
                layout.lanes.Add(W.Lane(home, "Coast Run", Magenta, false, (900f, -1700f, 70f), (600f, -1350f, 60f), (250f, -1100f, 50f)));
                layout.lanes.Add(W.Lane(home, "Outer Bend", Cyan, false, (-500f, -1400f, 70f), (-350f, -1200f, 60f), (-150f, -1050f, 50f)));
            }
            // Central lands along the causeway from either end of the strait; the plateau from the south-east, over the
            // island's shoulder; the southern hills from the east sea or back over the hills.
            layout.lanes.Add(W.Lane(central, "South Final", Gold, false, (-300f, -1700f, 60f), (-150f, -1250f, 50f)));
            layout.lanes.Add(W.Lane(central, "North Final", Gold, true, (300f, -1700f, 60f), (150f, -1250f, 50f)));
            layout.lanes.Add(W.Lane(north, "Plateau Final", Orange, false, (-400f, -1700f, 70f), (-150f, -1200f, 60f)));
            layout.lanes.Add(W.Lane(south, "East Final", Orange, false, (400f, -1600f, 60f), (150f, -1200f, 50f)));
            layout.lanes.Add(W.Lane(south, "West Final", Orange, true, (-300f, -1600f, 80f), (-100f, -1200f, 60f)));

            layout.bannerStands.Add(new BannerStandAnchor { airfield = west.name, distance = 150f });
            layout.bannerStands.Add(new BannerStandAnchor { airfield = east.name, distance = 150f });

            // Over the strait, up to 800 m: the plateau's edge to the north, the southern hills and one peak
            // rising through the south-west. The air start ring (800 m out) is above all of it.
            layout.arenas.Add(W.RamArena(new Vector2(100f, 200f), radius: 1000f, warning: 150f, ceiling: 800f,
                startRadius: 800f, startAltitude: 560f, patrolRadius: 500f, patrolAltitude: 450f));

            foreach (var (name, xz, agl) in new[]
            {
                ("Central Airfield", central.position, 250f), ("North Plateau", north.position, 250f),
                ("South Hills", south.position, 250f), ("The Strait", new Vector2(-300f, 300f), 250f),
                ("West Coast", new Vector2(-900f, 1000f), 200f), ("East Coast", new Vector2(1100f, -700f), 200f),
            })
                layout.fightAreas.Add(new FightAreaAnchor { name = name, point = new LayoutPoint(xz, agl) });

            string[] all = { west.name, east.name, central.name, north.name, south.name };
            layout.modes.Add(new ModeLayout { modeId = "Teams", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "Banner", airfields = all, bannerStands = true });
            layout.modes.Add(new ModeLayout { modeId = "Domination", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "RamRoyale", airfields = new string[0], arena = "RamRoyale" });
            return layout;
        }

        /// <summary>The strait first, so it wins over the halves either side of it.</summary>
        static MapRegions Regions()
        {
            var regions = new MapRegions { sea = "Sea", fallback = "Coast" };
            void Area(string name, RegionShape shape, Vector2 center, Vector2 size) =>
                regions.areas.Add(new RegionArea { name = name, shape = shape, center = center, size = size });
            Area("The Strait", RegionShape.Ellipse, new Vector2(100f, 250f), new Vector2(1000f, 300f));
            Area("North Plateau", RegionShape.Box, new Vector2(0f, 1700f), new Vector2(1000f, 1150f));
            Area("South Peaks", RegionShape.Box, new Vector2(300f, -1700f), new Vector2(1100f, 1100f));
            Area("Middle Hills", RegionShape.Box, new Vector2(100f, -275f), new Vector2(1100f, 325f));
            return regions;
        }

        /// <summary>
        /// The old Island's light and fog; banks over the strait, the plateau's shoulders and the sea off each home, each
        /// mirrored through the middle (the homes are too), lifted by the builder where a peak is under them.
        /// </summary>
        static MapSky Sky()
        {
            var sky = new MapSky { skybox = MapSkyBuilder.DaySky(new MapSky().fogColor), fogStart = 900f, fogEnd = 7000f, mirrorClouds = true };
            foreach (var (xz, radius, altitude, seed) in new[]
            {
                (new Vector2(-700f, 500f), 240f, 520f, 11),    // the strait's west mouth
                (new Vector2(-500f, 1500f), 250f, 600f, 23),   // the plateau's west shoulder (mirror: over the south hills)
                (new Vector2(-1500f, -1300f), 230f, 480f, 37), // the sea south of the west home
                (new Vector2(-1100f, 2300f), 220f, 650f, 41),  // off the plateau's north-west tip
                (new Vector2(-300f, -350f), 200f, 650f, 53),   // over the middle hills
            })
                sky.clouds.Add(new CloudBankAnchor(xz, radius, altitude, seed));
            return sky;
        }

        /// <summary>Open sea all round, with far islands from 1.5 km past the edge on the haze line.</summary>
        internal static MapOutskirts Outskirts() => new()
        {
            enabled = true, reach = 7000f, blend = 1000f, height = new Vector2(15f, 300f), featureSize = 1600f, ridged = 0.25f,
            land = 0.3f, landClearance = 1500f, treeBand = 0f,
        };
    }
}

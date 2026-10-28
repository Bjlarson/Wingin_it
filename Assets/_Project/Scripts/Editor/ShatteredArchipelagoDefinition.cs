using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using W = WingingIt.Editor.MapLayoutWriter;

namespace WingingIt.Editor
{
    /// <summary>
    /// Shattered Archipelago (island-map.md, Map List): a chain of steep islands running south-west to north-east, open
    /// sea on either side. No island has 800 m of level ground, so every airfield is a shore strip: a runway built up
    /// out of the shallows at sea level, the islands rising beside it. The homes sit out in the open sea north-west
    /// and south-east of the chain, so the chain itself is the line equally far from both, and the three capture
    /// fields stand along it. Ram Royale gets a 900 m circle over the inner islands, walled on its east by the Long
    /// Ridge, with the Spire inside. Written to the map's definition by
    /// <c>Winging It/Map/Write Shattered Archipelago Definition</c>; the terrain levels a pad under each field when built.
    /// </summary>
    public static class ShatteredArchipelagoDefinition
    {
        const string Path = "Assets/_Project/Maps/ShatteredArchipelago/ShatteredArchipelago.asset";

        /// <summary>A shore strip's runway height: clear of the waves, under the beach band's top.</summary>
        const float StripHeight = 8f;

        static readonly Color Magenta = new(0.95f, 0.35f, 0.9f), Cyan = new(0.2f, 0.85f, 1f), Lime = new(0.6f, 1f, 0.2f);
        static readonly Color Gold = new(1f, 0.8f, 0.2f), Orange = new(1f, 0.55f, 0.2f);

        [MenuItem("Winging It/Map/Write Shattered Archipelago Definition")]
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
                new LandmarkAnchor { kind = LandmarkKind.SummitRadar, xz = new Vector2(1100f, 1450f), searchHalfSize = new Vector2(450f, 250f), region = "Crown Island" },
            };
            map.sky = Sky();
            map.minimap = new MapMinimap();
            W.Save(map);
        }

        internal static MapLayout BuildLayout(MapDefinition map)
        {
            var ground = map.LoadHeightmap();
            var layout = new MapLayout();
            AirfieldAnchor Field(string name, int team, bool home, bool capturable, bool twoWay, Vector2 position, float yaw)
            {
                // Built out of the sea: never lower than a strip, however much of the pad was water.
                var field = W.Airfield(map, ground, name, team, home, capturable, twoWay, position, yaw);
                field.elevation = Mathf.Max(field.elevation, StripHeight);
                return field;
            }
            var west = Field("West Home Airfield", 0, true, false, false, new Vector2(-760f, 1010f), 53f);
            var east = Field("East Home Airfield", 1, true, false, false, new Vector2(760f, -1010f), 233f);
            var central = Field("Central Airfield", Airfield.Neutral, false, true, false, new Vector2(-150f, -100f), 345f);
            var western = Field("Western Isle Airfield", Airfield.Neutral, false, true, true, new Vector2(-950f, -700f), 150f);
            var ridge = Field("Long Ridge Airfield", Airfield.Neutral, false, true, false, new Vector2(770f, 580f), 135f);
            layout.airfields.AddRange(new[] { west, east, central, western, ridge });

            // Each home: in over the open sea behind it, round the near end of the chain, or along the far side of it.
            layout.lanes.Add(W.Lane(west, "West Sound", Lime, false, (601f, -1379f, 70f), (-2f, -900f, 50f)));
            layout.lanes.Add(W.Lane(west, "Inner Bend", Magenta, false, (863f, -450f, 80f), (372f, -769f, 60f), (46f, -886f, 50f)));
            layout.lanes.Add(W.Lane(west, "North Pass", Cyan, false, (536f, 861f, 70f), (474f, 31f, 60f), (262f, -849f, 55f)));
            layout.lanes.Add(W.Lane(east, "East Sound", Lime, false, (211f, -1148f, 70f), (-2f, -900f, 50f)));
            layout.lanes.Add(W.Lane(east, "Inner Bend", Magenta, false, (634f, 161f, 80f), (263f, -499f, 60f), (46f, -886f, 50f)));
            layout.lanes.Add(W.Lane(east, "South Pass", Cyan, false, (536f, 861f, 70f), (474f, 31f, 60f), (262f, -849f, 55f)));
            // The capture fields land from the sea: Central and the Long Ridge only one way (hills past the far end),
            // the Western Isle from either end.
            // Central's runway sits in a notch between two hills: its final comes in high over the sea.
            layout.lanes.Add(W.Lane(central, "South Final", Gold, false, (134f, -1433f, 140f), (0f, -1004f, 120f)));
            layout.lanes.Add(W.Lane(western, "North Final", Orange, false, (-329f, -1369f, 60f), (0f, -1000f, 50f)));
            layout.lanes.Add(W.Lane(western, "South Final", Orange, true, (411f, -988f, 60f), (33f, -943f, 50f)));
            layout.lanes.Add(W.Lane(ridge, "North Final", Orange, false, (-460f, -841f, 70f), (0f, -1000f, 50f)));

            layout.bannerStands.Add(new BannerStandAnchor { airfield = west.name, distance = 150f });
            layout.bannerStands.Add(new BannerStandAnchor { airfield = east.name, distance = 150f });

            // Over the inner islands, up to 750 m: the Long Ridge rises through its east edge, the Spire stands inside.
            // The air start ring (550 m out) is far above the islands (100 m at most).
            layout.arenas.Add(W.RamArena(new Vector2(-100f, 300f), radius: 900f, warning: 150f, ceiling: 750f,
                startRadius: 550f, startAltitude: 500f, patrolRadius: 450f, patrolAltitude: 450f));

            foreach (var (name, xz, agl) in new[]
            {
                ("Central Airfield", central.position, 250f), ("Western Isle", western.position, 200f),
                ("Long Ridge", ridge.position, 250f), ("Lagoon", new Vector2(0f, 150f), 300f),
                ("West Sound", new Vector2(-1200f, -100f), 200f), ("South Sound", new Vector2(300f, -900f), 200f),
            })
                layout.fightAreas.Add(new FightAreaAnchor { name = name, point = new LayoutPoint(xz, agl) });

            string[] all = { west.name, east.name, central.name, western.name, ridge.name };
            layout.modes.Add(new ModeLayout { modeId = "Teams", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "Banner", airfields = all, bannerStands = true });
            layout.modes.Add(new ModeLayout { modeId = "Domination", airfields = all });
            layout.modes.Add(new ModeLayout { modeId = "RamRoyale", airfields = new string[0], arena = "RamRoyale" });
            return layout;
        }

        /// <summary>Small islands first, so they win over the bigger areas round them.</summary>
        static MapRegions Regions()
        {
            var regions = new MapRegions { sea = "Sea", fallback = "Outer Rocks" };
            void Area(string name, RegionShape shape, Vector2 center, Vector2 size) =>
                regions.areas.Add(new RegionArea { name = name, shape = shape, center = center, size = size });
            Area("Lagoon Isle", RegionShape.Ellipse, new Vector2(-100f, -250f), new Vector2(350f, 350f));
            Area("Saddle Peak", RegionShape.Ellipse, new Vector2(-450f, -700f), new Vector2(350f, 350f));
            Area("Northern Isles", RegionShape.Box, new Vector2(-200f, 550f), new Vector2(600f, 350f));
            Area("Long Ridge", RegionShape.Ellipse, new Vector2(800f, 100f), new Vector2(700f, 350f));
            Area("Crown Island", RegionShape.Box, new Vector2(1250f, 1200f), new Vector2(650f, 550f));
            Area("Western Isle", RegionShape.Ellipse, new Vector2(-1450f, -850f), new Vector2(600f, 400f));
            return regions;
        }

        /// <summary>
        /// Closer fog than the open maps (a tight map) and banks over the sounds between the islands, each mirrored
        /// through the middle; the builder lifts any bank that would touch a peak.
        /// </summary>
        static MapSky Sky()
        {
            var sky = new MapSky { skybox = MapSkyBuilder.GreyboxSky(), fogStart = 800f, fogEnd = 6000f, mirrorClouds = true };
            foreach (var (xz, radius, altitude, seed) in new[]
            {
                (new Vector2(-400f, 1300f), 230f, 480f, 13),  // the north sound, ahead of the west home
                (new Vector2(-800f, 600f), 220f, 560f, 29),   // between the west home and the Northern Isles
                (new Vector2(-1000f, 1250f), 200f, 500f, 31), // behind the west home
                (new Vector2(0f, 1000f), 200f, 520f, 47),     // the north channel
                (new Vector2(-1400f, 50f), 220f, 600f, 59),   // the West Sound
            })
                sky.clouds.Add(new CloudBankAnchor(xz, radius, altitude, seed));
            return sky;
        }
    }
}

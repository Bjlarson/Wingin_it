using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Maps;

namespace WingingIt.Tests
{
    public class MapLayoutTests
    {
        static MapLayout Sample()
        {
            var l = new MapLayout();
            l.airfields.Add(new AirfieldAnchor { name = "A", team = 0, home = true });
            l.airfields.Add(new AirfieldAnchor { name = "B", capturable = true });
            l.lanes.Add(new LaneAnchor { airfield = "A", name = "L1" });
            l.lanes.Add(new LaneAnchor { airfield = "B", name = "L2" });
            l.arenas.Add(new ArenaAnchor { modeId = "Ram" });
            l.modes.Add(new ModeLayout { modeId = "Teams", airfields = new[] { "A", "B" } });
            l.modes.Add(new ModeLayout { modeId = "Ram", airfields = new string[0], arena = "Ram" });
            return l;
        }

        [Test]
        public void ModeUsesOnlyItsAirfieldsAndLanes()
        {
            var l = Sample();
            l.modes[0].airfields = new[] { "A" };
            Assert.AreEqual(new[] { "A" }, l.AirfieldsFor("Teams").Select(a => a.name).ToArray());
            Assert.AreEqual(new[] { "L1" }, l.LanesFor("Teams").Select(x => x.name).ToArray());
            Assert.IsEmpty(l.AirfieldsFor("Ram"));
            Assert.IsEmpty(l.AirfieldsFor("Unknown"));
        }

        [Test]
        public void ValidateFlagsUnknownNames()
        {
            var l = Sample();
            Assert.IsEmpty(l.Validate());
            l.lanes.Add(new LaneAnchor { airfield = "Z", name = "bad" });
            l.modes[1].arena = "Nope";
            Assert.AreEqual(2, l.Validate().Count);
        }

        [Test]
        public void IslandLayoutIsSound()
        {
            var def = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Maps/Island/Island.asset");
            Assert.IsNotNull(def);
            if (def.layout.airfields.Count == 0)
                Assert.Ignore("Run Winging It/Map/Write Island Definition first.");
            Assert.IsEmpty(def.layout.Validate());
            Assert.AreEqual(5, def.layout.AirfieldsFor("Teams").Count());
            Assert.AreEqual(12, def.layout.LanesFor("Teams").Count());
            // Ram Royale keeps only the home fields (closed by its arena): their hangars are the menu's backdrop.
            Assert.IsTrue(def.layout.AirfieldsFor("RamRoyale").All(a => a.home));
            Assert.AreEqual(2, def.layout.AirfieldsFor("RamRoyale").Count());
            Assert.IsNotNull(def.layout.Arena("RamRoyale"));
            Assert.IsTrue(def.layout.airfields.All(a => Mathf.Approximately(a.elevation, 6f)), "the Island's fields sit on their 6 m pads");
        }

        /// <summary>The new maps whose layouts are written by hand (Write Broad Reach / Shattered Archipelago Definition).</summary>
        static readonly string[] NewMaps = { "BroadReach", "ShatteredArchipelago" };

        [TestCaseSource(nameof(NewMaps))]
        public void NewMapLayoutIsSound(string mapName)
        {
            var def = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>($"Assets/_Project/Maps/{mapName}/{mapName}.asset");
            Assert.IsNotNull(def);
            if (def.layout.airfields.Count == 0)
                Assert.Ignore($"Run the Write {def.displayName} Definition menu first.");
            var layout = def.layout;
            Assert.IsEmpty(layout.Validate());
            foreach (var mode in new[] { "Teams", "Banner", "Domination", "RamRoyale" })
                Assert.IsNotNull(layout.Mode(mode), $"{mode} has a layout");
            Assert.AreEqual(5, layout.AirfieldsFor("Teams").Count());
            Assert.AreEqual(2, layout.bannerStands.Count);
            Assert.IsEmpty(layout.AirfieldsFor("RamRoyale"), "Ram Royale plays its arena alone");
            Assert.IsNotNull(layout.Arena("RamRoyale"));

            // Each capture field is as far from one home as from the other (within 3%).
            var homes = layout.airfields.Where(a => a.home).OrderBy(a => a.team).ToArray();
            Assert.AreEqual(2, homes.Length);
            var capture = layout.airfields.Where(a => a.capturable).ToArray();
            Assert.AreEqual(3, capture.Length);
            foreach (var f in capture)
            {
                float a = Vector2.Distance(f.position, homes[0].position), b = Vector2.Distance(f.position, homes[1].position);
                Assert.Less(Mathf.Abs(a - b) / Mathf.Max(a, b), 0.03f, $"{f.name}: {a:F0} m vs {b:F0} m from the homes");
            }

            // Everything sits well inside the flight boundary.
            foreach (var f in layout.airfields)
            {
                Assert.Less(f.position.magnitude + 600f, def.boundaryRadius, f.name);
                Assert.Greater(f.elevation, def.seaLevel + 5f, $"{f.name}'s runway stands clear of the sea");
            }
            foreach (var l in layout.lanes)
                foreach (var p in l.points)
                    Assert.Less(p.xz.magnitude, def.boundaryRadius - 300f, $"{l.airfield} {l.name}");
        }

        [Test]
        public void PiecesFor_AMode_AreItsAirfieldsStandsAndArena()
        {
            var l = Sample();
            l.bannerStands.Add(new BannerStandAnchor { airfield = "A" });
            l.bannerStands.Add(new BannerStandAnchor { airfield = "B" });
            l.modes.Add(new ModeLayout { modeId = "Banner", airfields = new[] { "A" }, bannerStands = true });

            var teams = l.PiecesFor("Teams");
            Assert.AreEqual(new[] { "A", "B" }, teams.airfields.Select(a => a.name).ToArray());
            Assert.IsEmpty(teams.bannerStands, "Teams has no stands");
            Assert.IsEmpty(teams.arenas);

            var banner = l.PiecesFor("Banner");
            Assert.AreEqual(new[] { "A" }, banner.bannerStands.Select(s => s.airfield).ToArray(), "only stands at the mode's own fields");

            var ram = l.PiecesFor("Ram");
            Assert.IsEmpty(ram.airfields);
            Assert.AreEqual("Ram", ram.arenas.Single().modeId);
        }

        [Test]
        public void FightAreasFor_AMode_AreItsOwn_ElseTheMaps()
        {
            var l = Sample();
            l.fightAreas.Add(new FightAreaAnchor { name = "Map" });
            l.modes[1].fightAreas = new[] { new FightAreaAnchor { name = "Ram" } };
            Assert.AreEqual(new[] { "Map" }, l.FightAreasFor("Teams").Select(a => a.name).ToArray(), "no areas of its own");
            Assert.AreEqual(new[] { "Ram" }, l.FightAreasFor("Ram").Select(a => a.name).ToArray());
            Assert.AreEqual(new[] { "Map" }, l.FightAreasFor(null).Select(a => a.name).ToArray(), "free flight");
            Assert.AreEqual(new[] { "Map" }, l.FightAreasFor("Unknown").Select(a => a.name).ToArray());
        }

        [Test]
        public void PiecesFor_NoModeOrAnUnknownOne_IsEveryPiece()
        {
            var l = Sample();
            l.bannerStands.Add(new BannerStandAnchor { airfield = "A" });
            foreach (var pieces in new[] { l.PiecesFor(null), l.PiecesFor("Unknown") })
            {
                Assert.AreEqual(2, pieces.airfields.Count);
                Assert.AreEqual(1, pieces.bannerStands.Count);
                Assert.AreEqual(1, pieces.arenas.Count);
            }
        }
        [Test]
        public void RegionAt_FirstAreaWins_AndLandOnlyAreasSkipTheSea()
        {
            var r = new MapRegions { sea = "Sea", fallback = "Grass" };
            r.areas.Add(new RegionArea { name = "Bay", shape = RegionShape.Ellipse, center = new Vector2(0f, -500f), size = new Vector2(200f, 100f), landOnly = false });
            r.areas.Add(new RegionArea { name = "Town", shape = RegionShape.Ellipse, center = Vector2.zero, size = new Vector2(300f, 300f) });
            r.areas.Add(new RegionArea { name = "East", shape = RegionShape.Box, center = new Vector2(1000f, 0f), size = new Vector2(500f, 1000f) });
            Assert.AreEqual("Town", r.RegionAt(new Vector2(100f, 100f)));
            Assert.AreEqual("Sea", r.RegionAt(new Vector2(100f, 100f), 0f), "a canal in town is sea");
            Assert.AreEqual("Bay", r.RegionAt(new Vector2(150f, -500f), 0f), "an area that counts over water");
            Assert.AreEqual("Grass", r.RegionAt(new Vector2(150f, -580f)), "outside the ellipse's short radius");
            Assert.AreEqual("East", r.RegionAt(new Vector2(1499f, -999f)));
            Assert.AreEqual("Grass", r.RegionAt(new Vector2(1501f, 0f)));
        }

        [Test]
        public void MirroredClouds_AreBuiltInPairsThroughTheCenter()
        {
            var sky = new MapSky();
            sky.clouds.Add(new CloudBankAnchor(new Vector2(-400f, 600f), 200f, 450f, 1));
            Assert.AreEqual(new[] { new Vector2(-400f, 600f), new Vector2(400f, -600f) }, sky.AllClouds().Select(c => c.xz).ToArray());
            sky.mirrorClouds = false;
            Assert.AreEqual(1, sky.AllClouds().Count());
        }

        [Test]
        public void IslandRegionsAndSkyAreWritten()
        {
            var def = UnityEditor.AssetDatabase.LoadAssetAtPath<MapDefinition>("Assets/_Project/Maps/Island/Island.asset");
            Assert.IsNotNull(def);
            if (def.regions.areas.Count == 0)
                Assert.Ignore("Run Winging It/Map/Write Island Definition first.");
            var r = def.regions;
            Assert.AreEqual("City", r.RegionAt(new Vector2(0f, 250f)));
            Assert.AreEqual("Sea", r.RegionAt(new Vector2(0f, 600f), 0f), "the canal");
            Assert.AreEqual("Mountains", r.RegionAt(new Vector2(-900f, 450f)));
            Assert.AreEqual("Desert", r.RegionAt(new Vector2(1500f, 0f)));
            Assert.AreEqual("LighthouseSea", r.RegionAt(new Vector2(0f, -1950f), 0f));
            Assert.AreEqual("Grassland", r.RegionAt(new Vector2(-1500f, -300f)));
            Assert.AreEqual(12, def.sky.AllClouds().Count(), "six banks, each mirrored");
            Assert.IsNotNull(def.sky.skybox);
            Assert.Greater(def.sky.fogEnd, def.sky.fogStart);
            Assert.Greater(def.minimap.margin, 1f);
        }
    }
}

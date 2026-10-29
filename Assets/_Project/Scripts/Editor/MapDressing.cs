using System.Collections.Generic;
using UnityEngine;
using WingingIt.Maps;
using WingingIt.World;
using D = WingingIt.Editor.MapDresser;

namespace WingingIt.Editor
{
    /// <summary>
    /// Each shipped heightmap map's own pack, placed by rules on its built terrain (<see cref="MapDresser"/>), so the
    /// maps read apart: the Island's fishing villages, boats, idols and jungle (Tropics, plus the landmarks pack's
    /// lighthouse); Broad Reach's farms, fields, windmills and balloons (Prairie); the Archipelago's sea stacks, arches,
    /// wrecks and white cliff villages (Isles, plus the Tropics yacht); the Mountain Range's ski villages, cable car,
    /// frozen ponds and ice (Alpine). Nothing goes on a pad or under an approach lane; a map whose pack hasn't been
    /// generated is left as it was.
    /// </summary>
    internal static class MapDressing
    {
        public static void Dress(MapDefinition map, Transform world, Terrain terrain)
        {
            switch (map.name)
            {
                case "IslandV2": Island(map, world, terrain); break;
                case "BroadReach": BroadReach(map, world, terrain); break;
                case "ShatteredArchipelago": Archipelago(map, world, terrain); break;
                case "MountainRange": Mountains(map, world, terrain); break;
            }
        }

        static readonly string[] Crops = { "CropWheat", "CropWheat", "CropGreen", "CropGreen", "CropPlowed" };

        // ------------------------------------------------------------------------------------------ The Island

        static void Island(MapDefinition map, Transform world, Terrain terrain)
        {
            var pack = MapPackModels.Load("Tropics");
            if (pack == null)
                return;
            var d = new D(map, world, terrain, "Tropics");

            // Fishing villages on the low coast: a long house, huts round it, stilt huts and a jetty on the shore.
            foreach (var v in d.Sites(7, 650f, p =>
            {
                float h = d.AboveSea(p);
                return h > 1.5f && h < 160f && d.Slope(p) < 22f && d.Roughness(p, 28f) < 14f && d.NearWater(p, 320f, 2f) && d.Clear(p, 40f, false);
            }))
                FishingVillage(d, pack, v);
            // Hill villages on the plateau and among the southern hills: a long house and huts round a clearing.
            foreach (var v in d.Sites(5, 800f, p =>
            {
                float h = d.AboveSea(p);
                return h > 60f && h < 450f && d.Slope(p) < 14f && d.Roughness(p, 30f) < 10f && d.Clear(p, 40f, false);
            }))
                Huts(d, pack, v, 5);

            // Over-water villages in the shallows under the cliffs: stilt huts either side of a jetty run out to sea.
            foreach (var v in d.Sites(5, 700f, p => d.AboveSea(p) is > -4f and < -0.5f && d.NearLand(p, 160f) && d.Clear(p, 45f, false)))
                WaterVillage(d, pack, v);

            // Yachts off the coasts, in deep water but in sight of land.
            foreach (var p in d.Sites(10, 450f, p => d.AboveSea(p) < -8f && d.NearLand(p, 900f) && d.Clear(p, 25f, false)))
                d.Float(pack, "Sailboat", p, d.Range(0f, 360f), 0.5f, 4f);

            // Lighthouses on two headlands, far apart (the landmarks pack's, without a landmark beacon).
            var lighthouses = LandmarkModels.Load();
            if (lighthouses != null)
                foreach (var p in d.Sites(2, 2500f, p =>
                {
                    float h = d.AboveSea(p);
                    return h > 8f && h < 220f && d.Slope(p) < 26f && d.Roughness(p, 8f) < 6f && d.NearWater(p, 200f, 3f) && d.Clear(p, 25f, true);
                }))
                {
                    var root = new GameObject("Lighthouse").transform;
                    root.SetParent(d.Group, false);
                    root.position = new Vector3(p.x, d.Lowest(p, 8f), p.y);
                    root.rotation = Quaternion.Euler(0f, D.YawToward(d.Downhill(p, 100f)), 0f);
                    lighthouses.Body("Lighthouse", root);
                    lighthouses.LighthouseLens(root);
                    lighthouses.LighthouseLamp(root);
                    d.Claim(p, 14f);
                    d.Count("Lighthouse");
                }

            // Stone idols on cliff edges and the plateau's rim, staring out to sea.
            foreach (var p in d.Sites(7, 600f, p =>
            {
                float h = d.AboveSea(p);
                if (h < 40f || h > 700f || d.Slope(p) > 16f || d.Roughness(p, 8f) > 4f || !d.Clear(p, 12f, false))
                    return false;
                var down = d.Downhill(p, 90f);
                return d.Ground(p) - d.Ground(p + down * 90f) > 25f;
            }))
                d.Put(pack, "StoneIdol", p, D.YawToward(d.Downhill(p, 90f)), d.Range(1.2f, 1.6f), solid: true, sink: 1.5f);

            // Bamboo lookouts on the hills.
            foreach (var p in d.Sites(4, 900f, p =>
            {
                float h = d.AboveSea(p);
                return h > 30f && h < 500f && d.Slope(p) < 12f && d.Roughness(p, 6f) < 2.5f && d.Clear(p, 10f, false);
            }))
                d.Put(pack, "Lookout", p, d.Range(0f, 360f), solid: true, sink: 0.5f);

            // Jungle: groves of broad canopy trees, bananas and flowering bushes among the nature pack's woods.
            string[] jungle = { "TreeJungle", "TreeJungle", "TreeBanana", "BushFlower" };
            foreach (var c in d.Sites(45, 260f, p => d.AboveSea(p) is > 5f and < 350f && d.Slope(p) < 24f))
                d.Grove(pack, jungle, c, 90f, 28, p => d.AboveSea(p) is > 3f and < 400f && d.Slope(p) < 28f);
            d.Finish();
        }

        static void FishingVillage(D d, MapPackModels pack, Vector2 v)
        {
            var sea = d.Downhill(v, 120f);
            var across = new Vector2(-sea.y, sea.x);
            Huts(d, pack, v, 6);
            // Stilt huts and the jetty where the beach meets the water (only a low village has a beach to reach).
            if (d.AboveSea(v) > 35f)
                return;
            Vector2? shore = null;
            for (float s = 20f; s < 220f; s += 6f)
                if (d.AboveSea(v + sea * s) < 0.5f)
                {
                    shore = v + sea * s;
                    break;
                }
            if (shore == null)
                return;
            foreach (float lateral in new[] { -34f, 34f, -60f })
            {
                var p = shore.Value + across * lateral + sea * d.Range(2f, 10f);
                if (d.AboveSea(p) is > -2.5f and < 1f && d.Clear(p, 7f, false))
                    d.Put(pack, "StiltHut", p, D.YawToward(-sea), y: Mathf.Max(d.Ground(p), d.Map.seaLevel - 0.2f));
            }
            var end = shore.Value + sea * 44f;
            if (d.AboveSea(end) < -1f && d.Clear(shore.Value + sea * 20f, 22f, false))
            {
                d.Put(pack, "Jetty", shore.Value, D.YawToward(sea), y: d.Map.seaLevel);
                foreach (float lateral in new[] { -9f, 10f })
                {
                    var p = end + across * lateral - sea * 8f;
                    if (d.AboveSea(p) < -1f)
                        d.Float(pack, "Outrigger", p, D.YawToward(sea) + d.Range(-25f, 25f), 0.25f, 4f);
                }
            }
        }

        static void WaterVillage(D d, MapPackModels pack, Vector2 v)
        {
            // Out from the land: the opposite of the nearest ground above the sea.
            Vector2 land = Vector2.zero;
            foreach (var p in D.Ring(v, 120f, 16))
                if (d.AboveSea(p) > 0f)
                    land += p - v;
            var sea = land.sqrMagnitude > 0f ? -land.normalized : d.Downhill(v, 120f);
            var across = new Vector2(-sea.y, sea.x);
            float yaw = D.YawToward(sea);
            d.Put(pack, "Jetty", v, yaw, y: d.Map.seaLevel);
            foreach (float along in new[] { 8f, 20f, 32f })
                foreach (float side in new[] { -9f, 9f })
                {
                    var p = v + sea * along + across * side;
                    if (d.AboveSea(p) < -0.3f && d.Rng.NextDouble() < 0.8)
                        d.Put(pack, "StiltHut", p, yaw + (side < 0f ? 90f : -90f), y: d.Map.seaLevel - 0.2f);
                }
            foreach (float lateral in new[] { -10f, 11f })
            {
                var p = v + sea * 50f + across * lateral;
                if (d.AboveSea(p) < -1f)
                    d.Float(pack, "Outrigger", p, yaw + d.Range(-30f, 30f), 0.25f, 4f);
            }
        }

        /// <summary>A long house (most villages) facing downhill, and up to <paramref name="count"/> huts round it facing in.</summary>
        static void Huts(D d, MapPackModels pack, Vector2 v, int count)
        {
            if (d.Rng.NextDouble() < 0.7)
                d.Put(pack, "LongHouse", v, D.YawToward(d.Downhill(v, 120f)), sink: 0.3f);
            for (int i = 0, huts = 0; i < count * 3 && huts < count; i++)
            {
                float a = d.Range(0f, Mathf.PI * 2f), r = d.Range(22f, 50f);
                var p = v + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (d.AboveSea(p) > 1f && d.Slope(p) < 22f && d.Roughness(p, 6f) < 3.5f && d.Clear(p, 6f, false))
                {
                    d.Put(pack, "BeachHut", p, D.YawToward(v - p), sink: 0.2f);
                    huts++;
                }
            }
            d.Grove(pack, new[] { "TreeBanana", "BushFlower", "BushFlower" }, v, 75f, 18, p => d.AboveSea(p) > 1.5f && d.Slope(p) < 25f);
        }

        // ------------------------------------------------------------------------------------------ Broad Reach

        static void BroadReach(MapDefinition map, Transform world, Terrain terrain)
        {
            var pack = MapPackModels.Load("Prairie");
            if (pack == null)
                return;
            var d = new D(map, world, terrain, "Prairie");

            // Homesteads on the level ground, each with its fields round it.
            foreach (var f in d.Sites(16, 650f, p => d.Slope(p) < 5f && d.Roughness(p, 45f) < 4f && d.Clear(p, 75f, false)))
                Farm(d, pack, f);

            // Hamlets: a church, a water tower and a few houses round a green.
            foreach (var h in d.Sites(3, 1600f, p => d.Slope(p) < 6f && d.Roughness(p, 50f) < 5f && d.Clear(p, 95f, false)))
            {
                float yaw = d.Range(0f, 360f);
                d.Put(pack, "Church", h, yaw, solid: true, sink: 0.3f);
                var tower = h + D.Rotate(new Vector2(34f, 10f), yaw);
                if (d.Clear(tower, 7f, true))
                    d.Put(pack, "WaterTower", tower, yaw, solid: true);
                for (int i = 0; i < 6; i++)
                {
                    var p = h + D.Rotate(new Vector2(Mathf.Cos(i * 1.05f), Mathf.Sin(i * 1.05f)) * 52f, yaw);
                    if (d.Slope(p) < 10f && d.Clear(p, 9f, false))
                        d.Put(pack, "Farmhouse", p, D.YawToward(h - p) - 90f, solid: true, sink: 0.3f);
                }
                d.Grove(pack, new[] { "TreePoplar" }, h, 90f, 14, p => d.Slope(p) < 12f);
            }

            // Windmills on the low hills' shoulders.
            foreach (var p in d.Sites(6, 900f, p => d.Slope(p) < 9f && d.Roughness(p, 10f) < 2.5f && d.Clear(p, 16f, true)))
            {
                var mill = d.Put(pack, "Windmill", p, d.Range(0f, 360f), solid: true, sink: 0.4f);
                d.Rotor(pack, "WindmillSails", "Windmill", mill, d.Range(28f, 40f));
            }

            // The patchwork between the farms: blocks of fields with hay and the odd herd.
            foreach (var c in d.Sites(34, 420f, p => d.Slope(p) < 6f && d.Roughness(p, 40f) < 4f && d.Clear(p, 60f, false)))
            {
                float yaw = d.Range(0f, 90f);
                int w = d.Rng.Next(2, 4), l = d.Rng.Next(2, 4);
                Fields(d, pack, c, yaw, -w * 0.5f + 0.5f, w * 0.5f - 0.5f, -l * 0.5f + 0.5f, l * 0.5f - 0.5f);
                if (d.Rng.NextDouble() < 0.4)
                    d.Grove(pack, new[] { "HayBale", "HayBale", "HayStack" }, c + D.Rotate(new Vector2(0f, l * 21f + 10f), yaw), 14f, 4, p => d.Slope(p) < 10f);
            }
            foreach (var c in d.Sites(16, 500f, p => d.Slope(p) < 8f && d.Clear(p, 30f, false)))
                d.Grove(pack, new[] { "Cow" }, c, 28f, 7, p => d.Slope(p) < 10f, 0.95f, 1.05f);

            // Hot-air balloons drifting over the valley, clear of the lanes and well above the ground.
            foreach (var p in d.Sites(7, 1300f, p => d.Clear(p, 260f, true, ignoreFootprints: true)))
            {
                var go = pack.Place("HotAirBalloon", d.Group, new Vector3(p.x, d.Ground(p) + d.Range(180f, 420f), p.y),
                    Quaternion.Euler(0f, d.Range(0f, 360f), 0f), Vector3.one * 1.4f, null);
                go.isStatic = false;
                go.AddComponent<Bobber>().Configure(3f, 1.5f, d.Range(8f, 12f), d.Range(90f, 160f), d.Range(300f, 480f));
            }
            d.Finish();
        }

        static void Farm(D d, MapPackModels pack, Vector2 f)
        {
            float yaw = d.Range(0f, 360f);
            Vector2 L(float x, float z) => f + D.Rotate(new Vector2(x, z), yaw);
            d.Put(pack, "Barn", L(4f, 14f), yaw, solid: true, sink: 0.3f);
            d.Put(pack, "Farmhouse", L(-26f, -8f), yaw + 90f, solid: true, sink: 0.3f);
            d.Put(pack, "Silo", L(22f, 26f), yaw, solid: true, sink: 0.5f);
            if (d.Rng.NextDouble() < 0.5)
                d.Put(pack, "Silo", L(22f, 37f), yaw, solid: true, sink: 0.5f);
            var pump = d.Put(pack, "Windpump", L(26f, -16f), yaw + d.Range(-40f, 40f), solid: true);
            d.Rotor(pack, "WindpumpRotor", "Windpump", pump, d.Range(90f, 150f));
            d.Put(pack, "HayStack", L(-6f, -18f), d.Range(0f, 360f));
            d.Grove(pack, new[] { "HayBale" }, L(10f, -26f), 9f, 3, p => true);
            // Fence round the yard, a gate gap in the near side.
            for (int side = 0; side < 4; side++)
                for (int k = 0; k < 7; k++)
                {
                    if (side == 0 && k == 3)
                        continue;
                    float s = -36f + k * 12f;
                    var local = side switch { 0 => new Vector2(s, -42f), 1 => new Vector2(42f, s), 2 => new Vector2(s, 42f), _ => new Vector2(-42f, s) };
                    var p = L(local.x, local.y);
                    if (d.Slope(p) < 15f)
                        d.Put(pack, "Fence", p, yaw + (side % 2 == 0 ? 0f : 90f), y: d.Ground(p) - 0.1f);
                }
            d.Grove(pack, new[] { "Cow" }, L(0f, -64f), 16f, 5, p => d.Slope(p) < 10f, 0.95f, 1.05f);
            // Fields beyond the yard, front and back, and a windbreak of poplars down one side.
            Fields(d, pack, f, yaw, -3f, 3f, 2f, 3.5f);
            Fields(d, pack, f, yaw, -2.5f, 2.5f, -3.5f, -2.5f);
            for (float z = -100f; z <= 150f; z += 10f)
            {
                var p = L(-150f, z);
                if (d.Slope(p) < 14f && d.Clear(p, 3f, false, ignoreFootprints: true))
                    d.Tree(pack, "TreePoplar", p, 0.9f, 1.15f);
            }
        }

        /// <summary>A block of 42 m field tiles in the frame of (<paramref name="c"/>, <paramref name="yaw"/>), tile indices i0..i1 by j0..j1.</summary>
        static void Fields(D d, MapPackModels pack, Vector2 c, float yaw, float i0, float i1, float j0, float j1)
        {
            for (float j = j0; j <= j1 + 0.01f; j += 1f)
            for (float i = i0; i <= i1 + 0.01f; i += 1f)
            {
                var p = c + D.Rotate(new Vector2(i * 42f, j * 42f), yaw);
                if (d.Slope(p) < 9f && d.Roughness(p, 20f) < 5f && d.Clear(p, 22f, false))
                    d.Lay(pack, d.Pick(Crops), p, yaw + (d.Rng.NextDouble() < 0.5 ? 0f : 90f), 0.15f);
            }
        }

        // ------------------------------------------------------------------------------------------ The Archipelago

        static void Archipelago(MapDefinition map, Transform world, Terrain terrain)
        {
            var pack = MapPackModels.Load("Isles");
            if (pack == null)
                return;
            var d = new D(map, world, terrain, "Isles");

            // Arches across the sounds: both feet in the water (or on the shore), open water under the span.
            int arches = 0;
            foreach (var c in d.Sites(40, 600f, p => d.AboveSea(p) < -4f && d.Clear(p, 95f, true)))
            {
                if (arches >= 3)
                    break;
                for (float yaw = 0f; yaw < 180f; yaw += 30f)
                {
                    var leg = D.Rotate(new Vector2(0f, 62f), yaw);
                    float a = d.AboveSea(c + leg), b = d.AboveSea(c - leg);
                    if (a is > -28f and < 2f && b is > -28f and < 2f)
                    {
                        d.Put(pack, "SeaArch", c, yaw, solid: true, y: map.seaLevel);
                        arches++;
                        break;
                    }
                }
            }

            // Sea stacks round the islands and out in the sounds: the obstacles to thread.
            string[] stacks = { "SeaStackA", "SeaStackA", "SeaStackB", "SeaStackC", "SeaStackC" };
            foreach (var p in d.Sites(36, 130f, p => d.AboveSea(p) is > -24f and < -3f && d.Clear(p, 26f, true)))
                d.Put(pack, d.Pick(stacks), p, d.Range(0f, 360f), d.Range(0.85f, 1.3f), solid: true, y: map.seaLevel);

            foreach (var p in d.Sites(3, 800f, p => d.AboveSea(p) is > -6f and < -1.5f && d.Clear(p, 25f, false)))
                d.Put(pack, "Shipwreck", p, d.Range(0f, 360f), y: map.seaLevel);

            // White villages on the slopes above the sea, each with its chapel, windmill and olive groves.
            var tropics = MapPackModels.Load("Tropics");
            foreach (var v in d.Sites(7, 550f, p =>
            {
                float h = d.AboveSea(p);
                return h > 10f && h < 260f && d.Slope(p) < 32f && d.NearWater(p, 320f, 3f) && d.Clear(p, 35f, false);
            }))
                CliffVillage(d, pack, v);

            foreach (var p in d.Sites(5, 700f, p =>
            {
                float h = d.AboveSea(p);
                return h > 15f && h < 320f && d.Slope(p) < 22f && d.Roughness(p, 7f) < 4f && d.NearWater(p, 260f, 3f) && d.Clear(p, 10f, false);
            }))
                d.Put(pack, "WatchtowerRuin", p, d.Range(0f, 360f), solid: true, sink: 1f);

            if (tropics != null)
                foreach (var p in d.Sites(9, 380f, p => d.AboveSea(p) < -10f && d.Clear(p, 25f, false)))
                    d.Float(tropics, "Sailboat", p, d.Range(0f, 360f), 0.5f, 4f);

            foreach (var c in d.Sites(70, 130f, p => d.AboveSea(p) is > 4f and < 300f && d.Slope(p) < 30f))
                d.Grove(pack, new[] { "TreeOlive", "TreeOlive", "TreeCypress" }, c, 60f, 14, p => d.AboveSea(p) > 3f && d.Slope(p) < 34f);
            d.Finish();
        }

        static void CliffVillage(D d, MapPackModels pack, Vector2 v)
        {
            // Houses step down the slope like Santorini's: each sits on the lowest ground under it, its uphill side
            // dug into the hill.
            var sea = d.Downhill(v, 120f);
            d.Put(pack, "Chapel", v, D.YawToward(sea), solid: true, sink: 0.5f);
            for (int i = 0, houses = 0; i < 18 && houses < 9; i++)
            {
                float a = d.Range(0f, Mathf.PI * 2f), r = d.Range(16f, 60f);
                var p = v + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (d.AboveSea(p) > 4f && d.Slope(p) < 36f && d.Roughness(p, 5f) < 7f && d.Clear(p, 7f, false))
                {
                    d.Put(pack, d.Rng.NextDouble() < 0.55 ? "CliffHouseA" : "CliffHouseB", p, D.YawToward(d.Downhill(p, 40f)),
                        solid: true, sink: 0.3f);
                    houses++;
                }
            }
            // The mill on the highest open spot nearby.
            Vector2? best = null;
            float top = float.MinValue;
            foreach (var p in D.Ring(v, 110f, 12))
                if (d.Ground(p) > top && d.Slope(p) < 22f && d.Roughness(p, 6f) < 4f && d.Clear(p, 10f, false))
                {
                    top = d.Ground(p);
                    best = p;
                }
            if (best != null)
            {
                var mill = d.Put(pack, "IslesMill", best.Value, D.YawToward(sea), solid: true, sink: 0.4f);
                d.Rotor(pack, "IslesMillSails", "IslesMill", mill, d.Range(20f, 30f));
            }
            for (float s = 30f; s < 260f; s += 8f)
            {
                var p = v + sea * s;
                if (d.AboveSea(p) < -2f)
                {
                    foreach (float lateral in new[] { -12f, 14f })
                    {
                        var q = p + new Vector2(-sea.y, sea.x) * lateral + sea * 10f;
                        if (d.AboveSea(q) < -1.5f && d.Clear(q, 6f, false))
                            d.Float(pack, "FishingBoat", q, d.Range(0f, 360f), 0.25f, 4f);
                    }
                    break;
                }
            }
            d.Grove(pack, new[] { "TreeCypress", "TreeOlive" }, v, 80f, 12, p => d.AboveSea(p) > 3f && d.Slope(p) < 28f);
        }

        // ------------------------------------------------------------------------------------------ The Mountain Range

        static void Mountains(MapDefinition map, Transform world, Terrain terrain)
        {
            var pack = MapPackModels.Load("Alpine");
            if (pack == null)
                return;
            var d = new D(map, world, terrain, "Alpine");

            var villages = d.Sites(4, 800f, p => d.AboveSea(p) < 780f && d.Slope(p) < 14f && d.Roughness(p, 40f) < 14f && d.Clear(p, 70f, false));
            foreach (var v in villages)
                SkiVillage(d, pack, v);

            // One cable car, from a village (or any gentle spot) up to a shoulder high above it.
            var starts = new List<Vector2>(villages);
            starts.AddRange(d.Sites(30, 300f, p => d.Slope(p) < 16f && d.Roughness(p, 12f) < 7f && d.Clear(p, 14f, true)));
            CableCarLine(d, pack, starts, villages.Count);

            foreach (var p in d.Sites(4, 700f, p => d.Slope(p) < 7f && d.Roughness(p, 36f) < 7f && d.Clear(p, 40f, false)))
                d.Lay(pack, "FrozenPond", p, d.Range(0f, 360f), 0.25f);

            foreach (var p in d.Sites(3, 1000f, p => d.Slope(p) < 12f && d.Roughness(p, 7f) < 3f && d.Clear(p, 10f, true)))
                d.Put(pack, "RangerTower", p, d.Range(0f, 360f), solid: true, sink: 0.5f);

            // Ice on the high ridges, tall firs and winter birches in the valleys' woods.
            foreach (var p in d.Sites(40, 160f, p => d.AboveSea(p) > 780f && d.Slope(p) < 32f && d.Clear(p, 5f, false)))
                d.Tree(pack, "IceShards", p, 1.0f, 2.2f);
            foreach (var c in d.Sites(40, 220f, p => d.AboveSea(p) < 820f && d.Slope(p) < 28f))
                d.Grove(pack, new[] { "TreeFirTall", "TreeFirTall", "TreeBirchWinter" }, c, 70f, 18,
                    p => d.AboveSea(p) < 840f && d.Slope(p) < 30f);
            d.Finish();
        }

        static void SkiVillage(D d, MapPackModels pack, Vector2 v)
        {
            var view = d.Downhill(v, 150f);
            d.Put(pack, "Chalet", v, D.YawToward(view), solid: true, sink: 0.5f);
            for (int i = 0, cabins = 0; i < 16 && cabins < 6; i++)
            {
                float a = d.Range(0f, Mathf.PI * 2f), r = d.Range(28f, 70f);
                var p = v + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                if (d.Slope(p) < 18f && d.Roughness(p, 6f) < 3.5f && d.Clear(p, 7f, false))
                {
                    d.Put(pack, "LogCabin", p, D.YawToward(v - p), solid: true, sink: 0.3f);
                    cabins++;
                }
            }
            var sign = v + view * 22f + new Vector2(-view.y, view.x) * 8f;
            if (d.Clear(sign, 2f, false))
                d.Put(pack, "Signpost", sign, D.YawToward(view));
            d.Grove(pack, new[] { "Snowman" }, v + view * 30f, 18f, 2, p => d.Slope(p) < 15f, 0.9f, 1.1f);
            d.Grove(pack, new[] { "TreeFirTall", "TreeBirchWinter" }, v, 110f, 20, p => d.Slope(p) < 28f);
        }

        static void CableCarLine(D d, MapPackModels pack, List<Vector2> starts, int villageCount)
        {
            float pylonHeight = pack.Pivot("CablePylonCable").y;
            Vector2 bestA = default, bestB = default;
            float bestGain = 0f;
            for (int s0 = 0; s0 < starts.Count; s0++)
            {
                // Beside a village (its downhill side kept free for the chalet's view), or on its own spot.
                var start = s0 < villageCount ? starts[s0] - d.Downhill(starts[s0], 150f) * 75f : starts[s0];
                if (!d.Clear(start, 12f, true) || d.Roughness(start, 10f) > 7f)
                    continue;
                for (int i = 0; i < 200; i++)
                {
                    float ang = d.Range(0f, Mathf.PI * 2f), dist = d.Range(550f, 1100f);
                    var b = start + new Vector2(Mathf.Cos(ang), Mathf.Sin(ang)) * dist;
                    float gain = d.Ground(b) - d.Ground(start);
                    if (gain <= bestGain || gain < 150f || d.Slope(b) > 20f || d.Roughness(b, 10f) > 7f || !d.Clear(b, 14f, true))
                        continue;
                    bool clear = true;
                    for (float s = 40f; s < dist - 40f && clear; s += 40f)
                        clear = d.Clear(Vector2.Lerp(start, b, s / dist), 8f, true, ignoreFootprints: true);
                    if (!clear)
                        continue;
                    bestGain = gain;
                    bestA = start;
                    bestB = b;
                }
            }
            if (bestGain <= 0f)
            {
                Debug.LogWarning("[Winging It] Mountain Range: no clear line for the cable car.");
                return;
            }

            var dir = (bestB - bestA).normalized;
            float yaw = D.YawToward(dir);
            var lower = d.Put(pack, "CableStation", bestA, yaw, solid: true, sink: 0.5f);
            var upper = d.Put(pack, "CableStation", bestB, yaw + 180f, solid: true, sink: 0.5f);
            Vector3 Exit(GameObject station) => station.transform.TransformPoint(pack.Pivot("CableStationCable") - pack.Pivot("CableStation"));

            // Cable supports: the two stations, then a pylon wherever the straight cable would pass under 15 m of ground.
            var supports = new List<Vector3> { Exit(lower), Exit(upper) };
            var pylons = new List<Vector2>();
            for (int iter = 0; iter < 10; iter++)
            {
                float worst = float.MaxValue;
                int segment = -1;
                Vector3 at = default;
                for (int i = 0; i < supports.Count - 1; i++)
                {
                    Vector3 p = supports[i], q = supports[i + 1];
                    float len = Vector2.Distance(new Vector2(p.x, p.z), new Vector2(q.x, q.z));
                    for (float s = 30f; s < len - 30f; s += 10f)
                    {
                        var pt = Vector3.Lerp(p, q, s / len);
                        float gap = pt.y - d.Ground(new Vector2(pt.x, pt.z));
                        if (gap < worst)
                        {
                            worst = gap;
                            segment = i;
                            at = pt;
                        }
                    }
                }
                if (worst >= 15f || segment < 0)
                    break;
                var xz = new Vector2(at.x, at.z);
                float foot = d.Lowest(xz, 3f) - 0.5f;
                supports.Insert(segment + 1, new Vector3(at.x, foot + pylonHeight, at.z));
                pylons.Add(xz);
            }
            foreach (var xz in pylons)
                d.Put(pack, "CablePylon", xz, yaw, solid: true, sink: 0.5f);

            // The two tracks either side of the line, as thin dark beams between the supports.
            var across = new Vector3(-dir.y, 0f, dir.x) * 3f;
            var cables = new GameObject("Cables").transform;
            cables.SetParent(d.Group, false);
            var cube = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var ink = new ClassModels.PlaneMaterials().Ink;
            foreach (var offset in new[] { across, -across })
                for (int i = 0; i < supports.Count - 1; i++)
                {
                    Vector3 p = supports[i] + offset, q = supports[i + 1] + offset;
                    var seg = new GameObject("Cable");
                    seg.transform.SetParent(cables, false);
                    seg.transform.SetPositionAndRotation((p + q) * 0.5f, Quaternion.LookRotation(q - p));
                    seg.transform.localScale = new Vector3(0.22f, 0.22f, Vector3.Distance(p, q));
                    seg.AddComponent<MeshFilter>().sharedMesh = cube;
                    var r = seg.AddComponent<MeshRenderer>();
                    r.sharedMaterial = ink;
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                }

            // Gondolas round the loop: up one track, down the other.
            var loop = new List<Vector3>();
            foreach (var s in supports)
                loop.Add(s + across);
            for (int i = supports.Count - 1; i >= 0; i--)
                loop.Add(supports[i] - across);
            var car = new GameObject("CableCar").transform;
            car.SetParent(d.Group, false);
            var gondolas = new Transform[10];
            for (int k = 0; k < gondolas.Length; k++)
            {
                var g = pack.Child("Gondola", car, Vector3.zero);
                g.isStatic = false;
                gondolas[k] = g.transform;
            }
            car.gameObject.AddComponent<CableCar>().Configure(loop.ToArray(), gondolas, 5f);
            Debug.Log($"[Winging It] Mountain Range cable car: {Vector2.Distance(bestA, bestB):F0} m, {bestGain:F0} m up, {pylons.Count} pylons");
        }
    }
}

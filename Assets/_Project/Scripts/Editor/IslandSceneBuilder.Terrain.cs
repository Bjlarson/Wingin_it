using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// The Island's ground (<see cref="Height"/>) and how its regions paint and grow it (<see cref="IslandPainter"/>),
    /// built through the shared <see cref="MapTerrainBuilder.BuildTerrain"/> and <see cref="MapScatter"/>, plus the
    /// desert mesas that stand on it.
    /// </summary>
    public static partial class IslandSceneBuilder
    {
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
        internal static float Height(float x, float z)
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

            // The lighthouse islet: a mound just inside the islet mesh (MapLandmarkBuilder.Lighthouse), whose cliffs and grassy top
            // at IsletHeight are what you see and hit.
            float islet = Vector2.Distance(new Vector2(x, z), IsletPosition);
            h = Mathf.Max(h, Mathf.Lerp(SeaFloor, IsletHeight - 1.5f, Smooth(IsletRadius + 20f, IsletRadius - 40f, islet)));

            // Airfield pads last: dead flat at LandHeight.
            return Mathf.Lerp(h, LandHeight, PadMask(x, z));
        }

        // ------------------------------------------------------------------ terrain

        static Terrain BuildTerrain(Transform parent, NatureModels nature)
        {
            Vector3 origin = new Vector3(-TerrainSize * 0.5f, TerrainBase, -TerrainSize * 0.5f);
            const int res = HeightmapResolution;
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
            return MapTerrainBuilder.BuildTerrain(TerrainDataPath, "Island Terrain", parent, origin,
                new Vector3(TerrainSize, TerrainHeight, TerrainSize), heights, 0f, NatureSeed, new IslandPainter(), nature);
        }

        /// <summary>Trees are scattered from this seed, grass and flowers from the next (the Island definition's seed).</summary>
        const int NatureSeed = 777;

        /// <summary>
        /// The Island's regions as splat and scatter: grass, sand (beaches, desert, seabed), rock (steep or high), snow
        /// (peaks) and paved city; woods, palms, cacti and pines by region; grass and flowers on the open grassland.
        /// </summary>
        sealed class IslandPainter : ITerrainPainter
        {
            public Rect TreeArea => new(-IslandHalfSize.x - 100f, -IslandHalfSize.y - 100f,
                2f * (IslandHalfSize.x + 100f), 2f * (IslandHalfSize.y + 100f));

            public (Vector4 layers, float paved) Layers(float x, float z, float h, float steep)
            {
                float snow = Smooth(380f, 440f, h) * (1f - Smooth(40f, 55f, steep));
                float rock = Mathf.Max(Smooth(22f, 34f, steep), Smooth(140f, 220f, h)) * (1f - snow);
                float rest = 1f - snow - rock;
                float paved = rest * CityMask(x, z) * (h > 1f ? 1f : 0f);
                float beach = Smooth(0.85f, 0.6f, LandMask(x, z));
                float sandMask = Mathf.Max(DesertMask(x, z), beach);
                float sand = (rest - paved) * sandMask;
                float grass = rest - paved - sand;
                return (new Vector4(grass, sand, rock, snow), paved);
            }

            public (string kind, float minScale, float maxScale)? PickTree(float x, float z, float h, float steep, double roll) =>
                IslandSceneBuilder.PickTree(x, z, h, steep, roll);

            /// <summary>Grass tufts and flowers on the grassland, none on pads, in the city or the desert.</summary>
            public (int grass, bool flowers) Detail(float x, float z, float h, float steep, System.Random rng)
            {
                if (Mathf.Abs(x) > IslandHalfSize.x + 100f || Mathf.Abs(z) > IslandHalfSize.y + 100f)
                    return (0, false);
                if (h < 2f || h > 250f || PadMask(x, z) > 0.01f || CityMask(x, z) > 0.02f || CanalDistance(x, z) < 90f
                    || DesertMask(x, z) > 0.3f || LandMask(x, z) < 0.85f || steep > 25f)
                    return (0, false);
                float meadow = Fbm(x, z, 120f, 2, 21.7f);
                int grass = meadow > 0.45f ? 2 : rng.NextDouble() < 0.5 ? 1 : 0;
                return (grass, meadow > 0.55f && rng.NextDouble() < 0.25);
            }
        }

        // ------------------------------------------------------------------ nature

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
            return Vector2.Distance(p, PyramidPosition) < MapLandmarkBuilder.PyramidBase * 0.75f || Vector2.Distance(p, IsletPosition) < 150f;
        }

        /// <summary>What grows at a spot (a <see cref="MapScatter.TreeKinds"/> name and its scale range), or null for nothing.</summary>
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
    }
}

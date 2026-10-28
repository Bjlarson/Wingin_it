using System.IO;
using UnityEditor;
using UnityEngine;
using WingingIt.Maps;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Turns a designer's grayscale heightmap picture (any size, 8-bit, from <c>Design/05-Levels</c>) into a map's
    /// 16-bit square RAW and its <see cref="MapDefinition"/>: squared by crop or by padding with sea, resampled to a
    /// terrain size, blurred to lose the 8-bit terraces, mapped to a world height range and faded to sea outside the
    /// flight boundary. Row 0 of the picture is north, so it is flipped to the heightmap's south-first rows.
    /// Re-running a spec rewrites the RAW and the numbers in the definition but keeps its rules.
    /// </summary>
    public static class MapHeightmapImporter
    {
        const string MapsFolder = B.Root + "/Maps";

        public struct Spec
        {
            public string name, displayName, sourcePng;
            /// <summary>Pad the picture with sea to a square instead of cropping its middle.</summary>
            public bool pad;
            public int resolution;
            public float worldSize;
            /// <summary>The terrain's lowest world height (the RAW's 0) and its range up to the RAW's 1.</summary>
            public float baseHeight, heightRange;
            /// <summary>World heights a black and a white pixel stand for.</summary>
            public float groundMin, groundMax;
            public float gamma;
            /// <summary>Fade the ground to sea outside the flight boundary (an island map); false runs the land out to the terrain's edge.</summary>
            public bool seaFade;
            /// <summary>Lowest ground kept (shallow lake and sea floors instead of the full black-to-sea depth).</summary>
            public float floorMin;
            /// <summary>Meters of fine rock-like noise added to the land, for a low-resolution picture (fades to nothing at the sea).</summary>
            public float detail;
            public int blurRadius, blurPasses;
            public float boundaryRadius, ceiling;
            public int seed;
        }

        public static readonly Spec BroadReach = new()
        {
            name = "BroadReach", displayName = "Broad Reach", sourcePng = "Design/05-Levels/Valley.png",
            resolution = 2049, worldSize = 8192f, baseHeight = -40f, heightRange = 1440f,
            groundMin = 12f, groundMax = 1400f, gamma = 1.3f, floorMin = -40f, blurRadius = 2, blurPasses = 3,
            boundaryRadius = 3800f, ceiling = 1500f, seed = 4101,
        };

        // A range, not islands: black maps to just above the sea, so the valleys stay dry.
        public static readonly Spec MountainRange = new()
        {
            name = "MountainRange", displayName = "Mountain Range", sourcePng = "Design/05-Levels/Mountins.png",
            resolution = 2049, worldSize = 4608f, baseHeight = -40f, heightRange = 1060f,
            groundMin = 20f, groundMax = 1020f, gamma = 1f, floorMin = 20f, blurRadius = 2, blurPasses = 3,
            boundaryRadius = 2100f, ceiling = 1100f, seed = 5202,
        };

        // A small, soft picture (329 px): stretched 6x and roughened with noise. The only sea map besides the Island.
        public static readonly Spec Archipelago = new()
        {
            name = "ShatteredArchipelago", displayName = "Shattered Archipelago", sourcePng = "Design/05-Levels/archipelago.png", pad = true,
            resolution = 2049, worldSize = 4096f, baseHeight = -20f, heightRange = 1120f,
            groundMin = -260f, groundMax = 1030f, gamma = 1f, seaFade = true, floorMin = -20f, detail = 60f,
            blurRadius = 3, blurPasses = 3, boundaryRadius = 1900f, ceiling = 1100f, seed = 7404,
        };

        public static readonly Spec IslandV2 = new()
        {
            name = "IslandV2", displayName = "The Island", sourcePng = "Design/05-Levels/island.png", pad = true,
            resolution = 2049, worldSize = 7000f, baseHeight = -40f, heightRange = 1440f,
            groundMin = -40f, groundMax = 1400f, gamma = 1.6f, seaFade = true, floorMin = -40f, blurRadius = 2, blurPasses = 3,
            boundaryRadius = 3300f, ceiling = 1500f, seed = 6303,
        };

        [MenuItem("Winging It/Map/Import Heightmap Images (Broad Reach, Mountain Range, Archipelago, Island v2)")]
        static void ImportAll()
        {
            Import(BroadReach);
            Import(MountainRange);
            Import(Archipelago);
            Import(IslandV2);
        }

        public static MapDefinition Import(Spec spec)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            var picture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!picture.LoadImage(File.ReadAllBytes(Path.Combine(projectRoot, spec.sourcePng))))
                throw new System.InvalidOperationException($"Could not read {spec.sourcePng}.");
            var pixels = picture.GetPixels32();
            int pw = picture.width, ph = picture.height;
            Object.DestroyImmediate(picture);

            int res = spec.resolution;
            // The square of the picture that fills the map, in picture pixels: bottom-left corner and side.
            float side = spec.pad ? Mathf.Max(pw, ph) : Mathf.Min(pw, ph);
            float x0 = (pw - side) * 0.5f, y0 = (ph - side) * 0.5f;

            float Pixel(int px, int py)
            {
                if (px < 0 || py < 0 || px >= pw || py >= ph)
                    return 0f;
                return pixels[py * pw + px].r / 255f; // texture rows start at the bottom: already south-first
            }

            var norm = new float[res * res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float fx = x0 + x / (float)(res - 1) * (side - 1f), fy = y0 + z / (float)(res - 1) * (side - 1f);
                int ix = Mathf.FloorToInt(fx), iy = Mathf.FloorToInt(fy);
                float tx = fx - ix, ty = fy - iy;
                float a = Mathf.Lerp(Pixel(ix, iy), Pixel(ix + 1, iy), tx);
                float b = Mathf.Lerp(Pixel(ix, iy + 1), Pixel(ix + 1, iy + 1), tx);
                norm[z * res + x] = Mathf.Pow(Mathf.Lerp(a, b, ty), spec.gamma);
            }
            for (int pass = 0; pass < spec.blurPasses; pass++)
                norm = BoxBlur(norm, res, spec.blurRadius);

            var map = new Heightmap(res);
            float half = spec.worldSize * 0.5f;
            float fadeStart = spec.boundaryRadius + 150f, fadeEnd = spec.boundaryRadius + 650f;
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = (x / (float)(res - 1) - 0.5f) * spec.worldSize, wz = (z / (float)(res - 1) - 0.5f) * spec.worldSize;
                float h = Mathf.Lerp(spec.groundMin, spec.groundMax, norm[z * res + x]);
                if (spec.detail > 0f)
                {
                    float rough = Mathf.PerlinNoise(wx / 140f + 11.3f, wz / 140f) * 0.55f + Mathf.PerlinNoise(wx / 55f, wz / 55f + 7.1f) * 0.3f
                        + Mathf.PerlinNoise(wx / 22f + 3.7f, wz / 22f) * 0.15f - 0.5f;
                    h += rough * spec.detail * 2f * TerrainRules.Smooth(0f, 120f, h);
                }
                h = Mathf.Max(h, spec.floorMin);
                if (spec.seaFade)
                    h = Mathf.Lerp(spec.baseHeight, h, 1f - TerrainRules.Smooth(fadeStart, fadeEnd, Mathf.Sqrt(wx * wx + wz * wz)));
                map.Values[z, x] = Mathf.Clamp01((h - spec.baseHeight) / spec.heightRange);
            }

            B.EnsureFolder(MapsFolder);
            B.EnsureFolder($"{MapsFolder}/{spec.name}");
            string rawPath = $"{MapsFolder}/{spec.name}/{spec.name}_height.bytes";
            File.WriteAllBytes(rawPath, map.ToR16());
            AssetDatabase.ImportAsset(rawPath);

            string defPath = $"{MapsFolder}/{spec.name}/{spec.name}.asset";
            var definition = AssetDatabase.LoadAssetAtPath<MapDefinition>(defPath);
            if (definition == null)
            {
                definition = ScriptableObject.CreateInstance<MapDefinition>();
                AssetDatabase.CreateAsset(definition, defPath);
            }
            // Tall maps: the height bands of the rules follow the peaks (rock from a quarter up, snow near the top).
            var rules = TerrainRules.Default;
            float peak = spec.groundMax;
            rules.rockHeight = new Vector2(peak * 0.25f, peak * 0.4f);
            rules.snowHeight = new Vector2(peak * 0.62f, peak * 0.72f);
            rules.treeLine = peak * 0.5f;
            definition.rules = rules;
            definition.displayName = spec.displayName;
            definition.heightmapRaw = AssetDatabase.LoadAssetAtPath<TextAsset>(rawPath);
            definition.worldSize = spec.worldSize;
            definition.baseHeight = spec.baseHeight;
            definition.heightRange = spec.heightRange;
            definition.seaLevel = 0f;
            definition.boundaryRadius = spec.boundaryRadius;
            definition.ceiling = spec.ceiling;
            definition.seed = spec.seed;
            EditorUtility.SetDirty(definition);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Imported {spec.sourcePng} -> {rawPath} ({res}x{res}, {spec.worldSize / (res - 1):0.0} m a sample)");
            return definition;
        }

        /// <summary>Separable box blur with clamped edges; a few passes approximate a gaussian.</summary>
        static float[] BoxBlur(float[] src, int res, int radius)
        {
            if (radius <= 0)
                return src;
            var tmp = new float[src.Length];
            var dst = new float[src.Length];
            float inv = 1f / (radius * 2 + 1);
            for (int z = 0; z < res; z++)
            {
                int row = z * res;
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += src[row + Mathf.Clamp(k, 0, res - 1)];
                for (int x = 0; x < res; x++)
                {
                    tmp[row + x] = sum * inv;
                    sum += src[row + Mathf.Min(x + radius + 1, res - 1)] - src[row + Mathf.Max(x - radius, 0)];
                }
            }
            for (int x = 0; x < res; x++)
            {
                float sum = 0f;
                for (int k = -radius; k <= radius; k++)
                    sum += tmp[Mathf.Clamp(k, 0, res - 1) * res + x];
                for (int z = 0; z < res; z++)
                {
                    dst[z * res + x] = sum * inv;
                    sum += tmp[Mathf.Min(z + radius + 1, res - 1) * res + x] - tmp[Mathf.Max(z - radius, 0) * res + x];
                }
            }
            return dst;
        }
    }
}

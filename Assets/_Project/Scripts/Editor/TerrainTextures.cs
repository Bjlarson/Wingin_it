using System.IO;
using UnityEditor;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// The Island's terrain layer textures (Pack 3): stylized and seamless, made here instead of painted, so they stay
    /// on the art direction's biome colours (lowpoly-asset-style-guide.html) and the nature pack's palette. Each is an
    /// albedo and a normal map from the same height field: grass with soft meadow patches and tufts, sand with wind
    /// ripples, rock in flat-shaded facets with cracks (like the low-poly rocks on it), snow with blue drifts, and city
    /// paving in 6 m slabs. Detail sits at small scales and the big patches stay faint, so a tile doesn't repeat
    /// visibly from the air. Written once; delete the PNGs to make them again.
    /// </summary>
    internal static class TerrainTextures
    {
        public const string Folder = FlightTestSceneBuilder.TexturesFolder + "/Terrain";
        const int Size = 1024;

        public enum Kind { Grass, Sand, Rock, Snow, Paved }

        /// <summary>The albedo and normal map for a layer, generating them if they're missing.</summary>
        public static (Texture2D albedo, Texture2D normal) Load(Kind kind)
        {
            string albedoPath = $"{Folder}/{kind}.png", normalPath = $"{Folder}/{kind}_Normal.png";
            if (!File.Exists(albedoPath) || !File.Exists(normalPath))
                Generate(kind, albedoPath, normalPath);
            return (AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath), AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        }

        static void Generate(Kind kind, string albedoPath, string normalPath)
        {
            FlightTestSceneBuilder.EnsureFolder(Folder);
            var color = new Color[Size * Size];
            var height = new float[Size * Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
                (color[y * Size + x], height[y * Size + x]) = kind switch
                {
                    Kind.Grass => Grass(u, v),
                    Kind.Sand => Sand(u, v),
                    Kind.Rock => Rock(u, v),
                    Kind.Snow => Snow(u, v),
                    _ => Paved(u, v),
                };
            }
            float strength = kind switch { Kind.Rock => 5f, Kind.Paved => 3f, Kind.Snow => 1.5f, _ => 2.5f };
            Write(albedoPath, color, false);
            Write(normalPath, NormalMap(height, strength), true);
        }

        // ------------------------------------------------------------------ the layers (u, v in 0..1, seamless)

        static (Color, float) Grass(float u, float v)
        {
            float patches = Fbm(u, v, 4, 4, 11); // faint meadow patches, ~20 m across
            float clumps = Fbm(u, v, 24, 3, 12);
            var (f1, _, id) = Voronoi(u, v, 220, 13); // tufts ~0.4 m
            float tuft = Hash(id, 14);
            Color dark = Rgb(0x4E9A4A), light = Rgb(0x6DB955), dry = Rgb(0x9DB858);
            Color c = Color.Lerp(dark, light, Bands(0.35f * patches + 0.65f * clumps, 4, 0.18f));
            c = Color.Lerp(c, dry, 0.35f * Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.58f, 0.7f, Fbm(u, v, 6, 3, 15))));
            c *= 0.93f + 0.12f * tuft;
            return (c, clumps * 0.6f + Mathf.Clamp01(1f - f1 * 220f * 1.2f) * tuft * 0.4f);
        }

        static (Color, float) Sand(float u, float v)
        {
            // Wind ripples: wavy stripes, a whole number of them per tile so they wrap.
            float warp = Fbm(u, v, 3, 3, 21) * 2.5f + Fbm(u, v, 12, 2, 22) * 0.4f;
            float ripple = Mathf.Sin(2f * Mathf.PI * (v * 36f + u * 6f + warp)) * 0.5f + 0.5f;
            float patches = Fbm(u, v, 4, 3, 23);
            float grain = Hash(Mathf.FloorToInt(u * Size), Mathf.FloorToInt(v * Size), 24);
            Color c = Color.Lerp(Rgb(0xE8B26B), Rgb(0xF6CD8A), 0.5f * patches + 0.5f * Bands(ripple, 3, 0.35f));
            c *= 0.97f + 0.05f * grain;
            return (c, ripple * 0.7f + patches * 0.3f);
        }

        static (Color, float) Rock(float u, float v)
        {
            // Facets ~2.5 m across, each a flat tilted plane with its own tone (the flat-shaded rocks on it), and thin,
            // faint cracks between them so it reads as rock, not paving, even stretched down a cliff.
            var (f1, f2, id) = Voronoi(u, v, 20, 31, out Vector2 toSite);
            float tone = Hash(id, 32);
            Vector2 tilt = new Vector2(Hash(id, 33) - 0.5f, Hash(id, 34) - 0.5f);
            float crack = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.0008f, 0.003f, f2 - f1));
            float patches = Fbm(u, v, 5, 3, 36);
            float grit = Fbm(u, v, 96, 2, 35);
            Color c = Color.Lerp(Rgb(0x737C8C), Rgb(0x99A2B1), Bands(0.6f * tone + 0.4f * patches, 4, 0.12f));
            c *= 0.95f + 0.08f * grit;
            c = Color.Lerp(c * 0.84f, c, crack);
            return (c, Vector2.Dot(tilt, toSite) * 46f - (1f - crack) * 0.15f + grit * 0.08f);
        }

        static (Color, float) Snow(float u, float v)
        {
            float drifts = Fbm(u, v, 5, 4, 41);
            float sparkle = Fbm(u, v, 48, 2, 42);
            Color c = Color.Lerp(Rgb(0xD9E6F5), Rgb(0xF6F9FC), Bands(drifts, 3, 0.25f));
            c *= 0.98f + 0.03f * sparkle;
            return (c, drifts + sparkle * 0.15f);
        }

        static (Color, float) Paved(float u, float v)
        {
            const int slabs = 8; // 6 m on the 48 m tile
            float gx = u * slabs, gy = v * slabs;
            int ix = Mathf.FloorToInt(gx), iy = Mathf.FloorToInt(gy);
            float edge = Mathf.Min(Mathf.Min(gx - ix, 1f - (gx - ix)), Mathf.Min(gy - iy, 1f - (gy - iy))) * Size / slabs; // px to a joint
            float joint = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f, 2.5f, edge));
            float slab = Hash(ix % slabs, iy % slabs, 51);
            float wear = Fbm(u, v, 16, 3, 52);
            Color c = Color.Lerp(Rgb(0xAEB6C3), Rgb(0xC3CAD5), 0.6f * slab + 0.4f * wear);
            c = Color.Lerp(Color.Lerp(Rgb(0x7F8898), c, 0.35f), c, joint);
            return (c, joint + wear * 0.15f);
        }

        // ------------------------------------------------------------------ noise (periodic, so every texture tiles)

        static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                uint h = (uint)(x * 374761393 + y * 668265263 + seed * 1274126177);
                h = (h ^ (h >> 13)) * 1274126177u;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777216f;
            }
        }

        static float Hash(int id, int seed) => Hash(id, id * 31 + 7, seed);

        static int Wrap(int i, int period) => ((i % period) + period) % period;

        /// <summary>Value noise on a lattice of <paramref name="period"/> cells across the tile, 0..1.</summary>
        static float Noise(float u, float v, int period, int seed)
        {
            float x = u * period, y = v * period;
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float tx = x - x0, ty = y - y0;
            tx = tx * tx * (3f - 2f * tx);
            ty = ty * ty * (3f - 2f * ty);
            float H(int i, int j) => Hash(Wrap(i, period), Wrap(j, period), seed);
            return Mathf.Lerp(Mathf.Lerp(H(x0, y0), H(x0 + 1, y0), tx), Mathf.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
        }

        static float Fbm(float u, float v, int period, int octaves, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += amp * Noise(u, v, period << i, seed + i * 101);
                norm += amp;
                amp *= 0.5f;
            }
            return sum / norm;
        }

        static (float f1, float f2, int id) Voronoi(float u, float v, int period, int seed) => Voronoi(u, v, period, seed, out _);

        /// <summary>
        /// Distances (in tile units) to the nearest and second-nearest of one jittered site per cell, the nearest cell's
        /// id, and the offset from its site.
        /// </summary>
        static (float f1, float f2, int id) Voronoi(float u, float v, int period, int seed, out Vector2 toSite)
        {
            float x = u * period, y = v * period;
            int cx = Mathf.FloorToInt(x), cy = Mathf.FloorToInt(y);
            float f1 = float.MaxValue, f2 = float.MaxValue;
            int id = 0;
            toSite = Vector2.zero;
            for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                int wx = Wrap(cx + i, period), wy = Wrap(cy + j, period);
                var site = new Vector2(cx + i + 0.15f + 0.7f * Hash(wx, wy, seed), cy + j + 0.15f + 0.7f * Hash(wx, wy, seed + 1));
                Vector2 d = new Vector2(x, y) - site;
                float dist = d.magnitude / period;
                if (dist < f1)
                {
                    f2 = f1;
                    f1 = dist;
                    id = wy * period + wx;
                    toSite = d / period;
                }
                else if (dist < f2)
                    f2 = dist;
            }
            return (f1, f2, id);
        }

        /// <summary>Posterizes 0..1 into <paramref name="count"/> flat bands with soft steps (the toon look of the props).</summary>
        static float Bands(float t, int count, float softness)
        {
            float x = Mathf.Clamp01(t) * count;
            float i = Mathf.Floor(x), f = x - i;
            float step = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.5f - softness, 0.5f + softness, f));
            return Mathf.Clamp01((i + step - 0.5f) / (count - 1));
        }

        static Color Rgb(int hex) => new Color(((hex >> 16) & 0xFF) / 255f, ((hex >> 8) & 0xFF) / 255f, (hex & 0xFF) / 255f, 1f);

        // ------------------------------------------------------------------ output

        static Color[] NormalMap(float[] height, float strength)
        {
            var n = new Color[height.Length];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                float H(int i, int j) => height[Wrap(j, Size) * Size + Wrap(i, Size)];
                float dx = (H(x + 1, y) - H(x - 1, y)) * strength, dy = (H(x, y + 1) - H(x, y - 1)) * strength;
                var normal = new Vector3(-dx, -dy, 1f).normalized;
                n[y * Size + x] = new Color(normal.x * 0.5f + 0.5f, normal.y * 0.5f + 0.5f, normal.z * 0.5f + 0.5f, 1f);
            }
            return n;
        }

        static void Write(string path, Color[] pixels, bool normalMap)
        {
            var t = new Texture2D(Size, Size, TextureFormat.RGBA32, false, linear: normalMap);
            for (int i = 0; i < pixels.Length; i++)
                pixels[i].a = 1f; // opaque: the layers take a constant smoothness (MapTerrainBuilder.Layer)
            t.SetPixels(pixels);
            File.WriteAllBytes(path, t.EncodeToPNG());
            Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path);

            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = normalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.wrapMode = TextureWrapMode.Repeat;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 8;
            importer.mipmapEnabled = true;
            importer.maxTextureSize = Size;
            importer.SaveAndReimport();
        }
    }
}

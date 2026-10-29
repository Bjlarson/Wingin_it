using System;
using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>
    /// A square grid of ground heights, 0..1 over the map's height range, row = z, column = x (the layout
    /// <see cref="TerrainData.SetHeights"/> takes). Stored on disk as 16-bit little-endian RAW (the .bytes extension
    /// makes Unity import it as a <see cref="TextAsset"/>): 8-bit PNGs would step the Island's 800 m range in 3 m terraces.
    /// </summary>
    public sealed class Heightmap
    {
        public int Resolution { get; }
        public float[,] Values { get; }

        public Heightmap(int resolution)
        {
            if (resolution < 2)
                throw new ArgumentOutOfRangeException(nameof(resolution));
            Resolution = resolution;
            Values = new float[resolution, resolution];
        }

        /// <summary>True for the sizes a Unity terrain accepts: 2^n + 1 from 33 to 4097.</summary>
        public static bool IsTerrainResolution(int resolution)
        {
            for (int n = 33; n <= 4097; n = (n - 1) * 2 + 1)
                if (n == resolution)
                    return true;
            return false;
        }

        /// <summary>Reads 16-bit little-endian RAW, a square of any size.</summary>
        public static Heightmap FromR16(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 8 || bytes.Length % 2 != 0)
                throw new ArgumentException("A 16-bit RAW heightmap needs an even number of bytes.");
            int samples = bytes.Length / 2;
            int res = Mathf.RoundToInt(Mathf.Sqrt(samples));
            if (res * res != samples)
                throw new ArgumentException($"A RAW heightmap must be square; {samples} samples is not.");
            var map = new Heightmap(res);
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int i = (z * res + x) * 2;
                map.Values[z, x] = (bytes[i] | bytes[i + 1] << 8) / 65535f;
            }
            return map;
        }

        public byte[] ToR16()
        {
            var bytes = new byte[Resolution * Resolution * 2];
            for (int z = 0; z < Resolution; z++)
            for (int x = 0; x < Resolution; x++)
            {
                int v = Mathf.RoundToInt(Mathf.Clamp01(Values[z, x]) * 65535f);
                int i = (z * Resolution + x) * 2;
                bytes[i] = (byte)(v & 0xFF);
                bytes[i + 1] = (byte)(v >> 8);
            }
            return bytes;
        }

        /// <summary>Reads a readable grayscale image (red channel; 8-bit, so coarse over a tall range). Row 0 is the bottom, as in a texture.</summary>
        public static Heightmap FromTexture(Texture2D texture)
        {
            if (texture.width != texture.height)
                throw new ArgumentException("A heightmap image must be square.");
            var pixels = texture.GetPixels();
            var map = new Heightmap(texture.width);
            for (int z = 0; z < map.Resolution; z++)
            for (int x = 0; x < map.Resolution; x++)
                map.Values[z, x] = pixels[z * map.Resolution + x].r;
            return map;
        }

        /// <summary>The height (0..1) at a normalized position, bilinear. 0,0 is the south-west corner.</summary>
        public float Sample(float nx, float nz)
        {
            float fx = Mathf.Clamp01(nx) * (Resolution - 1), fz = Mathf.Clamp01(nz) * (Resolution - 1);
            int x0 = Mathf.Min((int)fx, Resolution - 2), z0 = Mathf.Min((int)fz, Resolution - 2);
            float tx = fx - x0, tz = fz - z0;
            float a = Mathf.Lerp(Values[z0, x0], Values[z0, x0 + 1], tx);
            float b = Mathf.Lerp(Values[z0 + 1, x0], Values[z0 + 1, x0 + 1], tx);
            return Mathf.Lerp(a, b, tz);
        }
    }
}

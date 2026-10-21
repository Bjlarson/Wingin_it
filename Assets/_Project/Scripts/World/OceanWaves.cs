using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// The sea's waves (living-world.md, section 2): rolling swells with chop on top, all summed sines. The single source
    /// of truth for the water shader (pushed as globals by <see cref="Apply"/>) and for anything that rides the waves in
    /// C#. Cosmetic only: the sea's collider stays flat at y = 0, and the waves never reach more than
    /// <see cref="MaxHeight"/> above or below it.
    /// </summary>
    public static class OceanWaves
    {
        /// <summary>One wave: travel direction (world XZ, normalized), wavelength (m), amplitude (m), speed (m/s).</summary>
        public readonly struct Wave
        {
            public readonly Vector2 Direction;
            public readonly float Wavelength, Amplitude, Speed;
            /// <summary>A long swell: calmed in shallow water (the canal, the beaches), where the chop isn't.</summary>
            public readonly bool Swell;

            public Wave(float headingDegrees, float wavelength, float amplitude, float speed, bool swell = false)
            {
                Swell = swell;
                float a = headingDegrees * Mathf.Deg2Rad;
                Direction = new Vector2(Mathf.Sin(a), Mathf.Cos(a));
                Wavelength = wavelength;
                Amplitude = amplitude;
                Speed = speed;
            }

            public float Number => 2f * Mathf.PI / Wavelength;
        }

        /// <summary>
        /// Where the sea's waves run (degrees from north, clockwise): down the match's <see cref="World.Wind"/>.
        /// </summary>
        public const float WindHeading = World.Wind.Heading;

        /// <summary>
        /// Two long rolling swells down the wind (the waves you see from the air), then the chop on top of them: two
        /// short waves down the wind and one crossing back against it, so the pattern never reads as one scrolling
        /// texture. The chop's wavelengths are each at least three of the surface's fine grid cells
        /// (<see cref="OceanSurface.DefaultStep"/>). Amplitudes sum to <see cref="MaxHeight"/>.
        /// </summary>
        public static readonly Wave[] Waves =
        {
            new(WindHeading, 70f, 0.65f, 8f, swell: true),
            new(WindHeading + 25f, 45f, 0.45f, 6f, swell: true),
            new(WindHeading, 12f, 0.15f, 2.4f),
            new(WindHeading + 35f, 18f, 0.12f, 3.2f),
            new(WindHeading + 160f, 28f, 0.08f, 1.8f),
        };

        // Perlin noise on top, so the sea isn't a regular lattice of sines: a low, slow "roughness" that makes patches of
        // the sea calmer or rougher (scaling every wave between RoughnessMin and 1), and a bumpy height noise of its own.
        // Both drift down the wind.
        public const float NoiseAmplitude = 0.25f, NoiseScale = 40f, NoiseSpeed = 3f;
        public const float RoughnessScale = 180f, RoughnessMin = 0.55f, RoughnessSpeed = 1.5f;

        /// <summary>
        /// The most the surface ever rises above (or falls below) y = 0, out at sea: every wave at once plus the noise.
        /// Visual only: the sea you hit stays flat at y = 0, so a plane skimming the swells can dip into a crest before it
        /// crashes.
        /// </summary>
        public const float MaxHeight = 1.7f;

        static readonly int WavesId = Shader.PropertyToID("_OceanWaves");
        static readonly int WaveMotionId = Shader.PropertyToID("_OceanWaveMotion");
        static readonly int NoiseId = Shader.PropertyToID("_OceanNoise");
        static readonly int RoughnessId = Shader.PropertyToID("_OceanRoughness");
        static readonly int MaxHeightId = Shader.PropertyToID("_OceanMaxHeight");

        static Vector2 Wind => new(Mathf.Sin(WindHeading * Mathf.Deg2Rad), Mathf.Cos(WindHeading * Mathf.Deg2Rad));

        /// <summary>
        /// Surface height above y = 0 at world <paramref name="x"/>, <paramref name="z"/> and <paramref name="time"/>
        /// (seconds, the shader's <c>_Time.y</c>, i.e. <see cref="Time.timeSinceLevelLoad"/>), out at sea: before the
        /// shader's distance fade and its calming of the swells in shallow water.
        /// </summary>
        public static float Height(float x, float z, float time)
        {
            float h = 0f;
            foreach (var w in Waves)
                h += w.Amplitude * Mathf.Sin((w.Direction.x * x + w.Direction.y * z - w.Speed * time) * w.Number);
            return h * Roughness(x, z, time) + HeightNoise(x, z, time);
        }

        /// <summary>How rough the sea is here and now: every wave is scaled by this, <see cref="RoughnessMin"/> to 1.</summary>
        public static float Roughness(float x, float z, float time)
        {
            Vector2 drift = Wind * (RoughnessSpeed * time);
            float n = Perlin((x - drift.x) / RoughnessScale, (z - drift.y) / RoughnessScale);
            return Mathf.Lerp(RoughnessMin, 1f, 0.5f + 0.5f * n);
        }

        /// <summary>The noise's own bumps (m): two octaves, within ±<see cref="NoiseAmplitude"/>.</summary>
        public static float HeightNoise(float x, float z, float time)
        {
            Vector2 drift = Wind * (NoiseSpeed * time);
            float qx = (x - drift.x) / NoiseScale, qz = (z - drift.y) / NoiseScale;
            return NoiseAmplitude * (Perlin(qx, qz) + 0.5f * Perlin(qx * 2f + 17.3f, qz * 2f + 17.3f)) / 1.5f;
        }

        static readonly Vector2[] Gradients =
        {
            new(1f, 0f), new(-1f, 0f), new(0f, 1f), new(0f, -1f),
            new(0.70710678f, 0.70710678f), new(-0.70710678f, 0.70710678f),
            new(0.70710678f, -0.70710678f), new(-0.70710678f, -0.70710678f),
        };

        /// <summary>
        /// 2D Perlin noise in -1..1 with an integer hash, written to match the water shader's <c>Perlin</c> line for line
        /// (Unity's <see cref="Mathf.PerlinNoise"/> has no shader twin).
        /// </summary>
        public static float Perlin(float x, float y)
        {
            float fx = Mathf.Floor(x), fy = Mathf.Floor(y);
            int ix = (int)fx, iy = (int)fy;
            float dx = x - fx, dy = y - fy;
            float Corner(int cx, int cy, float px, float py)
            {
                Vector2 g = Gradients[Hash(unchecked((uint)(ix + cx)), unchecked((uint)(iy + cy)))];
                return g.x * px + g.y * py;
            }
            float u = Quintic(dx), v = Quintic(dy);
            float bottom = Mathf.Lerp(Corner(0, 0, dx, dy), Corner(1, 0, dx - 1f, dy), u);
            float top = Mathf.Lerp(Corner(0, 1, dx, dy - 1f), Corner(1, 1, dx - 1f, dy - 1f), u);
            return Mathf.Lerp(bottom, top, v) * 1.41421356f; // unit gradients peak at ±√½
        }

        static float Quintic(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);

        static uint Hash(uint x, uint y)
        {
            unchecked
            {
                uint h = (x * 0x27d4eb2du) ^ (y * 0x165667b1u);
                h ^= h >> 15;
                h *= 0x85ebca6bu;
                h ^= h >> 13;
                return h & 7u;
            }
        }

        /// <summary>
        /// Hands the waves to the water shader: per wave (dir x, dir z, wave number, amplitude) and (phase speed in
        /// rad/s, 1 for a swell, 0, 0); the noise (amplitude, 1 / scale, speed,
        /// 1 / roughness scale) and (wind x, wind z, roughness minimum, roughness speed).
        /// </summary>
        public static void Apply()
        {
            var shape = new Vector4[Waves.Length];
            var motion = new Vector4[Waves.Length];
            for (int i = 0; i < Waves.Length; i++)
            {
                var w = Waves[i];
                shape[i] = new Vector4(w.Direction.x, w.Direction.y, w.Number, w.Amplitude);
                motion[i] = new Vector4(w.Speed * w.Number, w.Swell ? 1f : 0f, 0f, 0f);
            }
            Shader.SetGlobalVectorArray(WavesId, shape);
            Shader.SetGlobalVectorArray(WaveMotionId, motion);
            Vector2 wind = Wind;
            Shader.SetGlobalVector(NoiseId, new Vector4(NoiseAmplitude, 1f / NoiseScale, NoiseSpeed, 1f / RoughnessScale));
            Shader.SetGlobalVector(RoughnessId, new Vector4(wind.x, wind.y, RoughnessMin, RoughnessSpeed));
            Shader.SetGlobalFloat(MaxHeightId, MaxHeight);
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The sea's waves and the surface grid they ride on (living-world.md, section 2).</summary>
    public class OceanTests
    {
        [Test]
        public void Waves_NeverPassTheMaxHeight_SoTheFlatColliderNeverDisagreesMuch()
        {
            float sum = 0f;
            foreach (var w in OceanWaves.Waves)
                sum += w.Amplitude;
            Assert.LessOrEqual(sum, OceanWaves.MaxHeight + 1e-5f);
            Assert.LessOrEqual(OceanWaves.MaxHeight, 2f, "swells you can see, but a plane only dips a little into a crest");

            var rng = new System.Random(7);
            for (int i = 0; i < 2000; i++)
            {
                float h = OceanWaves.Height((float)rng.NextDouble() * 6000f - 3000f, (float)rng.NextDouble() * 6000f - 3000f,
                    (float)rng.NextDouble() * 900f);
                Assert.LessOrEqual(Mathf.Abs(h), OceanWaves.MaxHeight + 1e-5f);
            }
        }

        [Test]
        public void Perlin_StaysInRange_IsSmooth_AndRepeatsExactly()
        {
            var rng = new System.Random(11);
            for (int i = 0; i < 5000; i++)
            {
                float x = (float)rng.NextDouble() * 400f - 200f, y = (float)rng.NextDouble() * 400f - 200f;
                float n = OceanWaves.Perlin(x, y);
                Assert.That(n, Is.InRange(-1f, 1f));
                Assert.AreEqual(n, OceanWaves.Perlin(x, y), "same place, same value: every client agrees");
                Assert.AreEqual(n, OceanWaves.Perlin(x + 0.001f, y), 0.02f, "no jumps");
            }
            Assert.AreEqual(0f, OceanWaves.Perlin(3f, -7f), 1e-5f, "zero on the lattice, like any Perlin");
        }

        [Test]
        public void Noise_BreaksUpTheSea_IntoCalmerAndRougherPatches()
        {
            float lo = float.MaxValue, hi = float.MinValue;
            var bumps = new System.Collections.Generic.HashSet<float>();
            for (float x = 0f; x < 2000f; x += 37f)
            for (float z = 0f; z < 2000f; z += 41f)
            {
                float r = OceanWaves.Roughness(x, z, 5f);
                Assert.That(r, Is.InRange(OceanWaves.RoughnessMin, 1f));
                lo = Mathf.Min(lo, r);
                hi = Mathf.Max(hi, r);
                float n = OceanWaves.HeightNoise(x, z, 5f);
                Assert.LessOrEqual(Mathf.Abs(n), OceanWaves.NoiseAmplitude + 1e-5f);
                bumps.Add(Mathf.Round(n * 100f));
            }
            Assert.Greater(hi - lo, 0.25f, "some patches calm, some rough");
            Assert.Greater(bumps.Count, 10, "the noise has bumps of its own");
        }

        [Test]
        public void Waves_Move()
        {
            Assert.AreNotEqual(OceanWaves.Height(10f, 20f, 0f), OceanWaves.Height(10f, 20f, 1f));
        }

        [Test]
        public void EveryWave_SpansAtLeastThreeFineCells()
        {
            foreach (var w in OceanWaves.Waves)
                Assert.GreaterOrEqual(w.Wavelength, 3f * OceanSurface.DefaultStep, "the shader would drop it near the camera");
        }

        [Test]
        public void Swells_AreLongAndCarryMostOfTheHeight_UnderTheChop()
        {
            float swell = 0f, chop = 0f;
            foreach (var w in OceanWaves.Waves)
            {
                if (w.Swell)
                {
                    swell += w.Amplitude;
                    Assert.GreaterOrEqual(w.Wavelength, 40f, "a swell rolls: long enough to see from the air");
                }
                else
                    chop += w.Amplitude;
            }
            Assert.Greater(swell, chop);
            Assert.LessOrEqual(OceanWaves.Waves.Length, 5, "the water shader holds 5 waves (OCEAN_WAVES)");
        }

        [Test]
        public void AxisLines_FineInTheMiddle_GrowingOut_Symmetric()
        {
            float[] lines = OceanSurface.AxisLines(4f, 200f, 15000f, 1.08f);
            int mid = lines.Length / 2;
            Assert.AreEqual(0f, lines[mid]);
            for (int i = 1; i <= mid; i++)
            {
                Assert.AreEqual(-lines[mid + i], lines[mid - i], 1e-3f);
                float gap = lines[mid + i] - lines[mid + i - 1];
                if (lines[mid + i] <= 200f)
                    Assert.AreEqual(4f, gap, 1e-3f, "fine near the camera, so it snaps onto one world grid");
                else
                    Assert.GreaterOrEqual(gap, 4f - 1e-3f);
            }
            Assert.GreaterOrEqual(lines[^1], 15000f, "covers the whole 30 km sea");
        }

        [Test]
        public void Mesh_FitsSixteenBitIndices_AndCarriesTheGridSpacing()
        {
            var mesh = OceanSurface.BuildMesh();
            try
            {
                Assert.LessOrEqual(mesh.vertexCount, 65535, "16-bit indices for mobile");
                Assert.AreEqual(UnityEngine.Rendering.IndexFormat.UInt16, mesh.indexFormat);
                Assert.GreaterOrEqual(mesh.bounds.extents.x, 15000f);

                var vertices = mesh.vertices;
                var uvs = mesh.uv;
                int nearest = 0;
                for (int i = 1; i < vertices.Length; i++)
                    if (vertices[i].sqrMagnitude < vertices[nearest].sqrMagnitude)
                        nearest = i;
                Assert.AreEqual(OceanSurface.DefaultStep, uvs[nearest].x, 1e-4f, "fine spacing under the camera");
                foreach (var v in vertices)
                    Assert.AreEqual(0f, v.y, "flat: the shader adds the waves");
            }
            finally
            {
                Object.DestroyImmediate(mesh);
            }
        }

        [Test]
        public void Snapped_WholeSteps_OnTheSea()
        {
            var p = OceanSurface.Snapped(new Vector3(13.9f, 250f, -6.1f), 4f);
            Assert.AreEqual(new Vector3(12f, 0f, -8f), p);
        }
    }
}

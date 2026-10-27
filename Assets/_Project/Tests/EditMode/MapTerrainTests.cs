using NUnit.Framework;
using UnityEngine;
using WingingIt.Maps;

namespace WingingIt.Tests
{
    public class MapTerrainTests
    {
        [Test]
        public void R16_RoundTripsWithinOneStep()
        {
            var map = new Heightmap(33);
            for (int z = 0; z < 33; z++)
            for (int x = 0; x < 33; x++)
                map.Values[z, x] = (x * 33 + z) / (33f * 33f);
            var back = Heightmap.FromR16(map.ToR16());
            Assert.AreEqual(33, back.Resolution);
            for (int z = 0; z < 33; z++)
            for (int x = 0; x < 33; x++)
                Assert.AreEqual(map.Values[z, x], back.Values[z, x], 1f / 65535f);
        }

        [Test]
        public void R16_RejectsANonSquareFile()
        {
            Assert.Throws<System.ArgumentException>(() => Heightmap.FromR16(new byte[2 * 33 * 34]));
            Assert.Throws<System.ArgumentException>(() => Heightmap.FromR16(new byte[7]));
        }

        [Test]
        public void IsTerrainResolution_MeansPowerOfTwoPlusOne()
        {
            Assert.IsTrue(Heightmap.IsTerrainResolution(1025));
            Assert.IsTrue(Heightmap.IsTerrainResolution(2049));
            Assert.IsFalse(Heightmap.IsTerrainResolution(1024));
            Assert.IsFalse(Heightmap.IsTerrainResolution(17));
        }

        [Test]
        public void Sample_InterpolatesBetweenSamples_AndClampsOutside()
        {
            var map = new Heightmap(2);
            map.Values[0, 0] = 0f; map.Values[0, 1] = 1f; map.Values[1, 0] = 0f; map.Values[1, 1] = 1f;
            Assert.AreEqual(0.25f, map.Sample(0.25f, 0.5f), 1e-5f);
            Assert.AreEqual(1f, map.Sample(2f, 0f), 1e-5f);
        }

        [Test]
        public void MapDefinition_WorldHeightAndValueAreInverses()
        {
            var def = ScriptableObject.CreateInstance<MapDefinition>();
            def.baseHeight = -40f; def.heightRange = 800f;
            Assert.AreEqual(0f, def.WorldHeight(def.ToValue(0f)), 1e-3f);
            Assert.AreEqual(-40f, def.WorldHeight(0f));
            Assert.AreEqual(760f, def.WorldHeight(1f));
            Assert.AreEqual(1f, def.ToValue(5000f), "clamped");
            Object.DestroyImmediate(def);
        }

        [Test]
        public void Layers_FollowHeightAndSlope_AndSumToOne()
        {
            var rules = TerrainRules.Default;
            Vector4 beach = rules.Layers(1f, 2f), meadow = rules.Layers(40f, 3f), cliff = rules.Layers(60f, 50f), peak = rules.Layers(600f, 10f);
            Assert.Greater(beach.y, 0.9f, "sand at the shore");
            Assert.Greater(meadow.x, 0.99f, "grass on gentle low ground");
            Assert.Greater(cliff.z, 0.99f, "rock on a steep face");
            Assert.Greater(peak.w, 0.99f, "snow on a high gentle summit");
            Assert.Greater(rules.Layers(600f, 70f).z, 0.99f, "steep high faces stay bare rock");
            foreach (var w in new[] { beach, meadow, cliff, peak, rules.Layers(200f, 28f), rules.Layers(-20f, 0f) })
                Assert.AreEqual(1f, w.x + w.y + w.z + w.w, 1e-4f);
        }

        [Test]
        public void PickTree_NothingUnderwaterOrAboveTheTreeLine_AndPinesGoHighOaksLow()
        {
            var rules = TerrainRules.Default;
            Assert.IsNull(rules.PickTree(-5f, 0f, 0.01f, 0.5f), "underwater");
            Assert.IsNull(rules.PickTree(500f, 10f, 0.5f, 0.5f), "above the tree line");
            Assert.AreEqual("TreeOak", rules.PickTree(30f, 3f, 0.01f, 0.9f).Value.kind);
            string high = rules.PickTree(100f, 10f, 0.01f, 0.9f).Value.kind;
            Assert.IsTrue(high.StartsWith("TreePine"), high);
        }

        [Test]
        public void Detail_OnlyOnOpenLowGround()
        {
            var rules = TerrainRules.Default;
            Assert.AreEqual(2, rules.Detail(30f, 5f, 0.6f, 0.9f).grass);
            Assert.AreEqual(0, rules.Detail(30f, 40f, 0.6f, 0.1f).grass, "too steep");
            Assert.AreEqual(0, rules.Detail(300f, 5f, 0.6f, 0.1f).grass, "too high");
            Assert.AreEqual(0, rules.Detail(-3f, 5f, 0.6f, 0.1f).grass, "underwater");
        }
    }
}

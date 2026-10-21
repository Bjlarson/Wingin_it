using NUnit.Framework;
using UnityEngine;
using WingingIt.Effects;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Ground-effect dust and spray (living-world.md, section 1): when it kicks up, how much, and in what colour.</summary>
    public class GroundKickupTests
    {
        const float Stall = 32f, Max = 110f;

        [Test]
        public void HighOrSlow_KicksUpNothing()
        {
            Assert.AreEqual(0f, GroundKickup.StrengthAt(40f, Max, Stall, Max), "at the height limit");
            Assert.AreEqual(0f, GroundKickup.StrengthAt(200f, Max, Stall, Max));
            Assert.AreEqual(0f, GroundKickup.StrengthAt(2f, 0.59f * Stall, Stall, Max), "taxiing gives tyre smoke instead");
            Assert.Greater(GroundKickup.StrengthAt(2f, 0.6f * Stall, Stall, Max), 0f, "from 60% of the stall speed");
        }

        [Test]
        public void ThickensAsItDrops_AndAsItSpeedsUp()
        {
            float last = -1f;
            for (float h = 39f; h >= 0f; h -= 3f)
            {
                float s = GroundKickup.StrengthAt(h, Max * 0.8f, Stall, Max);
                Assert.Greater(s, last, $"thicker at {h} m");
                last = s;
            }
            last = -1f;
            for (float v = 0.6f * Stall; v <= Max; v += 8f)
            {
                float s = GroundKickup.StrengthAt(10f, v, Stall, Max);
                Assert.Greater(s, last, $"thicker at {v} m/s");
                last = s;
            }
            Assert.AreEqual(1f, GroundKickup.StrengthAt(0f, Max, Stall, Max), 1e-5f, "full on the deck at top speed");
            Assert.Less(GroundKickup.StrengthAt(35f, Max, Stall, Max), 0.2f, "a few wisps near the top");
        }

        [Test]
        public void Size_GrowsWithStrength_FromHalf()
        {
            float h = GroundKickup.FullSizeHeight;
            Assert.AreEqual(0f, GroundKickup.SizeAt(0f, h));
            Assert.AreEqual(0.5f, GroundKickup.SizeAt(1e-4f, h), 1e-3f);
            Assert.AreEqual(1f, GroundKickup.SizeAt(1f, h), 1e-5f);
            Assert.AreEqual(1f, GroundKickup.SizeAt(1f, 30f), 1e-5f, "no bigger higher up");
        }

        [Test]
        public void Size_ShrinksOnTheDeck_SoTheDustStaysUnderThePlane()
        {
            float last = 2f;
            for (float h = GroundKickup.FullSizeHeight; h >= 0f; h -= 1f)
            {
                float s = GroundKickup.SizeAt(1f, h);
                Assert.LessOrEqual(s, last, $"at {h} m");
                last = s;
            }
            Assert.AreEqual(0.3f, GroundKickup.SizeAt(1f, 0f), 1e-5f, "wheels on the ground: a low prop wash");
            Assert.AreEqual(0.3f, GroundKickup.SizeAt(1f, -1f), 1e-5f);
        }

        [Test]
        public void OnlyTheNearestFour_Emit()
        {
            var others = new[] { 50f, 80f, 120f, 400f };
            Assert.IsTrue(GroundKickup.IsAmongNearest(100f, others), "two closer: third nearest");
            Assert.IsTrue(GroundKickup.IsAmongNearest(300f, others), "three closer: fourth nearest");
            Assert.IsFalse(GroundKickup.IsAmongNearest(500f, others), "four closer");
            Assert.IsTrue(GroundKickup.IsAmongNearest(500f, new float[0]));
        }

        [Test]
        public void Lookup_PicksTheStrongestLayer()
        {
            var layers = new[] { GroundSurface.Grass, GroundSurface.Sand, GroundSurface.Rock, GroundSurface.Snow, GroundSurface.Paved };
            var weights = new float[2, 2, 5];
            weights[0, 0, 0] = 0.6f; weights[0, 0, 1] = 0.4f; // grass
            weights[0, 1, 1] = 0.5f; weights[0, 1, 4] = 0.3f; weights[0, 1, 0] = 0.2f; // sand
            weights[1, 0, 2] = 0.9f; weights[1, 0, 3] = 0.1f; // rock
            weights[1, 1, 4] = 1f; // paved
            var lookup = new EffectsSurface.Lookup(weights, layers);
            Assert.AreEqual(GroundSurface.Grass, lookup.At(0.25f, 0.25f));
            Assert.AreEqual(GroundSurface.Sand, lookup.At(0.75f, 0.25f), "x across, z up");
            Assert.AreEqual(GroundSurface.Rock, lookup.At(0.25f, 0.75f));
            Assert.AreEqual(GroundSurface.Paved, lookup.At(0.75f, 0.75f));
            Assert.AreEqual(GroundSurface.Grass, lookup.At(-1f, -1f), "clamped to the edge");
            Assert.AreEqual(GroundSurface.Paved, lookup.At(2f, 2f));
        }

        [Test]
        public void LayerNames_MapToSurfaces()
        {
            Assert.AreEqual(GroundSurface.Grass, EffectsSurface.FromLayerName("Grass"));
            Assert.AreEqual(GroundSurface.Sand, EffectsSurface.FromLayerName("Sand"));
            Assert.AreEqual(GroundSurface.Rock, EffectsSurface.FromLayerName("Rock"));
            Assert.AreEqual(GroundSurface.Snow, EffectsSurface.FromLayerName("Snow"));
            Assert.AreEqual(GroundSurface.Paved, EffectsSurface.FromLayerName("Paved"));
            Assert.AreEqual(GroundSurface.Grass, EffectsSurface.FromLayerName(null), "unknown: soil");
        }

        [Test]
        public void Colours_AreTheDesignTable_AndPavingKicksUpLess()
        {
            Assert.AreEqual((Color)new Color32(0xF4, 0xFB, 0xFF, 0xFF), EffectsSurface.Color(GroundSurface.Water));
            Assert.AreEqual((Color)new Color32(0xE6, 0xC8, 0x8A, 0xFF), EffectsSurface.Color(GroundSurface.Sand));
            Assert.AreEqual((Color)new Color32(0x8B, 0x6B, 0x4A, 0xFF), EffectsSurface.Color(GroundSurface.Grass), "brown dirt, not green");
            Assert.AreEqual((Color)new Color32(0xA3, 0x9E, 0x96, 0xFF), EffectsSurface.Color(GroundSurface.Rock));
            Assert.AreEqual((Color)new Color32(0xF2, 0xF6, 0xFA, 0xFF), EffectsSurface.Color(GroundSurface.Snow));
            Assert.AreEqual((Color)new Color32(0xB9, 0xB9, 0xC2, 0xFF), EffectsSurface.Color(GroundSurface.Paved));
            Assert.AreEqual(0f, EffectsSurface.Amount(GroundSurface.None), "roofs kick up nothing");
            Assert.Less(EffectsSurface.Amount(GroundSurface.Paved), EffectsSurface.Amount(GroundSurface.Sand));
            Assert.AreEqual(1f, EffectsSurface.Amount(GroundSurface.Water));
        }

        [Test]
        public void Wind_IsTheSeasWind_AndFlat()
        {
            Assert.AreEqual(Wind.Heading, OceanWaves.WindHeading);
            Assert.AreEqual(0f, Wind.Velocity.y);
            Assert.AreEqual(Wind.Speed, Wind.Velocity.magnitude, 1e-4f);
            Assert.Greater(Wind.Direction.x, 0f, "60°: toward the east-north-east");
            Assert.Greater(Wind.Direction.z, 0f);
        }
    }
}

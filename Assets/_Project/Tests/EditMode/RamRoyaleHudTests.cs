using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.UI;

namespace WingingIt.Tests
{
    public class RamRoyaleHudTests
    {
        [Test]
        public void PlanesLeft_ReadsFlyingOverEveryone()
        {
            Assert.AreEqual("7 / 16", HudText.PlanesLeft(7, 16));
            Assert.AreEqual("0 / 16", HudText.PlanesLeft(-1, 16));
        }

        [Test]
        public void RamFlash_IsOursWhenWeFlewIn_OrDidMore_TheirsWhenTheyFlewInAndDidMore_SilentForABump()
        {
            Assert.AreEqual("RAMMED BOT 5!", HudText.RamFlash("BOT 5", 120f, 36f, false, out var won));
            Assert.AreEqual(HudText.Tone.Good, won);
            Assert.AreEqual("RAMMED BOT 5!", HudText.RamFlash("BOT 5", 240f, 240f, false, out var headOn), "a head-on counts as ours");
            Assert.AreEqual(HudText.Tone.Good, headOn);
            Assert.AreEqual("RAMMED BOT 5!", HudText.RamFlash("BOT 5", 30f, 50f, true, out var clipped),
                "we flew in and took their last 30 HP of wing: still ours");
            Assert.AreEqual(HudText.Tone.Good, clipped);
            Assert.AreEqual("RAMMED BY BOT 5", HudText.RamFlash("BOT 5", 36f, 120f, false, out var lost));
            Assert.AreEqual(HudText.Tone.Bad, lost);
            Assert.IsNull(HudText.RamFlash("BOT 5", 0f, 0f, true, out _), "a harmless bump flashes nothing");
            Assert.AreEqual("120 DEALT  ·  36 TAKEN", HudText.RamDamage(119.6f, 36.2f));
        }

        [Test]
        public void PartLost_ByARam_IsKnockedOff()
        {
            Assert.AreEqual("TAIL KNOCKED OFF!", HudText.PartLost(PlanePartType.HorizontalStabilizer, rammed: true));
            Assert.AreEqual("WING SHOT OFF!", HudText.PartLost(PlanePartType.WingLeft));
            Assert.IsNull(HudText.PartLost(PlanePartType.Body, rammed: true));
        }

        [Test]
        public void ArenaEdge_GivesTheDistanceLeft()
        {
            Assert.AreEqual("ARENA EDGE  120 M  ·  TURN BACK", HudText.ArenaEdge(120.4f));
            Assert.AreEqual("ARENA EDGE  0 M  ·  TURN BACK", HudText.ArenaEdge(-5f));
        }

        [Test]
        public void ClosestApproach_HeadOn_HitsAtTheMeetingTime()
        {
            // 200 m apart, closing at 100 m/s, offset 10 m sideways.
            Assert.IsTrue(HudMath.ClosestApproach(new Vector3(10f, 0f, 200f), new Vector3(0f, 0f, -100f), out float t, out float miss));
            Assert.AreEqual(2f, t, 1e-4f);
            Assert.AreEqual(10f, miss, 1e-4f);
            Assert.AreEqual(100f, HudMath.ClosingSpeed(new Vector3(0f, 0f, 200f), new Vector3(0f, 0f, -100f)), 1e-4f);
        }

        [Test]
        public void ClosestApproach_Opening_IsNow()
        {
            Assert.IsFalse(HudMath.ClosestApproach(new Vector3(0f, 0f, 200f), new Vector3(0f, 0f, 50f), out float t, out float miss));
            Assert.AreEqual(0f, t);
            Assert.AreEqual(200f, miss, 1e-4f);
            Assert.Less(HudMath.ClosingSpeed(new Vector3(0f, 0f, 200f), new Vector3(0f, 0f, 50f)), 0f);
            Assert.IsFalse(HudMath.ClosestApproach(new Vector3(0f, 0f, 200f), Vector3.zero, out _, out _), "flying in formation");
        }
    }
}

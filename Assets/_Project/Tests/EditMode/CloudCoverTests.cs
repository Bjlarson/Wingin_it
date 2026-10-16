using NUnit.Framework;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Cloud cover math: how much of a sight line runs through a bank's puffs.</summary>
    public class CloudCoverTests
    {
        static readonly CloudPuff[] Ball = { new(Vector3.zero, new Vector3(100f, 50f, 100f)) };

        [Test]
        public void ChordLength_ThroughTheMiddle_IsTheDiameter()
        {
            Assert.AreEqual(200f, CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-500f, 0f, 0f), new Vector3(500f, 0f, 0f)), 1e-3f);
            Assert.AreEqual(100f, CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(0f, -500f, 0f), new Vector3(0f, 500f, 0f)), 1e-3f,
                "the vertical radius counts going up");
        }

        [Test]
        public void ChordLength_UsesTheBankOrigin_AndMissesBesideIt()
        {
            var origin = new Vector3(1000f, 400f, 0f);
            Assert.AreEqual(200f, CloudCover.ChordLength(Ball, origin, origin + Vector3.left * 300f, origin + Vector3.right * 300f), 1e-3f);
            Assert.AreEqual(0f, CloudCover.ChordLength(Ball, origin, new Vector3(-300f, 400f, 0f), new Vector3(300f, 400f, 0f)));
            Assert.AreEqual(0f, CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-500f, 60f, 0f), new Vector3(500f, 60f, 0f)), "passes over the top");
        }

        [Test]
        public void ChordLength_EndingInside_CountsOnlyTheInsidePart()
        {
            Assert.AreEqual(100f, CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-500f, 0f, 0f), Vector3.zero), 1e-3f);
            Assert.AreEqual(30f, CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-500f, 0f, 0f), new Vector3(-70f, 0f, 0f)), 1e-3f);
            Assert.AreEqual(20f, CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-10f, 0f, 0f), new Vector3(10f, 0f, 0f)), 1e-3f, "both ends inside");
        }

        [Test]
        public void ChordLength_OverlappingPuffs_AreNotCountedTwice()
        {
            CloudPuff[] two =
            {
                new(new Vector3(-50f, 0f, 0f), Vector3.one * 100f), // x -150 .. 50
                new(new Vector3(50f, 0f, 0f), Vector3.one * 100f),  // x -50 .. 150
                new(new Vector3(400f, 0f, 0f), Vector3.one * 20f),  // x 380 .. 420, separate
            };
            Assert.AreEqual(300f + 40f, CloudCover.ChordLength(two, Vector3.zero, new Vector3(-1000f, 0f, 0f), new Vector3(1000f, 0f, 0f)), 1e-3f);
        }

        [Test]
        public void ChordLength_ZeroLengthOrNoPuffs_IsZero()
        {
            Assert.AreEqual(0f, CloudCover.ChordLength(Ball, Vector3.zero, Vector3.zero, Vector3.zero));
            Assert.AreEqual(0f, CloudCover.ChordLength(new CloudPuff[0], Vector3.zero, Vector3.left, Vector3.right));
            Assert.AreEqual(0f, CloudCover.ChordLength(new[] { new CloudPuff(Vector3.zero, Vector3.zero) }, Vector3.zero, Vector3.left, Vector3.right));
        }

        [Test]
        public void Immersion_IsZeroOutside_AndOneAtTheCenter()
        {
            Assert.AreEqual(1f, CloudCover.Immersion(Ball, Vector3.zero, Vector3.zero), 1e-4f);
            Assert.AreEqual(0.5f, CloudCover.Immersion(Ball, Vector3.zero, new Vector3(50f, 0f, 0f)), 1e-4f);
            Assert.AreEqual(0.5f, CloudCover.Immersion(Ball, Vector3.zero, new Vector3(0f, 25f, 0f)), 1e-4f, "relative to the puff's own radii");
            Assert.AreEqual(0f, CloudCover.Immersion(Ball, Vector3.zero, new Vector3(150f, 0f, 0f)));
        }

        [Test]
        public void BlockDepth_LetsAShallowEdgeThrough()
        {
            // A plane 20 m inside the edge is still seen from outside; one 60 m in is not.
            float shallow = CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-600f, 0f, 0f), new Vector3(-80f, 0f, 0f));
            float deep = CloudCover.ChordLength(Ball, Vector3.zero, new Vector3(-600f, 0f, 0f), new Vector3(-40f, 0f, 0f));
            Assert.Less(shallow, CloudCover.BlockDepth);
            Assert.GreaterOrEqual(deep, CloudCover.BlockDepth);
        }
    }
}

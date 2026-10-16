using NUnit.Framework;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Approach lane polyline math: length, distance from the lane, and the next waypoint to steer for.</summary>
    public class ApproachLaneTests
    {
        // An L: 300 m east at 100 m up, then 400 m north while descending to 40 m.
        static readonly Vector3[] Path = { new(0f, 100f, 0f), new(300f, 100f, 0f), new(300f, 40f, 400f) };
        static readonly float SecondLeg = Vector3.Distance(Path[1], Path[2]);

        [Test]
        public void Length_AddsUpTheLegs_In3D()
        {
            Assert.AreEqual(300f + SecondLeg, ApproachLane.PathLength(Path), 0.01f);
            Assert.AreEqual(0f, ApproachLane.PathLength(new[] { Vector3.one }));
        }

        [Test]
        public void Distance_IsHorizontal_AndAlongCountsFromTheStart()
        {
            // Beside the first leg, 50 m south of it and far below: altitude doesn't count.
            float d = ApproachLane.FlatDistanceToPath(Path, new Vector3(120f, -500f, -50f), out float along);
            Assert.AreEqual(50f, d, 0.01f);
            Assert.AreEqual(120f, along, 0.01f);

            // Halfway up the second leg, 30 m east of it.
            d = ApproachLane.FlatDistanceToPath(Path, new Vector3(330f, 70f, 200f), out along);
            Assert.AreEqual(30f, d, 0.01f);
            Assert.AreEqual(300f + SecondLeg * 0.5f, along, 0.01f);
        }

        [Test]
        public void Distance_BeyondTheEnds_IsToTheEndPoints()
        {
            float d = ApproachLane.FlatDistanceToPath(Path, new Vector3(-40f, 0f, 30f), out float along);
            Assert.AreEqual(50f, d, 0.01f);
            Assert.AreEqual(0f, along, 0.01f);

            d = ApproachLane.FlatDistanceToPath(Path, new Vector3(300f, 0f, 500f), out along);
            Assert.AreEqual(100f, d, 0.01f);
            Assert.AreEqual(300f + SecondLeg, along, 0.01f);
        }

        [Test]
        public void Distance_FromAnEmptyPath_IsInfinite()
        {
            Assert.IsTrue(float.IsPositiveInfinity(ApproachLane.FlatDistanceToPath(new Vector3[0], Vector3.zero, out _)));
        }

        [Test]
        public void NextIndex_IsTheFirstPointStillAhead()
        {
            Assert.AreEqual(1, ApproachLane.NextIndex(Path, 0f));
            Assert.AreEqual(1, ApproachLane.NextIndex(Path, 299f));
            Assert.AreEqual(2, ApproachLane.NextIndex(Path, 301f));
            Assert.AreEqual(2, ApproachLane.NextIndex(Path, 5000f), "past the end: the last point");
        }
    }
}

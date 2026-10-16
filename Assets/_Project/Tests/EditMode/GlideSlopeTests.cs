using NUnit.Framework;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The landing-assist glide path: where it is, and when a plane counts as on approach and on the path.</summary>
    public class GlideSlopeTests
    {
        static readonly Vector2 Runway = new(40f, 600f);
        static readonly Vector3 DownTheRunway = new(0f, -2f, 40f);

        GlideSlope _slope;

        [SetUp]
        public void SetUp() => _slope = new GlideSlope();

        /// <summary>A runway-local point <paramref name="distance"/> m before the aim point, offset from the path.</summary>
        Vector3 Before(float distance, float right = 0f, float above = 0f) =>
            _slope.PathPoint(distance, Runway.y) + new Vector3(right, above, 0f);

        [Test]
        public void PathPoint_DescendsAtTheSlopeAngle_ToTheAimPoint()
        {
            Assert.AreEqual(-300f + _slope.aimDistance, _slope.AimZ(Runway.y), 1e-4f);
            Assert.AreEqual(Vector3.zero.y, _slope.PathPoint(0f, Runway.y).y, 1e-4f);
            Assert.AreEqual(1000f * Mathf.Tan(_slope.angle * Mathf.Deg2Rad), _slope.PathPoint(1000f, Runway.y).y, 1e-3f);
            Assert.AreEqual(0f, _slope.HeightAt(-100f), "flat past the aim point");
        }

        [Test]
        public void OnThePath_HeadingDownTheRunway_IsOnPath()
        {
            var guide = _slope.Evaluate(Before(800f), DownTheRunway, Runway);
            Assert.IsTrue(guide.OnApproach);
            Assert.IsTrue(guide.OnPath);
            Assert.AreEqual(800f, guide.Distance, 1e-3f);
            Assert.AreEqual(0f, guide.HeightError, 1e-3f);
        }

        [Test]
        public void HighLowAndOffCenter_AreReported()
        {
            var high = _slope.Evaluate(Before(800f, above: 60f), DownTheRunway, Runway);
            Assert.IsTrue(high.OnApproach && high.IsHigh && !high.OnPath);
            Assert.AreEqual(60f, high.HeightError, 1e-3f);

            var low = _slope.Evaluate(Before(800f, above: -40f), DownTheRunway, Runway);
            Assert.IsTrue(low.IsLow);

            var right = _slope.Evaluate(Before(800f, right: 60f), DownTheRunway, Runway);
            Assert.IsTrue(right.OnApproach && right.IsOffCenter);
            Assert.AreEqual(60f, right.Lateral, 1e-3f);
        }

        [Test]
        public void Tolerance_GrowsWithDistance()
        {
            Assert.AreEqual(_slope.baseTolerance, _slope.ToleranceAt(0f), 1e-4f);
            Assert.Greater(_slope.ToleranceAt(2000f), _slope.ToleranceAt(500f));
            Assert.IsTrue(_slope.Evaluate(Before(2000f, above: 40f), DownTheRunway, Runway).OnPath, "far out, 40 m high is close enough");
            Assert.IsFalse(_slope.Evaluate(Before(200f, above: 40f), DownTheRunway, Runway).OnPath, "close in, it isn't");
        }

        [Test]
        public void NotAnApproach_WhenHeadingAway_OutOfRange_OutsideTheCone_OrOverflying()
        {
            Assert.IsFalse(_slope.Evaluate(Before(800f), -DownTheRunway, Runway).OnApproach, "flying away from the runway");
            Assert.IsFalse(_slope.Evaluate(Before(800f), new Vector3(40f, 0f, 0f), Runway).OnApproach, "crossing it");
            Assert.IsFalse(_slope.Evaluate(Before(_slope.range + 100f), DownTheRunway, Runway).OnApproach, "too far out");
            Assert.IsFalse(_slope.Evaluate(Before(300f, right: 400f), DownTheRunway, Runway).OnApproach, "outside the cone");
            Assert.IsFalse(_slope.Evaluate(Before(800f, above: 300f), DownTheRunway, Runway).OnApproach, "high overflight");
        }

        [Test]
        public void PastTheAimPoint_StillOnApproachOverTheRunway_UntilItsEnd()
        {
            var over = _slope.Evaluate(new Vector3(0f, 3f, 0f), DownTheRunway, Runway);
            Assert.IsTrue(over.OnApproach);
            Assert.Less(over.Distance, 0f);
            Assert.IsFalse(_slope.Evaluate(new Vector3(0f, 30f, 320f), DownTheRunway, Runway).OnApproach, "past the far end");
        }

        [Test]
        public void Airfield_EvaluatesInItsOwnFrame()
        {
            // A runway heading east (+X in world).
            var root = new GameObject("Airfield");
            try
            {
                root.transform.SetPositionAndRotation(new Vector3(100f, 5f, 50f), Quaternion.Euler(0f, 90f, 0f));
                var field = root.AddComponent<Airfield>();
                field.Configure("Test Field", 0, true, Runway, null, null, null);
                Vector3 onPath = field.GlidePathPoint(800f);
                Assert.Less(onPath.x, 100f - 800f, "the approach is west of an east-facing runway");

                var guide = field.GetApproach(onPath, new Vector3(40f, -2f, 0f));
                Assert.IsTrue(guide.OnPath);
                Assert.AreEqual(800f, guide.Distance, 1e-2f);
                Assert.IsFalse(field.GetApproach(onPath, new Vector3(0f, -2f, 40f)).OnApproach, "heading north, across it");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }
    }
}

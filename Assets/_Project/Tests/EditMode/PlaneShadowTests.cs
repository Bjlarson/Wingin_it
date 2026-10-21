using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>The altitude blob shadow: shrinking and fading with height, handing over from the real shadow, and its footprint.</summary>
    public class PlaneShadowTests
    {
        [Test]
        public void OnTheGround_FullSizeAndDarkness()
        {
            var (scale, fade) = PlaneShadow.Shape(0f, 200f, 0.5f);
            Assert.AreEqual(1f, scale, 1e-5f);
            Assert.AreEqual(1f, fade, 1e-5f);
        }

        [Test]
        public void Higher_IsSmallerAndFainter_UntilGone()
        {
            float lastScale = 2f, lastFade = 2f;
            for (float h = 0f; h <= 200f; h += 20f)
            {
                var (scale, fade) = PlaneShadow.Shape(h, 200f, 0.5f);
                Assert.Less(scale, lastScale, $"shrinks at {h} m");
                Assert.Less(fade, lastFade, $"fades at {h} m");
                lastScale = scale;
                lastFade = fade;
            }
            var (top, gone) = PlaneShadow.Shape(500f, 200f, 0.5f);
            Assert.AreEqual(0.5f, top, 1e-5f, "never smaller than the high scale");
            Assert.AreEqual(0f, gone, 1e-5f);
        }

        [Test]
        public void Handover_HiddenWhileTheRealShadowReaches_ThenFadesIn()
        {
            Assert.AreEqual(0f, PlaneShadow.Handover(50f, 150f, 0.2f), "the plane's own shadow is there");
            Assert.AreEqual(0f, PlaneShadow.Handover(120f, 150f, 0.2f));
            Assert.That(PlaneShadow.Handover(135f, 150f, 0.2f), Is.GreaterThan(0f).And.LessThan(1f));
            Assert.AreEqual(1f, PlaneShadow.Handover(150f, 150f, 0.2f));
            Assert.AreEqual(1f, PlaneShadow.Handover(400f, 150f, 0.2f), "past the shadow distance");
            Assert.AreEqual(1f, PlaneShadow.Handover(10f, 0f, 0.2f), "shadows off: the blob everywhere");
        }

        [Test]
        public void LevelFlight_FullFootprintAlongTheHeading()
        {
            var heading = Quaternion.Euler(0f, 40f, 0f);
            var (forward, span, length) = PlaneShadow.Footprint(heading * Vector3.forward, heading * Vector3.right,
                heading * Vector3.up, Vector3.up);
            Assert.AreEqual(1f, span, 1e-4f);
            Assert.AreEqual(1f, length, 1e-4f);
            Assert.Greater(Vector3.Dot(forward, heading * Vector3.forward), 0.999f);
        }

        [Test]
        public void Banked_SpanShrinks_LengthStays()
        {
            var pose = Quaternion.Euler(0f, 0f, 60f);
            var (_, span, length) = PlaneShadow.Footprint(pose * Vector3.forward, pose * Vector3.right, pose * Vector3.up, Vector3.up);
            Assert.AreEqual(0.5f, span, 1e-3f, "cos 60°");
            Assert.AreEqual(1f, length, 1e-4f);
        }

        [Test]
        public void KnifeEdgeOrVertical_NeverVanishes()
        {
            var knife = Quaternion.Euler(0f, 0f, 90f);
            var (_, span, _) = PlaneShadow.Footprint(knife * Vector3.forward, knife * Vector3.right, knife * Vector3.up, Vector3.up);
            Assert.GreaterOrEqual(span, 0.15f);

            var dive = Quaternion.Euler(90f, 0f, 0f); // nose straight down
            var (forward, _, length) = PlaneShadow.Footprint(dive * Vector3.forward, dive * Vector3.right, dive * Vector3.up, Vector3.up);
            Assert.GreaterOrEqual(length, 0.15f);
            Assert.AreEqual(1f, forward.magnitude, 1e-4f, "still has a heading (from the plane's up)");
            Assert.AreEqual(0f, forward.y, 1e-4f, "flat on the ground");
        }

        [Test]
        public void OnASlope_LiesFlatOnIt()
        {
            var normal = Quaternion.Euler(25f, 0f, 0f) * Vector3.up;
            var (forward, _, _) = PlaneShadow.Footprint(Vector3.forward, Vector3.right, Vector3.up, normal);
            Assert.AreEqual(0f, Vector3.Dot(forward, normal), 1e-4f);
        }
    }
}

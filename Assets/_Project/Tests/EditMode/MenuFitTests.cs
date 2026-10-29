using NUnit.Framework;
using UnityEngine;
using WingingIt.UI;

namespace WingingIt.Tests
{
    public class MenuFitTests
    {
        [Test]
        public void SixteenByNine_IsTheReferenceScreen()
        {
            var (origin, scale, width, height) = MenuMath.Fit(1080f, new Rect(0f, 0f, 1920f, 1080f), 1f);
            Assert.AreEqual(Vector2.zero, origin);
            Assert.AreEqual(1f, scale, 1e-4f);
            Assert.AreEqual(1920f, width, 0.01f);
            Assert.AreEqual(1080f, height, 0.01f);
        }

        [Test]
        public void Tablet_ScalesByWidth_SoA1920LayoutFits()
        {
            var (_, _, width, height) = MenuMath.Fit(1536f, new Rect(0f, 0f, 2048f, 1536f), 1f); // 4:3
            Assert.AreEqual(1920f, width, 0.01f, "as wide as the layout");
            Assert.Greater(height, 1080f, "and taller");
        }

        [Test]
        public void Phone_StaysInsideTheNotchSafeArea()
        {
            // 2532 × 1170 landscape, notch side cut 141 px each side (19.5:9).
            var safe = new Rect(141f, 63f, 2250f, 1107f);
            var (origin, scale, width, height) = MenuMath.Fit(1170f, safe, 1f);
            Assert.AreEqual(141f, origin.x, 0.01f);
            Assert.AreEqual(0f, origin.y, 0.01f, "GUI y is from the top: the bottom inset doesn't move the origin");
            Assert.GreaterOrEqual(width, 1920f - 0.01f);
            Assert.AreEqual(1080f, height, 0.01f);
            Assert.AreEqual(safe.width, width * scale, 0.01f, "fills the safe width exactly");
        }

        [Test]
        public void BiggerUiScale_IsASmallerVirtualScreen()
        {
            var (_, scale, width, height) = MenuMath.Fit(1080f, new Rect(0f, 0f, 1920f, 1080f), 1.25f);
            Assert.AreEqual(1.25f, scale, 1e-4f);
            Assert.AreEqual(1080f / 1.25f, height, 0.01f);
            Assert.AreEqual(1920f / 1.25f, width, 0.01f);
        }
    }
}

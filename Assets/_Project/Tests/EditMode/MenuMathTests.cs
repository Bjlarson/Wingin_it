using NUnit.Framework;
using WingingIt.UI;

namespace WingingIt.Tests
{
    public class MenuMathTests
    {
        [Test]
        public void Step_WrapsBothWays()
        {
            Assert.AreEqual(1, MenuMath.Step(0, 1, 3));
            Assert.AreEqual(0, MenuMath.Step(2, 1, 3));
            Assert.AreEqual(2, MenuMath.Step(0, -1, 3));
            Assert.AreEqual(0, MenuMath.Step(0, 1, 1), "one row stays put");
            Assert.AreEqual(0, MenuMath.Step(5, 1, 0), "no rows");
        }

        [Test]
        public void Nudge_StepsOneNotch_SnappedAndClamped()
        {
            Assert.AreEqual(0.85f, MenuMath.Nudge(0.8f, 1, 0f, 1f, 0.05f), 1e-5f);
            Assert.AreEqual(0.75f, MenuMath.Nudge(0.8f, -1, 0f, 1f, 0.05f), 1e-5f);
            Assert.AreEqual(1f, MenuMath.Nudge(1f, 1, 0f, 1f, 0.05f), 1e-5f);
            Assert.AreEqual(0f, MenuMath.Nudge(0.01f, -1, 0f, 1f, 0.05f), 1e-5f);
            // off-grid values snap to the nearest notch first
            Assert.AreEqual(0.85f, MenuMath.Nudge(0.81f, 1, 0f, 1f, 0.05f), 1e-5f);
            // notches count from the minimum, in the slider's own units
            Assert.AreEqual(0.3f, MenuMath.Nudge(0.25f, 1, 0.25f, 3f, 0.05f), 1e-4f);
            Assert.AreEqual(61f, MenuMath.Nudge(60f, 1, 50f, 80f, 1f), 1e-4f);
        }

        [Test]
        public void PanelFade_RisesOverTheFadeTime()
        {
            Assert.AreEqual(0f, MenuMath.PanelFade(0f));
            Assert.AreEqual(0.5f, MenuMath.PanelFade(MenuMath.PanelFadeTime / 2f), 1e-5f);
            Assert.AreEqual(1f, MenuMath.PanelFade(MenuMath.PanelFadeTime));
            Assert.AreEqual(1f, MenuMath.PanelFade(10f));
        }

        [Test]
        public void Fraction_IsTheSliderPosition()
        {
            Assert.AreEqual(0.5f, MenuMath.Fraction(65f, 50f, 80f), 1e-5f);
            Assert.AreEqual(0f, MenuMath.Fraction(10f, 50f, 80f));
            Assert.AreEqual(1f, MenuMath.Fraction(100f, 50f, 80f));
        }
    }
}

using NUnit.Framework;
using WingingIt.Input;

namespace WingingIt.Tests
{
    public class TouchStickShapingTests
    {
        [Test]
        public void InsideTheDeadzone_DoesNothing()
        {
            Assert.AreEqual(0f, PlayerFlightInput.ShapeTouchAxis(0.04f, 0.05f, 0.3f, 1f));
            Assert.AreEqual(0f, PlayerFlightInput.ShapeTouchAxis(-0.05f, 0.05f, 0.3f, 1f));
        }

        [Test]
        public void FullTravel_IsFullInput_BothWays()
        {
            Assert.AreEqual(1f, PlayerFlightInput.ShapeTouchAxis(1f, 0.05f, 0.3f, 1f), 1e-5f);
            Assert.AreEqual(-1f, PlayerFlightInput.ShapeTouchAxis(-1f, 0.05f, 0.3f, 1f), 1e-5f);
        }

        [Test]
        public void Expo_GivesFinerControlNearTheMiddle()
        {
            float linear = PlayerFlightInput.ShapeTouchAxis(0.5f, 0f, 0f, 1f);
            float expo = PlayerFlightInput.ShapeTouchAxis(0.5f, 0f, 0.5f, 1f);
            Assert.AreEqual(0.5f, linear, 1e-5f);
            Assert.Less(expo, linear);
            Assert.Greater(expo, 0f);
        }

        [Test]
        public void Sensitivity_ScalesButNeverPastFull()
        {
            float normal = PlayerFlightInput.ShapeTouchAxis(0.5f, 0f, 0f, 1f);
            Assert.AreEqual(normal * 1.5f, PlayerFlightInput.ShapeTouchAxis(0.5f, 0f, 0f, 1.5f), 1e-5f);
            Assert.AreEqual(1f, PlayerFlightInput.ShapeTouchAxis(0.9f, 0f, 0f, 3f), 1e-5f);
        }
    }
}

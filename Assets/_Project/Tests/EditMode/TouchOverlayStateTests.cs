using NUnit.Framework;
using WingingIt.Settings;
using WingingIt.UI;

namespace WingingIt.Tests
{
    public class TouchOverlayStateTests
    {
        [Test]
        public void PhoneShowsTouchControlsUntilAControllerConnects()
        {
            var s = new TouchOverlayState();
            Assert.IsTrue(s.Show(TouchControlsMode.Auto, touchPlatform: true));
            s.PadConnected();
            Assert.IsFalse(s.Show(TouchControlsMode.Auto, true));
        }

        [Test]
        public void AlreadyPairedControllerHidesThemOnFirstUse()
        {
            var s = new TouchOverlayState();
            s.PadsChanged(anyPad: false);
            Assert.IsTrue(s.Show(TouchControlsMode.Auto, true));
            s.PadUsed();
            Assert.IsFalse(s.Show(TouchControlsMode.Auto, true));
        }

        [Test]
        public void UnpluggingTheLastControllerOrTouchingBringsThemBack()
        {
            var s = new TouchOverlayState();
            s.PadConnected();
            s.PadsChanged(anyPad: true); // another pad still connected
            Assert.IsFalse(s.Show(TouchControlsMode.Auto, true));
            s.PadsChanged(anyPad: false);
            Assert.IsTrue(s.Show(TouchControlsMode.Auto, true));

            s.PadUsed();
            s.Touched();
            Assert.IsTrue(s.Show(TouchControlsMode.Auto, true));
        }

        [Test]
        public void DesktopShowsThemOnlyAfterATouch_AndSettingsOverride()
        {
            var s = new TouchOverlayState();
            Assert.IsFalse(s.Show(TouchControlsMode.Auto, touchPlatform: false));
            s.Touched();
            Assert.IsTrue(s.Show(TouchControlsMode.Auto, false));

            s.PadConnected();
            Assert.IsTrue(s.Show(TouchControlsMode.On, false));
            Assert.IsFalse(s.Show(TouchControlsMode.Off, true));
        }
    }
}

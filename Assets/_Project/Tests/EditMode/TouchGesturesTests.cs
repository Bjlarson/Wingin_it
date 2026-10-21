using NUnit.Framework;
using UnityEngine;
using WingingIt.Input;

namespace WingingIt.Tests.EditMode
{
    public class TouchGesturesTests
    {
        const float Dt = 0.02f;

        static void Run(TouchGestures g, float seconds, bool down, Vector2 pos, ref int taps, ref int doubles)
        {
            for (float t = 0f; t < seconds; t += Dt)
            {
                g.Update(Dt, down, pos, 40f);
                if (g.Tap) taps++;
                if (g.DoubleTap) doubles++;
            }
        }

        [Test]
        public void Hold_FiresGunsAfterTapTime_AndStopsOnRelease()
        {
            var g = new TouchGestures();
            int taps = 0, doubles = 0;
            Run(g, 0.1f, true, Vector2.zero, ref taps, ref doubles);
            Assert.IsFalse(g.Holding);
            Run(g, 0.3f, true, Vector2.zero, ref taps, ref doubles);
            Assert.IsTrue(g.Holding);
            Run(g, 0.1f, false, Vector2.zero, ref taps, ref doubles);
            Assert.IsFalse(g.Holding);
            Run(g, 1f, false, Vector2.zero, ref taps, ref doubles);
            Assert.AreEqual(0, taps);
        }

        [Test]
        public void QuickTap_FiresOnceAfterDoubleTapWindow()
        {
            var g = new TouchGestures();
            int taps = 0, doubles = 0;
            Run(g, 0.1f, true, Vector2.zero, ref taps, ref doubles);
            Run(g, 0.2f, false, Vector2.zero, ref taps, ref doubles);
            Assert.AreEqual(0, taps);
            Run(g, 0.3f, false, Vector2.zero, ref taps, ref doubles);
            Assert.AreEqual(1, taps);
            Assert.AreEqual(0, doubles);
        }

        [Test]
        public void DoubleTap_FiresAbilityAndNoTap()
        {
            var g = new TouchGestures();
            int taps = 0, doubles = 0;
            Run(g, 0.1f, true, Vector2.zero, ref taps, ref doubles);
            Run(g, 0.1f, false, Vector2.zero, ref taps, ref doubles);
            Run(g, 0.1f, true, Vector2.zero, ref taps, ref doubles);
            Run(g, 1f, false, Vector2.zero, ref taps, ref doubles);
            Assert.AreEqual(1, doubles);
            Assert.AreEqual(0, taps);
        }

        [Test]
        public void QuickSwipe_IsNotATap()
        {
            var g = new TouchGestures();
            int taps = 0, doubles = 0;
            g.Update(Dt, true, Vector2.zero, 40f);
            g.Update(Dt, true, new Vector2(200f, 0f), 40f);
            Run(g, 1f, false, Vector2.zero, ref taps, ref doubles);
            Assert.AreEqual(0, taps);
        }
    }
}

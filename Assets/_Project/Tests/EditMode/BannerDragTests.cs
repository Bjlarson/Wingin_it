using NUnit.Framework;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class BannerDragTests
    {
        static float Base(FlightStat stat) => stat switch
        {
            FlightStat.MaxSpeed => 100f,
            FlightStat.Climb => 1f,
            FlightStat.Acceleration => 20f,
            _ => 0f,
        };

        [Test]
        public void Drag_SlowsTopSpeed_Climb_AndAcceleration()
        {
            var stack = new StatModifierStack(Base);
            new BannerDrag().Apply(stack);
            Assert.AreEqual(75f, stack.Get(FlightStat.MaxSpeed), 0.001f);
            Assert.AreEqual(0.7f, stack.Get(FlightStat.Climb), 0.001f);
            Assert.AreEqual(14f, stack.Get(FlightStat.Acceleration), 0.001f);
        }

        [Test]
        public void ApplyingTwice_DoesNotStack_AndRemoveRestoresEverything()
        {
            var stack = new StatModifierStack(Base);
            var drag = new BannerDrag();
            drag.Apply(stack);
            drag.Apply(stack);
            Assert.AreEqual(75f, stack.Get(FlightStat.MaxSpeed), 0.001f);

            BannerDrag.Remove(stack);
            Assert.AreEqual(100f, stack.Get(FlightStat.MaxSpeed), 0.001f);
            Assert.AreEqual(1f, stack.Get(FlightStat.Climb), 0.001f);
            Assert.AreEqual(20f, stack.Get(FlightStat.Acceleration), 0.001f);
        }

        [Test]
        public void CapSpeed_FullThrottleAndDiveNeverPassTheCap()
        {
            // Every step tries to gain 5 m/s (full throttle plus a dive); the carrier stays at the cap.
            float speed = 70f;
            for (int i = 0; i < 500; i++)
            {
                speed = BannerDrag.CapSpeed(speed, speed + 5f, 75f, 14f, 0.02f);
                Assert.LessOrEqual(speed, 75f);
            }
            Assert.AreEqual(75f, speed, 0.001f);
        }

        [Test]
        public void CapSpeed_AlreadyFaster_BleedsDownWithoutGaining()
        {
            // Grabbed the banner at 100 m/s in a dive: no gain, eases down to the cap at the bleed rate.
            float speed = BannerDrag.CapSpeed(100f, 103f, 75f, 14f, 0.5f);
            Assert.AreEqual(93f, speed, 0.001f);
            for (int i = 0; i < 10; i++)
                speed = BannerDrag.CapSpeed(speed, speed + 3f, 75f, 14f, 0.5f);
            Assert.AreEqual(75f, speed, 0.001f);
        }

        [Test]
        public void CapSpeed_UnderTheCap_Untouched()
        {
            Assert.AreEqual(60f, BannerDrag.CapSpeed(58f, 60f, 75f, 14f, 0.02f));
            Assert.AreEqual(50f, BannerDrag.CapSpeed(58f, 50f, 75f, 14f, 0.02f));
        }

        [Test]
        public void Drag_StacksWithOtherModifiers_AndRemovingItLeavesThemAlone()
        {
            var stack = new StatModifierStack(Base);
            var other = new object();
            stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, 0.85f, other);
            new BannerDrag().Apply(stack);
            Assert.AreEqual(100f * 0.85f * 0.75f, stack.Get(FlightStat.MaxSpeed), 0.001f);
            BannerDrag.Remove(stack);
            Assert.AreEqual(85f, stack.Get(FlightStat.MaxSpeed), 0.001f);
        }
    }
}

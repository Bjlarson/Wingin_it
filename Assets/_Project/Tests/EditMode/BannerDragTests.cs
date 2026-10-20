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

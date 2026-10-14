using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class StatModifierStackTests
    {
        FlightStats _stats;
        StatModifierStack _stack;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<FlightStats>();
            _stats.maxSpeed = 100f;
            _stats.rollRate = 200f;
            _stack = new StatModifierStack(_stats.GetBase);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        [Test]
        public void NoModifiers_ReturnsBaseValues()
        {
            Assert.AreEqual(100f, _stack.Get(FlightStat.MaxSpeed));
            Assert.AreEqual(1f, _stack.Get(FlightStat.Thrust));
            Assert.AreEqual(0f, _stack.Get(FlightStat.RollBias));
        }

        [Test]
        public void AddsApplyBeforeMultipliers()
        {
            _stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, 0.5f, "a");
            _stack.Add(FlightStat.MaxSpeed, ModifierOp.Add, 20f, "b");
            Assert.AreEqual(60f, _stack.Get(FlightStat.MaxSpeed), 0.0001f);
        }

        [Test]
        public void MultipliersStack()
        {
            _stack.Add(FlightStat.RollRate, ModifierOp.Multiply, 0.5f, "wing");
            _stack.Add(FlightStat.RollRate, ModifierOp.Multiply, 0.5f, "tail");
            Assert.AreEqual(50f, _stack.Get(FlightStat.RollRate), 0.0001f);
        }

        [Test]
        public void RemoveAllFromSource_RemovesOnlyThatSource()
        {
            var leftWing = new object();
            _stack.Add(FlightStat.RollBias, ModifierOp.Add, -40f, leftWing);
            _stack.Add(FlightStat.Lift, ModifierOp.Multiply, 0.6f, leftWing);
            _stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, 0.8f, "body");

            Assert.AreEqual(2, _stack.RemoveAllFromSource(leftWing));
            Assert.AreEqual(0f, _stack.Get(FlightStat.RollBias));
            Assert.AreEqual(1f, _stack.Get(FlightStat.Lift));
            Assert.AreEqual(80f, _stack.Get(FlightStat.MaxSpeed), 0.0001f);
        }

        [Test]
        public void ChangingBase_UpdatesValues()
        {
            _stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, 0.5f, "x");
            var other = ScriptableObject.CreateInstance<FlightStats>();
            other.maxSpeed = 200f;
            _stack.SetBase(other.GetBase);
            Assert.AreEqual(100f, _stack.Get(FlightStat.MaxSpeed), 0.0001f);
            Object.DestroyImmediate(other);
        }

        [Test]
        public void Changed_FiresOnAddAndRemove()
        {
            int count = 0;
            _stack.Changed += () => count++;
            _stack.Add(FlightStat.Thrust, ModifierOp.Multiply, 0f, "engine");
            _stack.RemoveAllFromSource("engine");
            _stack.RemoveAllFromSource("nothing");
            Assert.AreEqual(2, count);
        }
    }
}

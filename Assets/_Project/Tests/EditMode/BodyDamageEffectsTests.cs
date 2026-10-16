using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class BodyDamageEffectsTests
    {
        FlightStats _stats;
        StatModifierStack _stack;
        BodyDamageEffects _effects;
        readonly object _source = new();

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<FlightStats>();
            _stack = new StatModifierStack(_stats.GetBase);
            _effects = new BodyDamageEffects();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        [TestCase(1f, BodyDamageState.Healthy)]
        [TestCase(0.6f, BodyDamageState.Healthy)]
        [TestCase(0.59f, BodyDamageState.Damaged)]
        [TestCase(0.25f, BodyDamageState.Damaged)]
        [TestCase(0.24f, BodyDamageState.Critical)]
        [TestCase(0.01f, BodyDamageState.Critical)]
        [TestCase(0f, BodyDamageState.Destroyed)]
        public void State_FollowsBodyHealthThresholds(float normalizedHealth, BodyDamageState expected) =>
            Assert.AreEqual(expected, _effects.GetState(normalizedHealth));

        [Test]
        public void Healthy_NoModifiers()
        {
            _effects.Apply(_stack, BodyDamageState.Healthy, _source);
            Assert.IsEmpty(_stack.Modifiers);
        }

        [Test]
        public void Damaged_CutsTopSpeedOnly()
        {
            _effects.Apply(_stack, BodyDamageState.Damaged, _source);
            Assert.AreEqual(_stats.maxSpeed * 0.85f, _stack.Get(FlightStat.MaxSpeed), 0.001f);
            Assert.AreEqual(1f, _stack.Get(FlightStat.Climb));
            Assert.AreEqual(0f, _stack.Get(FlightStat.EngineSputter));
        }

        [Test]
        public void Critical_CutsSpeedAndClimb_AndSputters()
        {
            _effects.Apply(_stack, BodyDamageState.Critical, _source);
            Assert.AreEqual(_stats.maxSpeed * 0.85f, _stack.Get(FlightStat.MaxSpeed), 0.001f);
            Assert.AreEqual(0.7f, _stack.Get(FlightStat.Climb), 0.001f);
            Assert.Greater(_stack.Get(FlightStat.EngineSputter), 0f);
        }

        [Test]
        public void Reapplying_ReplacesPreviousState_AndLeavesOtherSourcesAlone()
        {
            var partSource = new object();
            _stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, 0.5f, partSource);
            _effects.Apply(_stack, BodyDamageState.Critical, _source);
            _effects.Apply(_stack, BodyDamageState.Damaged, _source);
            Assert.AreEqual(_stats.maxSpeed * 0.5f * 0.85f, _stack.Get(FlightStat.MaxSpeed), 0.001f);
            Assert.AreEqual(1f, _stack.Get(FlightStat.Climb));
        }
    }
}

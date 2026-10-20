using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class PartFlightEffectsTests
    {
        const float RollRate = 200f;

        FlightStats _stats;
        StatModifierStack _stack;
        PartFlightEffects _effects;
        readonly object _source = new();

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<FlightStats>();
            _stats.rollRate = RollRate;
            _stack = new StatModifierStack(_stats.GetBase);
            _effects = new PartFlightEffects();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        void Apply(params PlanePartType[] lost)
        {
            int mask = 0;
            foreach (var type in lost)
                mask |= PartFlightEffects.Bit(type);
            _effects.Apply(_stack, mask, RollRate, _source);
        }

        [Test]
        public void NothingLost_NoModifiers()
        {
            Apply();
            Assert.IsEmpty(_stack.Modifiers);
        }

        [Test]
        public void LostLeftWing_RollsLeft_LosesLift()
        {
            Apply(PlanePartType.WingLeft);
            Assert.AreEqual(-_effects.wingRollBias * RollRate, _stack.Get(FlightStat.RollBias), 0.001f);
            Assert.AreEqual(_effects.wingLift, _stack.Get(FlightStat.Lift), 0.001f);
            Assert.Less(_stack.Get(FlightStat.RollStability), 1f);
            Assert.AreEqual(RollRate * 0.5f, _stack.Get(FlightStat.RollRate), 0.001f); // half the ailerons
        }

        [Test]
        public void LostRightWing_RollsRight()
        {
            Apply(PlanePartType.WingRight);
            Assert.Greater(_stack.Get(FlightStat.RollBias), 0f);
        }

        [Test]
        public void RollBias_IsCounterSteerable()
        {
            // Full opposite stick on the weakened roll rate must beat the bias, or the plane can't be saved.
            Apply(PlanePartType.WingLeft);
            Assert.Greater(_stack.Get(FlightStat.RollRate), Mathf.Abs(_stack.Get(FlightStat.RollBias)) * 1.5f);
        }

        [Test]
        public void BothWings_NoLift()
        {
            Apply(PlanePartType.WingLeft, PlanePartType.WingRight);
            Assert.AreEqual(0f, _stack.Get(FlightStat.Lift), 0.001f);
            Assert.AreEqual(0f, _stack.Get(FlightStat.RollBias), 0.001f);
        }

        [Test]
        public void LostHorizontalStabilizer_NoPitchControl_Wobbles()
        {
            Apply(PlanePartType.HorizontalStabilizer);
            Assert.Greater(_stack.Get(FlightStat.PitchWobble), 0f);
            Assert.AreEqual(0f, _stack.Get(FlightStat.YawWobble));
            Assert.AreEqual(0f, _stack.Get(FlightStat.PitchRate), 0.001f); // no elevator, no pitch control
            Assert.AreEqual(0f, _stack.Get(FlightStat.PitchStability), 0.001f);
        }

        [Test]
        public void LostVerticalStabilizer_NoYawControl_SloppyTurns()
        {
            Apply(PlanePartType.VerticalStabilizer);
            Assert.Greater(_stack.Get(FlightStat.YawWobble), 0f);
            Assert.AreEqual(0f, _stack.Get(FlightStat.PitchWobble));
            Assert.AreEqual(0f, _stack.Get(FlightStat.YawRate), 0.001f); // no rudder, no yaw control
            Assert.Less(_stack.Get(FlightStat.Grip), _stats.grip);
        }

        [Test]
        public void BothStabilizers_NoRollAutoLevel()
        {
            Apply(PlanePartType.HorizontalStabilizer, PlanePartType.VerticalStabilizer);
            Assert.AreEqual(0f, _stack.Get(FlightStat.RollStability), 0.001f);
        }

        [Test]
        public void WingPlusStabilizer_StrongerRoll()
        {
            Apply(PlanePartType.WingLeft);
            float wingOnly = Mathf.Abs(_stack.Get(FlightStat.RollBias));
            Apply(PlanePartType.WingLeft, PlanePartType.VerticalStabilizer);
            Assert.Greater(Mathf.Abs(_stack.Get(FlightStat.RollBias)), wingOnly);
        }

        [Test]
        public void DeadEngine_NoThrust()
        {
            Apply(PlanePartType.Engine);
            Assert.AreEqual(0f, _stack.Get(FlightStat.Thrust));
        }

        [Test]
        public void Reapply_ReplacesOldEffects_KeepsOtherSources()
        {
            _stack.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, 0.85f, "body state");
            Apply(PlanePartType.WingLeft, PlanePartType.Engine);
            Apply();
            Assert.AreEqual(0f, _stack.Get(FlightStat.RollBias));
            Assert.AreEqual(1f, _stack.Get(FlightStat.Thrust));
            Assert.AreEqual(_stats.maxSpeed * 0.85f, _stack.Get(FlightStat.MaxSpeed), 0.001f);
        }
    }
}

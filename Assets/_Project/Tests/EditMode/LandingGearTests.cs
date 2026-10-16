using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>The automatic gear's deploy/retract rules, timing and drag, stepped by hand.</summary>
    public class LandingGearTests
    {
        const float Dt = 0.02f;
        const float Sinking = -5f;

        GameObject _go;
        FlightStats _stats;
        PlaneController _plane;
        LandingGear _gear;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<FlightStats>();
            _go = new GameObject("Plane");
            _gear = _go.AddComponent<LandingGear>(); // brings PlaneController + Rigidbody
            _plane = _go.GetComponent<PlaneController>();
            _plane.SetStats(_stats);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
            Object.DestroyImmediate(_stats);
        }

        void Run(float seconds, float height, float verticalSpeed)
        {
            for (float t = 0f; t < seconds - 0.0001f; t += Dt)
                _gear.Step(height, verticalSpeed, Dt);
        }

        [Test]
        public void High_StaysUp()
        {
            Run(3f, _gear.DeployHeight + 50f, Sinking);
            Assert.AreEqual(GearState.Up, _gear.State);
            Assert.AreEqual(0f, _gear.Extension);
        }

        [Test]
        public void Low_ButLevelOrClimbing_StaysUp()
        {
            Run(2f, _gear.DeployHeight - 10f, 0f);
            Run(2f, _gear.DeployHeight - 10f, 5f);
            Assert.AreEqual(GearState.Up, _gear.State);
        }

        [Test]
        public void LowAndDescending_Deploys_ThenLocksAfterDeployTime()
        {
            Run(Dt, _gear.DeployHeight - 1f, Sinking);
            Assert.AreEqual(GearState.Deploying, _gear.State);

            Run(_gear.DeployTime - 0.1f, _gear.DeployHeight - 1f, Sinking);
            Assert.AreEqual(GearState.Deploying, _gear.State, "not locked before the deploy time");
            Assert.IsFalse(_gear.IsLocked);

            Run(0.1f, _gear.DeployHeight - 1f, Sinking);
            Assert.AreEqual(GearState.Locked, _gear.State);
            Assert.IsTrue(_gear.IsLocked);
            Assert.AreEqual(1f, _gear.Extension);
        }

        [Test]
        public void LevelingOffLow_KeepsTheGearComingDown()
        {
            Run(0.2f, _gear.DeployHeight - 5f, Sinking);
            Run(_gear.DeployTime, _gear.DeployHeight - 5f, 0f); // skimming low
            Assert.AreEqual(GearState.Locked, _gear.State);

            Run(1f, _gear.RetractHeight - 1f, 3f); // climbing, but still under the retract height
            Assert.AreEqual(GearState.Locked, _gear.State, "no flicker between deploy and retract heights");
        }

        [Test]
        public void ClimbingAboveRetractHeight_Retracts()
        {
            _gear.Snap(true);
            Run(Dt, _gear.RetractHeight + 1f, 5f);
            Assert.AreEqual(GearState.Retracting, _gear.State);

            Run(_gear.RetractTime, _gear.RetractHeight + 10f, 5f);
            Assert.AreEqual(GearState.Up, _gear.State);
            Assert.AreEqual(0f, _gear.Extension);
        }

        [Test]
        public void DescendingAgainMidRetract_DeploysAgain()
        {
            _gear.Snap(true);
            Run(0.5f, _gear.RetractHeight + 1f, 5f);
            float partial = _gear.Extension;
            Assert.That(partial, Is.InRange(0.01f, 0.99f));

            Run(Dt, _gear.DeployHeight - 1f, Sinking);
            Assert.AreEqual(GearState.Deploying, _gear.State);
            Assert.Greater(_gear.Extension, partial, "resumes from where it was");
        }

        [Test]
        public void GearOut_AddsDrag_RemovedWhenStowed()
        {
            float baseMax = _plane.Modifiers.Get(FlightStat.MaxSpeed);

            Run(Dt, _gear.DeployHeight - 1f, Sinking);
            Assert.AreEqual(baseMax * _gear.GearMaxSpeed, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.001f, "drag as soon as it starts");

            Run(_gear.DeployTime, _gear.DeployHeight - 1f, Sinking);
            Assert.AreEqual(baseMax * _gear.GearMaxSpeed, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.001f, "applied once, not stacked");

            Run(_gear.RetractTime + 0.1f, _gear.RetractHeight + 5f, 5f);
            Assert.AreEqual(baseMax, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.001f);
        }

        [Test]
        public void StateChanged_FiresOncePerChange()
        {
            var states = new List<GearState>();
            _gear.StateChanged += (_, s) => states.Add(s);

            Run(_gear.DeployTime + 0.2f, _gear.DeployHeight - 1f, Sinking);
            Run(_gear.RetractTime + 0.2f, _gear.RetractHeight + 1f, 5f);

            CollectionAssert.AreEqual(
                new[] { GearState.Deploying, GearState.Locked, GearState.Retracting, GearState.Up }, states);
        }

        [Test]
        public void Snap_SetsFullyUpOrLocked()
        {
            _gear.Snap(true);
            Assert.AreEqual(GearState.Locked, _gear.State);
            Assert.AreEqual(1f, _gear.Extension);
            _gear.Snap(false);
            Assert.AreEqual(GearState.Up, _gear.State);
            Assert.AreEqual(0f, _gear.Extension);
        }
    }
}

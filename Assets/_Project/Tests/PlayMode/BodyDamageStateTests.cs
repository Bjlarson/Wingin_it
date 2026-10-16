using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Body damage states on the real plane in the FlightTest scene.</summary>
    public class BodyDamageStateTests
    {
        PlaneController _plane;
        PlaneHealth _health;
        PlanePart _body;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
            _body = _health.GetPart(PlanePartType.Body);
        }

        /// <summary>Damages the body down to <paramref name="fraction"/> of its max HP.</summary>
        void SetBodyHealth(float fraction) =>
            _health.ApplyDamage(_body, _body.Health - _body.MaxHealth * fraction, _body.transform.position);

        [Test]
        public void BodyDamage_StepsThroughStates()
        {
            var changes = new List<BodyDamageState>();
            _health.BodyStateChanged += (h, _) => changes.Add(h.BodyState);
            Assert.AreEqual(BodyDamageState.Healthy, _health.BodyState);

            SetBodyHealth(0.7f);
            Assert.AreEqual(BodyDamageState.Healthy, _health.BodyState);
            SetBodyHealth(0.5f);
            Assert.AreEqual(BodyDamageState.Damaged, _health.BodyState);
            SetBodyHealth(0.2f);
            Assert.AreEqual(BodyDamageState.Critical, _health.BodyState);
            SetBodyHealth(0f);
            Assert.AreEqual(BodyDamageState.Destroyed, _health.BodyState);
            Assert.IsFalse(_plane.IsAlive);

            CollectionAssert.AreEqual(
                new[] { BodyDamageState.Damaged, BodyDamageState.Critical, BodyDamageState.Destroyed }, changes);
        }

        [Test]
        public void OtherPartDamage_DoesNotChangeState()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            Assert.AreEqual(BodyDamageState.Healthy, _health.BodyState);
        }

        [Test]
        public void CriticalBody_StacksWithLostPart_AndRestoreClearsBoth()
        {
            float baseMax = _plane.Stats.maxSpeed;
            var engine = _health.GetPart(PlanePartType.Engine);
            _health.ApplyDamage(engine, engine.MaxHealth, engine.transform.position);
            SetBodyHealth(0.2f);

            Assert.AreEqual(0f, _plane.Modifiers.Get(FlightStat.Thrust), "dead engine still applies");
            Assert.Less(_plane.Modifiers.Get(FlightStat.MaxSpeed), baseMax);
            Assert.Less(_plane.Modifiers.Get(FlightStat.Climb), 1f);

            _health.RestoreAll();
            Assert.AreEqual(BodyDamageState.Healthy, _health.BodyState);
            Assert.AreEqual(baseMax, _plane.Modifiers.Get(FlightStat.MaxSpeed), 0.001f);
            Assert.AreEqual(1f, _plane.Modifiers.Get(FlightStat.Climb));
            Assert.AreEqual(0f, _plane.Modifiers.Get(FlightStat.EngineSputter));
            Assert.AreEqual(1f, _plane.Modifiers.Get(FlightStat.Thrust));
        }

        [UnityTest]
        public IEnumerator DamagedPlane_FliesSlowerAtFullThrottle()
        {
            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            SetBodyHealth(0.5f);
            for (int i = 0; i < 300; i++) // 6 s at full throttle, level
            {
                _plane.SetInput(new FlightInput { Throttle = 1f });
                yield return new WaitForFixedUpdate();
            }
            Assert.LessOrEqual(_plane.Speed, _plane.Stats.maxSpeed * 0.85f + 0.5f);
            Assert.Greater(_plane.Speed, _plane.Stats.maxSpeed * 0.8f);
        }

        [UnityTest]
        public IEnumerator CriticalPlane_EngineSputters()
        {
            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            SetBodyHealth(0.2f);
            bool sputtered = false;
            for (int i = 0; i < 1000 && !sputtered; i++) // up to 20 s
            {
                _plane.SetInput(default);
                yield return new WaitForFixedUpdate();
                sputtered |= _plane.IsSputtering && _plane.EffectiveThrust < 1f;
            }
            Assert.IsTrue(sputtered, "a Critical engine should cough now and then");
        }

        [UnityTest]
        public IEnumerator CriticalPlane_GunsJam_AndRecover()
        {
            var guns = _plane.GetComponent<PlaneGuns>();
            var gunStats = guns.Stats;
            float chance = gunStats.criticalJamChancePerSecond;
            float heat = gunStats.heatPerSecond;
            gunStats.criticalJamChancePerSecond = 1000f; // jams on the first shot
            gunStats.heatPerSecond = 0.01f;              // no overheat during the test
            try
            {
                _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
                int jams = 0;
                guns.Jammed += _ => jams++;

                for (int i = 0; i < 10; i++)
                {
                    _plane.SetInput(new FlightInput { FireGuns = true });
                    yield return new WaitForFixedUpdate();
                }
                Assert.AreEqual(0, jams, "a healthy plane never jams");

                SetBodyHealth(0.2f);
                for (int i = 0; i < 2; i++)
                {
                    _plane.SetInput(new FlightInput { FireGuns = true });
                    yield return new WaitForFixedUpdate();
                }
                Assert.IsTrue(guns.IsJammed);
                Assert.IsFalse(guns.IsFiring);

                gunStats.criticalJamChancePerSecond = 0f;
                float end = Time.time + gunStats.jamDuration + 0.2f;
                while (Time.time < end)
                {
                    _plane.SetInput(new FlightInput { FireGuns = true });
                    yield return new WaitForFixedUpdate();
                }
                Assert.IsFalse(guns.IsJammed);
                Assert.IsTrue(guns.IsFiring);
            }
            finally
            {
                gunStats.criticalJamChancePerSecond = chance;
                gunStats.heatPerSecond = heat;
            }
        }
    }
}

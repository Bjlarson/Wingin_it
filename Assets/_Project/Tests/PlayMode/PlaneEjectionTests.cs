using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// Ejecting from a crippled plane in the FlightTest scene: who may, the kill it gives, the sooner respawn, and the
    /// pilot floating down under its canopy.
    /// </summary>
    public class PlaneEjectionTests
    {
        static readonly Vector3 Start = new Vector3(0f, 600f, -1500f);

        PlaneController _plane;
        PlaneHealth _health;
        PlaneEjection _ejection;
        PlaneRespawner _respawner;
        PlaneController _attacker;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
            _ejection = _plane.GetComponent<PlaneEjection>();
            _respawner = _plane.GetComponent<PlaneRespawner>();
            Assert.IsNotNull(_ejection, "the plane has no PlaneEjection");
            Assert.IsNotEmpty(FlightTestScene.Dummies, "the scene needs a dummy to act as the attacker");
            _attacker = FlightTestScene.Dummies[0].GetComponent<PlaneController>();
            _plane.Respawn(Start, Quaternion.identity);
            yield return null;
        }

        void ShootOff(PlanePartType type)
        {
            var part = _health.GetPart(type);
            _health.ApplyDamage(part, part.MaxHealth, part.transform.position, _attacker);
        }

        [Test]
        public void HealthyPlane_CannotEject()
        {
            Assert.IsFalse(_ejection.CanEject);
            Assert.IsFalse(_ejection.TryEject());
            Assert.IsTrue(_plane.IsAlive);
        }

        [Test]
        public void ScratchedPlane_CannotEject()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(wing, wing.MaxHealth * 0.3f, wing.transform.position, _attacker);
            Assert.IsFalse(_health.IsCrippled);
            Assert.IsFalse(_ejection.TryEject(), "only a plane that's going down");
        }

        [UnityTest]
        public IEnumerator LostTail_Ejects_CreditsTheAttacker_AndRespawnsSooner()
        {
            ShootOff(PlanePartType.HorizontalStabilizer);
            Assert.IsTrue(_ejection.CanEject, "a plane with no tail can eject");

            Assert.IsTrue(_ejection.TryEject());
            Assert.IsFalse(_plane.IsAlive);
            Assert.AreEqual(DeathCause.Ejected, _plane.LastDeathCause);
            Assert.AreEqual(DeathCause.Ejected, _health.LastKill.Cause);
            Assert.AreEqual(_attacker, _health.LastKill.Killer, "ejecting still gives whoever crippled it the kill");
            Assert.AreEqual(_respawner.EjectRespawnDelay, _respawner.RespawnTimeLeft, 0.05f);
            Assert.Less(_respawner.EjectRespawnDelay, _respawner.RespawnDelay);

            Assert.IsFalse(_ejection.TryEject(), "once only");
            var pilot = _ejection.LastPilot;
            Assert.IsNotNull(pilot, "a pilot came out");
            Assert.Greater(pilot.transform.position.y, _plane.Position.y, "out of the top of the plane");

            yield return new WaitForSeconds(EjectedPilot.ChuteDelay + EjectedPilot.ChuteOpenTime + 2.5f);
            Assert.IsTrue(pilot.ChuteOpen);
            Assert.IsTrue(pilot.Parachute.gameObject.activeSelf, "the canopy is out");
            Assert.That(pilot.Velocity.y, Is.InRange(-EjectedPilot.DescentSpeed - 1.5f, -EjectedPilot.DescentSpeed + 1.5f),
                "floating down at about the descent rate");

            yield return new WaitForSeconds(1f); // the respawn is over by now
            Assert.IsTrue(_plane.IsAlive, "back after the shorter delay");
        }

        [UnityTest]
        public IEnumerator EjectInput_BailsOut()
        {
            ShootOff(PlanePartType.WingRight);
            _plane.SetInput(new FlightInput { Eject = true });
            yield return null;
            yield return null;
            Assert.IsFalse(_plane.IsAlive);
            Assert.AreEqual(DeathCause.Ejected, _plane.LastDeathCause);
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Per-class loadouts: each class only carries (and can only fire) its own weapons.</summary>
    public class LoadoutTests
    {
        static readonly Vector3 Start = new Vector3(0f, 600f, -1500f);

        PlaneController _plane;
        PlaneRockets _rockets;
        PlaneMissiles _missiles;
        PlaneBombs _bombs;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _rockets = _plane.GetComponent<PlaneRockets>();
            _missiles = _plane.GetComponent<PlaneMissiles>();
            _bombs = _plane.GetComponent<PlaneBombs>();
        }

        FlightStats Class(string displayName) => FlightTestScene.FindClass(c => c.displayName == displayName);

        [TestCase("Fighter", false, true, false)]
        [TestCase("Interceptor", false, true, false)]
        [TestCase("Heavy Fighter", true, false, true)]
        [TestCase("Support", true, false, false)]
        public void EachClass_CarriesItsLoadout(string cls, bool rockets, bool missiles, bool bombs)
        {
            _plane.SetStats(Class(cls));

            Assert.IsNotNull(_plane.Stats.guns, "every class carries guns");
            Assert.AreEqual(rockets, _rockets.IsEquipped, "rockets");
            Assert.AreEqual(missiles, _missiles.IsEquipped, "missiles");
            Assert.AreEqual(bombs, _bombs.IsEquipped, "bombs");
            Assert.AreEqual(rockets ? _plane.Stats.rockets.ammo : 0, _rockets.Ammo, "rocket ammo");
            Assert.AreEqual(missiles ? _plane.Stats.missiles.ammo : 0, _missiles.Ammo, "missile ammo");
            Assert.AreEqual(bombs ? _plane.Stats.bombs.ammo : 0, _bombs.Ammo, "bomb ammo");
        }

        [UnityTest]
        public IEnumerator WeaponsTheClassDoesntCarry_NeverFire()
        {
            _plane.SetStats(Class("Fighter")); // no rockets, no bombs
            _plane.Respawn(Start, Quaternion.identity);
            int rockets = 0, bombs = 0;
            _rockets.Fired += _ => rockets++;
            _bombs.Dropped += _ => bombs++;

            float end = Time.time + 1.5f;
            while (Time.time < end)
            {
                _plane.SetInput(new FlightInput { FireRockets = true, DropBomb = true });
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(0, rockets);
            Assert.AreEqual(0, bombs);
        }

        [UnityTest]
        public IEnumerator ClassWithoutMissiles_NeverLocks()
        {
            var heavy = Class("Heavy Fighter");
            _plane.SetStats(heavy);
            var dummy = FlightTestScene.Dummies[0];
            dummy.enabled = false;
            dummy.gameObject.SetActive(true);
            var target = dummy.GetComponent<PlaneController>();
            target.SetStats(heavy);
            _plane.Respawn(Start, Quaternion.identity);
            target.Respawn(Start + Vector3.forward * 400f, Quaternion.identity);

            float end = Time.time + 2.5f; // well past any lock time
            while (Time.time < end)
            {
                _plane.SetInput(new FlightInput { FireMissile = true });
                target.SetInput(default);
                Assert.IsNull(_missiles.LockTarget, "a class without missiles shouldn't lock (or warn the target)");
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual(0, _missiles.InFlight);
            Assert.IsFalse(PlaneMissiles.GetThreat(target).BeingLocked);
        }

        [Test]
        public void SwitchingClass_SwapsTheLoadout()
        {
            _plane.SetStats(Class("Heavy Fighter"));
            Assert.Greater(_bombs.Ammo, 0);
            Assert.AreEqual(0, _missiles.Ammo);

            _plane.SetStats(Class("Interceptor"));
            Assert.AreEqual(0, _bombs.Ammo, "bombs go away with the Heavy");
            Assert.AreEqual(0, _rockets.Ammo);
            Assert.AreEqual(_plane.Stats.missiles.ammo, _missiles.Ammo, "missiles come fully stocked");
        }
    }
}

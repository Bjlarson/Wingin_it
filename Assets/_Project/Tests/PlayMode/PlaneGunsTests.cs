using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Wing guns against a real second plane in the FlightTest scene.</summary>
    public class PlaneGunsTests
    {
        PlaneController _plane;
        PlaneGuns _guns;
        PlaneController _target;
        PlaneHealth _targetHealth;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _guns = _plane.GetComponent<PlaneGuns>();
            Assert.IsNotNull(_guns, "plane has no PlaneGuns");

            // Borrow a dummy as a target, flown by the test instead of its pilot.
            Assert.IsNotEmpty(FlightTestScene.Dummies, "FlightTest scene has no dummy planes");
            var dummy = FlightTestScene.Dummies[0];
            dummy.enabled = false;
            dummy.gameObject.SetActive(true);
            _target = dummy.GetComponent<PlaneController>();
            _targetHealth = dummy.GetComponent<PlaneHealth>();
            _target.SetStats(_plane.Stats); // same class = same cruise speed, so the gap holds
        }

        /// <summary>Both planes level, same heading, target <paramref name="distance"/> m dead ahead.</summary>
        void LineUp(float distance)
        {
            var start = new Vector3(0f, 600f, -1500f);
            _plane.Respawn(start, Quaternion.identity);
            _target.Respawn(start + Vector3.forward * distance, Quaternion.identity);
        }

        IEnumerator Fly(bool fire, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                _plane.SetInput(new FlightInput { FireGuns = fire });
                _target.SetInput(default);
                yield return new WaitForFixedUpdate();
            }
        }

        static float TotalHealth(PlaneHealth health)
        {
            float total = 0f;
            for (int i = 0; i < PlanePart.TypeCount; i++)
                total += health.GetPart((PlanePartType)i).Health;
            return total;
        }

        [UnityTest]
        public IEnumerator FiringAtPlaneAhead_DamagesItsParts_AndCreditsShooter()
        {
            LineUp(200f);
            float before = TotalHealth(_targetHealth);
            Object source = null;
            int hits = 0;
            _targetHealth.Damaged += (_, info) => source = info.Source;
            _guns.HitPlane += (_, health, _) => { if (health == _targetHealth) hits++; };

            yield return Fly(fire: true, 1f);
            yield return Fly(fire: false, 0.5f); // let the last bullets land

            Assert.Less(TotalHealth(_targetHealth), before, "bullets should damage the plane ahead");
            Assert.Greater(hits, 0);
            Assert.AreSame(_plane, source, "damage should be credited to the shooter");
        }

        [UnityTest]
        public IEnumerator Bullets_DoNotHitOwnPlane()
        {
            var health = _plane.GetComponent<PlaneHealth>();
            float before = TotalHealth(health);
            _target.gameObject.SetActive(false);
            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            yield return Fly(fire: true, 1f);
            Assert.AreEqual(before, TotalHealth(health));
        }

        [UnityTest]
        public IEnumerator LostWing_HalvesFireRate()
        {
            _target.gameObject.SetActive(false);
            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            int shots = 0;
            _guns.Fired += _ => shots++;
            Assert.AreEqual(_guns.GunCount, _guns.ActiveGunCount);

            yield return Fly(fire: true, 1f);
            int bothWings = shots;
            yield return Fly(fire: false, 1.5f); // cool down

            var wing = _plane.GetComponent<PlaneHealth>().GetPart(PlanePartType.WingLeft);
            _plane.GetComponent<PlaneHealth>().ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            Assert.AreEqual(_guns.GunCount / 2, _guns.ActiveGunCount);

            shots = 0;
            yield return Fly(fire: true, 1f);
            Assert.Greater(bothWings, 0);
            Assert.AreEqual(bothWings * 0.5f, shots, bothWings * 0.15f, "one wing should fire half as many rounds");
        }

        [UnityTest]
        public IEnumerator HoldingFire_Overheats_ThenCoolsDown()
        {
            _target.gameObject.SetActive(false);
            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            var s = _plane.Stats.guns;
            int shots = 0;
            _guns.Fired += _ => shots++;

            float timeout = Time.time + 1f / s.heatPerSecond + 1f;
            while (!_guns.IsOverheated && Time.time < timeout)
                yield return Fly(fire: true, 0.1f);
            Assert.IsTrue(_guns.IsOverheated, "holding the trigger should overheat the guns");

            shots = 0;
            yield return Fly(fire: true, 0.5f);
            Assert.AreEqual(0, shots, "overheated guns can't fire");

            timeout = Time.time + (1f - s.overheatRecoverAt) / s.coolPerSecond + s.coolDelay + 1f;
            while (_guns.IsOverheated && Time.time < timeout)
                yield return Fly(fire: false, 0.1f);
            Assert.IsFalse(_guns.IsOverheated, "guns should recover after cooling");
            Assert.LessOrEqual(_guns.Heat, s.overheatRecoverAt + 0.01f);
        }

        [UnityTest]
        public IEnumerator LeadPoint_IsAheadOfACrossingTarget()
        {
            // Target 300 m ahead, flying left to right across our nose.
            var start = new Vector3(0f, 600f, -1500f);
            _plane.Respawn(start, Quaternion.identity);
            _target.Respawn(start + Vector3.forward * 300f, Quaternion.LookRotation(Vector3.right));
            yield return Fly(fire: false, 0.1f);

            Assert.IsTrue(_guns.TryGetLeadPoint(_target, out Vector3 lead, out float distance));
            Assert.AreEqual(300f, distance, 20f);
            Assert.Greater(lead.x - _target.transform.position.x, 10f, "pipper should lead the target along its path");
        }
    }
}

using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Wing rockets against real dummy planes in the FlightTest scene.</summary>
    public class PlaneRocketsTests
    {
        static readonly Vector3 Start = new Vector3(0f, 600f, -1500f);

        PlaneController _plane;
        PlaneRockets _rockets;
        RocketStats _stats;
        FlightStats _flightStats;
        PlaneController _target;
        PlaneHealth _targetHealth;
        int _fired;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _rockets = _plane.GetComponent<PlaneRockets>();
            Assert.IsNotNull(_rockets, "plane has no PlaneRockets");
            var cls = FlightTestScene.FindClass(c => c.rockets != null); // a class that carries rockets

            // Copies, so tests can tune them without touching the assets. No spread: every rocket flies true.
            _flightStats = Object.Instantiate(cls);
            _stats = Object.Instantiate(cls.rockets);
            _stats.spreadAngle = 0f;
            _flightStats.rockets = _stats;
            _plane.SetStats(_flightStats);

            _target = ActivateDummy(0);
            _targetHealth = _target.GetComponent<PlaneHealth>();
            _rockets.Fired += _ => _fired++;
            _fired = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_flightStats);
            Object.Destroy(_stats);
        }

        /// <summary>Borrows a dummy, flown by the test instead of its pilot.</summary>
        PlaneController ActivateDummy(int index)
        {
            Assert.Greater(FlightTestScene.Dummies.Length, index, "FlightTest scene needs more dummy planes");
            var dummy = FlightTestScene.Dummies[index];
            dummy.enabled = false;
            dummy.gameObject.SetActive(true);
            var plane = dummy.GetComponent<PlaneController>();
            plane.SetStats(_flightStats); // same class = same cruise speed, so the gap holds
            return plane;
        }

        IEnumerator Fly(bool fire, float seconds, params PlaneController[] others)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                _plane.SetInput(new FlightInput { FireRockets = fire });
                _target.SetInput(default);
                foreach (var other in others)
                    other.SetInput(default);
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>One press: the button held for a single physics step.</summary>
        IEnumerator Press(params PlaneController[] others)
        {
            yield return Fly(fire: true, Time.fixedDeltaTime * 0.5f, others);
        }

        void ClearSkies()
        {
            _target.gameObject.SetActive(false);
            _plane.Respawn(Start, Quaternion.identity);
        }

        static float TotalHealth(PlaneHealth health)
        {
            float total = 0f;
            for (int i = 0; i < PlanePart.TypeCount; i++)
                total += health.GetPart((PlanePartType)i).Health;
            return total;
        }

        [UnityTest]
        public IEnumerator OnePress_FiresOneSalvo_AndUsesAmmo()
        {
            ClearSkies();
            Assert.AreEqual(_stats.ammo, _rockets.Ammo, "spawns with full rockets");

            yield return Press();
            yield return Fly(fire: false, _stats.salvoInterval * _stats.rocketsPerSalvo + 0.3f);

            Assert.AreEqual(_stats.rocketsPerSalvo, _fired);
            Assert.AreEqual(_stats.ammo - _stats.rocketsPerSalvo, _rockets.Ammo);
        }

        [UnityTest]
        public IEnumerator HoldingFire_EmptiesTheRacks_ThenStops()
        {
            ClearSkies();
            int salvos = Mathf.CeilToInt(_stats.ammo / (float)_stats.rocketsPerSalvo);
            yield return Fly(fire: true, salvos * _stats.salvoCooldown + 0.5f);

            Assert.AreEqual(_stats.ammo, _fired);
            Assert.AreEqual(0, _rockets.Ammo);

            yield return Fly(fire: true, _stats.salvoCooldown * 2f);
            Assert.AreEqual(_stats.ammo, _fired, "no rockets left to fire");
        }

        [UnityTest]
        public IEnumerator Respawn_Restocks()
        {
            ClearSkies();
            yield return Press();
            yield return Fly(fire: false, 0.5f);
            Assert.Less(_rockets.Ammo, _stats.ammo);

            _plane.Respawn(Start, Quaternion.identity);
            Assert.AreEqual(_stats.ammo, _rockets.Ammo);
        }

        [UnityTest]
        public IEnumerator DirectHit_DamagesThePartHit_SplashesTheBody_AndCreditsShooter()
        {
            _plane.Respawn(Start, Quaternion.identity);
            _target.Respawn(Start + Vector3.forward * 150f, Quaternion.identity);
            var body = _targetHealth.GetPart(PlanePartType.Body);
            float before = TotalHealth(_targetHealth);
            Object source = null;
            int hits = 0;
            _targetHealth.Damaged += (_, info) => source = info.Source;
            _rockets.HitPlane += (_, health, _) => { if (health == _targetHealth) hits++; };

            yield return Press();
            yield return Fly(fire: false, 1f);

            // The rails sit outboard under the wings and fire parallel to the nose, so they hit the target's wings.
            var wingL = _targetHealth.GetPart(PlanePartType.WingLeft);
            var wingR = _targetHealth.GetPart(PlanePartType.WingRight);
            Assert.Less(wingL.Health, wingL.MaxHealth, "left rocket should hit the left wing");
            Assert.Less(wingR.Health, wingR.MaxHealth, "right rocket should hit the right wing");
            Assert.Less(body.Health, body.MaxHealth, "the blasts should splash the body");
            Assert.Less(TotalHealth(_targetHealth), before);
            Assert.GreaterOrEqual(hits, 2 * _stats.rocketsPerSalvo, "each rocket deals direct + splash damage");
            Assert.AreSame(_plane, source, "damage should be credited to the shooter");
        }

        [UnityTest]
        public IEnumerator Splash_DamagesTheBodyOfANearbyPlane_ButNotItsParts()
        {
            var wingman = ActivateDummy(1);
            var wingmanHealth = wingman.GetComponent<PlaneHealth>();
            _plane.Respawn(Start, Quaternion.identity);
            _target.Respawn(Start + Vector3.forward * 150f, Quaternion.identity);
            // Flying just above the target, clear of the rockets' path but inside the blast.
            wingman.Respawn(Start + Vector3.forward * 150f + Vector3.up * 5f, Quaternion.identity);

            yield return Press(wingman);
            yield return Fly(fire: false, 1f, wingman);

            var body = wingmanHealth.GetPart(PlanePartType.Body);
            Assert.Less(body.Health, body.MaxHealth, "a plane near the blast takes splash damage on the body");
            for (int i = 0; i < PlanePart.TypeCount; i++)
            {
                var part = wingmanHealth.GetPart((PlanePartType)i);
                if (part.partType != PlanePartType.Body)
                    Assert.AreEqual(part.MaxHealth, part.Health, $"splash should only hit the body, not {part.partType}");
            }
        }

        [UnityTest]
        public IEnumerator Rockets_NeverDamageTheirOwnPlane()
        {
            var health = _plane.GetComponent<PlaneHealth>();
            _plane.Respawn(Start, Quaternion.identity);
            // Target close ahead, so our own plane is inside the blast radius.
            _target.Respawn(Start + Vector3.forward * 12f, Quaternion.identity);
            float before = TotalHealth(health);

            yield return Press();
            yield return Fly(fire: false, 0.5f);

            Assert.Less(TotalHealth(_targetHealth), TotalHealth(health), "the target should have been hit");
            Assert.AreEqual(before, TotalHealth(health));
        }

        [UnityTest]
        public IEnumerator LostWing_LosesItsRail_TheOtherStillFires()
        {
            ClearSkies();
            var health = _plane.GetComponent<PlaneHealth>();
            Assert.AreEqual(_rockets.RailCount, _rockets.ActiveRailCount);

            var wing = health.GetPart(PlanePartType.WingLeft);
            health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            Assert.AreEqual(_rockets.RailCount - 1, _rockets.ActiveRailCount);

            yield return Press();
            yield return Fly(fire: false, 0.5f);
            Assert.AreEqual(_stats.rocketsPerSalvo, _fired, "the whole salvo comes off the remaining rail");
        }
    }
}

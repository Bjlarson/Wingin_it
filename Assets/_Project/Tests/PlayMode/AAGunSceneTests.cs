using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Home airfield AA: shoots enemies lingering over the field after a lock-on delay, never friendlies, only at home.</summary>
    public class AAGunSceneTests
    {
        const float OrbitHeight = 300f, OrbitRadius = 350f;

        Airfield _home;
        AAGun[] _guns;
        PlaneController _orbiter;
        PlaneHealth _orbiterHealth;
        float _aaDamage;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            var player = FlightTestScene.Player;
            _home = Airfield.NearestFriendly(player.transform.position, player.Team);
            Assert.IsNotNull(_home, "no friendly airfield in the scene");
            _guns = FlightTestScene.AAGuns;
            foreach (var gun in _guns)
                gun.enabled = true;
            _aaDamage = 0f;
        }

        [TearDown]
        public void ClearDevHostile() => AAGun.DevHostilePlane = null;

        /// <summary>A drone on <paramref name="team"/> circling the airfield at 300 m, well inside AA range.</summary>
        void StartOrbiter(int team)
        {
            var drone = FlightTestScene.Dummies[0];
            drone.gameObject.SetActive(true);
            Vector3 center = _home.transform.position + Vector3.up * OrbitHeight;
            drone.Configure(center, OrbitRadius, orbitClockwise: true);
            _orbiter = drone.GetComponent<PlaneController>();
            _orbiter.Team = team;
            _orbiter.Respawn(center + Vector3.right * OrbitRadius, Quaternion.LookRotation(Vector3.back));
            _orbiterHealth = _orbiter.GetComponent<PlaneHealth>();
            _orbiterHealth.Damaged += (_, info) =>
            {
                if (info.Source is AAGun)
                    _aaDamage += info.Amount;
            };
        }

        bool AnyGunTargeting(PlaneController plane) => _guns.Any(g => g.Target == plane);

        /// <summary>Flies until <paramref name="until"/> is true or time runs out, checking the lock-on delay every step.</summary>
        IEnumerator Fly(float seconds, System.Func<bool> until = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end && (until == null || !until()))
            {
                foreach (var gun in _guns)
                    if (gun.IsFiring)
                        Assert.GreaterOrEqual(gun.TrackTime, gun.Stats.acquireTime, $"{gun.name} fired before its lock-on delay");
                yield return new WaitForFixedUpdate();
            }
        }

        [Test]
        public void HomeAirfield_HasFourArmedAAGuns_OnItsTeam()
        {
            Assert.AreEqual(4, _guns.Length);
            foreach (var gun in _guns)
            {
                Assert.AreEqual(_home, gun.Airfield, gun.name);
                Assert.AreEqual(_home.Team, gun.Team, gun.name);
                Assert.IsTrue(gun.IsArmed, gun.name);
            }
        }

        [UnityTest]
        public IEnumerator EnemyLingeringOverTheField_GetsTrackedAndShot()
        {
            StartOrbiter(team: 1);
            yield return Fly(3f, () => AnyGunTargeting(_orbiter));
            Assert.IsTrue(AnyGunTargeting(_orbiter), "an AA gun picks up the enemy");
            yield return Fly(15f, () => _aaDamage > 0f);
            Assert.Greater(_aaDamage, 0f, "AA shells hit the lingering enemy");
            Assert.IsInstanceOf<AAGun>(_orbiterHealth.LastAttacker);
        }

        [UnityTest]
        public IEnumerator EnemyHiddenInCloud_IsNeverTargeted()
        {
            // One wide, flat puff wrapping the whole orbit: from the ground every sight line crosses 100+ m of cloud.
            var cloud = new GameObject("Test Cloud").AddComponent<CloudBank>();
            cloud.transform.position = _home.transform.position + Vector3.up * OrbitHeight;
            cloud.Configure(new[] { new CloudPuff(Vector3.zero, new Vector3(OrbitRadius + 250f, 120f, OrbitRadius + 250f)) });
            try
            {
                StartOrbiter(team: 1);
                yield return Fly(4f, () => AnyGunTargeting(_orbiter));
                Assert.IsFalse(AnyGunTargeting(_orbiter), "AA can't see a plane inside the cloud");
                Assert.AreEqual(0f, _aaDamage);
            }
            finally
            {
                Object.Destroy(cloud.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator FriendlyPlanes_AreNeverTargeted()
        {
            StartOrbiter(team: _home.Team);
            yield return Fly(6f);
            Assert.IsFalse(AnyGunTargeting(_orbiter));
            Assert.IsFalse(_guns.Any(g => g.IsFiring));
            Assert.AreEqual(0f, _aaDamage);
        }

        [UnityTest]
        public IEnumerator DevHostile_TurnsOurOwnAAOnAFriendly()
        {
            StartOrbiter(team: _home.Team);
            AAGun.DevHostilePlane = _orbiter;
            yield return Fly(3f, () => AnyGunTargeting(_orbiter));
            Assert.IsTrue(AnyGunTargeting(_orbiter));
        }

        [UnityTest]
        public IEnumerator NonHomeAirfield_AAStaysIdle()
        {
            _home.Configure(_home.DisplayName, _home.Team, home: false, _home.RunwaySize, _home.AirSpawn, _home.RunwaySpawn, _home.RepairZone);
            StartOrbiter(team: 1);
            yield return Fly(6f);
            Assert.IsFalse(_guns.Any(g => g.IsArmed));
            Assert.IsFalse(AnyGunTargeting(_orbiter));
            Assert.AreEqual(0f, _aaDamage);
        }

        [UnityTest]
        public IEnumerator AAKill_CreditsTheGun_WithNoKillerPlane()
        {
            StartOrbiter(team: 1);
            var body = _orbiterHealth.GetPart(PlanePartType.Body);
            _orbiterHealth.ApplyDamage(body, body.Health - 1f, _orbiter.Position); // one hit from death, no attacker
            KillInfo? kill = null;
            _orbiterHealth.Killed += k => kill = k;

            yield return Fly(20f, () => kill.HasValue);
            Assert.IsTrue(kill.HasValue, "the AA finishes off the enemy");
            Assert.IsInstanceOf<AAGun>(kill.Value.Source);
            Assert.IsNull(kill.Value.Killer, "no plane gets an AA kill");
        }
    }
}

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;
using Object = UnityEngine.Object;

namespace WingingIt.Tests
{
    /// <summary>Lock-on homing missiles against real dummy planes in the FlightTest scene.</summary>
    public class PlaneMissilesTests
    {
        static readonly Vector3 Start = new Vector3(0f, 600f, -1500f);

        PlaneController _plane;
        PlaneMissiles _missiles;
        MissileStats _stats;
        FlightStats _flightStats;
        PlaneController _target;
        PlaneHealth _targetHealth;
        int _fired;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _missiles = _plane.GetComponent<PlaneMissiles>();
            Assert.IsNotNull(_missiles, "plane has no PlaneMissiles");
            var cls = FlightTestScene.FindClass(c => c.missiles != null); // a class that carries missiles

            // Copies, so tests can tune them without touching the assets.
            _flightStats = Object.Instantiate(cls);
            _stats = Object.Instantiate(cls.missiles);
            _flightStats.missiles = _stats;
            _plane.SetStats(_flightStats);

            var dummy = FlightTestScene.Dummies[0];
            dummy.enabled = false; // flown by the test instead of its pilot
            dummy.gameObject.SetActive(true);
            _target = dummy.GetComponent<PlaneController>();
            _target.SetStats(_flightStats); // same class = same cruise speed, so the gap holds
            _targetHealth = _target.GetComponent<PlaneHealth>();

            _missiles.Fired += (_, _) => _fired++;
            _fired = 0;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_flightStats);
            Object.Destroy(_stats);
        }

        /// <summary>Both planes fly straight and level; the target 400 m dead ahead.</summary>
        void LineUp(float gap = 400f)
        {
            _plane.Respawn(Start, Quaternion.identity);
            _target.Respawn(Start + Vector3.forward * gap, Quaternion.identity);
        }

        IEnumerator Fly(bool fire, float seconds, Func<FlightInput> targetInput = null, Func<bool> until = null)
        {
            float end = Time.time + seconds;
            while (Time.time < end && (until == null || !until()))
            {
                _plane.SetInput(new FlightInput { FireMissile = fire });
                _target.SetInput(targetInput != null ? targetInput() : default);
                yield return new WaitForFixedUpdate();
            }
        }

        IEnumerator Lock()
        {
            yield return Fly(fire: false, _stats.lockTime + 0.3f);
            Assert.IsTrue(_missiles.IsLocked, "should be locked after keeping the target on the nose");
        }

        static float TotalHealth(PlaneHealth health)
        {
            float total = 0f;
            for (int i = 0; i < PlanePart.TypeCount; i++)
                total += health.GetPart((PlanePartType)i).Health;
            return total;
        }

        [UnityTest]
        public IEnumerator Lock_BuildsWhileTheTargetIsOnTheNose_AndWarnsTheTarget()
        {
            LineUp();
            Assert.AreEqual(_stats.ammo, _missiles.Ammo, "spawns with full missiles");

            yield return Fly(fire: false, _stats.lockTime * 0.5f);
            Assert.AreSame(_target, _missiles.LockTarget);
            Assert.IsFalse(_missiles.IsLocked);
            Assert.That(_missiles.LockProgress, Is.InRange(0.2f, 0.8f));
            var threat = PlaneMissiles.GetThreat(_target);
            Assert.IsTrue(threat.BeingLocked, "the target gets a lock warning");
            Assert.IsFalse(threat.Locked);

            yield return Fly(fire: false, _stats.lockTime * 0.5f + 0.3f);
            Assert.IsTrue(_missiles.IsLocked);
            Assert.IsTrue(PlaneMissiles.GetThreat(_target).Locked);
            Assert.IsFalse(PlaneMissiles.GetThreat(_plane).BeingLocked, "the target has no missiles locking us");
        }

        [UnityTest]
        public IEnumerator Teammate_DeadAhead_GetsNoLock()
        {
            _target.Team = _plane.Team;
            LineUp();
            yield return Fly(fire: false, _stats.lockTime + 0.3f);
            Assert.IsNull(_missiles.LockTarget, "teammates are never locked");
            Assert.IsFalse(_missiles.IsLocked);
            Assert.IsFalse(PlaneMissiles.GetThreat(_target).BeingLocked);
        }

        [UnityTest]
        public IEnumerator TargetLeavingTheCone_BreaksTheLock()
        {
            LineUp();
            yield return Fly(fire: false, _stats.lockTime * 0.5f);
            Assert.AreSame(_target, _missiles.LockTarget);

            // The target is suddenly 90° off the nose.
            _target.Respawn(_plane.Position + Vector3.right * 400f, Quaternion.identity);
            yield return Fly(fire: false, 0.1f);
            Assert.IsNull(_missiles.LockTarget);
            Assert.AreEqual(0f, _missiles.LockProgress);
            Assert.IsFalse(PlaneMissiles.GetThreat(_target).BeingLocked);
        }

        [UnityTest]
        public IEnumerator Terrain_BlocksTheLock()
        {
            LineUp();
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.transform.position = Start + Vector3.forward * 300f;
            wall.transform.localScale = new Vector3(60f, 60f, 5f);
            try
            {
                yield return Fly(fire: false, _stats.lockTime * 0.5f);
                Assert.IsNull(_missiles.LockTarget, "no lock through a wall");
            }
            finally
            {
                Object.Destroy(wall);
            }
        }

        static CloudBank SpawnCloud(Vector3 position, float radius)
        {
            var cloud = new GameObject("Test Cloud").AddComponent<CloudBank>();
            cloud.transform.position = position;
            cloud.Configure(new[] { new CloudPuff(Vector3.zero, Vector3.one * radius) });
            return cloud;
        }

        [UnityTest]
        public IEnumerator ThickCloud_BlocksTheLock_AThinOneDoesNot()
        {
            LineUp();
            var cloud = SpawnCloud(Start + Vector3.forward * 200f, 60f); // 120 m of cloud on the sight line
            try
            {
                yield return Fly(fire: false, _stats.lockTime * 0.5f);
                Assert.IsNull(_missiles.LockTarget, "no lock through thick cloud");
                Assert.IsFalse(PlaneMissiles.GetThreat(_target).BeingLocked);

                cloud.Configure(new[] { new CloudPuff(Vector3.zero, Vector3.one * 15f) }); // 30 m: a wisp
                yield return Lock();
            }
            finally
            {
                Object.Destroy(cloud.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator Missile_LosesATargetThatHidesInCloud()
        {
            LineUp(500f);
            // The target flies into a cloud just ahead of it; the missile, still outside, loses sight of it.
            var cloud = SpawnCloud(Start + Vector3.forward * 750f, 200f);
            float before = TotalHealth(_targetHealth);
            PlaneController evadedBy = null;
            _missiles.Evaded += (_, plane) => evadedBy = plane;
            try
            {
                Assert.IsTrue(_missiles.LaunchAt(_target));
                // Once lost, the target pulls up out of the missile's straight path.
                yield return Fly(fire: false, _stats.lifetime + 0.5f,
                    () => evadedBy != null ? new FlightInput { Pitch = 1f, AutoThrottle = true } : default,
                    () => _missiles.InFlight == 0);
                Assert.AreSame(_target, evadedBy, "the seeker loses the target in the cloud");
                Assert.AreEqual(before, TotalHealth(_targetHealth), 1e-3f, "the lost missile misses");
            }
            finally
            {
                Object.Destroy(cloud.gameObject);
            }
        }

        [UnityTest]
        public IEnumerator PressWithoutLock_DoesNotFire()
        {
            _target.gameObject.SetActive(false);
            _plane.Respawn(Start, Quaternion.identity);
            yield return Fly(fire: true, 0.5f);
            Assert.AreEqual(0, _fired);
            Assert.AreEqual(_stats.ammo, _missiles.Ammo);
        }

        [UnityTest]
        public IEnumerator Locked_OneMissilePerPress()
        {
            LineUp(600f);
            yield return Lock();

            yield return Fly(fire: true, _stats.launchCooldown * 2f + 0.2f);
            Assert.AreEqual(1, _fired, "holding the button fires once");
            Assert.AreEqual(_stats.ammo - 1, _missiles.Ammo);

            yield return Fly(fire: false, 0.1f);
            yield return Fly(fire: true, 0.1f);
            Assert.AreEqual(2, _fired, "a second press fires again");
        }

        [UnityTest]
        public IEnumerator Missile_HitsAStraightFlyingTarget_AndCreditsTheShooter()
        {
            LineUp();
            float before = TotalHealth(_targetHealth);
            Object source = null;
            _targetHealth.Damaged += (_, info) => source = info.Source;
            int evaded = 0;
            _missiles.Evaded += (_, _) => evaded++;

            yield return Lock();
            yield return Fly(fire: true, 0.1f);
            Assert.AreEqual(1, _fired);
            Assert.AreEqual(1, PlaneMissiles.GetThreat(_target).Incoming, "the target sees the missile coming");

            yield return Fly(fire: false, _stats.lifetime, until: () => _missiles.InFlight == 0);
            Assert.Less(TotalHealth(_targetHealth), before, "the missile should hit");
            Assert.AreSame(_plane, source, "damage should be credited to the shooter");
            Assert.AreEqual(0, evaded);
            Assert.AreEqual(0, PlaneMissiles.GetThreat(_target).Incoming);
        }

        /// <summary>
        /// A missile chases the target from 500 m behind; the target flies straight until the missile is
        /// <paramref name="breakDistance"/> m away, then pulls into a hard loop.
        /// </summary>
        IEnumerator ChaseAndBreak(float breakDistance, Action<bool> breakStarted)
        {
            LineUp(500f);
            Assert.IsTrue(_missiles.LaunchAt(_target));
            bool breaking = false;
            yield return Fly(fire: false, _stats.lifetime + 0.5f, () =>
            {
                var threat = PlaneMissiles.GetThreat(_target);
                breaking |= threat.Incoming > 0 && threat.NearestDistance < breakDistance;
                return breaking ? new FlightInput { Pitch = 1f, AutoThrottle = true } : default;
            }, () => _missiles.InFlight == 0);
            breakStarted(breaking);
        }

        [UnityTest]
        public IEnumerator HardTurn_WhenTheMissileIsClose_ShakesItOff()
        {
            float before = TotalHealth(_targetHealth);
            PlaneController evadedBy = null;
            _missiles.Evaded += (_, plane) => evadedBy = plane;
            bool broke = false;

            yield return ChaseAndBreak(_stats.evadeRange * 0.75f, b => broke = b);

            Assert.IsTrue(broke, "the missile should have closed in");
            Assert.AreSame(_target, evadedBy, "the hard turn should break the missile's lock");
            Assert.AreEqual(before, TotalHealth(_targetHealth), "a dodged missile does no damage");
        }

        [UnityTest]
        public IEnumerator HardTurn_TooLate_StillGetsHit()
        {
            float before = TotalHealth(_targetHealth);
            int evaded = 0;
            _missiles.Evaded += (_, _) => evaded++;
            bool broke = false;

            yield return ChaseAndBreak(40f, b => broke = b);

            Assert.IsTrue(broke);
            Assert.AreEqual(0, evaded);
            Assert.Less(TotalHealth(_targetHealth), before, "breaking at the last instant is too late");
        }

        [UnityTest]
        public IEnumerator HardTurn_FarAway_DoesNotCount()
        {
            // Turning hard while the missile is still out of evade range builds nothing.
            LineUp(_stats.evadeRange + 300f);
            Assert.IsTrue(_missiles.LaunchAt(_target));
            yield return Fly(fire: false, 0.8f, () => new FlightInput { Pitch = 1f, AutoThrottle = true });
            var threat = PlaneMissiles.GetThreat(_target);
            Assert.AreEqual(1, threat.Incoming);
            Assert.Greater(threat.NearestDistance, _stats.evadeRange);
            Assert.AreEqual(0f, threat.EvadeProgress);
        }

        [UnityTest]
        public IEnumerator Respawn_Restocks()
        {
            LineUp(600f);
            yield return Lock();
            yield return Fly(fire: true, 0.1f);
            Assert.Less(_missiles.Ammo, _stats.ammo);

            _plane.Respawn(Start, Quaternion.identity);
            Assert.AreEqual(_stats.ammo, _missiles.Ammo);
        }
    }
}

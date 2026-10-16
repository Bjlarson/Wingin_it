using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Bombs, the impact preview, and the ground targets in the FlightTest scene.</summary>
    public class PlaneBombsTests
    {
        static readonly Vector3 Start = new Vector3(0f, 300f, -1500f);

        PlaneController _plane;
        PlaneBombs _bombs;
        BombStats _stats;
        FlightStats _flightStats;
        int _dropped;
        Vector3? _explodedAt;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _bombs = _plane.GetComponent<PlaneBombs>();
            Assert.IsNotNull(_bombs, "plane has no PlaneBombs");
            var cls = FlightTestScene.FindClass(c => c.bombs != null); // a class that carries bombs

            // Copies, so tests can tune them without touching the assets.
            _flightStats = Object.Instantiate(cls);
            _stats = Object.Instantiate(cls.bombs);
            _flightStats.bombs = _stats;
            _plane.SetStats(_flightStats);

            _bombs.Dropped += _ => _dropped++;
            _bombs.Exploded += (_, point) => _explodedAt = point;
            _dropped = 0;
            _explodedAt = null;
            _plane.Respawn(Start, Quaternion.identity);
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Object.Destroy(_flightStats);
            Object.Destroy(_stats);
        }

        IEnumerator Fly(bool drop, float seconds)
        {
            float end = Time.time + seconds;
            while (Time.time < end)
            {
                _plane.SetInput(new FlightInput { DropBomb = drop });
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>One press: the button held for a single physics step.</summary>
        IEnumerator Press() => Fly(drop: true, Time.fixedDeltaTime * 0.5f);

        IEnumerator WaitForExplosion(float timeout)
        {
            float end = Time.time + timeout;
            while (_explodedAt == null && Time.time < end)
                yield return Fly(drop: false, Time.fixedDeltaTime);
        }

        [UnityTest]
        public IEnumerator OnePress_DropsOneBomb_AndUsesAmmo()
        {
            Assert.AreEqual(_stats.ammo, _bombs.Ammo, "spawns with full bombs");
            yield return Press();
            yield return Fly(drop: false, 0.3f);

            Assert.AreEqual(1, _dropped);
            Assert.AreEqual(_stats.ammo - 1, _bombs.Ammo);
            Assert.AreEqual(1, _bombs.InFlight);
        }

        [UnityTest]
        public IEnumerator Holding_EmptiesTheBay_ThenRespawnRestocks()
        {
            yield return Fly(drop: true, _stats.dropInterval * (_stats.ammo + 2));
            Assert.AreEqual(_stats.ammo, _dropped, "no bombs left to drop");
            Assert.AreEqual(0, _bombs.Ammo);

            _plane.Respawn(Start, Quaternion.identity);
            Assert.AreEqual(_stats.ammo, _bombs.Ammo);
        }

        [UnityTest]
        public IEnumerator ImpactPreview_MatchesWhereTheBombLands()
        {
            Assert.IsTrue(_bombs.TryPredictImpact(out var predicted), "a bomb from 300 m should hit something");
            yield return Press();
            yield return WaitForExplosion(_stats.lifetime);

            Assert.IsNotNull(_explodedAt, "the bomb never landed");
            // The bomb leaves one physics step after the prediction, so allow that much travel.
            float tolerance = _plane.Speed * Time.fixedDeltaTime * 2f + 1f;
            Assert.Less(Vector3.Distance(predicted.Point, _explodedAt.Value), tolerance,
                $"predicted {predicted.Point}, landed {_explodedAt.Value}");
        }

        [UnityTest]
        public IEnumerator BombOnATruck_DestroysIt_AndCreditsTheBomber()
        {
            var truck = GroundTarget.All.FirstOrDefault(t => t.DisplayName == "Truck");
            Assert.IsNotNull(truck, "FlightTest scene has no truck");
            Object destroyedBy = null;
            void OnDestroyed(GroundTarget t, Object source)
            {
                if (t == truck)
                    destroyedBy = source;
            }
            GroundTarget.AnyDestroyed += OnDestroyed;
            float damageDealt = 0f;
            _bombs.HitGroundTarget += (_, t, dmg) => { if (t == truck) damageDealt += dmg; };

            try
            {
                // Line the plane up using the preview itself: shift it so the predicted impact lands on the truck.
                Vector3 approach = truck.transform.position + new Vector3(0f, 300f, -1000f);
                // Exactly one physics step after each respawn (a frame can run several), so the plane has flown the
                // same distance when it predicts as when it drops.
                _plane.Respawn(approach, Quaternion.identity);
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(_bombs.TryPredictImpact(out var impact));
                Vector3 offset = impact.Point - truck.transform.position;
                offset.y = 0f;
                _plane.Respawn(approach - offset, Quaternion.identity);
                yield return new WaitForFixedUpdate();

                yield return Press();
                yield return WaitForExplosion(_stats.lifetime);

                Assert.IsNotNull(_explodedAt, "the bomb never landed");
                Assert.Greater(damageDealt, 0f, "the truck should be inside the blast");
                Assert.IsTrue(truck.IsDestroyed, $"a bomb on target should destroy a truck (dealt {damageDealt:0})");
                Assert.AreEqual(_plane, destroyedBy, "the bomber gets the credit");
            }
            finally
            {
                GroundTarget.AnyDestroyed -= OnDestroyed;
            }
        }

        [UnityTest]
        public IEnumerator Bombs_BarelyHurtPlanes()
        {
            // A plane flying right where the bomb explodes takes only light body damage.
            var dummy = FlightTestScene.Dummies[0];
            dummy.enabled = false;
            dummy.gameObject.SetActive(true);
            var target = dummy.GetComponent<PlaneController>();
            target.SetStats(_flightStats);
            var health = target.GetComponent<PlaneHealth>();
            var hit = 0f;
            _bombs.HitPlane += (_, h, info) => { if (h == health) hit += info.Amount; };

            // Same class and speed, 40 m directly below: the bomb keeps our velocity, so it falls onto it. Respawn both
            // together, or the frames since setup leave us a few steps ahead.
            _plane.Respawn(Start, Quaternion.identity);
            target.Respawn(Start + Vector3.down * 40f, Quaternion.identity);
            float end = Time.time + 4f;
            yield return Press();
            while (_explodedAt == null && Time.time < end)
            {
                _plane.SetInput(default);
                target.SetInput(default);
                yield return new WaitForFixedUpdate();
            }

            Assert.IsNotNull(_explodedAt, "the bomb should have hit the plane below");
            Assert.Greater(hit, 0f);
            Assert.LessOrEqual(hit, _stats.directDamage + _stats.planeSplashDamage + 0.01f);
            Assert.IsTrue(target.IsAlive, "one bomb never kills a healthy plane");
        }
    }
}

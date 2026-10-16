using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>Per-part HP and hitboxes on the real plane in the FlightTest scene.</summary>
    public class PlaneDamageTests
    {
        PlaneController _plane;
        PlaneHealth _health;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
            Assert.IsNotNull(_health, "plane has no PlaneHealth");
        }

        [Test]
        public void EveryPartExists_WithHitboxAndFullHealth()
        {
            for (int i = 0; i < PlanePart.TypeCount; i++)
            {
                var type = (PlanePartType)i;
                var part = _health.GetPart(type);
                Assert.IsNotNull(part, $"missing {type}");
                Assert.IsNotEmpty(part.GetComponentsInChildren<Collider>(), $"{type} has no hitbox");
                Assert.Greater(part.MaxHealth, 0f);
                Assert.AreEqual(part.MaxHealth, part.Health);
            }
        }

        [Test]
        public void Damage_ReducesOnlyThatPart()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            float taken = _health.ApplyDamage(wing, 10f, wing.transform.position);
            Assert.AreEqual(10f, taken);
            Assert.AreEqual(wing.MaxHealth - 10f, wing.Health, 0.001f);
            var other = _health.GetPart(PlanePartType.WingRight);
            Assert.AreEqual(other.MaxHealth, other.Health);
        }

        [Test]
        public void DestroyedPart_IgnoresFurtherDamage()
        {
            var stab = _health.GetPart(PlanePartType.VerticalStabilizer);
            int destroyedEvents = 0;
            _health.PartDestroyed += (_, info) => { if (info.Part == stab) destroyedEvents++; };

            float taken = _health.ApplyDamage(stab, stab.MaxHealth * 3f, stab.transform.position);
            Assert.AreEqual(stab.MaxHealth, taken, 0.001f, "overkill damage is clamped to remaining HP");
            Assert.IsTrue(stab.IsDestroyed);
            Assert.AreEqual(0f, _health.ApplyDamage(stab, 10f, stab.transform.position));
            Assert.AreEqual(1, destroyedEvents);
            Assert.IsTrue(_plane.IsAlive, "losing a stabilizer doesn't kill the plane");
        }

        [Test]
        public void DeadEngine_DoesNotDetach_AndPlaneSurvives()
        {
            var engine = _health.GetPart(PlanePartType.Engine);
            Assert.IsFalse(engine.CanDetach);
            _health.ApplyDamage(engine, engine.MaxHealth, engine.transform.position);
            Assert.IsTrue(engine.IsDestroyed);
            Assert.IsFalse(engine.IsDetached);
            Assert.IsTrue(engine.gameObject.activeInHierarchy, "a dead engine keeps its hitbox");
            Assert.IsTrue(_plane.IsAlive);
        }

        [Test]
        public void DestroyedWing_BreaksOff_RemovingItsHitbox()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            Vector3 target = wing.GetComponentInChildren<Collider>().bounds.center;
            Assert.IsNotNull(wing.brokenEdge, "wing has no broken edge");

            _health.ApplyDamage(wing, wing.MaxHealth, target);

            Assert.IsTrue(wing.IsDetached);
            Assert.IsFalse(wing.gameObject.activeInHierarchy);
            Assert.IsTrue(wing.brokenEdge.activeInHierarchy, "broken edge shows on the fuselage");
            Assert.IsNotEmpty(Object.FindObjectsByType<PlaneDebris>(FindObjectsSortMode.None), "the wing falls away as debris");
            Assert.IsTrue(_plane.IsAlive);

            // Shots through where the wing was now miss the plane entirely.
            Physics.SyncTransforms();
            Vector3 from = target + _plane.transform.up * 20f;
            bool hitWing = Physics.Raycast(from, -_plane.transform.up, out var hit, 40f)
                && PlanePart.TryGetFromCollider(hit.collider, out var hitPart) && hitPart == wing;
            Assert.IsFalse(hitWing, "a lost wing can't be hit");
        }

        [Test]
        public void RestoreAll_ReattachesLostParts()
        {
            var stab = _health.GetPart(PlanePartType.HorizontalStabilizer);
            _health.ApplyDamage(stab, stab.MaxHealth, stab.transform.position);
            Assert.IsTrue(stab.IsDetached);

            _health.RestoreAll();

            Assert.IsFalse(stab.IsDetached);
            Assert.IsTrue(stab.gameObject.activeInHierarchy);
            Assert.IsFalse(stab.brokenEdge.activeInHierarchy);
            Assert.AreEqual(stab.MaxHealth, stab.Health);
        }

        [UnityTest]
        public IEnumerator PlaneKeepsFlying_ThroughItsOwnDebris()
        {
            var wing = _health.GetPart(PlanePartType.WingRight);
            var stab = _health.GetPart(PlanePartType.VerticalStabilizer);
            _health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            _health.ApplyDamage(stab, stab.MaxHealth, stab.transform.position);

            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(_plane.IsAlive, "losing parts must not crash the plane into its own debris");
        }

        [UnityTest]
        public IEnumerator BodyAtZero_ExplodesInMidAir_RespawnsAfterDelay_WithAllParts()
        {
            var respawner = _plane.GetComponent<PlaneRespawner>();
            Assert.IsNotNull(respawner);
            Assert.GreaterOrEqual(respawner.RespawnDelay, 4f, "the explosion needs a few seconds before the respawn");

            var wing = _health.GetPart(PlanePartType.WingRight);
            _health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            var body = _health.GetPart(PlanePartType.Body);
            Vector3 deathPosition = _plane.Position;
            _health.ApplyDamage(body, body.MaxHealth, body.transform.position);
            Assert.IsFalse(_plane.IsAlive, "body at 0 HP destroys the plane");
            Assert.AreEqual(DeathCause.ShotDown, _plane.LastDeathCause);
            Assert.AreEqual(BodyDamageState.Destroyed, _health.BodyState);

            yield return null;
            Assert.IsNotEmpty(Object.FindObjectsByType<PlaneDebris>(FindObjectsSortMode.None), "the plane breaks into wreckage");
            Assert.IsNotNull(GameObject.Find("Explosion"), "an explosion plays where it died");

            yield return new WaitForSeconds(respawner.RespawnDelay - 0.5f);
            Assert.IsFalse(_plane.IsAlive, "still dead until the delay runs out");
            Assert.Greater(respawner.RespawnTimeLeft, 0f);
            Assert.AreEqual(deathPosition, _plane.Position, "the wreck doesn't keep flying");

            float timeout = Time.time + 2f;
            while (!_plane.IsAlive && Time.time < timeout)
                yield return null;
            Assert.IsTrue(_plane.IsAlive, "plane should respawn");
            Assert.AreEqual(wing.MaxHealth, wing.Health, "respawn restores lost parts");
            Assert.IsFalse(wing.IsDetached, "respawn reattaches lost parts");
            Assert.AreEqual(body.MaxHealth, body.Health);
            Assert.AreEqual(BodyDamageState.Healthy, _health.BodyState);
        }

        [Test]
        public void DamageAfterDeath_IsIgnored()
        {
            var body = _health.GetPart(PlanePartType.Body);
            _health.ApplyDamage(body, body.MaxHealth, body.transform.position);
            var wing = _health.GetPart(PlanePartType.WingLeft);
            Assert.AreEqual(0f, _health.ApplyDamage(wing, 10f, wing.transform.position), "a wreck can't be shot");
            Assert.AreEqual(wing.MaxHealth, wing.Health);
        }

        // ------------------------------------------------------------------ flight effects of lost parts

        void Destroy(PlanePartType type)
        {
            var part = _health.GetPart(type);
            _health.ApplyDamage(part, part.MaxHealth, part.transform.position);
        }

        /// <summary>Bank angle: + = right wing up (banked left).</summary>
        float Bank => Mathf.Atan2(_plane.transform.right.y, _plane.transform.up.y) * Mathf.Rad2Deg;

        [UnityTest]
        public IEnumerator LostLeftWing_HandsOff_RollsLeft()
        {
            Destroy(PlanePartType.WingLeft);
            for (float t = 0f; t < 1.5f; t += Time.fixedDeltaTime)
            {
                _plane.SetInput(default);
                yield return new WaitForFixedUpdate();
            }
            Assert.Greater(Bank, 20f, "with no input the plane should roll toward the missing wing");
        }

        [UnityTest]
        public IEnumerator LostWing_CounterSteering_KeepsItFlying()
        {
            Destroy(PlanePartType.WingRight);
            float startAlt = _plane.transform.position.y;
            for (float t = 0f; t < 10f; t += Time.fixedDeltaTime)
            {
                // A focused pilot: hold the wings level and the nose a little up.
                float pitchAngle = Mathf.Asin(_plane.transform.forward.y) * Mathf.Rad2Deg;
                _plane.SetInput(new FlightInput
                {
                    Roll = Mathf.Clamp(Bank / 15f, -1f, 1f),
                    Pitch = Mathf.Clamp((6f - pitchAngle) / 10f, -1f, 1f),
                });
                yield return new WaitForFixedUpdate();
            }
            Assert.IsTrue(_plane.IsAlive);
            Assert.Greater(_plane.transform.up.y, 0.8f, "counter-steering should hold the wings roughly level");
            Assert.Greater(_plane.transform.position.y, startAlt - 60f, "one wing should be survivable, not a plummet");
        }

        [UnityTest]
        public IEnumerator DeadEngine_Glides()
        {
            float startSpeed = _plane.Speed;
            Destroy(PlanePartType.Engine);
            Assert.AreEqual(0f, _plane.Modifiers.Get(FlightStat.Thrust));
            for (float t = 0f; t < 3f; t += Time.fixedDeltaTime)
            {
                _plane.SetInput(new FlightInput { Throttle = 1f, Boost = true });
                yield return new WaitForFixedUpdate();
            }
            Assert.IsFalse(_plane.IsBoosting, "a dead engine can't boost");
            Assert.Less(_plane.Speed, startSpeed - 3f, "no thrust: the plane slows down");
            Assert.Greater(_plane.Speed, startSpeed - 20f, "but it glides rather than stopping dead");
        }

        [Test]
        public void RestoreAll_ClearsFlightEffects()
        {
            Destroy(PlanePartType.WingLeft);
            Destroy(PlanePartType.HorizontalStabilizer);
            Destroy(PlanePartType.Engine);
            Assert.AreNotEqual(0, _health.LostPartsMask);
            Assert.AreNotEqual(0f, _plane.Modifiers.Get(FlightStat.RollBias));

            _health.RestoreAll();

            Assert.AreEqual(0, _health.LostPartsMask);
            Assert.AreEqual(0f, _plane.Modifiers.Get(FlightStat.RollBias));
            Assert.AreEqual(0f, _plane.Modifiers.Get(FlightStat.PitchWobble));
            Assert.AreEqual(1f, _plane.Modifiers.Get(FlightStat.Lift));
            Assert.AreEqual(1f, _plane.Modifiers.Get(FlightStat.Thrust));
        }

        [Test]
        public void RaycastAtPart_FindsThatPart()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            Vector3 target = wing.GetComponentInChildren<Collider>().bounds.center;
            Vector3 from = target + _plane.transform.up * 20f;
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(from, -_plane.transform.up, out var hit, 40f));
            Assert.IsTrue(PlanePart.TryGetFromCollider(hit.collider, out var hitPart));
            Assert.AreEqual(PlanePartType.WingLeft, hitPart.partType);
            Assert.Greater(_health.ApplyDamage(hit.collider, 5f, hit.point), 0f);
        }

        [Test]
        public void ThinParts_HaveHittableEdgeOnHitbox()
        {
            // A shot from straight ahead at wing height must still hit the wing despite it being 0.22 m thick.
            var wing = _health.GetPart(PlanePartType.WingRight);
            Vector3 target = wing.GetComponentInChildren<Collider>().bounds.center + _plane.transform.up * 0.25f;
            Vector3 from = target + _plane.transform.forward * 30f;
            Physics.SyncTransforms();
            Assert.IsTrue(Physics.Raycast(from, -_plane.transform.forward, out var hit, 60f));
            Assert.IsTrue(PlanePart.TryGetFromCollider(hit.collider, out var hitPart));
            Assert.AreEqual(PlanePartType.WingRight, hitPart.partType);
        }

        [Test]
        public void MaxHealth_DependsOnClass()
        {
            FlightStats tank = null, fast = null;
            foreach (var s in Resources.FindObjectsOfTypeAll<FlightStats>())
            {
                if (s.displayName == "Heavy Fighter") tank = s;
                if (s.displayName == "Interceptor") fast = s;
            }
            if (tank == null || fast == null)
                Assert.Ignore("class assets aren't loaded in this scene");

            _plane.SetStats(fast);
            float fastBody = _health.GetPart(PlanePartType.Body).MaxHealth;
            _plane.SetStats(tank);
            float tankBody = _health.GetPart(PlanePartType.Body).MaxHealth;
            Assert.Greater(tankBody, fastBody, "the Heavy should have a tougher body than the Interceptor");
        }
    }
}

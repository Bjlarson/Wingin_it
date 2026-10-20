using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Effects;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>The VFX pack (Pack 8): the library, the pool, and the plane's damage smoke on the FlightTest plane.</summary>
    public class VfxTests
    {
        PlaneController _plane;
        PlaneHealth _health;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _health = _plane.GetComponent<PlaneHealth>();
        }

        [Test]
        public void Library_HasAnEffectForEveryKind_AndTheShieldAndFlameMeshes()
        {
            var library = Vfx.Library;
            Assert.IsNotNull(library, "Resources/VfxLibrary is missing: run the scene builder");
            foreach (VfxKind kind in System.Enum.GetValues(typeof(VfxKind)))
                Assert.IsNotNull(library.Get(kind), $"no prefab for {kind}");
            Assert.IsNotNull(library.shieldMesh);
            Assert.IsNotNull(library.afterburnerMesh);
        }

        [UnityTest]
        public IEnumerator OneShot_RecyclesIntoThePool_AndIsReused()
        {
            var first = Vfx.Play(VfxKind.ExplosionAir, new Vector3(0f, 300f, 0f));
            Assert.IsNotNull(first);
            Assert.IsTrue(first.IsPlaying);
            Assert.AreEqual(1, Vfx.PlayingCount(VfxKind.ExplosionAir));

            yield return new WaitForSeconds(first.Lifetime + 0.2f);
            Assert.IsFalse(first.IsPlaying, "a one-shot ends once its last particle is gone");
            Assert.AreEqual(0, Vfx.PlayingCount(VfxKind.ExplosionAir));

            var second = Vfx.Play(VfxKind.ExplosionAir, new Vector3(0f, 300f, 0f));
            Assert.AreSame(first, second, "the pooled copy is played again rather than a new one made");
        }

        [UnityTest]
        public IEnumerator Looping_FollowsItsParent_ThenStopsAndFinishes()
        {
            var carrier = new GameObject("Carrier").transform;
            carrier.position = new Vector3(0f, 300f, 0f);
            var smoke = Vfx.Attach(VfxKind.DamageSmokeHeavy, carrier, Vector3.zero);
            Assert.IsNotNull(smoke);
            for (int i = 0; i < 20; i++)
            {
                carrier.position += Vector3.forward * 3f;
                yield return null;
            }
            Assert.AreEqual(carrier, smoke.transform.parent);
            Assert.Greater(smoke.GetComponentInChildren<ParticleSystem>().particleCount, 0, "puffs stream out as it moves");

            smoke.Stop();
            Assert.AreNotEqual(carrier, smoke.transform.parent, "a stopped emitter lets go of what it followed");
            Assert.IsTrue(smoke.IsPlaying, "its puffs finish first");
            yield return new WaitForSeconds(smoke.Lifetime + 0.2f);
            Assert.IsFalse(smoke.IsPlaying);
            Object.Destroy(carrier.gameObject);
        }

        [UnityTest]
        public IEnumerator DamageSmoke_FollowsTheBodyState()
        {
            var body = _health.GetPart(PlanePartType.Body);
            _health.ApplyDamage(body, body.MaxHealth * 0.5f, body.transform.position); // Damaged
            yield return null;
            Assert.AreEqual(1, Vfx.PlayingCount(VfxKind.DamageSmokeLight), "grey wisps when Damaged");
            Assert.AreEqual(0, Vfx.PlayingCount(VfxKind.DamageFire));

            _health.ApplyDamage(body, body.MaxHealth * 0.35f, body.transform.position); // Critical
            yield return null;
            Assert.AreEqual(1, Vfx.PlayingCount(VfxKind.DamageSmokeHeavy), "black smoke when Critical");
            Assert.AreEqual(1, Vfx.PlayingCount(VfxKind.DamageFire), "and fire");
            foreach (var e in _plane.GetComponentsInChildren<VfxEffect>())
                Assert.AreNotEqual(VfxKind.DamageSmokeLight, e.Kind, "the wisps let go when the smoke thickens");

            _health.RestoreAll();
            yield return null;
            Assert.IsEmpty(_plane.GetComponentsInChildren<VfxEffect>(), "a repaired plane stops smoking");
        }

        [UnityTest]
        public IEnumerator PartBreakingOff_SparksAndTrailsSmoke()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            yield return null;
            Assert.AreEqual(1, Vfx.PlayingCount(VfxKind.PartDetach));
            var debris = Object.FindAnyObjectByType<PlaneDebris>();
            Assert.IsNotNull(debris);
            Assert.IsNotNull(debris.GetComponentInChildren<VfxEffect>(), "the falling wing trails smoke");
        }
    }
}

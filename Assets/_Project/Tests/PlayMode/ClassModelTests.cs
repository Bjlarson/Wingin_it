using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>
    /// The finished class models on the shared plane prefab: each shows only for its class, hitboxes kept. Every class has
    /// a model now, so a copy of the Fighter's stats (no model is registered for it) stands in for "a class that still
    /// uses the placeholder boxes".
    /// </summary>
    public class ClassModelTests
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        PlaneController _plane;
        FlightStats _noModelClass;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _plane = FlightTestScene.Player;
            _plane.SetStats(FlightTestScene.FindClass(c => c.displayName == "Fighter"));
            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            yield return null;
        }

        [TearDown]
        public void DestroyNoModelClass()
        {
            if (_noModelClass != null)
                Object.Destroy(_noModelClass);
        }

        static bool IsModel(Renderer r) => r.name.EndsWith(" Model");

        static FlightStats Class(string name) => FlightTestScene.FindClass(c => c.displayName == name);

        FlightStats NoModelClass => _noModelClass != null ? _noModelClass : _noModelClass = Object.Instantiate(Class("Fighter"));

        Renderer[] Visible(bool model) =>
            _plane.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r is MeshRenderer && IsModel(r) == model).ToArray();

        /// <summary>The gun muzzles' spans (|x| in the model's space, before the class scale).</summary>
        float[] MuzzleSpans() => _plane.GetComponentsInChildren<Transform>().Where(t => t.name == "Muzzle")
            .Select(t => Mathf.Abs(t.parent.localPosition.x + t.localPosition.x)).ToArray();

        [Test]
        public void Fighter_ShowsItsModel_InPlaceOfTheBoxes_KeepingHitboxes()
        {
            Assert.IsNotEmpty(Visible(model: true), "Fighter model is showing");
            Assert.IsEmpty(Visible(model: false), "placeholder boxes are hidden");
            foreach (var part in _plane.GetComponentsInChildren<PlanePart>())
                Assert.IsTrue(part.GetComponentsInChildren<Collider>().Any(c => c.enabled), $"{part.partType} keeps its hitbox");
        }

        [UnityTest]
        public IEnumerator OtherClass_ShowsTheBoxes_AndBackAgain()
        {
            _plane.SetStats(NoModelClass);
            yield return null;
            Assert.IsEmpty(Visible(model: true), "Fighter model hidden on a class without a model");
            Assert.IsNotEmpty(Visible(model: false), "placeholder boxes shown");

            _plane.SetStats(FlightTestScene.FindClass(c => c.displayName == "Fighter"));
            yield return null;
            Assert.IsNotEmpty(Visible(model: true));
            Assert.IsEmpty(Visible(model: false));
        }

        [UnityTest]
        public IEnumerator ClassChangedWhileDown_StaysHidden_UntilRespawn()
        {
            _plane.Crash(_plane.Position);
            yield return null;
            _plane.SetStats(NoModelClass);
            yield return null;
            Assert.IsEmpty(_plane.GetComponentsInChildren<Renderer>().Where(r => r.enabled && r is MeshRenderer), "nothing shows while down");

            _plane.Respawn(new Vector3(0f, 600f, -1500f), Quaternion.identity);
            yield return null;
            Assert.IsEmpty(Visible(model: true), "respawned as the new class: no Fighter model");
            Assert.IsNotEmpty(Visible(model: false));
        }

        [UnityTest]
        public IEnumerator Interceptor_ShowsItsOwnModel_WithGunsOnItsWingRootCannons()
        {
            float[] fighterSpans = MuzzleSpans();
            Assert.That(fighterSpans, Has.All.GreaterThan(2f), "Fighter guns sit out on the wings");

            _plane.SetStats(Class("Interceptor"));
            yield return null;
            var shown = Visible(model: true);
            Assert.IsNotEmpty(shown, "Interceptor model is showing");
            Assert.That(shown.Select(r => r.name), Has.All.EqualTo("Interceptor Model"), "only the Interceptor's model");
            Assert.IsEmpty(Visible(model: false), "placeholder boxes are hidden");
            Assert.That(MuzzleSpans(), Has.All.LessThan(2f), "guns moved onto the wing-root cannons");

            _plane.SetStats(Class("Fighter"));
            yield return null;
            Assert.That(Visible(model: true).Select(r => r.name), Has.All.EqualTo("Fighter Model"));
            Assert.That(MuzzleSpans(), Is.EqualTo(fighterSpans).Within(1e-4f), "guns back where the Fighter has them");
        }

        [UnityTest]
        public IEnumerator HeavyFighter_SpinsItsWingProps_AndLosesOneWithItsWing()
        {
            _plane.SetStats(Class("Heavy Fighter"));
            yield return null;
            Assert.That(Visible(model: true).Select(r => r.name), Has.All.EqualTo("HeavyFighter Model"), "only the Heavy's model");
            Assert.IsEmpty(Visible(model: false), "placeholder boxes are hidden");

            var health = _plane.GetComponent<PlaneHealth>();
            var wing = health.GetPart(PlanePartType.WingRight);
            // The wing carries its engine: the prop and its blur disc (the prop-blur material) sit off the wing's pivot.
            var prop = wing.GetComponentsInChildren<MeshRenderer>()
                .Single(r => IsModel(r) && r.transform.localPosition != Vector3.zero
                             && r.sharedMaterials.All(m => m.name != "Plane_PropBlur")).transform;
            Quaternion before = prop.localRotation;
            yield return new WaitForSeconds(0.2f);
            Assert.Greater(Quaternion.Angle(before, prop.localRotation), 1f, "the wing prop spins");

            health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            yield return null;
            Assert.IsFalse(prop.gameObject.activeInHierarchy, "the prop leaves with the wing");
        }

        [UnityTest]
        public IEnumerator Support_TurnsItsRadar_AndMovesRailsAndWingHitboxesUpToItsHighWing()
        {
            Transform[] rails = _plane.GetComponentsInChildren<Transform>().Where(t => t.name == "Rocket Rail").ToArray();
            Transform[] hitboxes = _plane.GetComponentsInChildren<BoxCollider>().Where(c => c.name == "Wing").Select(c => c.transform).ToArray();
            Assert.AreEqual(2, rails.Length);
            Assert.AreEqual(2, hitboxes.Length);
            float[] railHeights = rails.Select(t => t.localPosition.y).ToArray();

            _plane.SetStats(Class("Support"));
            yield return null;
            Assert.That(Visible(model: true).Select(r => r.name), Has.All.EqualTo("Support Model"), "only the Support's model");
            Assert.IsEmpty(Visible(model: false), "placeholder boxes are hidden");
            Assert.That(rails.Select(t => t.localPosition.y), Has.All.GreaterThan(0.3f), "rockets leave from the under-wing pods");
            Assert.That(hitboxes.Select(t => t.localPosition.y), Has.All.GreaterThan(0.6f), "wing hitboxes sit on the high wing");
            Assert.That(hitboxes.Select(t => t.GetComponent<Collider>().enabled), Has.All.True);

            var body = _plane.GetComponent<PlaneHealth>().GetPart(PlanePartType.Body).transform;
            var radar = body.GetComponentsInChildren<MeshRenderer>()
                .Single(r => r.name == "Support Model" && r.transform.parent == body && r.transform.localPosition.y > 1f).transform;
            Quaternion before = radar.localRotation;
            yield return new WaitForSeconds(0.3f);
            Assert.Greater(Quaternion.Angle(before, radar.localRotation), 3f, "the radar dome turns");
            Assert.Greater(Vector3.Dot(radar.up, body.up), 0.999f, "about its vertical axis");

            _plane.SetStats(Class("Fighter"));
            yield return null;
            Assert.That(rails.Select(t => t.localPosition.y), Is.EqualTo(railHeights).Within(1e-4f), "rails back on the prefab's pods");
            Assert.That(hitboxes.Select(t => t.localPosition), Has.All.EqualTo(Vector3.zero), "wing hitboxes back on the low wing");
        }

        [UnityTest]
        public IEnumerator TeamZones_TakeTheTeamColor()
        {
            _plane.Team = 1;
            yield return null;
            var team = Visible(model: true).SelectMany(r => r.sharedMaterials.Select((m, i) => (r, m, i)))
                .Where(x => x.m != null && x.m.name == "Plane_Team").ToArray();
            Assert.IsNotEmpty(team, "the model has team zones");
            var (renderer, _, index) = team[0];
            Assert.IsFalse(renderer.HasPropertyBlock(), "tinted by a shared material, so it stays in the SRP Batcher");
            Vector4 tint = renderer.sharedMaterials[index].GetVector(BaseColorId);
            Assert.Greater(tint.x, tint.z, "team 1 (red) tint is red");

            _plane.Team = 0;
            yield return null;
            tint = renderer.sharedMaterials[index].GetVector(BaseColorId);
            Assert.Greater(tint.z, tint.x, "team 0 (blue) tint is blue");
        }

        [UnityTest]
        public IEnumerator LostWing_FallsAsModelDebris_AndShowsTheStump()
        {
            var health = _plane.GetComponent<PlaneHealth>();
            var wing = health.GetPart(PlanePartType.WingRight);
            health.ApplyDamage(wing, wing.MaxHealth, wing.transform.position);
            yield return null;
            Assert.IsTrue(wing.IsDetached);
            Assert.IsTrue(wing.brokenEdge.GetComponentsInChildren<Renderer>().Any(r => r.enabled && IsModel(r)), "modelled stump shows");
            var debris = Object.FindObjectsByType<PlaneDebris>(FindObjectsSortMode.None);
            Assert.IsTrue(debris.Any(d => d.GetComponentsInChildren<Renderer>().Any(r => r.enabled && IsModel(r))), "debris carries the model");
        }
    }
}

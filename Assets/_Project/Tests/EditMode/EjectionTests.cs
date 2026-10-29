using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Flight;
using WingingIt.UI;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// Bailing out (<see cref="PlaneEjection"/>, <see cref="EjectedPilot"/>): who may eject, how the pilot falls, the
    /// HUD lines and the input action. The eject itself, in a scene, is in the PlayMode <c>PlaneEjectionTests</c>.
    /// </summary>
    public class EjectionTests
    {
        [Test]
        public void OnlyALiveCrippledPlaneInTheAir_CanEject()
        {
            Assert.IsTrue(PlaneEjection.CanEjectFrom(alive: true, grounded: false, crippled: true));
            Assert.IsFalse(PlaneEjection.CanEjectFrom(alive: true, grounded: false, crippled: false), "a healthy plane");
            Assert.IsFalse(PlaneEjection.CanEjectFrom(alive: true, grounded: true, crippled: true), "parked: repair instead");
            Assert.IsFalse(PlaneEjection.CanEjectFrom(alive: false, grounded: false, crippled: true), "already down");
        }

        [Test]
        public void FreeFall_SpeedsUpDownward()
        {
            var v = Vector3.zero;
            for (int i = 0; i < 30; i++)
                v = EjectedPilot.Step(v, chuteOpen: false, 1f / 60f);
            Assert.Less(v.y, -4f, "half a second of free fall");
        }

        [Test]
        public void UnderTheCanopy_ThePilotSettlesToTheDescentRate()
        {
            // Fired out of a plane at 100 m/s, falling fast: the canopy slows it to a gentle straight-down float.
            var v = new Vector3(0f, -20f, 100f);
            for (int i = 0; i < 60 * 5; i++)
                v = EjectedPilot.Step(v, chuteOpen: true, 1f / 60f);
            Assert.AreEqual(-EjectedPilot.DescentSpeed, v.y, 0.1f);
            Assert.Less(new Vector2(v.x, v.z).magnitude, 0.5f);
        }

        [Test]
        public void TheCanopy_BloomsOpenAfterTheDelay()
        {
            Assert.AreEqual(0f, EjectedPilot.Bloom(0f));
            Assert.AreEqual(0f, EjectedPilot.Bloom(EjectedPilot.ChuteDelay));
            Assert.AreEqual(1f, EjectedPilot.Bloom(EjectedPilot.ChuteDelay + EjectedPilot.ChuteOpenTime));
            float mid = EjectedPilot.Bloom(EjectedPilot.ChuteDelay + EjectedPilot.ChuteOpenTime * 0.5f);
            Assert.That(mid, Is.InRange(0.3f, 0.7f));
        }

        [Test]
        public void Hud_NamesWhoGetsTheKill()
        {
            Assert.AreEqual("EJECTED", HudText.Ejected(null));
            Assert.AreEqual("EJECTED  (RED 3 GETS THE KILL)", HudText.Ejected("RED 3"));
            Assert.AreEqual("HOLD X TO EJECT", HudText.EjectHint("x"));
            Assert.AreEqual("HOLD TO EJECT", HudText.EjectHint(""));
        }

        [Test]
        public void EjectAction_IsAHold_OnKeyboardAndGamepad()
        {
            var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Settings/FlightControls.inputactions");
            Assert.IsNotNull(asset);
            var eject = asset.FindActionMap("Flight", true).FindAction("Eject", true);
            StringAssert.Contains("Hold", eject.interactions, "a stray press can't throw the pilot out");
            bool keyboard = false, gamepad = false;
            foreach (var b in eject.bindings)
            {
                keyboard |= b.groups.Contains("Keyboard&Mouse");
                gamepad |= b.groups.Contains("Gamepad");
            }
            Assert.IsTrue(keyboard && gamepad);
        }

        [Test]
        public void PlanePrefab_CanEject_WithThePilotPrefabs()
        {
            var plane = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Plane.prefab");
            Assert.IsNotNull(plane);
            var ejection = plane.GetComponent<PlaneEjection>();
            Assert.IsNotNull(ejection, "rebuild the plane prefab (FlightTestSceneBuilder.BuildAll)");
            var so = new SerializedObject(ejection);
            Assert.IsNotNull(so.FindProperty("pilotPrefab").objectReferenceValue, "pilot prefab");
            Assert.IsNotNull(so.FindProperty("parachutePrefab").objectReferenceValue, "parachute prefab");
        }
    }
}

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Loads the FlightTest scene and hands the player plane to the test (player input off, dummies parked, AA off).</summary>
    public static class FlightTestScene
    {
        public static PlaneController Player { get; private set; }
        public static DummyPilot[] Dummies { get; private set; }
        public static AAGun[] AAGuns { get; private set; }

        public static IEnumerator Load()
        {
            yield return SceneManager.LoadSceneAsync("FlightTest", LoadSceneMode.Single);
            yield return null;

            var go = GameObject.Find("Player Plane");
            Assert.IsNotNull(go, "FlightTest scene has no Player Plane");
            Player = go.GetComponent<PlaneController>();
            foreach (var mb in Player.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name is "PlayerFlightInput" or "ClassSwitcher" or "DamageTester")
                    mb.enabled = false;

            // Dummies would fly into shots and sweeps; tests that want a target activate one.
            Dummies = UnityEngine.Object.FindObjectsByType<DummyPilot>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            foreach (var dummy in Dummies)
                dummy.gameObject.SetActive(false);

            // Tests put enemy dummies near the home airfield; its AA would shoot them. AA tests switch it back on.
            AAGuns = UnityEngine.Object.FindObjectsByType<AAGun>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID);
            foreach (var gun in AAGuns)
                gun.enabled = false;
            AAGun.DevHostilePlane = null;
        }

        /// <summary>The first class in the scene's class list that matches, e.g. one whose loadout carries rockets.</summary>
        public static FlightStats FindClass(Func<FlightStats, bool> match)
        {
            var switcher = Player.GetComponent<ClassSwitcher>();
            Assert.IsNotNull(switcher, "Player Plane has no ClassSwitcher to find classes from");
            foreach (var cls in switcher.Classes)
                if (cls != null && match(cls))
                    return cls;
            Assert.Fail("no class in the scene matches");
            return null;
        }
    }
}

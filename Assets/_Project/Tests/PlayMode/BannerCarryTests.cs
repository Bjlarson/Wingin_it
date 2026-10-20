using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Capture the Banner in the Island: grabbing at the enemy stand, towing (drag, no boost / repair), and dropping on death.</summary>
    public class BannerCarryTests
    {
        MatchManager _match;
        PlaneController _player;
        BannerMode _mode;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameSettings.UseForTests(new GameSettings(false, null));
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
            _mode = new BannerMode();
            _match.SetMode(_mode);
            _match.CountdownLength = 0.2f;
            _match.OpenClassSelect();
            _match.SelectClass(0);
            float t = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Playing && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);
        }

        [TearDown]
        public void ResetTime()
        {
            Time.timeScale = 1f;
            GameSettings.UseForTests(null);
        }

        /// <summary>The player (West) flying low through the East stand's zone, passing beside its pole.</summary>
        IEnumerator GrabTheEastBanner()
        {
            var stand = BannerStand.Of(1);
            Vector3 at = stand.Position + new Vector3(-10f, 12f, 12f); // beside the pole (a plane in it crashes), inside the zone
            _player.Respawn(at, Quaternion.LookRotation(Vector3.right));
            float t = Time.time;
            while (_mode.Board?.CarriedBy(_player) != 1 && Time.time - t < 2f)
                yield return null;
        }

        [UnityTest]
        public IEnumerator FlyingLowThroughTheEnemyStand_GrabsTheBanner_AndTheCarrierDrags()
        {
            float topSpeed = _player.Modifiers.Get(FlightStat.MaxSpeed), climb = _player.Modifiers.Get(FlightStat.Climb);
            yield return GrabTheEastBanner();

            Assert.AreEqual(1, _mode.Board.CarriedBy(_player), "the player took East's banner");
            Assert.IsTrue(_player.CarryingBanner);
            Assert.AreEqual(topSpeed * 0.75f, _player.Modifiers.Get(FlightStat.MaxSpeed), 0.01f);
            Assert.AreEqual(climb * 0.7f, _player.Modifiers.Get(FlightStat.Climb), 0.01f);

            var cloth = FindCloth(1);
            Assert.AreEqual(TowedBanner.Place.Towed, cloth.Where);
            yield return new WaitForSeconds(1f);
            Assert.Less((cloth.Position - _player.transform.position).magnitude, 60f, "the cloth trails the carrier on its cable");
            Assert.Greater((cloth.Position - _player.transform.position).magnitude, 20f);
        }

        [UnityTest]
        public IEnumerator ACarrier_CannotBoost()
        {
            yield return GrabTheEastBanner();
            _player.GetComponent<PlayerFlightInput>().enabled = false; // so the test's input stands
            _player.SetInput(new FlightInput { Boost = true, AutoThrottle = true });
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsFalse(_player.IsBoosting, "no Boost while towing");
        }

        [UnityTest]
        public IEnumerator ACarrierShotDown_DropsTheBanner_WhereItWentDown()
        {
            yield return GrabTheEastBanner();
            _player.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, _player.Position, null);
            yield return null;

            Assert.AreEqual(BannerState.Dropped, _mode.Board.Get(1).State);
            Assert.IsNull(_mode.Board.Get(1).Carrier);
            Assert.IsFalse(_player.CarryingBanner);
            Assert.AreEqual(TowedBanner.Place.Lying, FindCloth(1).Where);
            Assert.Greater(_mode.Board.Get(1).ReturnLeft, 0f);
        }

        [UnityTest]
        public IEnumerator ADroppedBanner_GoesHomeByItself_AfterTheReturnTime()
        {
            yield return GrabTheEastBanner();
            _mode.Board.ReturnTime = 0.3f;
            _player.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, _player.Position, null);
            _mode.Board.Get(1).ReturnLeft = 0.3f;
            yield return new WaitForSeconds(0.6f);

            Assert.AreEqual(BannerState.Home, _mode.Board.Get(1).State);
            Assert.AreEqual(TowedBanner.Place.Home, FindCloth(1).Where);
        }

        [UnityTest]
        public IEnumerator ACarrier_IsRevealedToTheEnemy_AndGrabbingCallsItOutOnTheHud()
        {
            Assert.IsFalse(PlaneAbility.IsRevealedTo(_player, 1), "no radar: not revealed while empty-handed");
            yield return GrabTheEastBanner();

            Assert.IsTrue(PlaneAbility.IsRevealedTo(_player, 1), "the carrier shows to East the whole time");
            Assert.IsFalse(PlaneAbility.IsRevealedTo(_player, 0), "not to its own team's radar");
            var hud = Object.FindAnyObjectByType<WingingIt.UI.FlightHUD>();
            yield return null;
            Assert.IsNotNull(hud);
            Assert.IsTrue(hud.CalloutTexts.Any(t => t.Contains("BANNER")), "a callout names the grab");
        }

        static TowedBanner FindCloth(int team) =>
            Object.FindObjectsByType<TowedBanner>(FindObjectsSortMode.None).First(b => b.Team == team);
    }
}

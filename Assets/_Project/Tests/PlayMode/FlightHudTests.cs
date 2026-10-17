using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.UI;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The flight HUD in the Island's Teams match: shown in play, score popups, the kill feed and the minimap.</summary>
    public class FlightHudTests
    {
        MatchManager _match;
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        IEnumerator StartPlaying()
        {
            _match.CountdownLength = 0.2f;
            _match.OpenClassSelect();
            _match.SelectClass(0);
            float t = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Playing && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);
            yield return null; // the HUD's Start
        }

        [UnityTest]
        public IEnumerator Hud_IsHiddenInTheMenu_AndShowsInPlay_WithAMinimap()
        {
            Assert.IsNull(Object.FindAnyObjectByType<FlightHUD>(), "hidden in the menu");
            yield return StartPlaying();
            var hud = Object.FindAnyObjectByType<FlightHUD>();
            Assert.IsNotNull(hud, "shown in play");
            Assert.IsNotNull(hud.Minimap, "the level was photographed for the minimap");
            Assert.IsTrue(RenderSettings.fog, "fog back on after the photo");
            foreach (var cloud in Object.FindObjectsByType<CloudBank>(FindObjectsSortMode.None))
                Assert.IsTrue(cloud.GetComponentsInChildren<Renderer>().All(r => r.enabled), "clouds shown again");
            Assert.IsTrue(_player.GetComponentsInChildren<Renderer>().Any(r => r.enabled), "planes shown again");
        }

        [UnityTest]
        public IEnumerator OurKill_PopsUpPoints_AndGoesInTheKillFeed()
        {
            yield return StartPlaying();
            var hud = Object.FindAnyObjectByType<FlightHUD>();
            var enemy = PlaneController.All.First(p => p.Team != _player.Team);
            var health = enemy.GetComponent<PlaneHealth>();

            health.ApplyDamage(PlanePartType.WingLeft, 10000f, enemy.Position, _player);
            CollectionAssert.Contains(hud.PopupTexts.ToList(), "WING SHOT OFF!");

            health.ApplyDamage(PlanePartType.Body, 10000f, enemy.Position, _player);
            CollectionAssert.Contains(hud.PopupTexts.ToList(), "+50  KILL");
            CollectionAssert.Contains(hud.KillFeedLines.ToList(), $"YOU DOWNED {enemy.name.ToUpperInvariant()}");
        }

        [UnityTest]
        public IEnumerator Assist_PopsUp_ButOtherPilotsKillsDoNot()
        {
            yield return StartPlaying();
            var hud = Object.FindAnyObjectByType<FlightHUD>();
            var mate = PlaneController.All.First(p => p.Team == _player.Team && p != _player);
            var enemies = PlaneController.All.Where(p => p.Team != _player.Team).ToList();

            var first = enemies[0].GetComponent<PlaneHealth>();
            first.ApplyDamage(PlanePartType.Body, first.GetPart(PlanePartType.Body).MaxHealth * 0.5f, enemies[0].Position, _player);
            first.ApplyDamage(PlanePartType.Body, 10000f, enemies[0].Position, mate);
            CollectionAssert.AreEqual(new[] { "+20  ASSIST" }, hud.PopupTexts.ToList(), "our assist, not the teammate's kill");
            CollectionAssert.Contains(hud.KillFeedLines.ToList(),
                $"{mate.name.ToUpperInvariant()} DOWNED {enemies[0].name.ToUpperInvariant()}");
        }

        [UnityTest]
        public IEnumerator KillFeed_KeepsCountingWhileWeAreDead()
        {
            yield return StartPlaying();
            var hud = Object.FindAnyObjectByType<FlightHUD>();
            _player.Crash(_player.Position);
            yield return null;
            Assert.IsFalse(hud.isActiveAndEnabled, "hidden under the respawn picker");
            CollectionAssert.Contains(hud.KillFeedLines.ToList(), "YOU CRASHED");

            var mate = PlaneController.All.First(p => p.Team == _player.Team && p != _player);
            var enemy = PlaneController.All.First(p => p.Team != _player.Team);
            enemy.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, enemy.Position, mate);
            CollectionAssert.Contains(hud.KillFeedLines.ToList(), $"{mate.name.ToUpperInvariant()} DOWNED {enemy.name.ToUpperInvariant()}");
        }

        /// <summary>Puts the player 3.5 km from home toward the middle of the map, 500 m up, flying away from the nearest friendly field: never on approach.</summary>
        void FlyAwayFromHome()
        {
            var home = Airfield.All.First(f => f.IsHome && f.Team == _player.Team);
            Vector3 center = FlightBoundary.Active.transform.position;
            Vector3 toCenter = Vector3.ProjectOnPlane(center - home.transform.position, Vector3.up).normalized;
            Vector3 pos = home.transform.position + toCenter * 3500f;
            pos.y = 500f;
            var nearest = Airfield.NearestFriendly(pos, _player.Team);
            Vector3 away = Vector3.ProjectOnPlane(pos - nearest.transform.position, Vector3.up).normalized;
            _player.Respawn(pos, Quaternion.LookRotation(away));
        }

        [UnityTest]
        public IEnumerator Critical_SoundsTheAlarm_AndSaysReturnToBase_UntilRepaired()
        {
            yield return StartPlaying();
            FlyAwayFromHome();
            var feedback = Object.FindAnyObjectByType<DamageFeedback>();
            var health = _player.GetComponent<PlaneHealth>();
            yield return null;
            Assert.AreEqual(DamageAlert.None, feedback.Alert);
            Assert.IsFalse(feedback.AlarmPlaying);

            var body = health.GetPart(PlanePartType.Body);
            health.ApplyDamage(PlanePartType.Body, body.MaxHealth * 0.5f, _player.Position);
            yield return null;
            Assert.AreEqual(DamageAlert.Warning, feedback.Alert);
            Assert.AreEqual(1, feedback.WarningTones, "one warning tone on becoming Damaged");
            Assert.IsFalse(feedback.ShowingReturnToBase);

            health.ApplyDamage(PlanePartType.Body, body.MaxHealth * 0.3f, _player.Position);
            yield return null;
            Assert.AreEqual(DamageAlert.Critical, feedback.Alert);
            Assert.IsTrue(feedback.ShowingReturnToBase);
            Assert.IsTrue(feedback.AlarmPlaying);

            health.RestoreAll();
            yield return null;
            Assert.AreEqual(DamageAlert.None, feedback.Alert);
            Assert.IsFalse(feedback.ShowingReturnToBase);
            Assert.IsFalse(feedback.AlarmPlaying);
        }

        [UnityTest]
        public IEnumerator LosingAWing_IsCritical_AndTheAlarmStopsWhenWeDie()
        {
            yield return StartPlaying();
            FlyAwayFromHome();
            var feedback = Object.FindAnyObjectByType<DamageFeedback>();
            var health = _player.GetComponent<PlaneHealth>();
            yield return null;

            health.ApplyDamage(PlanePartType.WingRight, 10000f, _player.Position);
            yield return null;
            Assert.AreEqual(DamageAlert.Critical, feedback.Alert);
            Assert.IsTrue(feedback.AlarmPlaying);
            Assert.AreEqual(0, feedback.WarningTones, "the alarm, not the warning tone");

            _player.Crash(_player.Position);
            yield return null;
            Assert.IsFalse(feedback.AlarmPlaying, "silent once dead (and hidden under the picker)");
        }
    }
}

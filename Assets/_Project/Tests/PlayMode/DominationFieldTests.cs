using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Airfield Domination in the Island: capture fields start neutral, landing takes one, and the owner repairs and respawns there.</summary>
    public class DominationFieldTests
    {
        MatchManager _match;
        PlaneController _player;
        DominationMode _mode;

        static Airfield Central => Airfield.All.Single(f => f.DisplayName == "Central Airfield");

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameSettings.UseForTests(new GameSettings(false, null));
            yield return MapTestScene.Load();
            _match = Object.FindAnyObjectByType<MatchManager>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
            _mode = new DominationMode();
            _match.SetMode(_mode);
            _match.CountdownLength = 0.2f;
            _match.OpenClassSelect();
            _match.SelectClass(0);
            float t = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Playing && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);

            // Just the player: no bots to shoot it off the runway mid-test.
            _match.Roster.enabled = false;
            foreach (var plane in PlaneController.All.ToArray())
                if (plane != _player)
                    plane.gameObject.SetActive(false); // not destroyed: the roster still lists its bots
            _player.GetComponent<PlayerFlightInput>().enabled = false;
            yield return null;
            Assert.IsNotNull(_match.Fields, "the Domination world is up after the first frame of play");
            _mode.Board.CaptureTime = 1f;
        }

        [TearDown]
        public void ResetTime()
        {
            Time.timeScale = 1f;
            GameSettings.UseForTests(null);
        }

        IEnumerator LandAtCentral()
        {
            yield return RunwayLanding.FinalApproachAndLand(_player, Central);
            yield return RunwayLanding.RollToAStop(_player);
            Assert.IsTrue(Central.RepairZone.Contains(_player.Position), "the auto-brake stops a capturer in the zone");
        }

        IEnumerator WaitUntil(System.Func<bool> done, float seconds)
        {
            float t = Time.time;
            while (!done() && Time.time - t < seconds)
                yield return null;
        }

        [UnityTest]
        public IEnumerator CaptureFields_StartNeutral_WithTheirDefensesOn()
        {
            Assert.AreEqual(3, _match.Fields.Fields.Count);
            foreach (var field in _match.Fields.Fields)
            {
                Assert.IsTrue(field.IsNeutral, field.DisplayName);
                Assert.IsTrue(field.IsCapturable, field.DisplayName);
                Assert.IsTrue(field.GetComponent<FieldDefenses>().IsActive, field.DisplayName);
            }
            Assert.AreEqual(1, _match.SpawnFields.Count, "only home to spawn at before any capture");
            yield break;
        }

        [UnityTest]
        public IEnumerator LandingAndStopping_CapturesTheField_AndTheOwnerRepairsThere()
        {
            yield return LandAtCentral();
            yield return WaitUntil(() => Central.Team == _player.Team, 3f);

            Assert.AreEqual(_player.Team, Central.Team, "a second parked on Central (1 s capture) takes it");
            Assert.IsTrue(Central.IsFriendlyTo(_player.Team));
            Assert.IsFalse(Central.IsCapturableBy(_player.Team), "not capturable by its owner");
            Assert.IsTrue(Central.IsCapturableBy(1 - _player.Team));
            Assert.GreaterOrEqual(_match.Score.TeamScore(_player.Team), _mode.CapturePoints + _mode.PilotCapturePoints, "the capture scored");
            Assert.AreEqual(1, _match.Score.Get(_player).Captures);
            var hud = Object.FindAnyObjectByType<WingingIt.UI.FlightHUD>();
            Assert.IsNotNull(hud);
            yield return null;
            CollectionAssert.Contains(hud.CalloutTexts.ToArray(), "YOU CAPTURED CENTRAL");

            var repair = _player.GetComponent<PlaneRepair>();
            yield return WaitUntil(() => repair.IsRepairing, 2f);
            Assert.IsTrue(repair.IsRepairing, "once it's ours, the capturer repairs on the spot");
        }

        [UnityTest]
        public IEnumerator AfterACapture_ThePlayerCanChooseToRespawnAtTheField()
        {
            yield return LandAtCentral();
            yield return WaitUntil(() => Central.Team == _player.Team, 3f);
            Assert.AreEqual(_player.Team, Central.Team);

            _player.Respawn(Airfield.HomeOf(_player.Team).AirSpawn.position, Quaternion.identity);
            _player.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, _player.Position, null);
            yield return null;
            Assert.AreEqual(ClassPicker.Respawn, _match.Picker);
            CollectionAssert.AreEqual(new[] { Airfield.HomeOf(_player.Team), Central }, _match.SpawnFields, "home first, then owned fields");
            Assert.AreSame(Airfield.HomeOf(_player.Team), _match.SpawnField, "died over home: home by default");

            _match.ChooseSpawnField(Central);
            Assert.AreSame(Central, _match.SpawnField);
            yield return WaitUntil(() => _player.IsAlive, _player.GetComponent<PlaneRespawner>().RespawnDelay + 2f);

            Assert.IsTrue(_player.IsAlive);
            Assert.Less(Vector3.Distance(_player.Position, Central.AirSpawn.position), 150f, "respawned over Central");
        }

        [UnityTest]
        public IEnumerator EndingTheMatchFlow_PutsTheFieldsBackAsBuilt()
        {
            yield return LandAtCentral();
            yield return WaitUntil(() => Central.Team == _player.Team, 3f);
            Assert.AreEqual(_player.Team, Central.Team);

            _match.Dismiss();
            Assert.IsTrue(Central.IsNeutral);
            Assert.IsFalse(Central.IsCapturable);
            Assert.IsFalse(Central.GetComponent<FieldDefenses>().IsActive);
            Assert.IsNull(DominationWorld.Active);
        }
    }
}

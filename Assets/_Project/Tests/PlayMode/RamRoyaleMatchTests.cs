using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.CameraSystem;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.Settings;

namespace WingingIt.Tests
{
    /// <summary>Ram Royale in the Island: sixteen pilots each on their own team in the arena, one life, then spectating.</summary>
    public class RamRoyaleMatchTests
    {
        MatchManager _match;
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameSettings.UseForTests(new GameSettings(false, null));
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
            _match.SetMode(new RamRoyaleMode());
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

        IEnumerator WaitUntil(System.Func<bool> done, float seconds)
        {
            float t = Time.time;
            while (!done() && Time.time - t < seconds)
                yield return null;
        }

        [UnityTest]
        public IEnumerator SixteenPilots_EachOnTheirOwnTeam_InTheArena()
        {
            var planes = PlaneController.All.ToList();
            Assert.AreEqual(16, planes.Count, "the player and 15 bots");
            CollectionAssert.AreEquivalent(Enumerable.Range(0, 16), planes.Select(p => p.Team), "a team each");
            Assert.AreEqual(0, _player.Team);
            Assert.AreEqual(16, _match.TeamCount);
            Assert.AreEqual(16, _match.Score.TeamCount);

            Assert.IsNotNull(_match.Arena, "Ram Royale's arena is in force");
            Vector3 center = _match.Arena.transform.position;
            foreach (var plane in planes)
                Assert.Less(Vector2.Distance(new Vector2(plane.Position.x, plane.Position.z), new Vector2(center.x, center.z)),
                    _match.Arena.Radius, $"{plane.name} starts in the arena");

            Assert.AreNotEqual(_match.TeamColor(0), _match.TeamColor(5));
            var bot = planes.First(p => p.Team == 5);
            Assert.AreEqual(_match.PilotName(bot), _match.TeamName(5), "a team is named after its pilot");
            yield return null;
        }

        [UnityTest]
        public IEnumerator OutOfLives_PlayerSpectatesASurvivor_AndNeverRespawns()
        {
            var rig = Object.FindAnyObjectByType<FlightCameraRig>();
            _player.Crash(_player.Position);
            yield return null;
            Assert.IsTrue(_match.IsSpectating, "one life: out at once");
            Assert.AreEqual(ClassPicker.None, _match.Picker, "no respawn picker");
            Assert.AreEqual(_player, rig.Target, "the camera holds on the player's own explosion first");

            yield return WaitUntil(() => _match.Spectated != null, 5f);
            Assert.IsNotNull(_match.Spectated, "then follows a survivor");
            Assert.IsTrue(_match.Spectated.IsAlive);
            Assert.AreNotEqual(_player, _match.Spectated);
            Assert.AreEqual(_match.Spectated, rig.Target);
            Assert.IsTrue(rig.Spectating);

            var first = _match.Spectated;
            _match.CycleSpectated(1);
            Assert.AreNotEqual(first, _match.Spectated, "switches to someone else");
            Assert.AreEqual(_match.Spectated, rig.Target);

            var respawner = _player.GetComponent<PlaneRespawner>();
            yield return new WaitForSeconds(respawner.RespawnDelay + 0.5f);
            Assert.IsFalse(_player.IsAlive, "no second life");
            Assert.IsTrue(_match.IsSpectating);

            _match.EndMatch();
            Assert.IsFalse(_match.IsSpectating, "the scoreboard ends spectating");
            Assert.AreEqual(_player, rig.Target, "and the camera is the player's again");
            Assert.IsFalse(rig.Spectating);
        }

        [UnityTest]
        public IEnumerator Dismiss_PutsTheLevelsTeamsBack()
        {
            _match.Dismiss();
            yield return null;
            Assert.AreEqual(2, _match.Roster.TeamCount);
            Assert.IsTrue(PlaneController.All.All(p => p.Team < 2), "the free-for-all bots are gone");
        }
    }
}

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Match;
using WingingIt.UI;

namespace WingingIt.Tests
{
    /// <summary>The Island's Teams match flow: lobby → class select → countdown → play → scoreboard.</summary>
    public class MatchFlowTests
    {
        MatchManager _match;
        BotRoster _roster;
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(_match, "Island scene has no MatchManager");
            _roster = Object.FindAnyObjectByType<BotRoster>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        /// <summary>Picks <paramref name="cls"/> with a short countdown and waits for play.</summary>
        IEnumerator StartPlaying(int cls = 0, float matchLength = 300f)
        {
            _match.CountdownLength = 0.2f;
            _match.MatchLength = matchLength;
            _match.OpenClassSelect();
            _match.SelectClass(cls);
            float t = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Playing && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);
        }

        [Test]
        public void Lobby_FreezesTheWorld_WithNoBots_AndNoControls()
        {
            Assert.AreEqual(MatchPhase.Lobby, _match.Phase);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.AreEqual(0, _roster.Bots.Count, "bots join when the match starts");
            Assert.IsFalse(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.IsNull(Object.FindAnyObjectByType<FlightDebugHUD>(), "flight HUD hidden");
            Assert.AreEqual(7, _roster.PlannedBots(0).Count, "lobby shows 7 bots beside the player");
            Assert.AreEqual(8, _roster.PlannedBots(1).Count);
        }

        [UnityTest]
        public IEnumerator PickingAClass_SpawnsInIt_FillsTheTeams_ThenCountsDownToPlay()
        {
            _match.CountdownLength = 0.5f;
            _match.OpenClassSelect();
            Assert.AreEqual(MatchPhase.ClassSelect, _match.Phase);
            _match.SelectClass(2);
            Assert.AreEqual(_match.Classes[2], _player.Stats);
            Assert.AreEqual(MatchPhase.Countdown, _match.Phase);
            Assert.AreEqual(0f, Time.timeScale, "frozen for the countdown");
            Assert.AreEqual(15, _roster.Bots.Count, "the bots are in place for the countdown");
            Vector3 frozen = _roster.Bots[0].transform.position;
            yield return null;
            Assert.AreEqual(frozen, _roster.Bots[0].transform.position);

            float t = Time.realtimeSinceStartup;
            while (_match.Phase == MatchPhase.Countdown && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.IsNotNull(Object.FindAnyObjectByType<FlightDebugHUD>(), "flight HUD back");
            Assert.AreEqual(16, _match.Score.Pilots.Count, "everyone on the scoreboard");
        }

        [UnityTest]
        public IEnumerator Kills_ScoreForTheKillersTeam()
        {
            yield return StartPlaying();
            var west = PlaneController.All.First(p => p.Team == 0 && p != _player);
            var east = PlaneController.All.Where(p => p.Team == 1).ToList();

            east[0].GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, east[0].Position, west);
            Assert.AreEqual(50, _match.Score.TeamScore(0));
            Assert.AreEqual(1, _match.Score.Get(west).Kills);
            Assert.AreEqual(1, _match.Score.Get(east[0]).Deaths);

            east[1].Crash(east[1].Position); // nobody hit it: no score
            Assert.AreEqual(50, _match.Score.TeamScore(0));
            Assert.AreEqual(1, _match.Score.Get(east[1]).Deaths);
            Assert.AreEqual(0, _match.Score.Winner());
        }

        [UnityTest]
        public IEnumerator Helpers_GetAssists_ButNotChipDamageOrTeammates()
        {
            yield return StartPlaying();
            var west = PlaneController.All.Where(p => p.Team == 0 && p != _player).ToList();
            var east = PlaneController.All.Where(p => p.Team == 1).ToList();
            var victim = east[0].GetComponent<PlaneHealth>();
            float bodyHp = victim.GetPart(PlanePartType.Body).MaxHealth;
            Vector3 at = victim.Plane.Position;

            victim.ApplyDamage(PlanePartType.Body, bodyHp * 0.4f, at, west[0]); // a real share: assist
            victim.ApplyDamage(PlanePartType.Body, bodyHp * 0.05f, at, west[1]); // chip damage: none
            victim.ApplyDamage(PlanePartType.WingLeft, 10000f, at, west[2]); // shot off a wing: assist
            victim.ApplyDamage(PlanePartType.Body, bodyHp * 0.3f, at, east[1]); // friendly fire: listed, never scored
            victim.ApplyDamage(PlanePartType.Body, 10000f, at, west[3]);

            Assert.AreEqual(3, victim.LastKill.Assisters.Count, "west[0], west[2] and the teammate east[1]");
            Assert.AreEqual(1, _match.Score.Get(west[3]).Kills);
            Assert.AreEqual(1, _match.Score.Get(west[0]).Assists);
            Assert.AreEqual(0, _match.Score.Get(west[1]).Assists);
            Assert.AreEqual(1, _match.Score.Get(west[2]).Assists);
            Assert.AreEqual(0, _match.Score.Get(east[1]).Assists);
            Assert.AreEqual(50 + 2 * 20, _match.Score.TeamScore(0));
            Assert.AreEqual(0, _match.Score.TeamScore(1));

            // A new life starts with a clean slate.
            victim.Plane.Respawn(at + Vector3.up * 300f, Quaternion.identity);
            victim.ApplyDamage(PlanePartType.Body, 10000f, victim.Plane.Position, west[3]);
            Assert.AreEqual(0, victim.LastKill.Assisters.Count);
        }

        [UnityTest]
        public IEnumerator Objectives_OnlyScoreInPlay()
        {
            Assert.IsFalse(_match.AwardObjective(0, 100, "Convoy destroyed"), "not in the lobby");
            yield return StartPlaying();
            Assert.IsTrue(_match.AwardObjective(1, 100, "Convoy destroyed"));
            var east = PlaneController.All.First(p => p.Team == 1);
            Assert.IsTrue(_match.AwardObjective(1, 30, "Supply drop", east));
            Assert.AreEqual(130, _match.Score.TeamScore(1));
            Assert.AreEqual(30, _match.Score.Get(east).Points);
            Assert.AreEqual(1, _match.Score.Winner());
        }

        [UnityTest]
        public IEnumerator TimeRunningOut_EndsTheMatch_AndFreezesForTheScoreboard()
        {
            yield return StartPlaying(matchLength: 1f);
            yield return new WaitForSecondsRealtime(2f); // game time stops at the whistle
            Assert.AreEqual(MatchPhase.Ended, _match.Phase);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.IsFalse(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.AreEqual(MatchScore.Draw, _match.Score.Winner(), "0-0 is a draw");

            // Deaths after the whistle don't count.
            var east = PlaneController.All.First(p => p.Team == 1);
            east.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, east.Position, _player);
            Assert.AreEqual(0, _match.Score.TeamScore(0));
        }

        [UnityTest]
        public IEnumerator Dismiss_GivesFreeFlight()
        {
            _match.Dismiss();
            yield return null;
            yield return null;
            Assert.AreEqual(1f, Time.timeScale);
            Assert.IsTrue(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.AreEqual(15, _roster.Bots.Count);
        }
    }
}

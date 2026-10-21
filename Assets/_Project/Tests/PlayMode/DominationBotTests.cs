using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Airfield Domination with a full 8v8 of bots on the Island: they land to take fields and bomb towers.</summary>
    public class DominationBotTests
    {
        const float Duration = 240f;

        MatchManager _match;
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator StartMatch()
        {
            GameSettings.UseForTests(new GameSettings(false, null));
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            _match = Object.FindAnyObjectByType<MatchManager>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
            _match.SetMode(new DominationMode());
            _match.CountdownLength = 0.2f;
            _match.OpenClassSelect();
            _match.SelectClass(0);
            float t = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Playing && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);

            // The player plays along as a covering bot, so its team isn't a man down.
            _player.GetComponent<PlayerFlightInput>().enabled = false;
            _player.gameObject.AddComponent<BotPilot>();
            var brain = _player.gameObject.AddComponent<BotBrain>();
            brain.DominationRole = DominationRole.Cover;
            brain.RosterSlot = 7;
            yield return null;
            Assert.IsNotNull(_match.Fields);
        }

        [TearDown]
        public void ResetTime()
        {
            Time.timeScale = 1f;
            GameSettings.UseForTests(null);
        }

        [UnityTest]
        public IEnumerator EightVersusEight_BotsCaptureFields_AndBombTowers()
        {
            var world = _match.Fields;
            var captures = new List<string>();
            void OnCaptured(FieldCapture c) =>
                captures.Add($"{Time.time:F0}s {world.Fields[c.Field].DisplayName} -> {c.Team} ({c.Pilots.Count} pilots)");
            world.Captured += OnCaptured;

            var towers = world.Fields.Select(f => f.GetComponent<FieldDefenses>().Tower).ToList();
            int towersDown = 0, bombs = 0, crashes = 0;
            void OnDestroyed(GroundTarget target, Object _)
            {
                if (towers.Contains(target))
                    towersDown++;
            }
            GroundTarget.AnyDestroyed += OnDestroyed;
            void OnDropped(PlaneBombs _) => bombs++;
            var bays = PlaneController.All.Select(p => p.GetComponent<PlaneBombs>()).Where(b => b != null).ToList();
            foreach (var b in bays)
                b.Dropped += OnDropped;
            void OnKilled(KillInfo k)
            {
                if (k.Killer == null || k.Killer.IsAllyOf(k.Victim.Plane))
                    crashes++;
            }
            PlaneHealth.AnyKilled += OnKilled;

            var duties = new Dictionary<DominationDuty, float>();
            float parkedSeconds = 0f;
            Time.timeScale = 4f;
            for (float t = 0f; t < Duration && _match.Phase == MatchPhase.Playing; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                foreach (var b in BotBrain.All)
                {
                    duties.TryGetValue(b.DominationDuty, out float s);
                    duties[b.DominationDuty] = s + Time.fixedDeltaTime;
                    if (b.State == BotState.Capture)
                        parkedSeconds += Time.fixedDeltaTime;
                }
            }
            Time.timeScale = 1f;

            world.Captured -= OnCaptured;
            GroundTarget.AnyDestroyed -= OnDestroyed;
            PlaneHealth.AnyKilled -= OnKilled;
            foreach (var b in bays)
                if (b != null)
                    b.Dropped -= OnDropped;

            string owners = string.Join(", ", world.Fields.Select(f => $"{f.DisplayName}: {f.Team}"));
            Debug.Log($"[Domination bots] {captures.Count} captures, {towersDown} towers down, {bombs} bombs, {crashes} uncredited deaths, " +
                      $"{parkedSeconds:F0} bot-seconds parked capturing; owners {owners}; score {_match.Score.TeamScore(0)} - {_match.Score.TeamScore(1)}\n" +
                      string.Join("\n", captures) + "\nduty bot-seconds: " +
                      string.Join(", ", duties.Select(d => $"{d.Key} {d.Value:F0}")));

            Assert.Greater(duties.GetValueOrDefault(DominationDuty.Capture), 0f, "some bots were sent to capture");
            Assert.Greater(duties.GetValueOrDefault(DominationDuty.Cover), 0f, "some bots flew cover");
            Assert.GreaterOrEqual(captures.Count, 3, "bots land and take fields: at least every field once");
            Assert.Greater(bombs, 0, "Heavies bomb defended enemy towers");
        }
    }
}

using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Maps;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.UI;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The Island's Teams match flow: menu → class select → countdown → play (and the in-match menu) → scoreboard.</summary>
    public class MatchFlowTests
    {
        MatchManager _match;
        BotRoster _roster;
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            GameSettings.UseForTests(new GameSettings(false, null)); // defaults, nothing saved
            yield return MapTestScene.Load();
            _match = Object.FindAnyObjectByType<MatchManager>();
            Assert.IsNotNull(_match, "MainMenu + Map_Island have no MatchManager");
            _roster = Object.FindAnyObjectByType<BotRoster>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
        }

        [TearDown]
        public void ResetTime()
        {
            Time.timeScale = 1f;
            GameSettings.UseForTests(null);
        }

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
        public void Menu_FreezesTheWorld_WithNoBots_AndNoControls()
        {
            Assert.AreEqual(MatchPhase.Menu, _match.Phase);
            Assert.AreEqual(0f, Time.timeScale);
            Assert.AreEqual(0, _roster.Bots.Count, "bots join when the match starts");
            Assert.IsFalse(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.IsNull(Object.FindAnyObjectByType<FlightHUD>(), "flight HUD hidden");
            Assert.AreEqual(7, _roster.PlannedBots(0).Count, "class select shows 7 bots beside the player");
            Assert.AreEqual(8, _roster.PlannedBots(1).Count);
        }

        [Test]
        public void Menu_ParksThePlayer_InsideTheHomeHangar()
        {
            var spot = _match.HangarSpot;
            Assert.IsNotNull(spot, "the home airfield has a hangar spot");
            Assert.AreEqual(Airfield.HomeOf(0).HangarSpot, spot);
            Assert.IsTrue(_player.IsGrounded, "parked on its wheels");
            Assert.Less(Vector3.Distance(Flat(_player.Position), Flat(spot.position)), 0.5f);
            Assert.Greater(Vector3.Dot(_player.transform.forward, spot.forward), 0.99f, "nose to the doors");
            Assert.AreEqual(_match.Classes[_match.SelectedClass], _player.Stats);
            // under a roof
            Assert.IsTrue(Physics.Raycast(_player.Position + Vector3.up * 3f, Vector3.up, 30f, ~0, QueryTriggerInteraction.Ignore), "a roof overhead");
        }

        static Vector3 Flat(Vector3 v) => new(v.x, 0f, v.z);

        [Test]
        public void ClassSelect_ParksTheHighlightedClassInTheHangar_UntilBack()
        {
            int picked = _match.SelectedClass, other = (picked + 2) % _match.Classes.Count;
            _match.PreviewClass(other);
            Assert.AreEqual(_match.Classes[picked], _player.Stats, "no preview from the main menu");

            _match.OpenClassSelect();
            _match.PreviewClass(other);
            Assert.AreEqual(_match.Classes[other], _player.Stats);
            Assert.AreEqual(picked, _match.SelectedClass, "previewing picks nothing");
            Assert.AreEqual(MatchPhase.ClassSelect, _match.Phase);
            Assert.IsTrue(_player.IsGrounded, "still parked");
            Assert.Less(Vector3.Distance(Flat(_player.Position), Flat(_match.HangarSpot.position)), 0.5f);

            _match.BackToMenu();
            Assert.AreEqual(_match.Classes[picked], _player.Stats, "the menu shows the class picked last");
        }

        [UnityTest]
        public IEnumerator ClassSelect_BacksOutToTheMenu_AndThePlaneStaysParked()
        {
            _match.OpenClassSelect();
            Assert.AreEqual(MatchPhase.ClassSelect, _match.Phase);
            _match.BackToMenu();
            Assert.AreEqual(MatchPhase.Menu, _match.Phase);
            yield return null;
            Assert.IsTrue(_player.IsGrounded);
            Assert.AreEqual(0f, Time.timeScale);
        }

        [UnityTest]
        public IEnumerator InMatchMenu_LetsGoOfTheControls_ButTheMatchRunsOn()
        {
            yield return StartPlaying();
            var input = _player.GetComponent<PlayerFlightInput>();
            var touch = GameObject.Find("Touch Controls");
            Assert.IsNotNull(touch);

            _match.OpenMenu();
            Assert.IsTrue(_match.MenuOpen);
            Assert.AreEqual(1f, Time.timeScale, "never pauses");
            Assert.IsFalse(input.enabled, "controls let go");
            Assert.IsFalse(touch.activeSelf, "taps go to the menu");
            Assert.IsNotNull(Object.FindAnyObjectByType<FlightHUD>(), "the HUD stays up behind it");
            float left = _match.TimeLeft;
            yield return new WaitForSeconds(0.3f);
            Assert.Less(_match.TimeLeft, left, "the clock runs on");
            Assert.IsTrue(_player.IsAlive);

            _match.CloseMenu();
            Assert.IsFalse(_match.MenuOpen);
            Assert.IsTrue(input.enabled);
            Assert.IsTrue(touch.activeSelf);
        }

        [UnityTest]
        public IEnumerator InMatchMenu_ClosesWhenTimeRunsOut()
        {
            yield return StartPlaying(0, 0.5f);
            _match.OpenMenu();
            float t = Time.realtimeSinceStartup;
            while (_match.Phase == MatchPhase.Playing && Time.realtimeSinceStartup - t < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Ended, _match.Phase);
            Assert.IsFalse(_match.MenuOpen, "the scoreboard takes over");
            _match.OpenMenu();
            Assert.IsFalse(_match.MenuOpen, "no in-match menu on the scoreboard");
        }

        [UnityTest]
        public IEnumerator PlayAgain_ReloadsStraightToClassSelect_AndMainMenu_ToTheMenu()
        {
            yield return StartPlaying(1, 0.2f);
            float t = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Ended && Time.realtimeSinceStartup - t < 3f)
                yield return null;

            _match.PlayAgain();
            yield return null;
            yield return null;
            var again = Object.FindAnyObjectByType<MatchManager>();
            Assert.AreNotEqual(_match, again, "the level reloaded");
            Assert.AreEqual(MatchPhase.ClassSelect, again.Phase, "a rematch skips the menu");
            Assert.AreEqual(1, again.SelectedClass, "keeps the class");

            again.ReturnToMainMenu();
            yield return null;
            yield return null;
            var menu = Object.FindAnyObjectByType<MatchManager>();
            Assert.AreEqual(MatchPhase.Menu, menu.Phase);
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
            Assert.IsNotNull(Object.FindAnyObjectByType<FlightHUD>(), "flight HUD back");
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
            Assert.IsFalse(_match.AwardObjective(0, 100, "Convoy destroyed"), "not in the menu");
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
        public IEnumerator Crash_WithTheMenuRequestedInTheSameFrame_ShowsOnlyThePlaneSelect()
        {
            yield return StartPlaying(0);
            _player.Crash(_player.Position);
            _match.OpenMenu(); // before the manager's Update has opened the picker
            Assert.IsFalse(_match.MenuOpen, "no menu while going down");
            yield return null;
            Assert.AreEqual(ClassPicker.Respawn, _match.Picker);
            Assert.IsFalse(_match.MenuOpen);
        }

        [UnityTest]
        public IEnumerator Dying_OpensTheRespawnPicker_AndTheRespawnUsesThePick()
        {
            yield return StartPlaying(0);
            var input = _player.GetComponent<PlayerFlightInput>();
            Assert.AreEqual(ClassPicker.None, _match.Picker);
            Assert.IsFalse(_match.CanChangeClassAtAirfield, "in the air");

            _player.Crash(_player.Position);
            yield return null;
            Assert.AreEqual(ClassPicker.Respawn, _match.Picker);
            Assert.IsFalse(input.enabled, "controls off (mouse free) while picking");
            Assert.IsNull(Object.FindAnyObjectByType<FlightHUD>(), "flight HUD hidden under the picker");
            Assert.AreEqual(1f, Time.timeScale, "the match keeps running");
            Assert.Greater(_match.RespawnTimeLeft, 0f);

            _match.ChooseRespawnClass(3);
            Assert.AreEqual(_match.Classes[0], _player.Stats, "no change until the respawn");
            _player.GetComponent<PlaneRespawner>().SpawnNow();
            yield return null;
            Assert.AreEqual(_match.Classes[3], _player.Stats);
            Assert.AreEqual(ClassPicker.None, _match.Picker);
            Assert.IsTrue(input.enabled);
            Assert.IsNotNull(Object.FindAnyObjectByType<FlightHUD>());
        }

        [UnityTest]
        public IEnumerator ParkedAndRepairedAtHome_CanSwapClassOnTheSpot()
        {
            yield return StartPlaying(0);
            Assert.IsFalse(_match.ChangeClassAtAirfield(2), "not in the air");

            var home = World.Airfield.All.First(f => f.IsHome && f.Team == _player.Team);
            _player.SpawnOnGround(home.RepairZone.transform.position, home.transform.rotation);
            var repair = _player.GetComponent<PlaneRepair>();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(repair.IsRepairing, "parked at home: repairing (and refilling) first");
            Assert.IsFalse(_match.CanChangeClassAtAirfield, "not while the repair runs");

            float t = Time.time;
            while (repair.IsRepairing && Time.time - t < 8f)
                yield return null;
            Assert.IsTrue(_match.CanChangeClassAtAirfield, "repaired and stopped at home");
            Assert.AreEqual(home, _match.SwapAirfield);

            _match.OpenAirfieldPicker();
            Assert.AreEqual(ClassPicker.Airfield, _match.Picker);
            Assert.IsFalse(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.IsTrue(_match.ChangeClassAtAirfield(2));
            Assert.AreEqual(_match.Classes[2], _player.Stats);
            Assert.AreEqual(ClassPicker.None, _match.Picker);
            Assert.IsTrue(_player.GetComponent<PlayerFlightInput>().enabled);
            Assert.IsTrue(_player.IsGrounded, "still parked");
            Assert.IsTrue(_player.GetComponent<PlaneHealth>().IsFullHealth, "a fresh plane of the new class");
        }

        [UnityTest]
        public IEnumerator TheFreeClassSwitcher_IsOffInAMatch()
        {
            yield return StartPlaying(0);
            var switcher = _player.GetComponent<Dev.ClassSwitcher>();
            Assert.IsFalse(switcher.enabled);
            switcher.SelectClass(3); // what the touch CLASS button calls
            Assert.AreEqual(_match.Classes[0], _player.Stats);
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
            Assert.IsTrue(_player.GetComponent<Dev.ClassSwitcher>().enabled, "free class switching in the sandbox");
        }

        /// <summary>A made-up mode for the framework hooks: one life, a first-to-N win, its own spawn point.</summary>
        sealed class TestMode : MatchMode
        {
            public int WinAt = int.MaxValue;
            public int Lives;
            public Pose? Spawn;
            public string Arena;
            public readonly System.Collections.Generic.List<PlaneController> Spawned = new();

            public override void RecordKill(MatchScore score, in KillRecord kill) =>
                score.RecordDeath(kill.Victim.Pilot, kill.Victim.Name, kill.Victim.Team, kill.Killer.Pilot, kill.Killer.Name, kill.KillerTeam);

            public override int? EarlyWinner(MatchScore score) =>
                score.TeamScore(0) >= WinAt ? 0 : score.TeamScore(1) >= WinAt ? 1 : null;

            public override bool CanRespawn(PilotScore pilot) => Lives <= 0 || pilot == null || pilot.Deaths < Lives;

            public override bool TryGetSpawnPose(PlaneController plane, Vector3 deathPosition, out Pose pose, out bool onRunway)
            {
                onRunway = false;
                pose = Spawn ?? default;
                return Spawn != null && plane.Team == 0;
            }

            public override void OnSpawned(PlaneController plane) => Spawned.Add(plane);

            public override string ArenaId => Arena;

            public override System.Collections.Generic.IReadOnlyList<ScoreColumn> Columns { get; } =
                new[] { new ScoreColumn("K", p => p.Kills.ToString()) };
        }

        [Test]
        public void TheMatch_PlaysTeams_FromTheFirstCard()
        {
            Assert.IsInstanceOf<TeamsMode>(_match.Mode);
            Assert.AreEqual(0, _match.ModeIndex);
            Assert.AreEqual(GameModeInfo.All[0].Length, _match.MatchLength);
            Assert.AreSame(_match, PlaneRespawner.Rules, "the match sets the respawn rules");
        }

        [Test]
        public void EachMode_KeepsItsOwnBotDifficulty_AndOnlyThePlayedOneDrivesTheBots()
        {
            var teams = GameModeInfo.All[0];
            var other = GameModeInfo.All[1];
            var keep = (MatchManager.DifficultyFor(teams), MatchManager.DifficultyFor(other));
            try
            {
                _match.SetDifficulty(teams, BotDifficulty.Easy);
                _match.SetDifficulty(other, BotDifficulty.Hard);
                Assert.AreEqual(BotDifficulty.Easy, _match.Difficulty, "Teams is being played");
                Assert.AreEqual(BotDifficulty.Easy, _roster.Difficulty);
                Assert.AreEqual(BotDifficulty.Hard, MatchManager.DifficultyFor(other));
                Assert.AreEqual((int)BotDifficulty.Hard, PlayerPrefs.GetInt(other.DifficultyPref, -1), "remembered");

                _match.Difficulty = BotDifficulty.Normal;
                Assert.AreEqual(BotDifficulty.Normal, MatchManager.DifficultyFor(teams));
                Assert.AreEqual(BotDifficulty.Hard, MatchManager.DifficultyFor(other), "the other card is untouched");
            }
            finally
            {
                _match.SetDifficulty(teams, keep.Item1);
                _match.SetDifficulty(other, keep.Item2);
            }
        }

        [Test]
        public void AModeInDevelopment_CantBeSelected()
        {
            int wip = -1;
            for (int i = 0; i < GameModeInfo.All.Count; i++)
                if (!GameModeInfo.All[i].IsPlayable)
                    wip = i;
            if (wip < 0)
                Assert.Ignore("every mode is playable");
            _match.SelectMode(wip);
            Assert.AreEqual(0, _match.ModeIndex);
            Assert.IsInstanceOf<TeamsMode>(_match.Mode);
        }

        [UnityTest]
        public IEnumerator AModesSpawnPose_AndLoadoutHook_ApplyToEverySpawn()
        {
            var mode = new TestMode { Spawn = new Pose(new Vector3(0f, 600f, 0f), Quaternion.identity) };
            _match.SetMode(mode);
            Assert.AreSame(mode, _match.Mode);
            yield return StartPlaying();
            Assert.Less(Vector3.Distance(_player.Position, mode.Spawn.Value.position), 30f, "spawned where the mode said");
            Assert.Contains(_player, mode.Spawned);
            Assert.That(mode.Spawned.Count, Is.GreaterThan(8), "the bots' spawns too");
        }

        [UnityTest]
        public IEnumerator AModesEarlyWin_EndsTheMatch()
        {
            _match.SetMode(new TestMode { WinAt = 50 });
            yield return StartPlaying();
            var west = PlaneController.All.First(p => p.Team == 0 && p != _player);
            var east = PlaneController.All.First(p => p.Team == 1);
            east.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 10000f, east.Position, west);
            yield return null;
            yield return null;
            Assert.AreEqual(MatchPhase.Ended, _match.Phase);
            Assert.AreEqual(0, _match.Winner);
            Assert.AreEqual(0f, _match.TimeLeft, "ended before time ran out");
        }

        [UnityTest]
        public IEnumerator AModeWithOneLife_KeepsTheDeadOut()
        {
            _match.SetMode(new TestMode { Lives = 1 });
            yield return StartPlaying();
            var respawner = _player.GetComponent<PlaneRespawner>();
            _player.Crash(_player.Position);
            Time.timeScale = 10f; // run the respawn delay out quickly
            float t = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - t < 3f && respawner.RespawnTimeLeft > 0f)
                yield return null;
            yield return null;
            Time.timeScale = 1f;
            Assert.AreEqual(0f, respawner.RespawnTimeLeft, "the timer ran out");
            Assert.IsFalse(_player.IsAlive, "no lives left: stays down");
            Assert.AreEqual(1, _match.Score.Get(_player).Deaths);
        }

        // ---------- Arenas ----------

        static Vector3 Flat(Vector3 v, Vector3 center) => new(v.x - center.x, 0f, v.z - center.z);

        [UnityTest]
        public IEnumerator Teams_PlaysTheIslandAsBuilt()
        {
            float radius = FlightBoundary.Active.radius;
            yield return StartPlaying();
            Assert.IsNull(_match.Arena, "Teams has no arena of its own");
            Assert.IsFalse(FlightBoundary.Active.IsOverridden);
            Assert.AreEqual(radius, FlightBoundary.Active.radius);
            Assert.IsTrue(Airfield.All.All(f => f.IsOpen));
        }

        [Test]
        public void ModePieces_FollowTheMode_AndFreeFlightGetsThemAll()
        {
            Assert.IsNotNull(_match.Pieces, "the Island spawns its mode pieces from its layout");
            Assert.AreEqual(5, Airfield.All.Count, "Teams: every field");
            Assert.AreEqual(12, ApproachLane.All.Count);
            Assert.AreEqual(0, BannerStand.All.Count, "no banner stands in Teams");
            Assert.AreEqual(3, FieldDefenses.All.Count, "the capture fields' tower and light AA (dormant)");
            Assert.IsNull(ModeArena.For("RamRoyale"));
            var westHome = Airfield.HomeOf(0);

            _match.SetMode(new BannerMode());
            Assert.AreEqual(2, BannerStand.All.Count, "a stand at each home field");
            Assert.AreEqual(5, Airfield.All.Count);

            _match.SetMode(new RamRoyaleMode());
            Assert.AreEqual(2, Airfield.All.Count, "only the home fields (their hangars are the menu's backdrop)");
            Assert.IsTrue(Airfield.All.All(f => f.IsHome));
            Assert.AreEqual(6, ApproachLane.All.Count, "the home fields keep their lanes");
            Assert.AreEqual(0, BannerStand.All.Count);
            Assert.AreEqual(0, FieldDefenses.All.Count);
            Assert.IsNotNull(ModeArena.For("RamRoyale"));
            Assert.AreSame(westHome, Airfield.HomeOf(0), "a field both modes use stays put");
            Assert.IsNotNull(_match.HangarSpot);

            _match.Dismiss();
            Assert.AreEqual(5, Airfield.All.Count, "free flight: the map as built");
            Assert.AreEqual(12, ApproachLane.All.Count);
            Assert.AreEqual(2, BannerStand.All.Count);
            Assert.AreEqual(3, FieldDefenses.All.Count);
            Assert.IsNotNull(ModeArena.For("RamRoyale"));
        }

        [UnityTest]
        public IEnumerator Bots_PatrolTheLayoutsFightAreas_NotOnesBakedForTheIsland()
        {
            var pieces = _match.Pieces;
            var islandXZ = pieces.Map.layout.fightAreas.Select(a => a.point.xz).ToList();
            CollectionAssert.AreEqual(islandXZ, _roster.FightAreas.Select(FlatXZ).ToList(), "Teams patrols the Island's areas");
            foreach (var (area, anchor) in _roster.FightAreas.Zip(pieces.Map.layout.fightAreas, (a, b) => (a, b)))
                Assert.AreEqual(ModePieceBuilder.HighestPointNear(anchor.point.xz, 150f) + anchor.point.agl, area.y, 0.5f,
                    $"{anchor.name} flies its height above the ground and pieces");

            // Another map: the same pieces builder, a layout with one field per team and its own patrols.
            var island = pieces.Map;
            var other = Object.Instantiate(island);
            other.layout = new MapLayout();
            other.layout.airfields.Add(island.layout.Airfield("West Home Airfield"));
            other.layout.airfields.Add(island.layout.Airfield("East Home Airfield"));
            other.layout.fightAreas.Add(new FightAreaAnchor { name = "North", point = new LayoutPoint(new Vector2(0f, 1500f), 300f) });
            other.layout.fightAreas.Add(new FightAreaAnchor { name = "South", point = new LayoutPoint(new Vector2(0f, -1500f), 300f) });
            other.layout.modes.Add(new ModeLayout { modeId = "Teams", airfields = new[] { "West Home Airfield", "East Home Airfield" } });
            try
            {
                pieces.Configure(other, pieces.Kit);
                _match.SetMode(new TeamsMode());
                Assert.AreEqual(2, Airfield.All.Count, "only the other map's fields");
                CollectionAssert.AreEqual(new[] { new Vector2(0f, 1500f), new Vector2(0f, -1500f) }, _roster.FightAreas.Select(FlatXZ).ToArray(),
                    "bots patrol the other map's areas");
                yield return StartPlaying();
                foreach (var bot in _roster.Bots)
                    foreach (var point in bot.PatrolRoute)
                        Assert.Contains(point, _roster.FightAreas.ToList(), $"{bot.name} patrols the other map");
            }
            finally
            {
                pieces.Configure(island, pieces.Kit);
                Object.Destroy(other);
            }
        }

        static Vector2 FlatXZ(Vector3 p) => new(p.x, p.z);

        [UnityTest]
        public IEnumerator DominationAndBanner_PlayAnotherMapsLayout_NotTheIslands()
        {
            var pieces = _match.Pieces;
            var island = pieces.Map;
            _match.SetMode(new BannerMode());
            var islandStands = new[] { BannerStand.Of(0).Position, BannerStand.Of(1).Position };

            // Another map: one capture field (Central) and banner stands 150 m further out than the Island's.
            var other = Object.Instantiate(island);
            other.layout = new MapLayout();
            string[] fields = { "West Home Airfield", "East Home Airfield", "Central Airfield" };
            foreach (var n in fields)
                other.layout.airfields.Add(island.layout.Airfield(n));
            foreach (var s in island.layout.bannerStands)
                other.layout.bannerStands.Add(new BannerStandAnchor { airfield = s.airfield, distance = s.distance + 150f });
            other.layout.fightAreas.AddRange(island.layout.fightAreas);
            other.layout.modes.Add(new ModeLayout { modeId = BannerMode.Id, airfields = new[] { fields[0], fields[1] }, bannerStands = true });
            other.layout.modes.Add(new ModeLayout { modeId = DominationMode.Id, airfields = fields });
            try
            {
                pieces.Configure(other, pieces.Kit);
                _match.SetMode(new BannerMode());
                Assert.AreEqual(2, Airfield.All.Count, "Banner on the other map: its two home fields");
                for (int t = 0; t < 2; t++)
                    Assert.AreEqual(150f, Vector3.Distance(islandStands[t], BannerStand.Of(t).Position), 1f, $"team {t}'s stand is where the other map puts it");

                _match.SetMode(new DominationMode());
                yield return StartPlaying();
                var world = DominationWorld.Active;
                Assert.IsNotNull(world);
                var central = Airfield.All.Single(f => f.DisplayName == "Central Airfield");
                CollectionAssert.AreEqual(new[] { central }, world.Fields, "the other map's one capture field, not the Island's three");
                Assert.AreEqual(1, world.Board.FieldCount);
                Assert.IsFalse(Airfield.All.Any(f => f.DisplayName.StartsWith("South")), "no Island-only fields");

                yield return new WaitForSeconds(3f); // bots pick their fields every 2 s
                var picked = _roster.Bots.Select(b => b.DominationField).Where(f => f != null).ToList();
                Assert.IsNotEmpty(picked, "bots go for a field");
                Assert.IsTrue(picked.All(f => f == central), "every bot goes for the one field the map has");
            }
            finally
            {
                pieces.Configure(island, pieces.Kit);
                Object.Destroy(other);
            }
        }

        [UnityTest]
        public IEnumerator RamRoyalesArena_ShrinksTheMap_ClosesTheFields_AndStartsEveryoneOnARing()
        {
            Assert.IsNull(ModeArena.For("RamRoyale"), "Teams spawns no arena");
            var islandAreas = _roster.FightAreas.ToList();
            _match.SetMode(new TestMode { Arena = "RamRoyale" });
            var arena = ModeArena.For("RamRoyale");
            Assert.IsNotNull(arena, "the Island's layout has a Ram Royale arena, spawned with the mode");
            Assert.IsFalse(arena.IsApplied, "not before the match");
            yield return StartPlaying();

            Assert.AreSame(arena, _match.Arena);
            var boundary = FlightBoundary.Active;
            Vector3 c = arena.transform.position;
            Assert.AreEqual(Flat(c, Vector3.zero), Flat(boundary.Center, Vector3.zero), "the play area is the arena's");
            Assert.AreEqual(arena.Radius, boundary.radius);
            Assert.AreEqual(arena.Ceiling, boundary.ceiling);
            Assert.IsTrue(boundary.hardEdge);
            Assert.IsTrue(Airfield.All.All(f => !f.IsOpen), "every field closed");
            Assert.IsNull(Airfield.NearestFriendly(_player.Position, 0), "nowhere to repair");
            Assert.IsTrue(AAGun.All.All(g => !g.IsArmed), "no AA");
            CollectionAssert.AreEqual(arena.FightAreas, _roster.FightAreas, "bots patrol the arena");

            var planes = PlaneController.All.Where(p => p.IsAlive).ToList();
            Assert.That(planes.Count, Is.EqualTo(16), "a full field");
            foreach (var p in planes)
            {
                Vector3 flat = Flat(p.Position, c);
                Assert.AreEqual(arena.StartRingRadius, flat.magnitude, 60f, $"{p.name} starts on the ring");
                Assert.AreEqual(arena.StartAltitude, p.Position.y, 30f, $"{p.name} at the start height");
                Assert.Greater(Vector3.Dot(p.Rotation * Vector3.forward, -flat.normalized), 0.8f, $"{p.name} faces in");
            }
            float closest = float.MaxValue;
            for (int i = 0; i < planes.Count; i++)
                for (int j = i + 1; j < planes.Count; j++)
                    closest = Mathf.Min(closest, Vector3.Distance(planes[i].Position, planes[j].Position));
            Assert.Greater(closest, 150f, "spread round the ring, nobody on top of anybody");

            var buoys = Object.FindAnyObjectByType<BoundaryBuoys>();
            Assert.Greater(buoys.ArenaCount, 20, "buoys mark the arena's edge");
            foreach (Transform marker in buoys.ArenaRing)
                IslandSceneTests.AssertStandsOnSurface(marker);

            _match.Dismiss();
            Assert.IsFalse(arena.IsApplied);
            Assert.IsFalse(boundary.IsOverridden, "free flight is the Island as built");
            Assert.IsFalse(boundary.hardEdge);
            Assert.IsTrue(Airfield.All.All(f => f.IsOpen));
            Assert.AreEqual(0, buoys.ArenaCount);
            CollectionAssert.AreEqual(islandAreas, _roster.FightAreas, "the Island's patrols are back");
        }

        [UnityTest]
        public IEnumerator AHardEdge_PutsOutAPlaneThatStaysPastIt()
        {
            _match.SetMode(new TestMode { Arena = "RamRoyale" });
            yield return StartPlaying();
            var boundary = FlightBoundary.Active;
            Vector3 outward = Vector3.forward;
            Vector3 start = boundary.Center + outward * (boundary.radius - 30f) + Vector3.up * 220f;
            _player.Respawn(start, Quaternion.LookRotation(outward));
            float t = Time.time;
            bool sawOutside = false;
            while (_player.IsAlive && Time.time - t < 8f)
            {
                sawOutside |= _player.IsOutOfBounds;
                Assert.Greater(Vector3.Dot(_player.transform.forward, outward), 0.7f, "no turn-back at a hard edge");
                yield return null;
            }
            Assert.IsTrue(sawOutside);
            Assert.IsFalse(_player.IsAlive, "out after the grace");
            Assert.AreEqual(DeathCause.OutOfBounds, _player.LastDeathCause);
            Assert.That(Time.time - t, Is.GreaterThan(boundary.outOfBoundsGrace), "not before the grace ran out");
        }

        [UnityTest]
        public IEnumerator OverTheCeiling_NearAHardEdge_TheTakeoverTurnsBackIn()
        {
            // Hands off, over the ceiling, close to the edge and heading out: the ceiling takeover used to hold the
            // heading, flying the plane out of the arena while the pilot couldn't steer.
            _match.SetMode(new TestMode { Arena = "RamRoyale" });
            yield return StartPlaying();
            var boundary = FlightBoundary.Active;
            Vector3 outward = Vector3.forward;
            Vector3 start = boundary.Center + outward * (boundary.radius - 120f);
            start.y = boundary.ceiling + 40f;
            _player.Respawn(start, Quaternion.LookRotation(outward));
            // Until the takeover hands the stick back (back under the ceiling): below it, the edge is the pilot's job.
            float t = Time.time, outside = 0f;
            while (_player.IsAlive && _player.Position.y > boundary.ceiling && Time.time - t < 8f)
            {
                outside = Mathf.Max(outside, _player.OutOfBoundsTime);
                yield return null;
            }
            Assert.IsTrue(_player.IsAlive, $"flown out of the arena ({_player.LastDeathCause}, {outside:0.0} s outside)");
            Assert.LessOrEqual(_player.Position.y, boundary.ceiling, "brought back under the ceiling");
            Assert.Less(outside, boundary.outOfBoundsGrace, "never outside long enough to be out");
            Assert.Less(Vector3.Dot(_player.Rotation * Vector3.forward, outward), 0f, "handed back heading in");
        }

        [UnityTest]
        public IEnumerator Bots_StayInsideAHardEdge()
        {
            _match.SetMode(new TestMode { Arena = "RamRoyale" });
            yield return StartPlaying();
            int outs = 0;
            string why = "";
            void OnKilled(KillInfo kill)
            {
                if (kill.Cause != DeathCause.OutOfBounds || !kill.Victim.TryGetComponent<BotBrain>(out var brain))
                    return; // the player's hands are off the stick here: it flies straight out
                outs++;
                why += $"{kill.Victim.name} ({brain.State}, lost parts {kill.Victim.LostPartsMask}, body {kill.Victim.BodyState}); ";
            }
            PlaneHealth.AnyKilled += OnKilled;
            try
            {
                Time.timeScale = 3f;
                float t = Time.time;
                while (Time.time - t < 30f)
                    yield return null;
            }
            finally
            {
                Time.timeScale = 1f;
                PlaneHealth.AnyKilled -= OnKilled;
            }
            Assert.AreEqual(0, outs, "no bot flew out of the arena in 30 s: " + why);
        }
    }
}

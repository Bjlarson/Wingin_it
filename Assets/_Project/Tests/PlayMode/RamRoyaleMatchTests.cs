using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.CameraSystem;
using WingingIt.Flight;
using WingingIt.Input;
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
        public IEnumerator WeaponsAreOff_AndSupportFliesARamBoost()
        {
            var planes = PlaneController.All.ToList();
            foreach (var plane in planes)
            {
                Assert.IsTrue(plane.WeaponsOff, $"{plane.name} has weapons off");
                Assert.IsTrue(plane.RamDamage, $"{plane.name} has ram damage on");
                Assert.IsFalse(plane.GetComponent<PlaneGuns>().IsEquipped, $"{plane.name}: no guns");
                Assert.IsFalse(plane.GetComponent<PlaneRockets>().IsEquipped, $"{plane.name}: no rockets");
                Assert.IsFalse(plane.GetComponent<PlaneMissiles>().IsEquipped, $"{plane.name}: no missiles");
                Assert.IsFalse(plane.GetComponent<PlaneBombs>().IsEquipped, $"{plane.name}: no bombs");
            }
            var supports = planes.Where(p => p.Stats.ability is RepairAuraAbility).ToList();
            Assert.IsNotEmpty(supports, "the roster has Support bots");
            foreach (var plane in supports)
                Assert.IsInstanceOf<RamBoostAbility>(plane.GetComponent<PlaneAbility>().Stats, $"{plane.name}: Ram Boost in place of the aura");
            foreach (var plane in planes.Where(p => p.Stats.ability is not RepairAuraAbility))
                Assert.AreSame(plane.Stats.ability, plane.GetComponent<PlaneAbility>().Stats, $"{plane.name} keeps its class ability");

            // Holding every trigger fires nothing.
            var guns = _player.GetComponent<PlaneGuns>();
            var rockets = _player.GetComponent<PlaneRockets>();
            var missiles = _player.GetComponent<PlaneMissiles>();
            var bombs = _player.GetComponent<PlaneBombs>();
            int rocketAmmo = rockets.Ammo, missileAmmo = missiles.Ammo, bombAmmo = bombs.Ammo;
            _player.GetComponent<PlayerFlightInput>().enabled = false; // so the test's input stands
            for (int i = 0; i < 30; i++)
            {
                _player.SetInput(new FlightInput { Throttle = 1f, FireGuns = true, FireRockets = true, FireMissile = i % 2 == 0, DropBomb = true });
                yield return new WaitForFixedUpdate();
                Assert.IsFalse(guns.IsFiring, "the guns don't fire");
            }
            Assert.AreEqual(rocketAmmo, rockets.Ammo, "no rockets fired");
            Assert.AreEqual(missileAmmo, missiles.Ammo, "no missiles fired");
            Assert.AreEqual(bombAmmo, bombs.Ammo, "no bombs dropped");
            Assert.IsFalse(missiles.IsLocked, "no lock-on");
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
        public IEnumerator LastPlaneFlying_EndsTheMatch_AndWins()
        {
            var bots = PlaneController.All.Where(p => p != _player).ToList();
            foreach (var bot in bots.Skip(1))
                bot.Crash(bot.Position);
            yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase, "two still flying");

            bots[0].Crash(bots[0].Position);
            yield return null;
            Assert.AreEqual(MatchPhase.Ended, _match.Phase, "the last plane flying ends it");
            Assert.AreEqual(_player.Team, _match.Winner);
            Assert.AreEqual(2, _match.Score.Get(bots[0]).Place);
        }

        [UnityTest]
        public IEnumerator AtTime_MostBodyHpWins()
        {
            // Every bot down to a tenth of its body; the player untouched.
            foreach (var plane in PlaneController.All.Where(p => p != _player))
            {
                var h = plane.GetComponent<PlaneHealth>();
                h.ApplyDamage(PlanePartType.Body, h.GetPart(PlanePartType.Body).MaxHealth * 0.9f, plane.Position);
            }
            yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);
            _match.EndMatch();
            Assert.AreEqual(_player.Team, _match.Winner, "the most body HP left wins at time");
        }

        [UnityTest]
        public IEnumerator Hud_ShowsPlanesLeft_MarksTheNearestPlane_AndFlashesARam()
        {
            Assert.AreNotEqual(MatchWidgets.None, _match.Mode.Widgets & MatchWidgets.PlanesLeft, "planes left on the score bar");
            Assert.AreEqual(MatchWidgets.None, _match.Mode.Widgets & MatchWidgets.TeamScores);
            var hud = Object.FindAnyObjectByType<WingingIt.UI.FlightHUD>();
            Assert.IsNotNull(hud);

            // A bot flown straight by the test, idling ahead of the player in the middle of the arena.
            var bot = PlaneController.All.First(p => p != _player);
            bot.GetComponent<WingingIt.Bots.BotBrain>().enabled = false;
            bot.GetComponent<WingingIt.Bots.BotPilot>().enabled = false;
            _player.GetComponent<PlayerFlightInput>().enabled = false;
            Vector3 middle = _match.Arena.transform.position;
            middle.y = 250f;
            _player.Respawn(middle + new Vector3(0f, 0f, -60f), Quaternion.identity, 1f);
            bot.Respawn(middle, Quaternion.identity, 0f);
            _player.RamDamage = bot.RamDamage = true; // respawning clears it
            _player.WeaponsOff = bot.WeaponsOff = true;
            yield return null;
            Assert.AreEqual(bot, hud.NearestPlane, "the marker is on the nearest plane");

            float t = Time.time;
            while (hud.RamFlashText == null && Time.time - t < 8f)
            {
                _player.SetInput(new FlightInput { Throttle = 1f, Boost = true });
                bot.SetInput(default);
                yield return new WaitForFixedUpdate();
            }
            Assert.AreEqual($"RAMMED {_match.PilotName(bot).ToUpperInvariant()}!", hud.RamFlashText, "ramming from behind flashes our hit");
        }

        [UnityTest]
        public IEnumerator SixteenBots_RamEachOther_AndKnockPlanesOut()
        {
            // The player flies as a bot too, so it isn't a sitting duck flying straight out of the arena.
            _player.GetComponent<PlayerFlightInput>().enabled = false;
            _player.gameObject.AddComponent<WingingIt.Bots.BotPilot>();
            _player.gameObject.AddComponent<WingingIt.Bots.BotBrain>();

            int rams = 0, crashes = 0, outOfArena = 0;
            void OnRammed(RamInfo info)
            {
                if (info.HitterDamage > 0f || info.VictimDamage > 0f)
                    rams++;
            }
            void OnKilled(KillInfo k)
            {
                if (k.Killer == null)
                    crashes++;
                if (k.Cause == DeathCause.OutOfBounds)
                {
                    outOfArena++;
                    var brain = k.Victim.GetComponent<WingingIt.Bots.BotBrain>();
                    Debug.Log($"[RamBots] {k.Victim.name} out of the arena: lost parts {k.Victim.LostPartsMask}, body " +
                              $"{k.Victim.GetPart(PlanePartType.Body).NormalizedHealth:0.00}, state {brain?.State}, " +
                              $"speed {k.Victim.Plane.Speed:0}, y {k.Victim.Plane.Position.y:0}, heading in " +
                              $"{Vector3.Dot(k.Victim.Plane.Velocity.normalized, (_match.Arena.transform.position - k.Victim.Plane.Position).normalized):0.00}, " +
                              $"killer {(k.Killer != null ? k.Killer.name : "none")}");
                }
            }
            PlaneController.AnyRammed += OnRammed;
            PlaneHealth.AnyKilled += OnKilled;
            var states = new System.Collections.Generic.Dictionary<WingingIt.Bots.BotState, float>();

            Time.timeScale = 4f;
            for (float t = 0f; t < 120f && _match.Phase == MatchPhase.Playing; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                foreach (var b in WingingIt.Bots.BotBrain.All)
                {
                    if (!b.Pilot.Plane.IsAlive)
                        continue; // a wreck keeps its last state
                    states.TryGetValue(b.State, out float s);
                    states[b.State] = s + Time.fixedDeltaTime;
                }
            }
            Time.timeScale = 1f;
            PlaneController.AnyRammed -= OnRammed;
            PlaneHealth.AnyKilled -= OnKilled;

            int flying = PlaneController.All.Count(p => p.IsAlive);
            Debug.Log($"[RamBots] {rams} rams, {16 - flying} out ({crashes} uncredited, {outOfArena} out of the arena), " +
                      $"phase {_match.Phase}; bot-seconds: " +
                      string.Join(", ", states.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value:0}")));
            Assert.GreaterOrEqual(rams, 8, "bots go for each other");
            Assert.LessOrEqual(flying, 13, "rams knock planes out");
            Assert.LessOrEqual(crashes, 3, "few planes go down with nobody's help");
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

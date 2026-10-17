using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The Island's <see cref="BotRoster"/>: bots fill both teams to 8v8 around the player.</summary>
    public class BotRosterTests
    {
        PlaneController _player;
        BotRoster _roster;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            Object.FindAnyObjectByType<MatchManager>()?.Dismiss(); // free flight: no lobby, no match clock
            yield return null; // the roster starts filling
            var go = GameObject.Find("Player Plane");
            Assert.IsNotNull(go, "Island scene has no Player Plane");
            _player = go.GetComponent<PlaneController>();
            foreach (var mb in _player.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name is "PlayerFlightInput" or "ClassSwitcher" or "DamageTester")
                    mb.enabled = false;
            _roster = Object.FindAnyObjectByType<BotRoster>();
            Assert.IsNotNull(_roster, "Island scene has no Bot Roster");
            yield return new WaitForFixedUpdate();
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        IEnumerable<PlaneController> Team(int team) => PlaneController.All.Where(p => p.Team == team);

        [Test]
        public void BothTeams_HaveEightPlanes_WithThePlayerInOne()
        {
            Assert.AreEqual(8, Team(0).Count(), "team 0: the player + 7 bots");
            Assert.AreEqual(8, Team(1).Count(), "team 1: 8 bots");
            Assert.AreEqual(15, _roster.Bots.Count);
            Assert.IsNull(_player.GetComponent<BotBrain>());
        }

        [Test]
        public void EachTeam_FliesTheClassMix_CountingThePlayer()
        {
            var classes = Team(1).Select(p => p.Stats).Distinct().ToList();
            Assert.AreEqual(4, classes.Count, "every class flies on team 1");
            foreach (int team in new[] { 0, 1 })
            {
                var byName = Team(team).GroupBy(p => p.Stats.displayName).ToDictionary(g => g.Key, g => g.Count());
                string mix = string.Join(", ", byName.Select(kv => $"{kv.Value} {kv.Key}"));
                Assert.AreEqual(3, byName.GetValueOrDefault("Fighter"), $"team {team}: {mix}");
                Assert.AreEqual(2, byName.GetValueOrDefault("Interceptor"), $"team {team}: {mix}");
                Assert.AreEqual(2, byName.GetValueOrDefault("Heavy Fighter"), $"team {team}: {mix}");
                Assert.AreEqual(1, byName.GetValueOrDefault("Support"), $"team {team}: {mix}");
            }
        }

        [Test]
        public void Bots_StartOverTheirOwnHomeField_ClearOfEachOther()
        {
            var planes = PlaneController.All.ToList();
            foreach (var bot in _roster.Bots)
            {
                var plane = bot.GetComponent<PlaneController>();
                var home = Airfield.All.First(f => f.IsHome && f.Team == plane.Team);
                Assert.Less(Vector3.Distance(plane.Position, home.AirSpawn.position), 200f, $"{plane.name} isn't over its home field");
                foreach (var other in planes)
                    if (other != plane)
                        Assert.Greater(Vector3.Distance(plane.Position, other.Position), 25f, $"{plane.name} spawned on top of {other.name}");
            }
        }

        [Test]
        public void Bots_PatrolTheFightAreas_SpreadAcrossThem()
        {
            Assert.AreEqual(5, _roster.FightAreas.Count);
            foreach (int team in new[] { 0, 1 })
            {
                var visited = new HashSet<Vector3>();
                foreach (var bot in _roster.Bots.Where(b => b.GetComponent<PlaneController>().Team == team))
                {
                    Assert.AreEqual(2, bot.PatrolRoute.Count, $"{bot.name} has no two-point patrol");
                    foreach (var point in bot.PatrolRoute)
                    {
                        Assert.Contains(point, _roster.FightAreas.ToList());
                        visited.Add(point);
                    }
                }
                Assert.AreEqual(5, visited.Count, $"team {team} leaves a fight area empty");
            }
        }

        [Test]
        public void FightAreas_AreClearOfTerrainAndOutOfTheHomeAA()
        {
            foreach (var area in _roster.FightAreas)
            {
                Assert.IsTrue(Physics.Raycast(area + Vector3.up * 1000f, Vector3.down, out var hit, 3000f));
                Assert.Greater(area.y - hit.point.y, 100f, $"fight area {area} is too low");
                foreach (var gun in Object.FindObjectsByType<AAGun>(FindObjectsSortMode.None).Where(g => g.Team != Airfield.Neutral))
                    Assert.Greater(Vector3.Distance(area, gun.transform.position), gun.Stats.range, $"fight area {area} is under {gun.name}");
            }
        }

        [Test]
        public void Difficulty_AppliesToEveryBot()
        {
            _roster.Difficulty = BotDifficulty.Hard;
            foreach (var bot in _roster.Bots)
                Assert.AreEqual(BotDifficulty.Hard, bot.Difficulty);
        }

        [UnityTest]
        public IEnumerator AHumanJoining_TakesASlot_WhenABotDies()
        {
            var bot = _roster.Bots.First(b => b.GetComponent<PlaneController>().Team == 1);
            // A human joins team 1: a team 0 bot handed over to "a player" (its brain taken out; the pilot just holds its last order).
            var turncoat = _roster.Bots.First(b => b.GetComponent<PlaneController>().Team == 0);
            Object.Destroy(turncoat.GetComponent<BotBrain>());
            turncoat.GetComponent<PlaneController>().Team = 1;
            yield return null;
            _roster.Refresh();
            Assert.AreEqual(8, Team(0).Count(), "team 0 got a bot for the slot it lost");
            Assert.AreEqual(9, Team(1).Count(), "bots wait to die before leaving");

            var plane = bot.GetComponent<PlaneController>();
            plane.Crash(plane.Position);
            _roster.Refresh();
            yield return null;
            Assert.IsTrue(bot == null, "the dead bot left");
            Assert.AreEqual(8, Team(1).Count());
        }

        [UnityTest]
        public IEnumerator ABotThatLeaves_IsReplaced()
        {
            var bot = _roster.Bots.First(b => b.GetComponent<PlaneController>().Team == 0);
            Object.Destroy(bot.gameObject);
            yield return null;
            _roster.Refresh();
            Assert.AreEqual(8, Team(0).Count());
            Assert.AreEqual(15, _roster.Bots.Count);
        }

        /// <summary>
        /// The phase's exit check, unattended: two full teams of Normal bots (the player one of them) fly from their home
        /// fields to the fight areas and fight. In three minutes both teams should score kills (as the game credits them:
        /// shot down, or crashed with an enemy's hits on it in the last 20 s), and running into the ground or each other
        /// with nobody to blame should stay rare next to that.
        /// </summary>
        const float Duration = 180f;

        [UnityTest]
        public IEnumerator EightVersusEight_BotsMeetAndFight()
        {
            // The player plays along as a bot, so team 0 isn't a man down.
            _player.gameObject.AddComponent<BotPilot>();
            var playerBot = _player.gameObject.AddComponent<BotBrain>();
            playerBot.SetPatrol(_roster.FightAreas.Take(2));

            var killed = new int[2];
            var hits = new int[2];
            int collisions = 0, creditedCrashes = 0, partsLost = 0, retreats = 0, repairs = 0;
            var collisionLog = new List<string>();
            void OnKilled(KillInfo k)
            {
                var p = k.Victim.Plane;
                Vector3 at = p.Position;
                if (k.Killer != null && !k.Killer.IsAllyOf(p))
                {
                    killed[p.Team]++;
                    if (k.Cause == DeathCause.Collision)
                        creditedCrashes++;
                }
                else
                {
                    collisions++;
                    var b = p.GetComponent<BotBrain>();
                    var ph = p.GetComponent<PlaneHealth>();
                    var field = Airfield.NearestFriendly(at, p.Team);
                    collisionLog.Add($"{p.name} at {at:F0}: {(b != null ? b.State.ToString() : "-")}" +
                                     $"{(b != null && b.IsRetreating ? " (retreating)" : "")}, speed {p.Speed:F0}, " +
                                     $"body {ph.GetPart(PlanePartType.Body).NormalizedHealth:P0}, lost parts {ph.LostPartsMask}, " +
                                     $"{(field != null ? Vector3.Distance(at, field.transform.position).ToString("F0") + " m from home field" : "")}");
                }
            }
            var planes = PlaneController.All.ToList();
            var healths = planes.Select(p => p.GetComponent<PlaneHealth>()).ToList();
            var repairers = planes.Select(p => p.GetComponent<PlaneRepair>()).ToList();
            float damage = 0f, healed = 0f, maxOnOne = 0f;
            var bodyDamage = new Dictionary<PlaneHealth, float>();
            void OnDamaged(PlaneHealth h, PartDamageInfo i)
            {
                hits[h.GetComponent<PlaneController>().Team]++;
                damage += i.Amount;
                if (i.Part.partType == PlanePartType.Body)
                {
                    bodyDamage.TryGetValue(h, out float b);
                    bodyDamage[h] = b + i.Amount;
                    maxOnOne = Mathf.Max(maxOnOne, h.GetPart(PlanePartType.Body).MaxHealth - h.GetPart(PlanePartType.Body).Health);
                }
            }
            void OnHealed(PlaneHealth h, float amount, Object _) => healed += amount;
            void OnPartLost(PlaneHealth h, PartDamageInfo _) => partsLost++;
            void OnRepaired(PlaneRepair _) => repairs++;
            PlaneHealth.AnyKilled += OnKilled;
            foreach (var h in healths)
            {
                h.Damaged += OnDamaged;
                h.PartDestroyed += OnPartLost;
                h.Healed += OnHealed;
            }
            foreach (var r in repairers)
                r.Completed += OnRepaired;
            int bullets = 0, gunHits = 0, missiles = 0, missileHits = 0, missilesEvaded = 0;
            void OnBullet(PlaneGuns _) => bullets++;
            void OnGunHit(PlaneGuns g, PlaneHealth h, PartDamageInfo i) => gunHits++;
            void OnMissile(PlaneMissiles m, PlaneController _) => missiles++;
            void OnMissileHit(PlaneMissiles m, PlaneHealth h, PartDamageInfo i) => missileHits++;
            void OnMissileEvaded(PlaneMissiles m, PlaneController _) => missilesEvaded++;
            var guns = planes.Select(p => p.GetComponent<PlaneGuns>()).Where(g => g != null).ToList();
            var launchers = planes.Select(p => p.GetComponent<PlaneMissiles>()).Where(m => m != null).ToList();
            foreach (var g in guns)
            {
                g.Fired += OnBullet;
                g.HitPlane += OnGunHit;
            }
            foreach (var m in launchers)
            {
                m.Fired += OnMissile;
                m.HitPlane += OnMissileHit;
                m.Evaded += OnMissileEvaded;
            }

            Time.timeScale = 4f;
            float inGunRangeSeconds = 0f;
            var offNose = new float[5]; // seconds attacking with the lead point <3, <10, <30, <90, 90+ deg off the nose
            var diagGuns = new Dictionary<BotBrain, PlaneGuns>();
            float engagedSeconds = 0f;
            var wasRetreating = new HashSet<BotBrain>();
            for (float t = 0f; t < Duration; t += Time.fixedDeltaTime)
            {
                yield return new WaitForFixedUpdate();
                if (BotBrain.All.Any(b => b.State == BotState.Attack))
                    engagedSeconds += Time.fixedDeltaTime;
                foreach (var b in BotBrain.All)
                {
                    if (b.State != BotState.Attack || b.Target == null)
                        continue;
                    if (!diagGuns.TryGetValue(b, out var bg))
                        diagGuns[b] = bg = b.GetComponent<PlaneGuns>();
                    if (bg == null || !bg.TryGetLeadPoint(b.Target, out Vector3 lead, out _))
                        continue;
                    float a = Vector3.Angle(b.transform.forward, lead - b.transform.position);
                    offNose[a < 3f ? 0 : a < 10f ? 1 : a < 30f ? 2 : a < 90f ? 3 : 4] += Time.fixedDeltaTime;
                }
                inGunRangeSeconds += Time.fixedDeltaTime * BotBrain.All.Count(b => b.State == BotState.Attack && b.Target != null &&
                    Vector3.Distance(b.transform.position, b.Target.Position) < 600f);
                foreach (var b in BotBrain.All)
                {
                    if (b.IsRetreating && wasRetreating.Add(b))
                        retreats++;
                    else if (!b.IsRetreating)
                        wasRetreating.Remove(b);
                }
            }
            Time.timeScale = 1f;
            PlaneHealth.AnyKilled -= OnKilled;
            foreach (var h in healths)
                if (h != null)
                {
                    h.Damaged -= OnDamaged;
                    h.PartDestroyed -= OnPartLost;
                    h.Healed -= OnHealed;
                }
            foreach (var r in repairers)
                if (r != null)
                    r.Completed -= OnRepaired;
            foreach (var g in guns)
                if (g != null)
                {
                    g.Fired -= OnBullet;
                    g.HitPlane -= OnGunHit;
                }
            foreach (var m in launchers)
                if (m != null)
                {
                    m.Fired -= OnMissile;
                    m.HitPlane -= OnMissileHit;
                    m.Evaded -= OnMissileEvaded;
                }

            string summary = $"[8v8] {Duration:F0} s: killed team 0 {killed[0]}, team 1 {killed[1]} ({creditedCrashes} of them crashed); hits taken team 0 {hits[0]}, " +
                             $"team 1 {hits[1]}; parts lost {partsLost}; retreats {retreats}, repairs {repairs}; collisions {collisions}; " +
                             $"someone attacking {engagedSeconds:F0} s, bot-seconds attacking inside 600 m {inGunRangeSeconds:F0}; bullets {bullets}, " +
                             $"gun hits {gunHits}; missiles {missiles}, hits {missileHits}, evaded {missilesEvaded}; damage {damage:F0} (body {bodyDamage.Values.Sum():F0}), healed {healed:F0}, worst body damage on one plane {maxOnOne:F0}; lead off nose <3/<10/<30/<90/90+ deg: {string.Join("/", offNose.Select(x => x.ToString("F0")))} s\n" + string.Join("\n", collisionLog);
            Debug.Log(summary);
            Assert.Greater(engagedSeconds, Duration / 4f, "the teams hardly fought\n" + summary);
            Assert.Greater(killed[0], 0, "team 1 scored no kills\n" + summary);
            Assert.Greater(killed[1], 0, "team 0 scored no kills\n" + summary);
            Assert.LessOrEqual(collisions, (killed[0] + killed[1]) / 2 + 2, "more planes flew into things than a fight explains\n" + summary);
        }
    }
}

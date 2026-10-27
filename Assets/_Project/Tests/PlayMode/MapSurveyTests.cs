using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// Not a pass / fail test: plays a whole match of each mode on each map with bots only (the player flies as one)
    /// and writes where the planes spent their time, where they fought and died, and how, to
    /// <c>Logs/MapSurvey-&lt;map&gt;-&lt;mode&gt;.txt</c>. Used for the Phase 6.7 mode-vs-map review; add a map to
    /// <see cref="MapScenes"/> once its layout has every mode. The map loads as in the game (<see cref="MapTestScene"/>).
    /// Explicit, so the normal suite skips it.
    /// </summary>
    [Explicit("Survey: run by hand")]
    public class MapSurveyTests
    {
        const float SampleInterval = 0.5f;
        const float FightRange = 600f;
        const float Cell = 400f;

        MatchManager _match;
        PlaneController _player;

        static IEnumerable<string> Modes => new[] { "Teams", "Banner", "Domination", "RamRoyale" };
        /// <summary>Map scenes to survey: every game map.</summary>
        static IEnumerable<string> MapScenes => new[] { MapTestScene.Island, MapTestScene.BroadReach, MapTestScene.ShatteredArchipelago };

        [TearDown]
        public void ResetTime()
        {
            Time.timeScale = 1f;
            GameSettings.UseForTests(null);
        }

        [UnityTest, Timeout(900000)]
        public IEnumerator SurveyAMatch([ValueSource(nameof(MapScenes))] string mapScene, [ValueSource(nameof(Modes))] string modeName)
        {
            GameSettings.UseForTests(new GameSettings(false, null));
            yield return MapTestScene.Load(mapScene);
            var map = WingingIt.Maps.MapScene.CurrentMap;
            Assert.IsNotNull(map, $"{mapScene} has no MapDefinition");
            s_regions = map.regions;
            float halfMap = map.worldSize * 0.5f;
            _match = Object.FindAnyObjectByType<MatchManager>();
            _player = GameObject.Find("Player Plane").GetComponent<PlaneController>();
            MatchMode mode = modeName switch
            {
                "Banner" => new BannerMode(),
                "Domination" => new DominationMode(),
                "RamRoyale" => new RamRoyaleMode(),
                _ => new TeamsMode(),
            };
            _match.SetMode(mode);
            _match.CountdownLength = 0.2f;
            _match.OpenClassSelect();
            _match.SelectClass(0);
            float wait = Time.realtimeSinceStartup;
            while (_match.Phase != MatchPhase.Playing && Time.realtimeSinceStartup - wait < 3f)
                yield return null;
            Assert.AreEqual(MatchPhase.Playing, _match.Phase);

            _player.GetComponent<PlayerFlightInput>().enabled = false;
            _player.gameObject.AddComponent<BotPilot>();
            var playerBrain = _player.gameObject.AddComponent<BotBrain>();
            playerBrain.RosterSlot = 7;
            playerBrain.BannerRole = BannerPlan.RoleForSlot(7);
            playerBrain.DominationRole = DominationPlan.RoleForSlot(7);
            yield return null;

            var regionTime = new Dictionary<string, float>();
            var regionFight = new Dictionary<string, float>();
            var stateTime = new Dictionary<BotState, float>();
            int n = Mathf.CeilToInt(2f * halfMap / Cell);
            var fightGrid = new float[n, n];
            var flyGrid = new float[n, n];
            var deaths = new List<string>();
            var deathRegions = new Dictionary<string, int>();
            var events = new List<string>();
            var causes = new Dictionary<DeathCause, int>();
            float lonely = 0f, alive = 0f, firstContact = -1f, altSum = 0f, outside = 0f, maxRadius = 0f;
            // Banner runners: the closest each came to the enemy stand, at any height and while low enough to grab.
            var runnerNearest = new Dictionary<string, (float any, float low)>();
            var strays = new Dictionary<string, string>(); // the first time each plane is 500 m past the edge
            var altBands = new float[5]; // < 50, 50–150, 150–400, 400–800, 800+

            void OnKilled(KillInfo k)
            {
                var p = k.Victim.Plane;
                string r = Region(p.Position);
                deathRegions.TryGetValue(r, out int c);
                deathRegions[r] = c + 1;
                causes[k.Cause] = causes.GetValueOrDefault(k.Cause) + 1;
                deaths.Add($"{Time.time:F0}s {p.name} {k.Cause} in {r} at ({p.Position.x:F0}, {p.Position.y:F0}, {p.Position.z:F0}) by {(k.Killer != null ? k.Killer.name : k.Source != null ? k.Source.name : "nobody")}");
            }
            PlaneHealth.AnyKilled += OnKilled;
            void OnBanner(BannerEvent e) => events.Add($"{Time.time:F0}s banner {e.BannerTeam} {e.Kind} by team {e.PilotTeam}");
            void OnCaptured(FieldCapture c) => events.Add($"{Time.time:F0}s field {_match.Fields.Fields[c.Field].DisplayName} -> {c.Team}");
            // Both worlds are made on the first tick of play, so hook them up once they exist.
            BannerWorld banners = null;
            DominationWorld fieldsWorld = null;

            float start = Time.time, nextSample = start;
            Time.timeScale = 4f;
            while (_match.Phase == MatchPhase.Playing && Time.time - start < 400f)
            {
                yield return new WaitForFixedUpdate();
                if (banners == null && _match.Banners != null)
                    (banners = _match.Banners).EventApplied += OnBanner;
                if (fieldsWorld == null && _match.Fields != null)
                    (fieldsWorld = _match.Fields).Captured += OnCaptured;
                foreach (var b in BotBrain.All)
                {
                    if (b.BannerRole != BannerRole.Runner || banners == null)
                        continue;
                    var plane = b.GetComponent<PlaneController>();
                    if (plane == null || !plane.IsAlive)
                        continue;
                    foreach (var s in BannerStand.All)
                    {
                        if (s.Team == plane.Team)
                            continue;
                        float d = Vector2.Distance(new Vector2(plane.Position.x, plane.Position.z), new Vector2(s.Position.x, s.Position.z));
                        var best = runnerNearest.GetValueOrDefault(plane.name, (any: float.MaxValue, low: float.MaxValue));
                        best.any = Mathf.Min(best.any, d);
                        if (plane.Position.y - GroundHeight(plane.Position) < s.ZoneHeight)
                            best.low = Mathf.Min(best.low, d);
                        runnerNearest[plane.name] = best;
                    }
                }
                if (Time.time < nextSample)
                    continue;
                nextSample += SampleInterval;
                foreach (var p in PlaneController.All)
                {
                    if (!p.IsAlive)
                        continue;
                    alive += SampleInterval;
                    var edge = FlightBoundary.Active;
                    Vector3 middle = edge != null ? edge.Center : Vector3.zero;
                    float radius = new Vector2(p.Position.x - middle.x, p.Position.z - middle.z).magnitude;
                    maxRadius = Mathf.Max(maxRadius, radius);
                    if (edge != null && radius > edge.radius)
                    {
                        outside += SampleInterval;
                        if (radius > edge.radius + 500f && !strays.ContainsKey(p.name))
                        {
                            var stray = p.GetComponent<BotBrain>();
                            strays[p.name] = $"{Time.time - start:F0}s {p.name} {radius:F0} m out at ({p.Position.x:F0}, {p.Position.y:F0}, {p.Position.z:F0}) " +
                                             $"speed {p.Velocity.magnitude:F0} state {(stray != null ? stray.State.ToString() : "-")}";
                        }
                    }
                    string r = Region(p.Position);
                    Add(regionTime, r, SampleInterval);
                    float nearest = float.MaxValue;
                    foreach (var o in PlaneController.All)
                        if (o != p && o.IsAlive && o.Team != p.Team)
                            nearest = Mathf.Min(nearest, Vector3.Distance(o.Position, p.Position));
                    float ground = Mathf.Max(0f, p.Position.y - GroundHeight(p.Position));
                    altSum += ground * SampleInterval;
                    altBands[ground < 50f ? 0 : ground < 150f ? 1 : ground < 400f ? 2 : ground < 800f ? 3 : 4] += SampleInterval;
                    int gx = Mathf.Clamp((int)((p.Position.x + halfMap) / Cell), 0, n - 1);
                    int gz = Mathf.Clamp((int)((p.Position.z + halfMap) / Cell), 0, n - 1);
                    flyGrid[gx, gz] += SampleInterval;
                    if (nearest < FightRange)
                    {
                        Add(regionFight, r, SampleInterval);
                        fightGrid[gx, gz] += SampleInterval;
                    }
                    if (nearest < 900f && firstContact < 0f)
                        firstContact = Time.time - start;
                    if (nearest > 1500f)
                        lonely += SampleInterval;
                    var brain = p.GetComponent<BotBrain>();
                    if (brain != null)
                    {
                        stateTime.TryGetValue(brain.State, out float s);
                        stateTime[brain.State] = s + SampleInterval;
                    }
                }
            }
            float length = Time.time - start;
            Time.timeScale = 1f;
            PlaneHealth.AnyKilled -= OnKilled;
            if (banners != null)
                banners.EventApplied -= OnBanner;
            if (fieldsWorld != null)
                fieldsWorld.Captured -= OnCaptured;

            float fightTotal = regionFight.Values.Sum();
            var sb = new StringBuilder();
            sb.AppendLine($"[MapSurvey] {mapScene} {modeName}: {length:F0} s of play, ended {_match.Phase}; score {_match.Score.TeamScore(0)} - {_match.Score.TeamScore(1)}");
            sb.AppendLine($"plane-seconds alive {alive:F0}; in a fight (enemy within {FightRange:F0} m) {fightTotal / alive:P0}; lonely (nobody within 1.5 km) {lonely / alive:P0}; first contact (900 m) at {firstContact:F1} s");
            sb.AppendLine($"height above ground: mean {altSum / alive:F0} m; <50 {altBands[0] / alive:P0}, 50-150 {altBands[1] / alive:P0}, 150-400 {altBands[2] / alive:P0}, 400-800 {altBands[3] / alive:P0}, 800+ {altBands[4] / alive:P0}");
            sb.AppendLine("region: share of time / share of fight time / deaths");
            foreach (var r in regionTime.Keys.Union(regionFight.Keys).OrderByDescending(k => regionTime.GetValueOrDefault(k)))
                sb.AppendLine($"  {r,-14} {regionTime.GetValueOrDefault(r) / alive,5:P0} {regionFight.GetValueOrDefault(r) / Mathf.Max(1f, fightTotal),5:P0} {deathRegions.GetValueOrDefault(r)}");
            sb.AppendLine("bot states: " + string.Join(", ", stateTime.OrderByDescending(s => s.Value).Select(s => $"{s.Key} {s.Value / alive:P0}")));
            sb.AppendLine($"deaths {deaths.Count}: " + string.Join(", ", causes.Select(c => $"{c.Key} {c.Value}")));
            sb.AppendLine($"past the boundary: {outside / alive:P1} of plane time; furthest from the middle {maxRadius:F0} m");
            if (runnerNearest.Count > 0)
                sb.AppendLine("banner runners, closest to the enemy stand (any height / under the zone's height): " +
                              string.Join(", ", runnerNearest.Select(r => $"{r.Key} {r.Value.any:F0} / {(r.Value.low < float.MaxValue ? r.Value.low.ToString("F0") : "never low")}")));
            sb.AppendLine(Grid("fight heat (north up, 400 m cells, 0-9)", fightGrid, n));
            sb.AppendLine(Grid("presence heat", flyGrid, n));
            sb.AppendLine("500 m+ past the edge:\n" + string.Join("\n", strays.Values));
            sb.AppendLine("events:\n" + string.Join("\n", events));
            sb.AppendLine("deaths:\n" + string.Join("\n", deaths));
            string text = sb.ToString();
            Debug.Log(text);
            Directory.CreateDirectory("Logs");
            File.WriteAllText(Path.Combine("Logs", $"MapSurvey-{mapScene.Replace("Map_", "")}-{modeName}.txt"), text);
            Assert.Greater(alive, 0f);
        }

        static void Add(Dictionary<string, float> d, string k, float v) => d[k] = d.GetValueOrDefault(k) + v;

        static float GroundHeight(Vector3 p)
        {
            var t = Terrain.activeTerrain;
            return t != null ? Mathf.Max(0f, t.SampleHeight(p) + t.transform.position.y) : 0f;
        }

        static Maps.MapRegions s_regions;

        /// <summary>The surveyed map's regions, from its <c>MapDefinition</c>.</summary>
        static Maps.MapRegions Regions => s_regions ?? new Maps.MapRegions();

        /// <summary>Where on the map a point is: near an airfield or banner stand, else the map's region.</summary>
        static string Region(Vector3 p)
        {
            var flat = new Vector2(p.x, p.z);
            foreach (var f in Airfield.All)
                if (Vector2.Distance(flat, new Vector2(f.transform.position.x, f.transform.position.z)) < 400f)
                    return f.IsHome ? "HomeField" : f.DisplayName.Replace(" Airfield", "");
            foreach (var s in BannerStand.All)
                if (Vector2.Distance(flat, new Vector2(s.Position.x, s.Position.z)) < 400f)
                    return "BannerStand";
            return Regions.RegionAt(flat, GroundHeight(p));
        }

        static string Grid(string title, float[,] grid, int n)
        {
            float max = 0f;
            foreach (float v in grid)
                max = Mathf.Max(max, v);
            var sb = new StringBuilder(title + "\n");
            for (int z = n - 1; z >= 0; z--)
            {
                for (int x = 0; x < n; x++)
                    sb.Append(grid[x, z] <= 0f ? '.' : (char)('0' + Mathf.Min(9, Mathf.FloorToInt(grid[x, z] / max * 9.99f))));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}

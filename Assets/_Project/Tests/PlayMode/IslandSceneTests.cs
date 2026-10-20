using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The greybox Island map: its airfields, its scale, and the sea.</summary>
    public class IslandSceneTests
    {
        PlaneController _player;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            Object.FindAnyObjectByType<MatchManager>()?.Dismiss(); // free flight: no menu, no match clock
            Object.FindAnyObjectByType<BotRoster>()?.Clear(); // an empty sky: these tests fly alone
            yield return null;
            var go = GameObject.Find("Player Plane");
            Assert.IsNotNull(go, "Island scene has no Player Plane");
            _player = go.GetComponent<PlaneController>();
            foreach (var mb in _player.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name is "PlayerFlightInput" or "ClassSwitcher" or "DamageTester")
                    mb.enabled = false;
            foreach (var gun in Object.FindObjectsByType<AAGun>(FindObjectsSortMode.None))
                gun.enabled = false;
        }

        [Test]
        public void HasTwoHomeAirfields_AndThreeNeutralOnes()
        {
            var fields = Airfield.All;
            Assert.AreEqual(5, fields.Count);
            Assert.AreEqual(3, fields.Count(f => f.IsNeutral && !f.IsHome), "Central plus the south pair");
            Assert.IsTrue(fields.Any(f => f.IsHome && f.Team == 0), "no team 0 home airfield");
            Assert.IsTrue(fields.Any(f => f.IsHome && f.Team == 1), "no team 1 home airfield");
            Assert.IsTrue(fields.Any(f => f.IsNeutral && !f.IsHome), "no neutral airfield");
            Assert.AreEqual(0, _player.Team);
            var home = Airfield.NearestFriendly(_player.transform.position, _player.Team);
            Assert.IsTrue(home != null && home.IsHome && home.Team == 0, "the player doesn't start over its home airfield");
        }

        [Test]
        public void EachHomeField_HasABannerStand_PastTheFarEndOfItsRunway()
        {
            Assert.AreEqual(2, BannerStand.All.Count);
            foreach (var home in Airfield.All.Where(f => f.IsHome))
            {
                var stand = BannerStand.Of(home.Team);
                Assert.IsNotNull(stand, $"team {home.Team} has no banner stand");
                Vector3 local = home.transform.InverseTransformPoint(stand.Position);
                Assert.Greater(local.z, home.RunwaySize.y * 0.5f, "the stand isn't past the far end of the runway");
                Assert.Less(Mathf.Abs(local.x), 1f, "the stand isn't in line with the runway");
                Assert.IsNotNull(stand.FlagMount);
                Assert.Greater(stand.FlagMount.position.y - stand.Position.y, stand.ZoneHeight, "the pole is shorter than the grab zone");
            }
            Assert.IsNotNull(BannerStand.PositionsByTeam(2));
        }

        [Test]
        public void EveryAirfield_SitsOnFlatGround_ClearOfObstacles()
        {
            foreach (var field in Airfield.All)
            {
                float half = field.RunwaySize.y * 0.5f;
                for (float z = -half; z <= half; z += 50f)
                {
                    // The runway itself is the first thing below.
                    Vector3 over = field.transform.TransformPoint(new Vector3(0f, 150f, z));
                    Assert.IsTrue(LandingGear.ProbeGround(over, out var hit), $"{field.DisplayName}: nothing under the runway at {z}");
                    Assert.AreEqual(field.transform.position.y + 0.1f, hit.point.y, 0.05f, $"{field.DisplayName}: something above the runway at {z}");

                    // Flat land on both sides, level with it, so a runway excursion isn't a cliff.
                    foreach (float side in new[] { -40f, 40f })
                    {
                        Vector3 beside = field.transform.TransformPoint(new Vector3(side, 150f, z));
                        Assert.IsTrue(Physics.Raycast(beside, Vector3.down, out var ground, 300f), $"{field.DisplayName}: no ground beside the runway");
                        Assert.AreEqual(field.transform.position.y, ground.point.y, 1f, $"{field.DisplayName}: uneven ground at x {side}, z {z}");
                    }
                }
            }
        }

        /// <summary>island-map.md: the island takes ~45–60 s to cross at a Fighter's cruise speed.</summary>
        [Test]
        public void CrossingTheIsland_TakesAbout45To60Seconds_AtFighterCruise()
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            for (float x = -3000f; x <= 3000f; x += 10f)
            {
                if (!Physics.Raycast(new Vector3(x, 1000f, 0f), Vector3.down, out var hit, 2000f) || PlaneController.IsWater(hit.collider))
                    continue;
                minX = Mathf.Min(minX, x);
                maxX = Mathf.Max(maxX, x);
            }
            float cruise = FighterCruise();
            float seconds = (maxX - minX) / cruise;
            Assert.That(seconds, Is.InRange(45f, 60f), $"{maxX - minX:0} m at {cruise:0} m/s");
            Assert.Less(maxX, FlightBoundary.Active.radius - FlightBoundary.Active.warningMargin, "the boundary warning starts over land");
        }

        float FighterCruise()
        {
            var fighter = _player.GetComponent<ClassSwitcher>().Classes.First(c => c.displayName == "Fighter");
            return Mathf.Lerp(fighter.minSpeed, fighter.maxSpeed, fighter.cruiseThrottle);
        }

        /// <summary>Where fights happen: the middle of the city (IslandSceneBuilder.CityCenter) and every landmark.</summary>
        static (string name, Vector3 position)[] FightAreas() =>
            Landmark.All.Select(l => (l.DisplayName, l.transform.position)).Prepend(("City center", new Vector3(0f, 6f, 250f))).ToArray();

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        static Airfield Home(int team) => Airfield.All.First(f => f.IsHome && f.Team == team);

        /// <summary>
        /// island-map.md, Retreat times: from every fight area, each team's nearest repair field is 10–20 s away at
        /// Fighter cruise, counting the neutral Central field as captured. (Without it, the far regions are ~30 s.)
        /// </summary>
        [Test]
        public void EveryFightArea_Is10To20Seconds_FromAFriendlyAirfield_WithCentralCaptured()
        {
            float cruise = FighterCruise();
            foreach (var (name, position) in FightAreas())
            foreach (int team in new[] { 0, 1 })
            {
                var nearest = Airfield.All.Where(f => f.IsFriendlyTo(team) || f.IsNeutral).OrderBy(f => FlatDistance(position, f.transform.position)).First();
                float seconds = FlatDistance(position, nearest.transform.position) / cruise;
                Assert.That(seconds, Is.InRange(10f, 20f), $"{name}: team {team}'s nearest field ({nearest.DisplayName}) is {seconds:0.0} s away");
            }
        }

        /// <summary>
        /// Neither team is closer to the contested areas (the city and the ocean), and each team's home-region landmark
        /// (Summit Radar for west, The Pyramid for east) is the same flight time from its own home field.
        /// </summary>
        [Test]
        public void BothTeams_AreEquallyFarFromTheFights()
        {
            float cruise = FighterCruise();
            Vector3 west = Home(0).transform.position, east = Home(1).transform.position;
            Vector3 Region(MapRegion region) => Landmark.All.First(l => l.Region == region).transform.position;
            foreach (var (name, position) in FightAreas().Where(a => a.name == "City center").Append(("Ocean landmark", Region(MapRegion.Ocean))))
                Assert.AreEqual(FlatDistance(position, west) / cruise, FlatDistance(position, east) / cruise, 1f, $"{name} favors one team");

            Vector3 summit = Region(MapRegion.Mountains), pyramid = Region(MapRegion.Desert);
            Assert.AreEqual(FlatDistance(summit, west) / cruise, FlatDistance(pyramid, east) / cruise, 1f, "home-region landmarks are uneven");
        }

        /// <summary>No fight area sits under a home field's flak: fighting there would be a free kill for that team.</summary>
        [Test]
        public void NoFightArea_IsInsideAHomeAirfieldsAAUmbrella()
        {
            foreach (var gun in Airfield.All.Where(f => f.IsHome).SelectMany(f => f.GetComponentsInChildren<AAGun>(true)))
            foreach (var (name, position) in FightAreas())
                Assert.Greater(FlatDistance(position, gun.transform.position), gun.Stats.range, $"{name} is in range of {gun.Airfield.DisplayName}'s AA");
        }

        [Test]
        public void EveryRegion_HasOneLandmark()
        {
            foreach (MapRegion region in System.Enum.GetValues(typeof(MapRegion)))
                Assert.AreEqual(1, Landmark.All.Count(l => l.Region == region), $"{region} should have exactly one landmark");
        }

        /// <summary>A landmark is solid, and stands clearly above everything around it.</summary>
        [Test]
        public void EveryLandmark_TowersOverItsSurroundings()
        {
            foreach (var landmark in Landmark.All)
            {
                Vector3 top = landmark.TopPosition;
                Assert.IsTrue(Physics.Raycast(top + Vector3.up * 50f, Vector3.down, out var hit, 100f), $"{landmark.DisplayName}: nothing solid under the beacon");
                Assert.IsTrue(hit.collider.transform.IsChildOf(landmark.transform), $"{landmark.DisplayName}: {hit.collider.name} is above it");

                for (int i = 0; i < 16; i++)
                {
                    Vector3 around = top + Quaternion.Euler(0f, i * 22.5f, 0f) * Vector3.forward * 300f;
                    if (Physics.Raycast(new Vector3(around.x, 2000f, around.z), Vector3.down, out var ground, 3000f))
                        Assert.Less(ground.point.y, top.y - 40f, $"{landmark.DisplayName}: {ground.collider.name} 300 m away is nearly as tall");
                }
            }
        }

        /// <summary>From the middle of the map at combat altitude, every landmark's beacon is in plain sight.</summary>
        [Test]
        public void EveryLandmark_IsVisibleFromTheMiddleOfTheMap()
        {
            Vector3 eye = new Vector3(0f, 600f, -300f);
            foreach (var landmark in Landmark.All)
            {
                Vector3 top = landmark.TopPosition;
                bool blocked = Physics.Linecast(eye, top, out var hit) && !hit.collider.transform.IsChildOf(landmark.transform);
                Assert.IsFalse(blocked, $"{landmark.DisplayName} is hidden behind {(blocked ? hit.collider.name : "")}");
            }
        }

        // ------------------------------------------------------------------ approach lanes (island-map.md)

        static Airfield[] Homes() => Airfield.All.Where(f => f.IsHome).ToArray();

        /// <summary>The direction a lane's last leg arrives at its final fix from.</summary>
        static Vector3 FinalLeg(ApproachLane lane) => (lane.Points[^1] - lane.Points[^2]).normalized;

        /// <summary>Each home field has 2–3 lanes, all joining its glide path at the same final approach fix.</summary>
        [Test]
        public void EveryHomeAirfield_HasTwoOrThreeLanes_EndingOnItsGlidePath()
        {
            foreach (var field in Homes())
            {
                var lanes = ApproachLane.Into(field);
                Assert.That(lanes.Count, Is.InRange(2, 3), $"{field.DisplayName} has {lanes.Count} lanes");
                Vector3 fix = lanes[0].FinalFix;
                foreach (var lane in lanes)
                {
                    Assert.Less(Vector3.Distance(lane.FinalFix, fix), 0.1f, $"{lane.DisplayName} ends somewhere else");
                    Assert.GreaterOrEqual(lane.Points.Count, 3, $"{lane.DisplayName} is a single leg");
                    // Arriving down the last leg counts as on approach, and on the path.
                    var guide = field.GetApproach(lane.FinalFix, FinalLeg(lane) * 60f);
                    Assert.IsTrue(guide.OnPath, $"{field.DisplayName} {lane.DisplayName}: the final fix isn't on the glide path");
                }
            }
        }

        /// <summary>Central lands from either end: one lane into each, ending on that end's glide path, the same length.</summary>
        [Test]
        public void Central_IsTwoWay_WithOneLaneIntoEachEnd()
        {
            var central = Airfield.All.Single(f => f.DisplayName == "Central Airfield");
            Assert.IsTrue(central.IsTwoWay);
            var lanes = ApproachLane.Into(central);
            Assert.AreEqual(2, lanes.Count);
            Assert.AreEqual(lanes[0].Length, lanes[1].Length, lanes[0].Length * 0.02f, "the two ends' lanes differ in length");
            var ends = new HashSet<bool>();
            foreach (var lane in lanes)
            {
                Vector3 heading = FinalLeg(lane) * 60f;
                bool reverse = central.ApproachesFromReverseEnd(lane.FinalFix, heading);
                ends.Add(reverse);
                Assert.IsTrue(central.GetApproach(lane.FinalFix, heading).OnPath, $"{lane.DisplayName}: the final fix isn't on the glide path");
                Assert.Less(Vector3.Distance(lane.FinalFix, central.GlidePathPoint(600f, reverse)), 0.1f, $"{lane.DisplayName}: wrong end");
            }
            Assert.AreEqual(2, ends.Count, "both lanes go to the same end");
        }

        /// <summary>Every leg of every lane is open air: no terrain, building or landmark in the way, nor close below.</summary>
        [Test]
        public void EveryLane_IsClearOfTerrainAndBuildings()
        {
            const float radius = 12f, minClearance = 25f;
            foreach (var lane in ApproachLane.All)
            for (int i = 1; i < lane.Points.Count; i++)
            {
                Vector3 a = lane.Points[i - 1], b = lane.Points[i];
                var blocking = Physics.SphereCastAll(a, radius, (b - a).normalized, Vector3.Distance(a, b), ~0, QueryTriggerInteraction.Ignore)
                    .Where(h => h.collider.GetComponentInParent<PlaneController>() == null).ToArray();
                Assert.IsEmpty(blocking, $"{lane.Airfield.DisplayName} {lane.DisplayName} leg {i}: {(blocking.Length > 0 ? blocking[0].collider.name : "")} in the way");

                for (float t = 0f; t <= 1f; t += 25f / Vector3.Distance(a, b))
                {
                    Vector3 p = Vector3.Lerp(a, b, t);
                    if (Physics.Raycast(p, Vector3.down, out var ground, 1000f, ~0, QueryTriggerInteraction.Ignore))
                        Assert.GreaterOrEqual(p.y - ground.point.y, minClearance, $"{lane.Airfield.DisplayName} {lane.DisplayName} leg {i}: only {p.y - ground.point.y:0} m over {ground.collider.name}");
                }
            }
        }

        /// <summary>
        /// The point of having lanes: an enemy waiting on one doesn't cover the others. Until 500 m from the shared
        /// final fix, every lane stays at least 250 m from every other lane into the same field.
        /// </summary>
        [Test]
        public void LanesIntoAField_AreSeparateRoutes_UntilTheFinal()
        {
            const float mergeZone = 500f, separation = 250f;
            foreach (var field in Homes())
            {
                var lanes = ApproachLane.Into(field);
                foreach (var lane in lanes)
                foreach (var other in lanes.Where(l => l != lane))
                for (int i = 1; i < lane.Points.Count; i++)
                {
                    Vector3 a = lane.Points[i - 1], b = lane.Points[i];
                    for (float t = 0f; t <= 1f; t += 25f / Vector3.Distance(a, b))
                    {
                        Vector3 p = Vector3.Lerp(a, b, t);
                        if (lane.RemainingDistance(p) < mergeZone)
                            continue;
                        float d = other.DistanceFrom(p, out _);
                        Assert.GreaterOrEqual(d, separation, $"{field.DisplayName}: {lane.DisplayName} passes {d:0} m from {other.DisplayName}");
                    }
                }
            }
        }

        /// <summary>Lanes stay inside the boundary warning, and each home's final fix is under its own AA.</summary>
        [Test]
        public void Lanes_StayInBounds_AndEndUnderHomeAA()
        {
            var boundary = FlightBoundary.Active;
            foreach (var lane in ApproachLane.All)
                foreach (var p in lane.Points)
                    Assert.IsFalse(boundary.IsNearEdge(p), $"{lane.Airfield.DisplayName} {lane.DisplayName}: {p} is in the boundary warning");

            foreach (var field in Homes())
            {
                Vector3 fix = ApproachLane.Into(field)[0].FinalFix;
                int covering = field.GetComponentsInChildren<AAGun>(true).Count(g => Vector3.Distance(g.transform.position, fix) < g.Stats.range);
                Assert.GreaterOrEqual(covering, 2, $"{field.DisplayName}'s final fix is covered by {covering} AA guns");
            }
        }

        /// <summary>Fairness: both teams' lanes are the same lengths (East Home's are West Home's mirrored).</summary>
        [Test]
        public void BothHomes_LanesAreTheSameLengths()
        {
            float[] west = ApproachLane.Into(Home(0)).Select(l => l.Length).OrderBy(l => l).ToArray();
            float[] east = ApproachLane.Into(Home(1)).Select(l => l.Length).OrderBy(l => l).ToArray();
            Assert.AreEqual(west.Length, east.Length);
            for (int i = 0; i < west.Length; i++)
                Assert.AreEqual(west[i], east[i], west[i] * 0.05f, $"lane lengths differ: {west[i]:0} vs {east[i]:0} m");
        }

        /// <summary>
        /// A Fighter on autopilot, steering for each lane's next waypoint the way a bot will, flies every lane from its
        /// entry to the final fix without crashing or wandering off it, and arrives on approach to the runway.
        /// </summary>
        [UnityTest]
        public IEnumerator AutopilotFlies_EveryLane_ToTheFinalApproach()
        {
            Time.timeScale = 4f;
            try
            {
                foreach (var lane in ApproachLane.All.ToArray())
                {
                    string name = $"{lane.Airfield.DisplayName} {lane.DisplayName}";
                    Vector3 firstLeg = lane.Points[1] - lane.Points[0];
                    _player.Respawn(lane.Entry, Quaternion.LookRotation(firstLeg));
                    yield return new WaitForFixedUpdate();

                    float timeout = lane.Length / 50f + 10f, worst = 0f;
                    for (float t = 0f; t < timeout && _player.IsAlive && lane.RemainingDistance(_player.Position) > 60f; t += Time.fixedDeltaTime)
                    {
                        FlightAutopilot.SteerTowards(_player.Position, _player.Rotation, lane.NextWaypoint(_player.Position, 150f), 2f, 15f,
                            out float pitch, out float yaw, out float roll);
                        _player.SetInput(new FlightInput { Pitch = pitch, Yaw = yaw, Roll = roll, AutoThrottle = true });
                        worst = Mathf.Max(worst, lane.DistanceFrom(_player.Position, out _));
                        yield return new WaitForFixedUpdate();
                    }

                    Assert.IsTrue(_player.IsAlive, $"{name}: crashed ({_player.LastTouchdown.Fault})");
                    Assert.LessOrEqual(lane.RemainingDistance(_player.Position), 60f, $"{name}: didn't reach the final fix in time");
                    Assert.LessOrEqual(worst, lane.HalfWidth, $"{name}: strayed {worst:0} m off the lane");
                    Assert.IsTrue(lane.Airfield.GetApproach(_player.Position, _player.Velocity).OnApproach, $"{name}: not on approach at the fix");
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }
        }

        // ------------------------------------------------------------------ clouds (island-map.md)

        /// <summary>World-space puffs of a bank.</summary>
        static IEnumerable<(Vector3 center, Vector3 radii)> PuffsOf(CloudBank bank) =>
            bank.Puffs.Select(p => (bank.transform.position + p.center, p.radii));

        [Test]
        public void Clouds_ComeInMirroredPairs_SoBothTeamsGetTheSameCover()
        {
            var banks = CloudBank.All;
            Assert.GreaterOrEqual(banks.Count, 10, "enough cloud banks to fight around");
            foreach (var bank in banks)
            {
                Vector3 p = bank.transform.position;
                var twin = banks.FirstOrDefault(b => Vector3.Distance(b.transform.position, new Vector3(-p.x, p.y, -p.z)) < 1f);
                Assert.IsNotNull(twin, $"{bank.name} has no mirror for the other team");
                Assert.AreEqual(bank.Puffs.Count, twin.Puffs.Count, $"{bank.name} and {twin.name} differ");
            }
        }

        [Test]
        public void EveryCloud_HasAHidingSpot_AndFloatsClearOfTheGround()
        {
            foreach (var bank in CloudBank.All)
            {
                Assert.IsTrue(CloudCover.IsHidden(bank.transform.position), $"{bank.name}: its middle doesn't hide a plane");
                foreach (var (center, radii) in PuffsOf(bank))
                {
                    Vector3 bottom = center - Vector3.up * radii.y;
                    Assert.IsTrue(Physics.Raycast(bottom, Vector3.down, out var hit, 2000f), $"{bank.name}: nothing under it");
                    Assert.GreaterOrEqual(hit.distance, 60f, $"{bank.name} sits on {hit.collider.name}: no room to fly under it");
                }
            }
        }

        /// <summary>Clouds would let an attacker camp a home field safe from its AA, so none reach into the flak.</summary>
        [Test]
        public void NoCloud_IsInHomeAARange()
        {
            foreach (var gun in Airfield.All.Where(f => f.IsHome).SelectMany(f => f.GetComponentsInChildren<AAGun>(true)))
            foreach (var bank in CloudBank.All)
            foreach (var (center, radii) in PuffsOf(bank))
            {
                float nearest = Vector3.Distance(gun.Pivot, center) - Mathf.Max(radii.x, Mathf.Max(radii.y, radii.z));
                Assert.Greater(nearest, gun.Stats.range, $"{bank.name} is in range of {gun.Airfield.DisplayName}'s AA");
            }
        }

        /// <summary>Every fight area has a cloud to duck into within ~10 s at Fighter cruise.</summary>
        [Test]
        public void EveryFightArea_HasCloudCoverNearby()
        {
            float cruise = FighterCruise();
            foreach (var (name, position) in FightAreas())
            {
                float seconds = CloudBank.All.Min(b => FlatDistance(position, b.transform.position)) / cruise;
                Assert.LessOrEqual(seconds, 10f, $"{name}: the nearest cloud is {seconds:0.0} s away");
            }
        }

        [Test]
        public void Clouds_DontHideLandmarks_SpawnsOrApproaches()
        {
            Vector3 eye = new Vector3(0f, 600f, -300f);
            foreach (var landmark in Landmark.All)
            {
                Assert.IsFalse(CloudCover.IsInside(landmark.TopPosition), $"{landmark.DisplayName}'s top is in a cloud");
                Assert.IsTrue(CloudCover.CanSee(eye, landmark.TopPosition), $"{landmark.DisplayName} is hidden by cloud from mid-map");
            }
            foreach (var field in Airfield.All)
                Assert.IsFalse(CloudCover.IsInside(field.AirSpawn.position), $"{field.DisplayName}'s air spawn is in a cloud");
            foreach (var lane in ApproachLane.All)
                for (int i = 1; i < lane.Points.Count; i++)
                    Assert.AreEqual(0f, CloudCover.ChordLength(lane.Points[i - 1], lane.Points[i]), 1e-3f,
                        $"{lane.DisplayName} leg {i} flies through cloud");
        }

        // ------------------------------------------------------------------ boundary (island-map.md)

        /// <summary>A ring of buoys marks the edge all the way round, out on open sea, with nothing to crash into.</summary>
        [Test]
        public void BoundaryBuoys_RingTheEdge_OnOpenSea()
        {
            var boundary = FlightBoundary.Active;
            var ring = Object.FindAnyObjectByType<BoundaryBuoys>();
            Assert.IsNotNull(ring, "no boundary buoys");
            Assert.IsEmpty(ring.GetComponentsInChildren<Collider>(), "buoys shouldn't have colliders");

            var bearings = new List<float>();
            foreach (Transform buoy in ring.transform)
            {
                Assert.AreEqual(boundary.radius, boundary.HorizontalDistance(buoy.position), 1f, $"{buoy.name} isn't on the edge");
                Assert.IsTrue(Physics.Raycast(buoy.position + Vector3.up * 500f, Vector3.down, out var hit, 600f)
                    && PlaneController.IsWater(hit.collider), $"{buoy.name} isn't on open sea");
                Vector3 d = buoy.position - boundary.transform.position;
                bearings.Add(Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg);
            }
            Assert.AreEqual(ring.Count, bearings.Count, "every buoy has a light");
            bearings.Sort();
            bearings.Add(bearings[0] + 360f);
            float widest = 0f;
            for (int i = 1; i < bearings.Count; i++)
                widest = Mathf.Max(widest, bearings[i] - bearings[i - 1]);
            Assert.LessOrEqual(widest * Mathf.Deg2Rad * boundary.radius, 160f, "a gap in the ring");
        }

        /// <summary>
        /// Every class, flying out over the sea on every bearing (low, mid and high; straight out or along the edge;
        /// hands off or boosting outward against it) is turned back without crashing, doesn't stray far past the edge,
        /// and, hands off, isn't left pointing back out once the turn-back lets go.
        /// </summary>
        [UnityTest]
        public IEnumerator FlyingOutOverTheSea_TurnsBack_EveryClassAndBearing()
        {
            var boundary = FlightBoundary.Active;
            var classes = _player.GetComponent<ClassSwitcher>().Classes;
            float[] altitudes = { 30f, 600f, 1400f };
            Vector3 c = boundary.transform.position;
            var failures = new List<string>();
            Time.timeScale = 4f;
            try
            {
                for (int k = 0; k < classes.Count; k++)
                for (int i = 0; i < 8; i++)
                {
                    _player.SetStats(classes[k]);
                    float bearing = i * 45f + k * 11f;
                    Vector3 outward = Quaternion.Euler(0f, bearing, 0f) * Vector3.forward;
                    float altitude = altitudes[(i + k) % 3];
                    bool alongEdge = i % 2 == 1, fighting = (i / 2 + k) % 2 == 1;
                    Vector3 heading = alongEdge ? Quaternion.Euler(0f, 60f, 0f) * outward : outward;
                    Vector3 start = c + outward * (boundary.radius - 60f) + Vector3.up * altitude;
                    string name = $"{classes[k].name} bearing {bearing:0} alt {altitude:0} {(alongEdge ? "along" : "out")} {(fighting ? "fighting" : "hands-off")}";

                    _player.Respawn(start, Quaternion.LookRotation(heading), 1f);
                    yield return new WaitForFixedUpdate();

                    float worst = 0f, back = -1f, lowest = float.MaxValue;
                    bool wentOut = false;
                    for (float t = 0f; t < 40f && _player.IsAlive; t += Time.fixedDeltaTime)
                    {
                        var input = new FlightInput { Throttle = 1f };
                        if (fighting)
                        {
                            // Push on outward along the original heading, boosting.
                            Vector3 aim = _player.Position + heading * 1000f;
                            aim.y = altitude;
                            FlightAutopilot.SteerTowards(_player.Position, _player.Rotation, aim, 2f, 15f,
                                out input.Pitch, out input.Yaw, out input.Roll);
                            input.Boost = true;
                        }
                        _player.SetInput(input);
                        float past = boundary.HorizontalDistance(_player.Position) - boundary.radius;
                        worst = Mathf.Max(worst, past);
                        lowest = Mathf.Min(lowest, _player.Position.y);
                        wentOut |= past > 0f;
                        if (wentOut && past < 0f)
                        {
                            back = t;
                            break;
                        }
                        yield return new WaitForFixedUpdate();
                    }

                    if (!_player.IsAlive)
                    {
                        failures.Add($"{name}: crashed ({_player.LastDeathCause}, lowest {lowest:0} m)");
                        continue;
                    }
                    if (back < 0f)
                    {
                        failures.Add($"{name}: not back inside after 40 s (worst {worst:0} m past the edge)");
                        continue;
                    }
                    if (worst > 600f)
                        failures.Add($"{name}: strayed {worst:0} m past the edge");

                    // Hands off after the turn-back lets go: no drifting straight back out.
                    if (!fighting)
                    {
                        float again = 0f;
                        for (float t = 0f; t < 10f && _player.IsAlive; t += Time.fixedDeltaTime)
                        {
                            _player.SetInput(new FlightInput { AutoThrottle = true });
                            again = Mathf.Max(again, boundary.HorizontalDistance(_player.Position) - boundary.radius);
                            yield return new WaitForFixedUpdate();
                        }
                        if (!_player.IsAlive)
                            failures.Add($"{name}: crashed after the turn-back ({_player.LastDeathCause})");
                        else if (again > 0f)
                            failures.Add($"{name}: drifted {again:0} m back out after the turn-back let go");
                    }
                    Debug.Log($"[Boundary] {name}: back in {back:0.0} s, worst {worst:0} m past, lowest {lowest:0} m");
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }

        // ------------------------------------------------------------------ nature (Art/Models/Nature)

        /// <summary>
        /// The nature pack covers the island as terrain trees, but nothing grows on or off the ends of a runway, where
        /// planes come in low, or in the city.
        /// </summary>
        [Test]
        public void Nature_GrowsAcrossTheIsland_ButNotOnRunwaysOrInTheCity()
        {
            var terrain = Terrain.activeTerrain;
            var data = terrain.terrainData;
            Assert.Greater(data.treeInstanceCount, 1000, "the island is bare");
            Assert.Greater(data.treePrototypes.Length, 15, "too few kinds of tree, bush and rock");
            var spire = Landmark.All.First(l => l.Region == MapRegion.City);
            foreach (var tree in data.treeInstances)
            {
                Vector3 p = terrain.transform.position + Vector3.Scale(tree.position, data.size);
                Assert.Greater(FlatDistance(p, spire.transform.position), 350f, $"a {data.treePrototypes[tree.prototypeIndex].prefab.name} grows downtown at {p}");
                foreach (var field in Airfield.All)
                {
                    Vector3 local = field.transform.InverseTransformPoint(p);
                    Assert.IsFalse(Mathf.Abs(local.x) < 120f && Mathf.Abs(local.z) < field.RunwaySize.y * 0.5f + 300f,
                        $"a {data.treePrototypes[tree.prototypeIndex].prefab.name} grows in line with {field.DisplayName}'s runway at {p}");
                }
            }
        }

        /// <summary>The desert mesas and Lighthouse Rock's islet are meshes you can crash into, flat on top.</summary>
        [Test]
        public void Mesas_AndTheIslet_AreSolidMeshes()
        {
            var mesas = GameObject.Find("Mesas");
            Assert.IsNotNull(mesas, "no mesas");
            Assert.AreEqual(3, mesas.transform.childCount);
            foreach (Transform mesa in mesas.transform)
            {
                Assert.IsTrue(Physics.Raycast(mesa.position + Vector3.up * 1000f, Vector3.down, out var hit, 2000f), $"{mesa.name}: nothing solid");
                Assert.AreEqual(mesa, hit.collider.transform, $"{mesa.name}: hit {hit.collider.name} instead");
                Assert.Greater(hit.point.y - mesa.position.y, 80f, $"{mesa.name} is too low");
            }

            var lighthouse = Landmark.All.First(l => l.Region == MapRegion.Ocean);
            Vector3 lawn = lighthouse.transform.position + new Vector3(-30f, 200f, -20f);
            Assert.IsTrue(Physics.Raycast(lawn, Vector3.down, out var top, 400f), "nothing under the islet's top");
            Assert.AreEqual("Islet", top.collider.name);
            Assert.AreEqual(lighthouse.transform.position.y, top.point.y, 1f, "the islet's top isn't level with the lighthouse's foot");
        }

        [UnityTest]
        public IEnumerator SettingDownOnTheSea_IsADitching()
        {
            var gear = _player.GetComponent<LandingGear>();
            _player.Respawn(new Vector3(-700f, 15f, -2100f), Quaternion.Euler(2f, 0f, 0f)); // open sea, clear of Lighthouse Rock
            gear.Snap(true);
            yield return RunwayLanding.FlyToTouchdown(_player, verticalSpeed: -3f, throttle: -1f);
            Assert.IsFalse(_player.IsAlive, "the plane landed on the water");
            Assert.AreEqual(TouchdownFault.Ditched, _player.LastTouchdown.Fault);
        }
    }
}

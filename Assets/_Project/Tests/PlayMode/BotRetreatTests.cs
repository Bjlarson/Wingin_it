using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.World;
using Object = UnityEngine.Object;

namespace WingingIt.Tests
{
    /// <summary>
    /// <see cref="BotBrain"/>'s retreat to repair in the FlightTest scene: a damaged bot flies home, lands on the glide
    /// path, repairs, takes off and goes back to its patrol. The Fighter drone is turned into a bot on the home
    /// airfield's team (0); runs at 4× speed.
    /// </summary>
    public class BotRetreatTests
    {
        BotBrain _brain;
        PlaneController _plane;
        PlaneHealth _health;
        PlaneRepair _repair;
        Airfield _home;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            Log.Clear();
            yield return FlightTestScene.Load();
            FlightTestScene.Player.gameObject.SetActive(false);
            _home = Airfield.All.First(f => f.IsFriendlyTo(0));
            var dummy = FlightTestScene.Dummies.First(d => d.GetComponent<PlaneController>().Stats.displayName == "Fighter");
            _plane = dummy.GetComponent<PlaneController>();
            dummy.enabled = false;
            _plane.Team = 0;
            _plane.gameObject.AddComponent<BotPilot>();
            _brain = _plane.gameObject.AddComponent<BotBrain>();
            _health = _plane.GetComponent<PlaneHealth>();
            _repair = _plane.GetComponent<PlaneRepair>();
            _plane.gameObject.SetActive(true);
            // North of the field, heading away from it: it has to turn around and come in from the south.
            _plane.Respawn(new Vector3(300f, 400f, -200f), Quaternion.identity);
            yield return new WaitForFixedUpdate();
            Time.timeScale = 4f;
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        /// <summary>Diagnostics for failure messages.</summary>
        readonly List<string> Log = new();

        static IEnumerator Fly(float seconds, Func<bool> done = null)
        {
            for (float t = 0f; t < seconds && (done == null || !done()); t += Time.fixedDeltaTime)
                yield return new WaitForFixedUpdate();
        }

        void DamageBody(float fractionLeft)
        {
            var body = _health.GetPart(PlanePartType.Body);
            _health.ApplyDamage(PlanePartType.Body, body.MaxHealth * (1f - fractionLeft), _plane.Position);
        }

        /// <summary>Flies until the repair finishes, recording every state the bot was in and how it touched down.</summary>
        IEnumerator FlyHomeAndRepair(float timeout, List<BotState> states, List<TouchdownInfo> touchdowns)
        {
            bool repaired = false;
            void OnDone(PlaneRepair _) => repaired = true;
            void OnTouch(PlaneController p, TouchdownInfo info)
            {
                touchdowns.Add(info);
                Log.Add($"touchdown {info.Result} {info.Fault} at {p.Position} sink {info.SinkRate:0.0} pitch {info.Pitch:0} roll {info.Roll:0}");
            }
            // A short flight recorder: the last few seconds before a crash.
            var trace = new Queue<string>();
            float clock = 0f, sampleAt = 0f;
            void OnCrash(PlaneController p, Vector3 at)
            {
                Log.Add($"crashed at {at}, plane {p.Position} v{p.Velocity} rot {p.Rotation.eulerAngles} state {_brain.State} obstacle {_brain.Obstacle}");
                Log.AddRange(trace);
            }
            _repair.Completed += OnDone;
            _plane.TouchedDown += OnTouch;
            _plane.Crashed += OnCrash;
            try
            {
                yield return Fly(timeout, () =>
                {
                    clock += Time.fixedDeltaTime;
                    if (clock >= sampleAt)
                    {
                        sampleAt = clock + 0.5f;
                        var g = _home.GetApproach(_plane.Position, _plane.Velocity);
                        float bank = Vector3.SignedAngle(Vector3.ProjectOnPlane(_plane.Rotation * Vector3.up, _plane.Rotation * Vector3.forward),
                            Vector3.up, _plane.Rotation * Vector3.forward);
                        trace.Enqueue($"  {clock:0.0}s {_brain.State} at {_plane.Position:F0} v{_plane.Speed:0} vy{_plane.Velocity.y:0.0} bank {bank:0} " +
                                      $"in R{_plane.CurrentInput.Roll:0.00} P{_plane.CurrentInput.Pitch:0.00} onApp {g.OnApproach} dist {g.Distance:0} lat {g.Lateral:0} hErr {g.HeightError:0}");
                        if (trace.Count > 16)
                            trace.Dequeue();
                    }
                    if (states.Count == 0 || states[^1] != _brain.State)
                    {
                        states.Add(_brain.State);
                        if (_brain.State == BotState.TakeOff)
                            Log.Add($"takeoff from {_plane.Position} speed {_plane.Speed:0.0} grounded {_plane.IsGrounded}");
                        if (_brain.State == BotState.AvoidTerrain && Log.Count < 6)
                            Log.Add($"avoid at {_plane.Position} v{_plane.Velocity} obstacle {_brain.Obstacle} wp {_brain.RetreatWaypoint}");
                    }
                    return repaired || !_plane.IsAlive;
                });
            }
            finally
            {
                _repair.Completed -= OnDone;
                _plane.TouchedDown -= OnTouch;
                _plane.Crashed -= OnCrash;
            }
        }

        [UnityTest]
        public IEnumerator Damaged_FliesHome_LandsRepairsAndTakesOffAgain()
        {
            _brain.SetPatrol(new[] { new Vector3(300f, 400f, 300f), new Vector3(-300f, 400f, 300f) });
            DamageBody(0.3f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.IsTrue(_brain.IsRetreating, "30% body should send it home");
            Assert.AreEqual(_home, _brain.RetreatField);

            var states = new List<BotState>();
            var touchdowns = new List<TouchdownInfo>();
            yield return FlyHomeAndRepair(150f, states, touchdowns);

            string seen = string.Join(" > ", states);
            Assert.IsTrue(_plane.IsAlive, $"crashed ({_plane.LastDeathCause}, {_plane.LastTouchdown.Fault}); states: {seen}");
            Assert.AreEqual(1f, _health.GetPart(PlanePartType.Body).NormalizedHealth, 1e-4f, $"never repaired; states: {seen}\n{string.Join("\n", Log)}");
            CollectionAssert.IsSubsetOf(new[] { BotState.Retreat, BotState.Land, BotState.Repair }, states, seen);
            Assert.AreEqual(TouchdownResult.Landed, touchdowns[0].Result, $"first touchdown: {touchdowns[0].Fault}");
            Assert.AreEqual(0, _brain.GoArounds, $"needed a go-around; states: {seen}");
            Assert.AreEqual(RepairTier.Heavy, _repair.Tier);
            Assert.IsFalse(_brain.IsRetreating);

            // Back in the air and back to the patrol.
            yield return Fly(40f, () => !_plane.IsGrounded && _brain.State == BotState.Patrol);
            Assert.IsTrue(_plane.IsAlive);
            Assert.IsFalse(_plane.IsGrounded, "never took off");
            Assert.AreEqual(BotState.Patrol, _brain.State);
            yield return Fly(5f);
            Assert.Greater(_plane.Position.y, 60f, "climbing out to the patrol");
        }

        [UnityTest]
        public IEnumerator OneWinged_StillMakesItHome()
        {
            var wing = _health.GetPart(PlanePartType.WingLeft);
            _health.ApplyDamage(wing, wing.MaxHealth * 2f, _plane.Position);
            yield return Fly(0.1f);
            Assert.IsTrue(_brain.IsRetreating, "a lost wing should send it home");

            var states = new List<BotState>();
            var touchdowns = new List<TouchdownInfo>();
            yield return FlyHomeAndRepair(180f, states, touchdowns);

            string seen = string.Join(" > ", states);
            string landings = string.Join(", ", touchdowns.Select(t => $"{t.Result} {t.Fault} {t.Speed:0} m/s"));
            Assert.IsTrue(_plane.IsAlive, $"crashed ({_plane.LastDeathCause}); states: {seen}; touchdowns: {landings}\n{string.Join("\n", Log)}");
            Assert.AreEqual(0, _health.LostPartsMask, $"wing never reattached; states: {seen}; touchdowns: {landings}\n{string.Join("\n", Log)}");
            Assert.AreEqual(RepairTier.Critical, _repair.Tier);
        }

        [UnityTest]
        public IEnumerator LightDamage_KeepsFlying()
        {
            DamageBody(0.7f);
            yield return Fly(10f);
            Assert.IsFalse(_brain.IsRetreating);
            Assert.AreEqual(BotState.Patrol, _brain.State);
        }

        [UnityTest]
        public IEnumerator HealedInTheAir_CallsOffTheRetreat()
        {
            DamageBody(0.3f);
            yield return Fly(1f);
            Assert.IsTrue(_brain.IsRetreating);
            _health.HealFraction(1f); // a Support's Repair Aura
            yield return Fly(0.5f);
            Assert.IsFalse(_brain.IsRetreating);
        }

        [UnityTest]
        public IEnumerator RunwaySpawn_TakesOff()
        {
            Pose slot = _home.RunwaySpawnSlot(0);
            _plane.SpawnOnGround(slot.position, slot.rotation);
            yield return Fly(0.1f);
            Assert.IsTrue(_plane.IsGrounded);
            Assert.AreEqual(BotState.TakeOff, _brain.State);

            yield return Fly(40f, () => !_plane.IsGrounded && _brain.State == BotState.Patrol);
            Assert.IsTrue(_plane.IsAlive, $"crashed ({_plane.LastDeathCause})");
            Assert.IsFalse(_plane.IsGrounded, "still on the ground");
            Assert.AreEqual(BotState.Patrol, _brain.State);
        }

        [UnityTest]
        public IEnumerator RunwayTaken_HoldsUntilItClears()
        {
            // Another plane parked on the runway, short of the repair zone.
            var other = FlightTestScene.Player;
            other.gameObject.SetActive(true);
            Pose slot = _home.RunwaySpawnSlot(0);
            other.SpawnOnGround(slot.position, slot.rotation);

            DamageBody(0.3f);
            bool touchedDown = false;
            void OnTouch(PlaneController _, TouchdownInfo __) => touchedDown = true;
            _plane.TouchedDown += OnTouch;
            try
            {
                yield return Fly(90f, () => touchedDown || !_plane.IsAlive);
                Assert.IsTrue(_plane.IsAlive, $"crashed ({_plane.LastDeathCause})");
                Assert.IsFalse(touchedDown, "landed onto a busy runway");
                Assert.IsTrue(_brain.IsRetreating);

                other.gameObject.SetActive(false);
                yield return Fly(120f, () => _repair.IsRepairing || !_plane.IsAlive);
                Assert.IsTrue(_plane.IsAlive, $"crashed ({_plane.LastDeathCause})");
                Assert.IsTrue(_repair.IsRepairing, $"never landed once the runway cleared (state {_brain.State})");
            }
            finally
            {
                _plane.TouchedDown -= OnTouch;
            }
        }

        [UnityTest]
        public IEnumerator NoFriendlyField_DoesntRetreat()
        {
            _plane.Team = 1; // no team 1 airfield in the FlightTest scene
            DamageBody(0.3f);
            yield return Fly(2f);
            Assert.IsFalse(_brain.IsRetreating);
        }
    }

    /// <summary>A bot flies each of the Island's six approach lanes home and lands in the repair zone.</summary>
    public class IslandBotRetreatTests
    {
        PlaneController _plane;
        BotBrain _brain;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return MapTestScene.Load();
            Object.FindAnyObjectByType<MatchManager>()?.Dismiss(); // free flight: no menu, no match clock
            Object.FindAnyObjectByType<BotRoster>()?.Clear(); // an empty sky: these tests fly alone
            yield return null;
            var go = GameObject.Find("Player Plane");
            Assert.IsNotNull(go, "MainMenu + Map_Island have no Player Plane");
            _plane = go.GetComponent<PlaneController>();
            foreach (var mb in _plane.GetComponents<MonoBehaviour>())
                if (mb.GetType().Name is "PlayerFlightInput" or "ClassSwitcher" or "DamageTester")
                    mb.enabled = false;
            foreach (var gun in Object.FindObjectsByType<AAGun>(FindObjectsSortMode.None))
                gun.enabled = false;
            go.AddComponent<BotPilot>();
            _brain = go.AddComponent<BotBrain>();
            yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator EveryLane_FliesHomeAndLandsInTheRepairZone()
        {
            Time.timeScale = 4f;
            var repair = _plane.GetComponent<PlaneRepair>();
            var failures = new List<string>();
            try
            {
                foreach (var lane in ApproachLane.All.ToArray())
                {
                    // A neutral field repairs nobody until a team captures it (Airfield Domination), so no bot retreats to it.
                    if (lane.Airfield.IsNeutral)
                        continue;
                    string name = $"{lane.Airfield.DisplayName} {lane.DisplayName}";
                    _plane.Team = lane.Airfield.Team;
                    // A little short of the entry, flying toward it, like a bot leaving the fight.
                    Vector3 firstLeg = (lane.Points[1] - lane.Points[0]).normalized;
                    Vector3 start = lane.Entry - firstLeg * 300f + Vector3.up * 40f;
                    _plane.Respawn(start, Quaternion.LookRotation(Vector3.ProjectOnPlane(firstLeg, Vector3.up)));
                    yield return new WaitForFixedUpdate();
                    _brain.Retreat(lane);
                    int goArounds = _brain.GoArounds;

                    float timeout = lane.Length / 40f + 60f;
                    // A short flight recorder: what the bot was doing in the seconds before a go-around.
                    var trace = new Queue<string>();
                    string firstGoAround = null, crash = null;
                    void OnCrash(PlaneController p, Vector3 at) => crash ??=
                        $"crashed at {at:F1}: rot {p.Rotation.eulerAngles:F0} v{p.Velocity:F1} state {_brain.State}\n{string.Join("\n", trace)}";
                    _plane.Crashed += OnCrash;
                    float sampleAt = 0f;
                    for (float t = 0f; t < timeout && _plane.IsAlive && !repair.IsRepairing; t += Time.fixedDeltaTime)
                    {
                        if (t >= sampleAt)
                        {
                            sampleAt = t + 0.5f;
                            var g = lane.Airfield.GetApproach(_plane.Position, _plane.Velocity);
                            trace.Enqueue($"  {t:0.0}s {_brain.State} wp{_brain.RetreatWaypoint} at {_plane.Position:F0} v{_plane.Speed:0} " +
                                          $"onApp {g.OnApproach} dist {g.Distance:0} lat {g.Lateral:0} hErr {g.HeightError:0} obst {_brain.Obstacle:F0}");
                            if (trace.Count > 14)
                                trace.Dequeue();
                        }
                        if (firstGoAround == null && _brain.GoArounds > goArounds)
                            firstGoAround = $"first go-around: {_brain.LastGoAround}\n{string.Join("\n", trace)}";
                        yield return new WaitForFixedUpdate();
                    }
                    _plane.Crashed -= OnCrash;

                    if (crash != null)
                        failures.Add($"{name}: {crash}");
                    else if (!_plane.IsAlive)
                        failures.Add($"{name}: crashed ({_plane.LastDeathCause}, {_plane.LastTouchdown.Fault}) at {_plane.Position}");
                    else if (!repair.IsRepairing)
                        failures.Add($"{name}: not repairing after {timeout:0} s (state {_brain.State}, at {_plane.Position})");
                    else if (_brain.RetreatLane != lane)
                        failures.Add($"{name}: flew {(_brain.RetreatLane != null ? _brain.RetreatLane.DisplayName : "no lane")}");
                    else if (_brain.GoArounds > goArounds)
                        failures.Add($"{name}: needed {_brain.GoArounds - goArounds} go-around(s), last {_brain.LastGoAround} at {_brain.LastGoAroundAt}; {firstGoAround}");
                }
            }
            finally
            {
                Time.timeScale = 1f;
            }
            Assert.IsEmpty(failures, string.Join("\n", failures));
        }
    }
}

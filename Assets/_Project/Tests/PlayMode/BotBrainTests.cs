using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Dev;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    /// <summary>
    /// <see cref="BotBrain"/>: patrol, chase and attack, missile evasion and terrain avoidance, in the FlightTest scene.
    /// The Fighter drone (team 1) is turned into a bot; runs at 4× speed.
    /// </summary>
    public class BotBrainTests
    {
        BotBrain _brain;
        PlaneController _plane;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            FlightTestScene.Player.gameObject.SetActive(false); // an enemy the bots would go after; the missile test uses it
            _plane = MakeBot("Fighter", new Vector3(0f, 500f, -1500f), Quaternion.identity, out _brain);
            yield return new WaitForFixedUpdate();
            Time.timeScale = 4f;
        }

        [TearDown]
        public void ResetTime() => Time.timeScale = 1f;

        static PlaneController Drone(string className)
        {
            var dummy = FlightTestScene.Dummies.FirstOrDefault(d => d.GetComponent<PlaneController>().Stats.displayName == className);
            Assert.IsNotNull(dummy, $"no {className} drone in the FlightTest scene");
            return dummy.GetComponent<PlaneController>();
        }

        static PlaneController MakeBot(string className, Vector3 position, Quaternion rotation, out BotBrain brain)
        {
            var plane = Drone(className);
            plane.GetComponent<DummyPilot>().enabled = false;
            plane.gameObject.AddComponent<BotPilot>();
            brain = plane.gameObject.AddComponent<BotBrain>();
            plane.gameObject.SetActive(true);
            plane.Respawn(position, rotation);
            return plane;
        }

        /// <summary>Steps physics until <paramref name="done"/> or <paramref name="seconds"/> of game time pass.</summary>
        static IEnumerator Fly(float seconds, Func<bool> done = null)
        {
            for (float t = 0f; t < seconds && (done == null || !done()); t += Time.fixedDeltaTime)
                yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator Patrol_FliesTheRouteInALoop()
        {
            _brain.SetPatrol(new[] { new Vector3(0f, 500f, -500f), new Vector3(800f, 550f, -900f) });
            yield return Fly(40f, () => _brain.Waypoint == 1);
            Assert.AreEqual(1, _brain.Waypoint, "never reached the first point");
            Assert.AreEqual(BotState.Patrol, _brain.State);

            yield return Fly(40f, () => _brain.Waypoint == 0);
            Assert.AreEqual(0, _brain.Waypoint, "didn't loop back to the start");
            Assert.IsTrue(_plane.IsAlive);
        }

        [UnityTest]
        public IEnumerator NoRoute_CirclesWhereItStarted()
        {
            yield return Fly(20f);
            Assert.AreEqual(BotState.Patrol, _brain.State);
            Assert.AreEqual(BotManeuver.Orbit, _brain.Pilot.Maneuver);
            Assert.Less(Vector3.Distance(_brain.Pilot.Destination, new Vector3(0f, 500f, -1500f)), 1f);
        }

        [UnityTest]
        public IEnumerator SeesAnEnemy_ChasesAndShootsIt()
        {
            var enemy = Drone("Heavy Fighter"); // orbiting the middle of the field
            enemy.Team = 0;
            enemy.gameObject.SetActive(true);
            var health = enemy.GetComponent<PlaneHealth>();
            bool hitByBot = false;
            health.Damaged += (_, info) => hitByBot |= PlaneHealth.GetPlane(info.Source) == _plane;

            bool chased = false;
            yield return Fly(60f, () =>
            {
                chased |= _brain.State == BotState.Chase || _brain.State == BotState.Attack;
                return hitByBot;
            });

            Assert.IsTrue(chased, "never went after the enemy");
            Assert.AreEqual(enemy, _brain.Target);
            Assert.IsTrue(hitByBot, "never landed a hit");
            Assert.IsTrue(_plane.IsAlive);
        }

        [UnityTest]
        public IEnumerator ShootsDown_AnOrbitingDrone()
        {
            // The Heavy drone flies a steady orbit and never fights back: a Normal bot should finish it well inside a minute.
            var enemy = Drone("Heavy Fighter");
            enemy.Team = 0;
            enemy.gameObject.SetActive(true);
            var guns = _plane.GetComponent<PlaneGuns>();
            int bullets = 0, hits = 0;
            guns.Fired += _ => bullets++;
            guns.HitPlane += (_, _, _) => hits++;
            var offNose = new float[4];
            float t = 0f;
            yield return Fly(60f, () =>
            {
                t += Time.fixedDeltaTime;
                if (_brain.State == BotState.Attack && guns.TryGetLeadPoint(enemy, out Vector3 lead, out _))
                {
                    float a = Vector3.Angle(_plane.Rotation * Vector3.forward, lead - _plane.Position);
                    offNose[a < 3f ? 0 : a < 10f ? 1 : a < 30f ? 2 : 3] += Time.fixedDeltaTime;
                }
                return !enemy.IsAlive;
            });

            string report = $"[1v1 drone] {(enemy.IsAlive ? "alive" : "shot down")} after {t:F0} s; bullets {bullets}, hits {hits}; " +
                            $"lead off nose <3/<10/<30/30+ deg: {string.Join("/", offNose.Select(x => x.ToString("F1")))} s";
            Debug.Log(report);
            Assert.IsFalse(enemy.IsAlive, report);
            Assert.IsTrue(_plane.IsAlive, report);
        }

        [UnityTest]
        public IEnumerator IgnoresAllies()
        {
            var ally = Drone("Heavy Fighter"); // same team as the bot
            ally.gameObject.SetActive(true);
            yield return Fly(10f);
            Assert.IsNull(_brain.Target);
            Assert.AreEqual(BotState.Patrol, _brain.State);
        }

        [UnityTest]
        public IEnumerator IncomingMissile_BreaksAndShakesItOff()
        {
            // The player's plane, on a missile class, fires one up the bot's tail. This bot always notices it.
            var skill = _brain.Skill;
            skill.missileNoticeChance = 1f;
            _brain.ApplySkill(skill);
            var launcher = FlightTestScene.Player;
            launcher.SetStats(FlightTestScene.FindClass(c => c.missiles != null));
            launcher.gameObject.SetActive(true);
            launcher.Respawn(new Vector3(0f, 500f, -2300f), Quaternion.identity);
            var missiles = launcher.GetComponent<PlaneMissiles>();
            yield return new WaitForFixedUpdate();

            bool evaded = false, hit = false, evading = false;
            missiles.Evaded += (_, target) => evaded |= target == _plane;
            missiles.HitPlane += (_, victim, _) => hit |= victim.Plane == _plane;
            Assert.IsTrue(missiles.LaunchAt(_plane));

            yield return Fly(12f, () =>
            {
                evading |= _brain.State == BotState.Evade;
                return missiles.InFlight == 0;
            });

            Assert.IsTrue(evading, "never broke from the missile");
            Assert.IsFalse(hit, "the missile hit");
            Assert.IsTrue(evaded, "never shook the missile off");
            Assert.IsTrue(_plane.IsAlive);
        }

        [UnityTest]
        public IEnumerator UnnoticedMissile_Hits()
        {
            // Same shot, but the bot misses the warning: it flies on, and the missile gets it.
            var skill = _brain.Skill;
            skill.missileNoticeChance = 0f;
            _brain.ApplySkill(skill);
            var launcher = FlightTestScene.Player;
            launcher.SetStats(FlightTestScene.FindClass(c => c.missiles != null));
            launcher.gameObject.SetActive(true);
            launcher.Respawn(new Vector3(0f, 500f, -2300f), Quaternion.identity);
            var missiles = launcher.GetComponent<PlaneMissiles>();
            yield return new WaitForFixedUpdate();

            bool hit = false, evading = false;
            missiles.HitPlane += (_, victim, _) => hit |= victim.Plane == _plane;
            Assert.IsTrue(missiles.LaunchAt(_plane));
            yield return Fly(12f, () =>
            {
                evading |= _brain.State == BotState.Evade;
                return missiles.InFlight == 0;
            });

            Assert.IsFalse(evading, "broke from a missile it never noticed");
            Assert.IsTrue(hit, "the missile missed a plane flying straight on");
        }

        [UnityTest]
        public IEnumerator HeadOn_TwoBotsMiss()
        {
            // Two allies nose to nose, both ordered through the same point: neither should ram the other.
            var other = MakeBot("Interceptor", new Vector3(0f, 500f, 0f), Quaternion.Euler(0f, 180f, 0f), out var otherBrain);
            _brain.SetPatrol(new[] { new Vector3(0f, 500f, 1500f) });
            otherBrain.SetPatrol(new[] { new Vector3(0f, 500f, -1500f) });

            bool sidestepped = false;
            yield return Fly(25f, () =>
            {
                sidestepped |= _brain.State == BotState.AvoidCollision || otherBrain.State == BotState.AvoidCollision;
                return !_plane.IsAlive || !other.IsAlive;
            });

            Assert.IsTrue(_plane.IsAlive && other.IsAlive, "collided head-on");
            Assert.IsTrue(sidestepped, "never needed to sidestep: the test isn't testing it");
        }

        [UnityTest]
        public IEnumerator DivingAtTheGround_PullsUp()
        {
            // Low, nose down, ordered to a point on the deck.
            _plane.Respawn(new Vector3(0f, 160f, -1800f), Quaternion.Euler(30f, 0f, 0f));
            _brain.SetPatrol(new[] { new Vector3(0f, 5f, -1000f) });

            bool avoided = false;
            yield return Fly(25f, () =>
            {
                avoided |= _brain.State == BotState.AvoidTerrain;
                return !_plane.IsAlive;
            });

            Assert.IsTrue(_plane.IsAlive, "flew into the ground");
            Assert.IsTrue(avoided, "never pulled up");
        }

        [UnityTest]
        public IEnumerator LowThroughDowntown_MissesTheTowers()
        {
            // Downtown: ~30 towers 80–260 m tall around (450, 350). Fly straight through it at 110 m.
            _plane.Respawn(new Vector3(450f, 110f, -500f), Quaternion.identity);
            _brain.SetPatrol(new[] { new Vector3(450f, 110f, 2000f) });

            bool avoided = false;
            Vector3 crash = default;
            string last = "";
            _plane.Crashed += (_, point) => crash = point;
            yield return Fly(40f, () =>
            {
                avoided |= _brain.State == BotState.AvoidTerrain;
                if (_plane.IsAlive)
                    last = $"{_brain.State} at {_plane.Position}, vel {_plane.Velocity}, obstacle {_brain.Obstacle}";
                return _plane.Position.z > 900f || !_plane.IsAlive;
            });

            Assert.IsTrue(_plane.IsAlive, $"hit something at {crash}; last step: {last}");
            Assert.IsTrue(avoided, "never had to dodge anything: the test isn't testing avoidance");
            Assert.Greater(_plane.Position.z, 900f, $"didn't get through; last step: {last}");
        }

        [UnityTest]
        public IEnumerator TwoBots_Dogfight_WithoutFlyingIntoTheGround()
        {
            var red = MakeBot("Interceptor", new Vector3(0f, 450f, 800f), Quaternion.Euler(0f, 180f, 0f), out var redBrain);
            red.Team = 0;
            bool anyHit = false;
            string collided = null;
            foreach (var p in new[] { _plane, red })
            {
                p.Crashed += (plane, point) =>
                {
                    if (plane.LastDeathCause == DeathCause.Collision)
                        collided ??= $"{plane.name} at {point}";
                };
                p.GetComponent<PlaneHealth>().Damaged += (h, info) =>
                    anyHit |= PlaneHealth.GetPlane(info.Source) is { } shooter && shooter != h.Plane;
            }

            yield return Fly(90f);

            Assert.IsTrue(anyHit, "90 s and nobody landed a hit");
            Assert.IsNull(collided, "a bot flew into something");
        }

        // ------------------------------------------------------------------ difficulty

        /// <summary>An enemy (team 0) <paramref name="distance"/> m straight ahead of the bot, flying away from it.</summary>
        PlaneController EnemyAhead(string className, float distance)
        {
            var enemy = Drone(className);
            enemy.Team = 0;
            enemy.gameObject.SetActive(true);
            Vector3 forward = _plane.Rotation * Vector3.forward;
            enemy.GetComponent<DummyPilot>().StartTowRun(_plane.Position + forward * distance, forward, 60f);
            return enemy;
        }

        [UnityTest]
        public IEnumerator Difficulty_SetsTheSkillAndThePilotsHand()
        {
            _brain.Difficulty = BotDifficulty.Hard;
            var hard = BotSkill.For(BotDifficulty.Hard);
            Assert.AreEqual(hard.detectionRange, _brain.Skill.detectionRange);
            Assert.AreEqual(hard.steerSensitivity, _brain.Pilot.SteerSensitivity);
            Assert.AreEqual(hard.stickSlewRate, _brain.Pilot.StickSlewRate);
            Assert.AreEqual(hard.aimError, _brain.Pilot.AimError);
            yield break;
        }

        [UnityTest]
        public IEnumerator HardBot_SpotsFartherThanEasy()
        {
            _brain.Difficulty = BotDifficulty.Easy;
            var enemy = EnemyAhead("Heavy Fighter", 1700f);
            yield return Fly(0.5f);
            Assert.IsNull(_brain.Target, "Easy noticed an enemy 1.7 km away");

            _brain.Difficulty = BotDifficulty.Hard;
            yield return Fly(0.5f);
            Assert.AreEqual(enemy, _brain.Target, "Hard didn't notice an enemy 1.7 km away");
        }

        [UnityTest]
        public IEnumerator EasyBot_TakesAMomentToReact()
        {
            _brain.Difficulty = BotDifficulty.Easy;
            var enemy = EnemyAhead("Heavy Fighter", 900f);
            yield return Fly(0.3f);
            Assert.AreEqual(enemy, _brain.Target, "never noticed it");
            Assert.IsFalse(_brain.IsEngaged);
            Assert.AreEqual(BotState.Patrol, _brain.State, "went after it before reacting");

            yield return Fly(BotSkill.For(BotDifficulty.Easy).reactionTime);
            Assert.IsTrue(_brain.State == BotState.Chase || _brain.State == BotState.Attack, $"still {_brain.State}");
        }

        [UnityTest]
        public IEnumerator Interceptor_LightsTheAfterburner_OnALongChase()
        {
            var plane = MakeBot("Interceptor", new Vector3(-600f, 500f, -1500f), Quaternion.identity, out var brain);
            brain.Difficulty = BotDifficulty.Hard;
            var enemy = Drone("Heavy Fighter");
            enemy.Team = 0;
            enemy.gameObject.SetActive(true);
            enemy.GetComponent<DummyPilot>().StartTowRun(plane.Position + Vector3.forward * 2000f, Vector3.forward, 60f);
            var ability = plane.GetComponent<PlaneAbility>();

            yield return Fly(5f, () => ability.IsActive);
            Assert.IsTrue(ability.IsActive, $"no afterburner; state {brain.State}");
            Assert.AreEqual(BotState.Chase, brain.State);
        }

        [UnityTest]
        public IEnumerator EasyBot_NeverUsesItsAbility()
        {
            var plane = MakeBot("Interceptor", new Vector3(-600f, 500f, -1500f), Quaternion.identity, out var brain);
            brain.Difficulty = BotDifficulty.Easy;
            var enemy = Drone("Heavy Fighter");
            enemy.Team = 0;
            enemy.gameObject.SetActive(true);
            enemy.GetComponent<DummyPilot>().StartTowRun(plane.Position + Vector3.forward * 1250f, Vector3.forward, 60f);
            var ability = plane.GetComponent<PlaneAbility>();

            bool chased = false;
            yield return Fly(6f, () =>
            {
                chased |= brain.State == BotState.Chase;
                return ability.IsActive;
            });
            Assert.IsTrue(chased, "never chased: the test isn't testing it");
            Assert.IsFalse(ability.IsActive);
        }

        [UnityTest]
        public IEnumerator Heavy_RaisesItsShield_WhenHit()
        {
            var plane = MakeBot("Heavy Fighter", new Vector3(-600f, 500f, -1500f), Quaternion.identity, out _);
            var enemy = Drone("Support");
            enemy.Team = 0;
            enemy.gameObject.SetActive(true);
            enemy.GetComponent<DummyPilot>().StartTowRun(plane.Position + Vector3.forward * 800f, Vector3.forward, 60f);
            var ability = plane.GetComponent<PlaneAbility>();
            yield return Fly(0.5f);
            Assert.IsFalse(ability.IsActive, "raised it before being hit");

            plane.GetComponent<PlaneHealth>().ApplyDamage(PlanePartType.Body, 5f, plane.Position, enemy);
            yield return Fly(0.3f, () => ability.IsActive);
            Assert.IsTrue(ability.IsActive, "didn't raise the shield");
        }

        [UnityTest]
        public IEnumerator Support_SwitchesOnTheAura_WhenHurt()
        {
            var plane = MakeBot("Support", new Vector3(-600f, 500f, -1500f), Quaternion.identity, out _);
            var health = plane.GetComponent<PlaneHealth>();
            var ability = plane.GetComponent<PlaneAbility>();
            yield return Fly(0.5f);
            Assert.IsFalse(ability.IsActive, "used it at full health");

            var body = health.GetPart(PlanePartType.Body);
            health.ApplyDamage(PlanePartType.Body, body.MaxHealth * 0.4f, plane.Position);
            yield return Fly(0.3f, () => ability.IsActive);
            Assert.IsTrue(ability.IsActive, "didn't heal itself");
        }

        [UnityTest]
        public IEnumerator HardBot_OutfightsEasyBot()
        {
            // Two 90 s fights between the same two classes with the difficulty swapped, so the class matchup cancels out:
            // the Hard side must deal more damage both times. They start 1.2 km apart, head-on: inside even Easy's sight.
            var red = MakeBot("Interceptor", new Vector3(0f, 450f, -300f), Quaternion.Euler(0f, 180f, 0f), out var redBrain);
            red.Team = 0;
            float blueDealt = 0f, redDealt = 0f;
            foreach (var p in new[] { _plane, red })
                p.GetComponent<PlaneHealth>().Damaged += (h, info) =>
                {
                    var shooter = PlaneHealth.GetPlane(info.Source);
                    if (shooter == _plane && h.Plane == red) blueDealt += info.Amount;
                    if (shooter == red && h.Plane == _plane) redDealt += info.Amount;
                };

            string report = "";
            foreach (bool blueHard in new[] { true, false })
            {
                _plane.Respawn(new Vector3(0f, 500f, -1500f), Quaternion.identity);
                red.Respawn(new Vector3(0f, 450f, -300f), Quaternion.Euler(0f, 180f, 0f));
                _brain.Difficulty = blueHard ? BotDifficulty.Hard : BotDifficulty.Easy;
                redBrain.Difficulty = blueHard ? BotDifficulty.Easy : BotDifficulty.Hard;
                blueDealt = redDealt = 0f;
                yield return Fly(90f);

                float hardDealt = blueHard ? blueDealt : redDealt, easyDealt = blueHard ? redDealt : blueDealt;
                report += $"Hard {(blueHard ? "Fighter" : "Interceptor")} dealt {hardDealt:0}, Easy dealt {easyDealt:0}. ";
                Assert.Greater(hardDealt, easyDealt, report);
            }
            Debug.Log(report);
        }
    }
}

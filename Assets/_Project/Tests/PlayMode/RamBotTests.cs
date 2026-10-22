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
    /// <see cref="BotBrain"/> with ram damage on (Ram Royale), in the FlightTest scene: a drone turned into a bot rams
    /// the player plane (team 0, flown straight by the test) and breaks from a head-on. Runs at 4× speed.
    /// </summary>
    public class RamBotTests
    {
        PlaneController _target;
        RamInfo? _ram;

        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return FlightTestScene.Load();
            _target = FlightTestScene.Player;
            _ram = null;
            PlaneController.AnyRammed += OnRammed;
        }

        [TearDown]
        public void TearDown()
        {
            PlaneController.AnyRammed -= OnRammed;
            Time.timeScale = 1f;
        }

        void OnRammed(RamInfo info) => _ram ??= info;

        static PlaneController MakeBot(string className, Vector3 position, Quaternion rotation, out BotBrain brain)
        {
            var dummy = FlightTestScene.Dummies.FirstOrDefault(d => d.GetComponent<PlaneController>().Stats.displayName == className);
            Assert.IsNotNull(dummy, $"no {className} drone in the FlightTest scene");
            var plane = dummy.GetComponent<PlaneController>();
            dummy.enabled = false;
            plane.gameObject.AddComponent<BotPilot>();
            brain = plane.gameObject.AddComponent<BotBrain>();
            plane.gameObject.SetActive(true);
            plane.Respawn(position, rotation);
            plane.RamDamage = true; // respawning clears it
            return plane;
        }

        /// <summary>The player plane flying straight from <paramref name="position"/>, ram damage on.</summary>
        void PlaceTarget(Vector3 position, Quaternion rotation, float throttle)
        {
            _target.Respawn(position, rotation, throttle);
            _target.RamDamage = true;
            _target.SetInput(default);
        }

        static IEnumerator Fly(float seconds, Func<bool> done = null)
        {
            for (float t = 0f; t < seconds && (done == null || !done()); t += Time.fixedDeltaTime)
                yield return new WaitForFixedUpdate();
        }

        [UnityTest]
        public IEnumerator RamsAPlaneAhead_FromBehind_BoostingAtTheEnd()
        {
            var bot = MakeBot("Fighter", new Vector3(0f, 500f, -700f), Quaternion.identity, out var brain);
            PlaceTarget(new Vector3(150f, 500f, 0f), Quaternion.identity, 0.1f); // slow, a little off to the side
            Time.timeScale = 4f;

            float lastBoost = -10f;
            bool attacked = false;
            yield return Fly(40f, () =>
            {
                if (brain.Pilot.Boost)
                    lastBoost = Time.fixedTime;
                attacked |= brain.State == BotState.Attack && brain.Pilot.Maneuver == BotManeuver.Ram;
                return _ram != null;
            });

            Assert.IsTrue(_ram.HasValue, $"never rammed it (state {brain.State})");
            Assert.IsTrue(attacked, "flew the Ram order");
            Assert.AreEqual(bot, _ram.Value.Hitter, "the bot did the ramming");
            // A wing or tail hit can take off the part with less than the full hit (the rest is lost with it).
            Assert.IsTrue(_ram.Value.VictimDamage > _ram.Value.HitterDamage || _target.GetComponent<PlaneHealth>().LostPartsMask != 0,
                $"from behind, the plane rammed takes the worst of it ({_ram.Value.VictimDamage:0} vs {_ram.Value.HitterDamage:0})");
            Assert.IsTrue(bot.IsAlive, "the rammer flies on");
            Assert.Less(Time.fixedTime - lastBoost, 0.5f, "boosted into the ram");
        }

        [UnityTest]
        public IEnumerator SupportFiresItsRamBoost_JustBeforeTheRam()
        {
            var bot = MakeBot("Support", new Vector3(0f, 500f, -700f), Quaternion.identity, out _);
            var ability = bot.GetComponent<PlaneAbility>();
            ability.Override = ScriptableObject.CreateInstance<RamBoostAbility>();
            PlaceTarget(new Vector3(150f, 500f, 0f), Quaternion.identity, 0.1f);
            Time.timeScale = 4f;

            bool chargingAtRam = false;
            void OnRam(RamInfo info) => chargingAtRam |= info.Hitter == bot && ability.ActiveStats is RamBoostAbility;
            PlaneController.AnyRammed += OnRam;
            yield return Fly(40f, () => _ram != null);
            PlaneController.AnyRammed -= OnRam;

            Assert.IsTrue(_ram.HasValue, "never rammed it");
            Assert.IsTrue(chargingAtRam, "the Ram Boost was running when it hit");
        }

        [UnityTest]
        public IEnumerator HeadOn_BreaksAway_InsteadOfDyingWithIt()
        {
            var bot = MakeBot("Fighter", new Vector3(0f, 500f, -1200f), Quaternion.identity, out var brain);
            var skill = BotSkill.For(BotDifficulty.Normal);
            skill.ramDodgeChance = 1f;
            brain.ApplySkill(skill);
            // Nose to nose at cruise: meeting head-on would kill both, so it's not a ram worth taking.
            PlaceTarget(new Vector3(0f, 500f, 400f), Quaternion.Euler(0f, 180f, 0f), 0.6f);
            Time.timeScale = 4f;

            bool broke = false;
            yield return Fly(12f, () =>
            {
                broke |= brain.State == BotState.AvoidCollision;
                return !bot.IsAlive;
            });

            Assert.IsTrue(broke, "never broke away");
            Assert.IsTrue(bot.IsAlive, _ram.HasValue
                ? $"died in a ram: took {(_ram.Value.Hitter == bot ? _ram.Value.HitterDamage : _ram.Value.VictimDamage):0}"
                : "died");
        }
    }
}

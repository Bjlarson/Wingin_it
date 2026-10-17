using NUnit.Framework;
using UnityEngine;
using WingingIt.Bots;

namespace WingingIt.Tests
{
    public class BotDifficultyTests
    {
        static readonly BotSkill Easy = BotSkill.For(BotDifficulty.Easy);
        static readonly BotSkill Normal = BotSkill.For(BotDifficulty.Normal);
        static readonly BotSkill Hard = BotSkill.For(BotDifficulty.Hard);

        /// <summary>Asserts a &lt; b &lt; c (or a &gt; b &gt; c when <paramref name="higherIsHarder"/> is false).</summary>
        static void Ordered(string knob, float easy, float normal, float hard, bool higherIsHarder)
        {
            if (higherIsHarder)
            {
                Assert.Less(easy, normal, $"{knob}: Easy should be below Normal");
                Assert.Less(normal, hard, $"{knob}: Normal should be below Hard");
            }
            else
            {
                Assert.Greater(easy, normal, $"{knob}: Easy should be above Normal");
                Assert.Greater(normal, hard, $"{knob}: Normal should be above Hard");
            }
        }

        [Test]
        public void EveryKnob_GetsHarder_FromEasyToHard()
        {
            Ordered("detection range", Easy.detectionRange, Normal.detectionRange, Hard.detectionRange, true);
            Ordered("reaction time", Easy.reactionTime, Normal.reactionTime, Hard.reactionTime, false);
            Ordered("retarget interval", Easy.retargetInterval, Normal.retargetInterval, Hard.retargetInterval, false);
            Ordered("aim error", Easy.aimError, Normal.aimError, Hard.aimError, false);
            Ordered("gun aim tolerance (spray)", Easy.gunAimTolerance, Normal.gunAimTolerance, Hard.gunAimTolerance, false);
            Ordered("gun miss distance (spray)", Easy.gunMissDistance, Normal.gunMissDistance, Hard.gunMissDistance, false);
            Ordered("gun fire range (long shots)", Easy.gunFireRange, Normal.gunFireRange, Hard.gunFireRange, false);
            Ordered("rocket aim tolerance", Easy.rocketAimTolerance, Normal.rocketAimTolerance, Hard.rocketAimTolerance, false);
            Ordered("missile fire delay", Easy.missileFireDelay, Normal.missileFireDelay, Hard.missileFireDelay, false);
            Ordered("missile notice chance", Easy.missileNoticeChance, Normal.missileNoticeChance, Hard.missileNoticeChance, true);
            Ordered("evade start distance", Easy.evadeStartDistance, Normal.evadeStartDistance, Hard.evadeStartDistance, true);
            Ordered("evade reaction", Easy.evadeReactionTime, Normal.evadeReactionTime, Hard.evadeReactionTime, false);
            Ordered("retreat health", Easy.retreatHealth, Normal.retreatHealth, Hard.retreatHealth, true);
            Ordered("stick gain", Easy.steerSensitivity, Normal.steerSensitivity, Hard.steerSensitivity, true);
            Ordered("stick slew", Easy.stickSlewRate, Normal.stickSlewRate, Hard.stickSlewRate, true);
            Assert.IsFalse(Easy.useAbilities);
            Assert.IsTrue(Normal.useAbilities && Hard.useAbilities);
        }

        [Test]
        public void Normal_KeepsTheTuningTheBehaviorsWereBuiltAt()
        {
            Assert.AreEqual(1800f, Normal.detectionRange);
            Assert.AreEqual(3f, Normal.gunAimTolerance);
            Assert.AreEqual(2f, Normal.rocketAimTolerance);
            Assert.AreEqual(550f, Normal.evadeStartDistance);
            Assert.AreEqual(0.2f, Normal.evadeReactionTime);
            Assert.AreEqual(0.4f, Normal.retreatHealth);
            Assert.AreEqual(3f, Normal.steerSensitivity);
            Assert.AreEqual(5f, Normal.stickSlewRate);
        }

        [Test]
        public void EveryLevel_SpotsWithinDisengageRange()
        {
            // BotBrain drops targets beyond 2.4 km: a bot must never notice something it would drop at once.
            foreach (BotDifficulty d in System.Enum.GetValues(typeof(BotDifficulty)))
                Assert.Less(BotSkill.For(d).detectionRange, 2400f, d.ToString());
        }

        [Test]
        public void EasyBots_StillShakeAMissileOffSometimes()
        {
            // A ~250 m/s missile from the evade start distance: Easy must still have time to react and turn.
            float timeToImpact = Easy.evadeStartDistance / 250f;
            Assert.Greater(timeToImpact - Easy.evadeReactionTime, 0.8f);
        }

        // ------------------------------------------------------------------ aim error

        [Test]
        public void OffsetAim_TurnsByTheGivenAngles_AtTheSameDistance()
        {
            Vector3 pos = new(10f, 100f, 0f), aim = pos + new Vector3(0f, 0f, 400f);
            Vector3 yawed = BotSteering.OffsetAim(pos, aim, Vector3.up, 3f, 0f);
            Assert.AreEqual(3f, Vector3.Angle(aim - pos, yawed - pos), 1e-3f);
            Assert.Greater(yawed.x, pos.x, "positive yaw goes right");
            Assert.AreEqual(400f, Vector3.Distance(pos, yawed), 1e-2f);

            Vector3 pitched = BotSteering.OffsetAim(pos, aim, Vector3.up, 0f, 2f);
            Assert.AreEqual(2f, Vector3.Angle(aim - pos, pitched - pos), 1e-3f);
            Assert.Greater(pitched.y, pos.y, "positive pitch goes up");
        }

        [Test]
        public void OffsetAim_ZeroError_IsTheAimPoint()
        {
            Vector3 pos = new(0f, 300f, 0f), aim = new(120f, 340f, 500f);
            Assert.Less(Vector3.Distance(aim, BotSteering.OffsetAim(pos, aim, Vector3.up, 0f, 0f)), 1e-3f);
        }

        [Test]
        public void AimWander_StaysInRange_MovesSmoothly_AndDiffersPerBot()
        {
            float min = 1f, max = -1f;
            Vector2 last = BotSteering.AimWander(5f, 0f);
            for (float t = 0.02f; t < 60f; t += 0.02f)
            {
                Vector2 w = BotSteering.AimWander(5f, t);
                Assert.LessOrEqual(Mathf.Abs(w.x), 1f);
                Assert.LessOrEqual(Mathf.Abs(w.y), 1f);
                Assert.Less((w - last).magnitude, 0.1f, $"jumped at t={t}");
                min = Mathf.Min(min, w.x);
                max = Mathf.Max(max, w.x);
                last = w;
            }
            Assert.Greater(max - min, 0.8f, "barely wanders");
            Assert.AreNotEqual(BotSteering.AimWander(5f, 10f), BotSteering.AimWander(400f, 10f));
        }
    }
}

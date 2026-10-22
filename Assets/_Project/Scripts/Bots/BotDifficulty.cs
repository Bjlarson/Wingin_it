using System;
using UnityEngine;

namespace WingingIt.Bots
{
    /// <summary>How good a bot is. Picks a <see cref="BotSkill"/> preset (see <see cref="BotSkill.For"/>).</summary>
    public enum BotDifficulty
    {
        /// <summary>Slow to notice and react, sprays wide, never uses its ability, stays in a losing fight.</summary>
        Easy,
        /// <summary>The baseline the behaviors were tuned at.</summary>
        Normal,
        /// <summary>Sees farther, reacts sooner, aims tighter, goes home before it's in real trouble.</summary>
        Hard,
    }

    /// <summary>
    /// The difficulty knobs of a bot: what it notices, how fast it reacts, how well it aims and how hard it works the
    /// stick. The physics never changes: a hard bot flies the same plane as an easy one, just better.
    /// Terrain and collision avoidance aren't knobs: every bot keeps those, since a bot flying into a hill isn't a
    /// fair kill for anyone.
    /// </summary>
    [Serializable]
    public struct BotSkill
    {
        [Tooltip("Enemies farther than this (m) aren't noticed.")]
        public float detectionRange;
        [Tooltip("Seconds between first noticing an enemy and going after it.")]
        public float reactionTime;
        [Tooltip("Seconds between looks around for a better target.")]
        public float retargetInterval;
        [Tooltip("How far (deg) the aim wanders off the gun lead point while tracking.")]
        public float aimError;
        [Tooltip("Guns fire when the lead point is within this angle (deg) of the nose. Wider = more spraying.")]
        public float gunAimTolerance;
        [Tooltip("...and the bullets would pass within this many meters of the lead point. Tighter = fewer wasted rounds.")]
        public float gunMissDistance;
        [Tooltip("Guns only fire inside this range (m). The wing guns converge at ~350 m; long shots straddle the body.")]
        public float gunFireRange;
        [Tooltip("Rockets fire when their lead point is within this angle (deg) of the nose.")]
        public float rocketAimTolerance;
        [Tooltip("Seconds to hold a missile lock before firing.")]
        public float missileFireDelay;
        [Tooltip("Chance (0..1) to notice a missile chasing us at all, like a player who misses the lock warning. " +
                 "One roll per missile threat; unnoticed, it flies straight on.")]
        [Range(0f, 1f)] public float missileNoticeChance;
        [Tooltip("Start breaking when the nearest missile chasing us is this close (m).")]
        public float evadeStartDistance;
        [Tooltip("Seconds between a missile coming into range and the bot reacting.")]
        public float evadeReactionTime;
        [Tooltip("Ram Royale: chance (0..1) to break away from a plane lining up to ram us (when the hit would hurt us " +
                 "more than it). One roll per plane coming at us; the reaction delay is the missile one.")]
        [Range(0f, 1f)] public float ramDodgeChance;
        [Tooltip("Uses its class ability (Barrel Roll, Afterburner, Armor Shield, Repair Aura).")]
        public bool useAbilities;
        [Tooltip("Go home to repair when the body drops below this fraction (0..1). Losing any part always sends it home.")]
        [Range(0f, 1f)] public float retreatHealth;
        [Tooltip("Autopilot stick gain; higher = sharper corrections.")]
        public float steerSensitivity;
        [Tooltip("How fast the stick can move (full deflection per second).")]
        public float stickSlewRate;

        public static BotSkill For(BotDifficulty difficulty) => difficulty switch
        {
            BotDifficulty.Easy => new BotSkill
            {
                detectionRange = 1300f,
                reactionTime = 1.2f,
                retargetInterval = 2f,
                aimError = 3.5f,
                gunAimTolerance = 6f,
                gunMissDistance = 14f,
                gunFireRange = 800f,
                rocketAimTolerance = 4f,
                missileFireDelay = 1.2f,
                missileNoticeChance = 0.45f,
                evadeStartDistance = 400f,
                evadeReactionTime = 0.6f,
                ramDodgeChance = 0.4f,
                useAbilities = false,
                retreatHealth = 0.25f,
                steerSensitivity = 2.2f,
                stickSlewRate = 2.5f,
            },
            BotDifficulty.Hard => new BotSkill
            {
                detectionRange = 2200f,
                reactionTime = 0.2f,
                retargetInterval = 0.5f,
                aimError = 0.4f,
                gunAimTolerance = 2.5f,
                gunMissDistance = 4f,
                gunFireRange = 450f,
                rocketAimTolerance = 1.5f,
                missileFireDelay = 0f,
                missileNoticeChance = 0.85f,
                evadeStartDistance = 700f,
                evadeReactionTime = 0.1f,
                ramDodgeChance = 0.9f,
                useAbilities = true,
                retreatHealth = 0.5f,
                steerSensitivity = 3.6f,
                stickSlewRate = 8f,
            },
            _ => new BotSkill
            {
                detectionRange = 1800f,
                reactionTime = 0.5f,
                retargetInterval = 1f,
                aimError = 1.2f,
                gunAimTolerance = 3f,
                gunMissDistance = 7f,
                gunFireRange = 550f,
                rocketAimTolerance = 2f,
                missileFireDelay = 0.4f,
                missileNoticeChance = 0.65f,
                evadeStartDistance = 550f,
                evadeReactionTime = 0.2f,
                ramDodgeChance = 0.7f,
                useAbilities = true,
                retreatHealth = 0.4f,
                steerSensitivity = 3f,
                stickSlewRate = 5f,
            },
        };
    }
}

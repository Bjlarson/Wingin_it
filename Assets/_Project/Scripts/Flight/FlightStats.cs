using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Every flight value that can be changed at runtime by a <see cref="StatModifierStack"/>
    /// (part damage, damage states, abilities). Values not listed here are fixed per class.
    /// </summary>
    public enum FlightStat
    {
        MinSpeed,
        MaxSpeed,
        Acceleration,
        PitchRate,
        RollRate,
        YawRate,
        BankTurnRate,
        Responsiveness,
        BoostMultiplier,
        Grip,

        // Multipliers with a base of 1.
        Thrust,          // 0 = dead engine, glide only
        Lift,            // < 1 = sinks and noses down, like flying too slowly
        Climb,           // scales upward vertical speed
        RollStability,   // scales roll auto-leveling (lost wing / fin weakens it)
        PitchStability,  // scales pitch auto-leveling (lost horizontal stabilizer turns it off)

        // Additive effects with a base of 0.
        RollBias,        // deg/s of constant roll (lost wing); + = rolls right
        PitchWobble,     // deg/s amplitude of random pitch shake (lost horizontal stabilizer)
        YawWobble,       // deg/s amplitude of random yaw shake (lost vertical stabilizer)
        EngineSputter,   // 0..1 thrust lost in each random engine cough (Critical body)
    }

    /// <summary>
    /// Base flight tuning for one aircraft class, plus links to its part durability and its loadout: the weapons
    /// it carries, each linked to its tuning asset. An empty weapon slot means the class doesn't carry it.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Flight Stats", fileName = "FlightStats")]
    public class FlightStats : ScriptableObject
    {
        public const int StatCount = (int)FlightStat.EngineSputter + 1;

        [Header("Identity")]
        public string displayName = "Fighter";
        public Color bodyColor = new Color(0.85f, 0.85f, 0.85f);
        [Tooltip("Uniform scale of the placeholder model, for silhouette differences.")]
        public float visualScale = 1f;
        [Tooltip("Max HP of each part for this class.")]
        public PlaneDurability durability;

        [Header("Loadout")]
        [Tooltip("Machine gun tuning for this class. Every class carries guns.")]
        public GunStats guns;
        [Tooltip("Rocket tuning for this class. Empty = the class carries no rockets.")]
        public RocketStats rockets;
        [Tooltip("Homing missile tuning for this class. Empty = the class carries no missiles.")]
        public MissileStats missiles;
        [Tooltip("Bomb tuning for this class. Empty = the class carries no bombs.")]
        public BombStats bombs;

        [Header("Ability")]
        [Tooltip("The class's special ability. Empty = none.")]
        public AbilityStats ability;

        [Header("Speed (m/s)")]
        [Tooltip("Speed at the lowest throttle that still flies; below it the engine eases to idle (see ThrottleCurve).")]
        public float minSpeed = 40f;
        [Tooltip("Speed at full throttle, without boost.")]
        public float maxSpeed = 110f;
        [Range(0f, 1f)] public float cruiseThrottle = 0.65f;
        [Tooltip("Throttle change per second while W/S is held.")]
        public float throttleChangeRate = 0.6f;
        public float acceleration = 22f;
        public float deceleration = 14f;

        [Header("Rotation (deg/s)")]
        public float pitchRate = 85f;
        public float rollRate = 190f;
        public float yawRate = 35f;
        [Tooltip("Yaw rate (deg/s) produced by a 90° bank. Makes banking turn the plane.")]
        public float bankTurnRate = 50f;
        [Tooltip("How fast rotation rates follow the stick (1/s). Lower = more momentum.")]
        public float responsiveness = 6f;

        [Header("Stability")]
        [Tooltip("Roll back to wings-level when roll input is released (1/s).")]
        public float autoLevelRoll = 2.2f;
        [Tooltip("Pitch back toward the horizon when pitch input is released (1/s).")]
        public float autoLevelPitch = 0.5f;

        [Header("Momentum")]
        [Tooltip("Speed lost per second when climbing straight up (gained when diving).")]
        public float gravityInfluence = 14f;
        [Tooltip("How fast the velocity lines up with the nose (1/s). Lower = more drift.")]
        public float grip = 4f;

        [Header("Low speed")]
        [Tooltip("Below this speed the plane loses lift and gently noses down.")]
        public float stallSpeed = 32f;
        public float noseDropRate = 30f;
        [Tooltip("Idle speed as a fraction of the stall speed: throttle held all the way down settles just below the stall.")]
        [Range(0.5f, 1f)] public float idleSpeedFactor = 0.92f;
        [Tooltip("Pull (m/s²) of the idling engine below idle speed: keeps it turning, not enough to fly on.")]
        public float idleAcceleration = 3f;
        [Tooltip("Speed lost per second at idle in level flight. Lower = a longer glide.")]
        public float idleDeceleration = 3f;
        [Tooltip("Speed lost per second while pulling the nose up hard near the stall (a flare).")]
        public float flareDrag = 10f;

        /// <summary>The speed an idling engine pulls toward, just under the stall.</summary>
        public float IdleSpeed => stallSpeed * idleSpeedFactor;

        [Header("Damage")]
        [Tooltip("Speed lost per second in level flight with a dead engine. Diving gently holds speed (a glide).")]
        public float glideDeceleration = 3f;
        [Tooltip("Sink speed (m/s) with all lift lost to damage. Lost wings sink the plane toward a fraction of this.")]
        public float damageSinkSpeed = 25f;

        [Header("Boost")]
        public float boostMultiplier = 1.5f;
        public float boostAcceleration = 45f;
        [Tooltip("Seconds of boost from a full meter.")]
        public float boostDuration = 3f;
        [Tooltip("Seconds to refill an empty meter.")]
        public float boostRechargeTime = 6f;
        [Tooltip("Seconds after boosting before the meter starts refilling.")]
        public float boostRechargeDelay = 1f;

        [Header("Ram")]
        [Tooltip("How heavy the plane is in a ram (Ram Royale, see RamRules): heavier deals more damage and takes less. 1 = Fighter.")]
        [Min(0.1f)] public float ramMass = 1f;

        public float GetBase(FlightStat stat) => stat switch
        {
            FlightStat.MinSpeed => minSpeed,
            FlightStat.MaxSpeed => maxSpeed,
            FlightStat.Acceleration => acceleration,
            FlightStat.PitchRate => pitchRate,
            FlightStat.RollRate => rollRate,
            FlightStat.YawRate => yawRate,
            FlightStat.BankTurnRate => bankTurnRate,
            FlightStat.Responsiveness => responsiveness,
            FlightStat.BoostMultiplier => boostMultiplier,
            FlightStat.Grip => grip,
            FlightStat.Thrust => 1f,
            FlightStat.Lift => 1f,
            FlightStat.Climb => 1f,
            FlightStat.RollStability => 1f,
            FlightStat.PitchStability => 1f,
            FlightStat.RollBias => 0f,
            FlightStat.PitchWobble => 0f,
            FlightStat.YawWobble => 0f,
            FlightStat.EngineSputter => 0f,
            _ => 0f,
        };
    }
}

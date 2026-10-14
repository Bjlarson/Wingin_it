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
        AutoLevel,       // scales self-stabilization (lost wing / tail weakens it)

        // Additive effects with a base of 0.
        RollBias,        // deg/s of constant roll (lost wing); + = rolls right
        Wobble,          // deg/s amplitude of random pitch/yaw shake (lost tail)
    }

    /// <summary>Base flight tuning for one aircraft class.</summary>
    [CreateAssetMenu(menuName = "Winging It/Flight Stats", fileName = "FlightStats")]
    public class FlightStats : ScriptableObject
    {
        public const int StatCount = (int)FlightStat.Wobble + 1;

        [Header("Identity")]
        public string displayName = "Fighter";
        public Color bodyColor = new Color(0.85f, 0.85f, 0.85f);
        [Tooltip("Uniform scale of the placeholder model, for silhouette differences.")]
        public float visualScale = 1f;

        [Header("Speed (m/s)")]
        [Tooltip("Speed at zero throttle.")]
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

        [Header("Boost")]
        public float boostMultiplier = 1.5f;
        public float boostAcceleration = 45f;
        [Tooltip("Seconds of boost from a full meter.")]
        public float boostDuration = 3f;
        [Tooltip("Seconds to refill an empty meter.")]
        public float boostRechargeTime = 6f;
        [Tooltip("Seconds after boosting before the meter starts refilling.")]
        public float boostRechargeDelay = 1f;

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
            FlightStat.AutoLevel => 1f,
            FlightStat.RollBias => 0f,
            FlightStat.Wobble => 0f,
            _ => 0f,
        };
    }
}

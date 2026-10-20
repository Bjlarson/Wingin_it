using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>Why a plane was destroyed.</summary>
    public enum DeathCause
    {
        /// <summary>Hit terrain, a building, or another solid object.</summary>
        Collision,
        /// <summary>Body HP reached 0: explodes in mid-air.</summary>
        ShotDown,
        /// <summary>Stayed past a hard arena edge too long (<see cref="WingingIt.World.FlightBoundary.hardEdge"/>).</summary>
        OutOfBounds,
    }

    /// <summary>
    /// Arcade, velocity-driven flight (not an aerodynamics sim). Rotation comes from input × class
    /// rates, velocity follows the nose with a little drift, and gravity only nudges speed.
    /// The whole simulation runs from <see cref="FlightInput"/> in FixedUpdate, so it is
    /// deterministic from inputs (good for bots now and server authority later).
    /// With locked <see cref="LandingGear"/>, reaching flat ground is a touchdown judged by <see cref="LandingRules"/>:
    /// a clean one puts the plane on its wheels (<see cref="IsGrounded"/>), where it rolls, steers, auto-brakes at idle,
    /// and takes off again by pitching up at flying speed.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlaneController : MonoBehaviour
    {
        [SerializeField] FlightStats stats;
        [Tooltip("Planes on the same team are allies (repair aura, radar). Placeholder until Teams mode.")]
        [SerializeField] int team;

        [Header("Crash")]
        [Tooltip("A solid hit by the body or engine while faster than this destroys the plane (a wing or tail just breaks off).")]
        [SerializeField] float crashSpeed = 8f;
        [Tooltip("Fraction of speed lost when a wing or tail clips something and breaks off instead of crashing the plane.")]
        [Range(0f, 1f)] [SerializeField] float clipSpeedLoss = 0.15f;
        [Tooltip("Yaw jolt (deg/s) toward a wing that clips something: the snag drags the nose round.")]
        [SerializeField] float clipYawKick = 40f;

        [Header("Landing")]
        [SerializeField] LandingRules landingRules = new();
        [Tooltip("Surfaces flatter than this (normal.y) can be landed on. Steeper ones, and water, are a crash.")]
        [Range(0f, 1f)] [SerializeField] float minGroundNormal = 0.9f;

        [Header("On the ground")]
        [Tooltip("Speed lost per second while rolling above the throttle's target speed (m/s²).")]
        [SerializeField] float groundDeceleration = 4f;
        [Tooltip("Steering rate at taxi speed and above (deg/s). Yaw and roll input both steer.")]
        [SerializeField] float groundTurnRate = 30f;
        [Tooltip("Nose-up angle (deg) that full pitch-up input reaches on the ground at flying speed.")]
        [SerializeField] float takeoffPitch = 10f;
        [Tooltip("Lifts off once the nose is this far up (deg) at flying speed (the class stall speed).")]
        [SerializeField] float liftoffPitch = 4f;
        [Tooltip("At idle on the ground, the brakes stop the plane by themselves, in our repair zone if it's ahead on this runway.")]
        [SerializeField] AutoBrake autoBrake = new();

        [Tooltip("Reverse speed on the wheels (m/s): hold throttle down at a standstill to back up.")]
        [SerializeField] float reverseSpeed = 8f;

        [Header("Bank-style controls")]
        [Tooltip("Bank angle (deg) that full roll input holds in the Bank control style.")]
        [SerializeField] float bankStyleMaxBank = 60f;
        [Tooltip("How hard the plane rolls toward the stick's bank angle (rate per degree of error, 1/s).")]
        [SerializeField] float bankStyleGain = 4f;

        [Header("Boundary turn-back")]
        [SerializeField] float boundarySteerSensitivity = 2.5f;
        [Tooltip("Seconds for the turn-back autopilot to take full control.")]
        [SerializeField] float boundaryTakeoverTime = 0.6f;

        static readonly List<PlaneController> _all = new();

        Rigidbody _rb;
        Collider[] _colliders;
        StatModifierStack _modifiers;
        FlightInput _input;
        LandingGear _gear;
        PlaneHealth _health;

        Vector3 _position;
        Quaternion _rotation;
        Vector3 _velocity;
        float _sinkSpeed;
        float _stallLift = 1f;
        float _damageLift = 1f;
        Vector3 _angularRates; // x = pitch (+ nose up), y = yaw (+ right), z = roll (+ right), deg/s
        float _boundaryOverride;
        float _boostCooldown;
        float _wobbleSeed;
        float _simTime; // drives wobble, so it follows the simulation rather than the frame clock
        float _forcedRollRate, _forcedRollTime; // barrel roll
        float _groundPitch; // nose-up angle while on the wheels, deg
        bool _reversing; // backing up on the wheels
        bool _takeoffRun; // powered up on the wheels: auto-throttle holds cruise until the gear retracts

        public event Action<PlaneController, Vector3> Crashed;
        public event Action<PlaneController> Respawned;
        public event Action<PlaneController> StatsChanged;
        /// <summary>Raised on every touchdown (landing, bounce, or touchdown crash), before <see cref="Crashed"/>.</summary>
        public event Action<PlaneController, TouchdownInfo> TouchedDown;

        /// <summary>Every enabled plane (players, bots, dummies), for targeting and the HUD.</summary>
        public static IReadOnlyList<PlaneController> All => _all;

        public FlightStats Stats => stats;
        public int Team
        {
            get => team;
            set => team = value;
        }
        public StatModifierStack Modifiers => _modifiers ??= new StatModifierStack(BaseStat);
        public FlightInput CurrentInput => _input;
        public bool IsAlive { get; private set; } = true;
        /// <summary>How the plane was last destroyed (read it from <see cref="Crashed"/> handlers).</summary>
        public DeathCause LastDeathCause { get; private set; }
        public float Speed { get; private set; }
        public float Throttle { get; private set; }
        /// <summary>0..1 remaining boost.</summary>
        public float BoostMeter { get; private set; } = 1f;
        public bool IsBoosting { get; private set; }
        /// <summary>
        /// Towing a banner (Capture the Banner): no Boost, no Afterburner, no repair. Set by the match; the drag itself is
        /// a <see cref="BannerDrag"/> modifier. Cleared on respawn.
        /// </summary>
        public bool CarryingBanner { get; set; }
        public bool IsOutOfBounds { get; private set; }
        public bool IsNearBoundary { get; private set; }
        public bool IsNearCeiling { get; private set; }
        /// <summary>Seconds spent outside the play area this time (0 while inside). Past a hard edge's grace the plane is out.</summary>
        public float OutOfBoundsTime { get; private set; }
        /// <summary>0..1, how much lift the plane has (1 = flying normally).</summary>
        public float LiftFactor { get; private set; } = 1f;
        public Vector3 Velocity => _velocity + Vector3.down * _sinkSpeed;
        /// <summary>Simulated pose after the latest FixedUpdate (the transform is interpolated between steps).</summary>
        public Vector3 Position => _position;
        public Quaternion Rotation => _rotation;
        public float NormalizedSpeed => Speed / Mathf.Max(1f, Modifiers.Get(FlightStat.MaxSpeed));
        /// <summary>Thrust after damage, including engine sputter (0 = dead or coughing, 1 = full).</summary>
        public float EffectiveThrust { get; private set; } = 1f;
        public bool IsSputtering { get; private set; }
        /// <summary>A barrel roll has taken over the roll axis (see <see cref="StartBarrelRoll"/>).</summary>
        public bool IsBarrelRolling => _forcedRollTime > 0f;
        /// <summary>Rolling on its wheels after a clean landing.</summary>
        public bool IsGrounded { get; private set; }
        /// <summary>On the wheels at idle: the auto-brake is slowing the plane, or holding it stopped. Throttle up to release it.</summary>
        public bool IsAutoBraking { get; private set; }
        /// <summary>While <see cref="IsAutoBraking"/>: meters to the repair zone stop point, or null when just stopping.</summary>
        public float? AutoBrakeStopDistance { get; private set; }
        public AutoBrake AutoBrake => autoBrake;
        /// <summary>Backing up on the wheels (throttle held down at a standstill).</summary>
        public bool IsReversing => IsGrounded && _reversing;
        /// <summary>The most recent touchdown (check <see cref="HasTouchedDown"/>). Cleared on respawn.</summary>
        public TouchdownInfo LastTouchdown { get; private set; }
        public bool HasTouchedDown { get; private set; }
        public LandingRules LandingRules => landingRules;

        /// <summary>Another plane on the same team.</summary>
        public bool IsAllyOf(PlaneController other) => other != null && other != this && other.team == team;

        float BaseStat(FlightStat stat) => stats != null ? stats.GetBase(stat) : 0f;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _gear = GetComponent<LandingGear>();
            _health = GetComponent<PlaneHealth>();
            _colliders = GetComponentsInChildren<Collider>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _wobbleSeed = UnityEngine.Random.value * 100f;
            ResetState(transform.position, transform.rotation, stats != null ? stats.cruiseThrottle : 0.6f);
        }

        void OnEnable() => _all.Add(this);
        void OnDisable() => _all.Remove(this);

        /// <summary>Set by the player input, a bot brain, or the network each frame.</summary>
        public void SetInput(FlightInput input) => _input = input;

        /// <summary>
        /// Multiplies how fast the turn rates follow the controls (<see cref="FlightStat.Responsiveness"/>). The player's
        /// touch controls raise it: a thumb on glass has no feel, so the plane answers it more directly.
        /// </summary>
        public float ResponsivenessScale { get; set; } = 1f;

        public void SetStats(FlightStats newStats)
        {
            stats = newStats;
            Modifiers.SetBase(BaseStat);
            StatsChanged?.Invoke(this);
        }

        /// <summary>
        /// Rolls at <paramref name="rate"/> deg/s (+ = right) for <paramref name="duration"/> seconds, ignoring roll
        /// input and auto-leveling. Pitch and yaw still work. Rate × duration = 360 for a full roll.
        /// </summary>
        public void StartBarrelRoll(float rate, float duration)
        {
            _forcedRollRate = rate;
            _forcedRollTime = Mathf.Max(0f, duration);
        }

        /// <summary>Ends a barrel roll early, without carrying its roll rate on.</summary>
        public void StopBarrelRoll()
        {
            if (_forcedRollTime <= 0f)
                return;
            _forcedRollTime = 0f;
            _angularRates.z = 0f;
        }

        /// <summary>
        /// Puts the plane back in the air with a fresh plane's state, at cruise speed or at the speed of
        /// <paramref name="throttle"/> (0..1) if given.
        /// </summary>
        public void Respawn(Vector3 position, Quaternion rotation, float? throttle = null) =>
            Spawn(position, rotation, throttle ?? stats.cruiseThrottle, grounded: false);

        /// <summary>
        /// Puts the plane, fresh, parked on its wheels on the ground under <paramref name="position"/> (gear locked,
        /// engine idle, brake on), facing the level heading of <paramref name="rotation"/>. Throttle up or boost and
        /// pitch up at flying speed to take off. Falls back to an air spawn if there's no flat ground below.
        /// </summary>
        public void SpawnOnGround(Vector3 position, Quaternion rotation)
        {
            Vector3 flat = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up);
            Quaternion level = flat.sqrMagnitude > 0.001f ? Quaternion.LookRotation(flat) : Quaternion.identity;
            if (!LandingGear.ProbeGround(position + Vector3.up * 5f, out var ground) || !IsLandable(ground))
            {
                Respawn(position, level);
                return;
            }
            float clearance = _gear != null ? _gear.WheelClearance : 0f;
            Spawn(ground.point + Vector3.up * clearance, level, 0f, grounded: true);
        }

        void Spawn(Vector3 position, Quaternion rotation, float throttle, bool grounded)
        {
            Modifiers.Clear();
            CarryingBanner = false;
            ResetState(position, rotation, throttle);
            if (grounded)
            {
                IsGrounded = true; // set before Respawned, so the gear snaps down instead of up
                Speed = 0f;
                _velocity = Vector3.zero;
            }
            _rb.position = position;
            _rb.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            SetCollidersEnabled(true);
            IsAlive = true;
            Respawned?.Invoke(this);
        }

        void ResetState(Vector3 position, Quaternion rotation, float throttle)
        {
            _position = position;
            _rotation = rotation;
            Throttle = throttle;
            Speed = stats != null ? Mathf.Lerp(stats.minSpeed, stats.maxSpeed, throttle) : 0f;
            _velocity = rotation * Vector3.forward * Speed;
            _sinkSpeed = 0f;
            _angularRates = Vector3.zero;
            _boundaryOverride = 0f;
            OutOfBoundsTime = 0f;
            _simTime = 0f;
            BoostMeter = 1f;
            _boostCooldown = 0f;
            IsBoosting = false;
            IsSputtering = false;
            EffectiveThrust = 1f;
            _forcedRollTime = 0f;
            _input = default;
            IsGrounded = false;
            IsAutoBraking = false;
            AutoBrakeStopDistance = null;
            _reversing = false;
            _groundPitch = 0f;
            _takeoffRun = false;
            HasTouchedDown = false;
            LastTouchdown = default;
        }

        void FixedUpdate()
        {
            if (!IsAlive || stats == null)
                return;

            float dt = Time.fixedDeltaTime;
            _simTime += dt;
            FlightInput input = ApplyBoundary(_input, dt);
            if (!IsAlive)
                return; // out past a hard edge

            UpdateThrottleAndBoost(input, dt);
            Vector3 step;
            if (IsGrounded)
            {
                step = GroundStep(input, dt);
            }
            else
            {
                UpdateSpeed(dt);
                UpdateRotation(input, dt);
                step = UpdateVelocity(dt) * dt;
                if (CheckTouchdown(ref step))
                    return;
            }

            if (SweepForCollisions(step))
            {
                if (!IsAlive)
                    return;
                step = Vector3.zero; // bumped into something too slowly to crash: stop against it
            }

            _position += step;
            _rb.MovePosition(_position);
            _rb.MoveRotation(_rotation);
        }

        FlightInput ApplyBoundary(FlightInput input, float dt)
        {
            var boundary = FlightBoundary.Active;
            if (boundary == null)
            {
                IsOutOfBounds = IsNearBoundary = IsNearCeiling = false;
                return input;
            }

            IsOutOfBounds = boundary.IsOutside(_position);
            IsNearBoundary = boundary.IsNearEdge(_position);
            IsNearCeiling = boundary.IsNearCeiling(_position);
            bool aboveCeiling = boundary.IsAboveCeiling(_position);
            OutOfBoundsTime = IsOutOfBounds ? OutOfBoundsTime + dt : 0f;
            if (boundary.IsOut(OutOfBoundsTime))
            {
                Crash(_position, DeathCause.OutOfBounds);
                return input;
            }

            // A hard edge doesn't turn planes back: leaving is the pilot's own mistake (the ceiling still pushes down).
            bool takeOver = (IsOutOfBounds && !boundary.hardEdge) || aboveCeiling;
            _boundaryOverride = Mathf.MoveTowards(_boundaryOverride, takeOver ? 1f : 0f, dt / boundaryTakeoverTime);
            if (_boundaryOverride <= 0f)
                return input;

            Vector3 target;
            if (IsOutOfBounds && !boundary.hardEdge)
            {
                target = boundary.ReturnPoint(_position);
            }
            else
            {
                // Over the ceiling: keep heading, nose gently down.
                Vector3 flat = Vector3.ProjectOnPlane(_rotation * Vector3.forward, Vector3.up);
                if (flat.sqrMagnitude < 0.01f)
                    flat = _rotation * Vector3.up;
                target = _position + flat.normalized * 300f + Vector3.down * 80f;
            }

            FlightAutopilot.SteerTowards(_position, _rotation, target, boundarySteerSensitivity, 15f,
                out float pitch, out float yaw, out float roll);

            float w = _boundaryOverride;
            input.Pitch = Mathf.Lerp(input.Pitch, pitch, w);
            input.Yaw = Mathf.Lerp(input.Yaw, yaw, w);
            input.Roll = Mathf.Lerp(input.Roll, roll, w);
            return input;
        }

        void UpdateThrottleAndBoost(FlightInput input, float dt)
        {
            if (input.AutoThrottle)
            {
                // Locked gear means an approach (or the runway): auto-throttle idles so the plane slows to land, unless
                // the pilot boosted into a takeoff run (touch has no throttle control), which holds until the gear is up.
                if (_gear == null || !_gear.IsLocked)
                    _takeoffRun = false;
                else if (IsGrounded && IsBoosting)
                    _takeoffRun = true;
                float auto = _gear != null && _gear.IsLocked && !_takeoffRun ? 0f : stats.cruiseThrottle;
                Throttle = Mathf.MoveTowards(Throttle, auto, stats.throttleChangeRate * dt);
            }
            else
                Throttle = Mathf.Clamp01(Throttle + input.Throttle * stats.throttleChangeRate * dt);

            // Need a little meter to start a boost, so tapping on empty doesn't flicker.
            bool wantsBoost = input.Boost && !CarryingBanner && Modifiers.Get(FlightStat.Thrust) > 0f;
            IsBoosting = wantsBoost && (IsBoosting ? BoostMeter > 0f : BoostMeter > 0.15f);

            if (IsBoosting)
            {
                BoostMeter = Mathf.Max(0f, BoostMeter - dt / Mathf.Max(0.01f, stats.boostDuration));
                _boostCooldown = stats.boostRechargeDelay;
            }
            else if (_boostCooldown > 0f)
            {
                _boostCooldown -= dt;
            }
            else
            {
                BoostMeter = Mathf.Min(1f, BoostMeter + dt / Mathf.Max(0.01f, stats.boostRechargeTime));
            }
        }

        void UpdateSpeed(float dt)
        {
            var m = Modifiers;
            float thrust = m.Get(FlightStat.Thrust) * (1f - m.Get(FlightStat.EngineSputter) * SputterDip());
            EffectiveThrust = thrust;
            float maxSpeed = m.Get(FlightStat.MaxSpeed);
            float boostMax = maxSpeed * m.Get(FlightStat.BoostMultiplier);

            float target = Mathf.Lerp(m.Get(FlightStat.MinSpeed), maxSpeed, Throttle) * thrust;
            // A dead engine only has drag to fight, so the plane glides instead of stopping.
            float drag = Mathf.Lerp(stats.glideDeceleration, stats.deceleration, Mathf.Clamp01(thrust));
            float accel = Speed < target ? m.Get(FlightStat.Acceleration) : drag;
            if (IsBoosting)
            {
                target = boostMax * thrust;
                accel = stats.boostAcceleration;
            }

            Speed = Mathf.MoveTowards(Speed, target, accel * dt);

            // Climbing bleeds speed, diving builds it.
            float noseY = (_rotation * Vector3.forward).y;
            Speed -= noseY * stats.gravityInfluence * dt;
            Speed = Mathf.Clamp(Speed, 0f, boostMax * 1.25f);
        }

        /// <summary>0 normally, rising to 1 during short, irregular engine coughs.</summary>
        float SputterDip()
        {
            if (Modifiers.Get(FlightStat.EngineSputter) <= 0f)
            {
                IsSputtering = false;
                return 0f;
            }
            float dip = Mathf.InverseLerp(0.58f, 0.7f, Mathf.PerlinNoise(_simTime * 2.3f, _wobbleSeed + 50f));
            IsSputtering = dip > 0.5f;
            return dip;
        }

        void UpdateRotation(FlightInput input, float dt)
        {
            var m = Modifiers;
            Vector3 forward = _rotation * Vector3.forward;
            Vector3 up = _rotation * Vector3.up;
            Vector3 right = _rotation * Vector3.right;

            // Less control authority when slow.
            float authority = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(stats.stallSpeed * 0.5f, stats.stallSpeed * 1.3f, Speed));
            float horizontal = 1f - Mathf.Abs(forward.y); // stabilizers do little while pointing straight up/down
            float rollStability = m.Get(FlightStat.RollStability) * horizontal;
            float pitchStability = m.Get(FlightStat.PitchStability) * horizontal;

            float pitchRate = m.Get(FlightStat.PitchRate);
            float rollRate = m.Get(FlightStat.RollRate);
            float yawRate = m.Get(FlightStat.YawRate);

            Vector3 target = new Vector3(
                Mathf.Clamp(input.Pitch, -1f, 1f) * pitchRate,
                Mathf.Clamp(input.Yaw, -1f, 1f) * yawRate,
                Mathf.Clamp(input.Roll, -1f, 1f) * rollRate);

            // Bank angle: 0 = wings level, + = right wing up, ±180 = inverted.
            float bank = Mathf.Atan2(right.y, up.y) * Mathf.Rad2Deg;

            if (input.Style == ControlStyle.Bank)
            {
                // Stick is a bank angle (right wing down for + roll), held until the stick moves.
                float wanted = -Mathf.Clamp(input.Roll, -1f, 1f) * bankStyleMaxBank;
                target.z = Mathf.Clamp(-Mathf.DeltaAngle(bank, wanted) * bankStyleGain, -rollRate, rollRate);
            }
            else if (Mathf.Abs(input.Roll) < 0.05f)
                target.z = input.Style == ControlStyle.Hold ? 0f : Mathf.Clamp(bank * stats.autoLevelRoll * rollStability, -rollRate, rollRate);

            if (Mathf.Abs(input.Pitch) < 0.05f && input.Style != ControlStyle.Hold)
            {
                float pitchAngle = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                target.x = -pitchAngle * stats.autoLevelPitch * pitchStability * Mathf.Clamp01(up.y);
            }

            // Damage effects.
            target.z += m.Get(FlightStat.RollBias);
            float t = _simTime * 1.7f;
            float pitchWobble = m.Get(FlightStat.PitchWobble);
            if (pitchWobble > 0f)
                target.x += (Mathf.PerlinNoise(_wobbleSeed, t) * 2f - 1f) * pitchWobble;
            float yawWobble = m.Get(FlightStat.YawWobble);
            if (yawWobble > 0f)
                target.y += (Mathf.PerlinNoise(t, _wobbleSeed) * 2f - 1f) * yawWobble;

            target *= authority;
            float k = 1f - Mathf.Exp(-m.Get(FlightStat.Responsiveness) * ResponsivenessScale * dt);
            _angularRates = Vector3.Lerp(_angularRates, target, k);

            // A barrel roll spins at a fixed rate from the first step and stops dead on the last one, so it turns
            // exactly rate × duration without overshooting.
            bool rollEnds = false;
            if (_forcedRollTime > 0f)
            {
                float step = Mathf.Min(dt, _forcedRollTime);
                _forcedRollTime -= dt;
                rollEnds = _forcedRollTime <= 0f;
                _angularRates.z = _forcedRollRate * step / dt;
            }

            // Local rotation: +X pitches nose down in Unity, -Z rolls right.
            _rotation *= Quaternion.Euler(-_angularRates.x * dt, _angularRates.y * dt, -_angularRates.z * dt);
            if (rollEnds)
                _angularRates.z = 0f;

            // Banking turns the nose (arcade "bank and it turns"). Not during a barrel roll: turning about world up
            // while rolled on its side would pitch the plane and leave it off-level when the roll ends.
            if (_forcedRollTime <= 0f && !rollEnds)
            {
                float bankTurn = -Mathf.Sin(bank * Mathf.Deg2Rad) * m.Get(FlightStat.BankTurnRate) * authority * Mathf.Clamp01(up.y);
                _rotation = Quaternion.AngleAxis(bankTurn * dt, Vector3.up) * _rotation;
            }

            // Not enough lift (too slow, or lost wings): the nose sags toward the ground.
            _stallLift = Mathf.Clamp01(Speed / Mathf.Max(1f, stats.stallSpeed));
            _damageLift = Mathf.Clamp01(m.Get(FlightStat.Lift));
            LiftFactor = _stallLift * _damageLift;
            if (LiftFactor < 1f)
            {
                Vector3 fwd = _rotation * Vector3.forward;
                Vector3 axis = Vector3.Cross(fwd, Vector3.down);
                if (axis.sqrMagnitude > 0.0001f)
                {
                    float drop = stats.noseDropRate * (1f - LiftFactor) * dt;
                    _rotation = Quaternion.AngleAxis(drop, axis.normalized) * _rotation;
                }
            }

            _rotation = Quaternion.Normalize(_rotation);
        }

        Vector3 UpdateVelocity(float dt)
        {
            var m = Modifiers;
            Vector3 desired = _rotation * Vector3.forward * Speed;
            _velocity = Vector3.Lerp(_velocity, desired, 1f - Mathf.Exp(-m.Get(FlightStat.Grip) * dt));

            // Stalling falls freely; lift lost to damage settles into a steady sink the pilot can fly against.
            if (_stallLift < 1f)
                _sinkSpeed += (1f - LiftFactor) * Physics.gravity.magnitude * dt;
            else
                _sinkSpeed = Mathf.MoveTowards(_sinkSpeed, (1f - _damageLift) * stats.damageSinkSpeed, 15f * dt);

            Vector3 move = _velocity + Vector3.down * _sinkSpeed;
            if (move.y > 0f)
                move.y *= m.Get(FlightStat.Climb);
            return move;
        }

        /// <summary>
        /// If the locked wheels reach flat ground during this step, judges the touchdown: lands (switches to the ground),
        /// bounces (body damage and a hop), or crashes. Moves <paramref name="step"/> onto the surface for a landing or
        /// bounce. Returns true if the plane crashed.
        /// </summary>
        bool CheckTouchdown(ref Vector3 step)
        {
            if (_gear == null || !_gear.IsLocked || step.y > 0f)
                return false;
            if (!LandingGear.ProbeGround(_position, out var ground) || !IsLandable(ground))
                return false;
            float clearance = _gear.WheelClearance;
            if (ground.distance + step.y > clearance)
                return false;

            var info = JudgeTouchdown(gearLocked: true);
            float pitch = info.Pitch, speed = info.Speed;
            Vector3 right = _rotation * Vector3.right;

            float bounceDamage = 0f;
            if (info.Result == TouchdownResult.Bounced && _health != null)
            {
                var body = _health.GetPart(PlanePartType.Body);
                bounceDamage = body.MaxHealth * landingRules.bounceDamage;
                if (bounceDamage * _health.DamageTakenMultiplier >= body.Health) // a bounce that kills is a crash
                    info = new TouchdownInfo(TouchdownResult.Crashed, info.Fault, info.Speed, info.SinkRate, info.Pitch, info.Roll);
            }

            RecordTouchdown(info);
            switch (info.Result)
            {
                case TouchdownResult.Crashed:
                    Crash(ground.point);
                    return true;

                case TouchdownResult.Bounced:
                    if (bounceDamage > 0f)
                        _health.ApplyDamage(PlanePartType.Body, bounceDamage, ground.point, this);
                    // Nose up and a little upward speed: the plane hops, then settles back for another try.
                    _rotation = Quaternion.AngleAxis(pitch - landingRules.bouncePitch, right) * _rotation;
                    _velocity.y = Mathf.Max(2f, info.SinkRate * 0.3f);
                    _sinkSpeed = 0f;
                    _angularRates.x = 0f;
                    break;

                default:
                    IsGrounded = true;
                    _takeoffRun = false;
                    _groundPitch = Mathf.Clamp(pitch, 0f, takeoffPitch);
                    _sinkSpeed = 0f;
                    _angularRates = Vector3.zero;
                    StopBarrelRoll();
                    Speed = speed;
                    // Unless the pilot is powering up for a touch-and-go, touchdown pulls the throttle to idle so the
                    // auto-brake takes over.
                    if (!_input.Boost && (_input.AutoThrottle || _input.Throttle <= 0.1f))
                        Throttle = 0f;
                    break;
            }
            step.y = clearance - ground.distance; // wheels on the surface
            return false;
        }

        /// <summary>Runs the landing rules on the current attitude and velocity.</summary>
        TouchdownInfo JudgeTouchdown(bool gearLocked)
        {
            Vector3 forward = _rotation * Vector3.forward;
            Vector3 right = _rotation * Vector3.right;
            float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
            float roll = Mathf.Atan2(right.y, (_rotation * Vector3.up).y) * Mathf.Rad2Deg;
            Vector3 velocity = Velocity;
            float speed = Vector3.ProjectOnPlane(velocity, Vector3.up).magnitude;
            return landingRules.Evaluate(gearLocked, speed, -velocity.y, pitch, roll, stats.minSpeed);
        }

        void RecordTouchdown(TouchdownInfo info)
        {
            LastTouchdown = info;
            HasTouchedDown = true;
            TouchedDown?.Invoke(this, info);
        }

        /// <summary>
        /// One step on the wheels: throttle sets the roll speed, yaw or roll input steers, and pitching up at flying speed
        /// lifts off. At idle the <see cref="AutoBrake"/> stops the plane (in our repair zone if it's ahead on this
        /// runway) and holds it there. Returns the move for this step.
        /// </summary>
        Vector3 GroundStep(FlightInput input, float dt)
        {
            var m = Modifiers;
            float thrust = m.Get(FlightStat.Thrust);
            EffectiveThrust = thrust;
            IsSputtering = false;
            Vector3 flat = Vector3.ProjectOnPlane(_rotation * Vector3.forward, Vector3.up);
            float heading = Mathf.Atan2(flat.x, flat.z) * Mathf.Rad2Deg;

            // Throttle held down at a standstill backs the plane up (to reach the repair zone after a long landing).
            bool wantsReverse = input.Throttle < -0.1f && Throttle <= 0.001f && !IsBoosting && thrust > 0f;
            if (!_reversing && wantsReverse && Speed <= 0.5f)
                _reversing = true;

            IsAutoBraking = !_reversing && Throttle <= 0.001f && !IsBoosting;
            AutoBrakeStopDistance = IsAutoBraking ? StopDistance(flat.normalized) : null;
            if (_reversing)
            {
                Speed = wantsReverse
                    ? Mathf.MoveTowards(Speed, reverseSpeed, autoBrake.taxiAcceleration * dt)
                    : Mathf.MoveTowards(Speed, 0f, autoBrake.brakeDeceleration * dt);
                if (!wantsReverse && Speed <= 0.001f)
                    _reversing = false;
            }
            else if (IsAutoBraking)
            {
                Speed = autoBrake.NextSpeed(Speed, AutoBrakeStopDistance, thrust > 0f, dt);
            }
            else
            {
                float maxSpeed = m.Get(FlightStat.MaxSpeed);
                float target = (IsBoosting ? maxSpeed * m.Get(FlightStat.BoostMultiplier) : Throttle * maxSpeed) * thrust;
                float accel = Speed >= target ? groundDeceleration
                    : IsBoosting ? stats.boostAcceleration : m.Get(FlightStat.Acceleration);
                Speed = Mathf.MoveTowards(Speed, target, accel * dt);
            }

            float steer = Mathf.Clamp(input.Yaw + input.Roll, -1f, 1f);
            heading += steer * groundTurnRate * Mathf.Clamp01(Speed / 10f) * (_reversing ? -1f : 1f) * dt; // no pivoting on the spot

            bool flyingSpeed = Speed >= stats.stallSpeed;
            float pitchTarget = flyingSpeed && input.Pitch > 0.05f ? Mathf.Clamp01(input.Pitch) * takeoffPitch : 0f;
            _groundPitch = Mathf.MoveTowards(_groundPitch, pitchTarget, m.Get(FlightStat.PitchRate) * 0.25f * dt);

            _rotation = Quaternion.Euler(-_groundPitch, heading, 0f);
            _velocity = Quaternion.Euler(0f, heading, 0f) * Vector3.forward * (_reversing ? -Speed : Speed);
            _sinkSpeed = 0f;
            _angularRates = Vector3.zero;
            _stallLift = _damageLift = LiftFactor = 1f;

            // Only a pull-up lifts off: a nose-high touchdown (a flare, or a crippled plane holding its nose up) stays down.
            if (!_reversing && flyingSpeed && pitchTarget > 0f && _groundPitch >= liftoffPitch)
            {
                IsGrounded = IsAutoBraking = _reversing = false; // rotate and climb away along the nose
                AutoBrakeStopDistance = null;
                _velocity = _rotation * Vector3.forward * Speed;
                return _velocity * dt;
            }

            // Follow the surface. Probe from a little above so a small step up (onto the runway slab) still counts.
            const float stepUp = 1f, maxDrop = 1f;
            Vector3 step = _velocity * dt;
            float clearance = _gear != null ? _gear.WheelClearance : 0f;
            if (LandingGear.ProbeGround(_position + step + Vector3.up * stepUp, out var ground)
                && IsLandable(ground) && ground.distance - stepUp <= clearance + maxDrop)
                step.y = ground.point.y + clearance - _position.y;
            else
            {
                IsGrounded = IsAutoBraking = _reversing = false; // rolled off an edge: fall and fly
                AutoBrakeStopDistance = null;
            }
            return step;
        }

        /// <summary>Solid ground the wheels can touch down and roll on: flat enough, and not water.</summary>
        bool IsLandable(RaycastHit ground) => ground.normal.y >= minGroundNormal && !IsWater(ground.collider);

        /// <summary>
        /// True for the sea (anything on Unity's built-in Water layer). It's solid, so flying into it is a crash, but
        /// it can't be landed on: wheels on water are a ditching.
        /// </summary>
        public static bool IsWater(Collider surface) => surface.gameObject.layer == WaterLayer;

        /// <summary>Unity's built-in "Water" layer.</summary>
        public const int WaterLayer = 4;

        /// <summary>
        /// How far ahead (along <paramref name="heading"/>) the auto-brake should stop: the middle of the repair zone
        /// of the runway we're on, if it's ours and not behind us; else null (just stop).
        /// </summary>
        float? StopDistance(Vector3 heading)
        {
            var runway = Airfield.RunwayUnder(_position);
            var zone = runway != null ? runway.RepairZone : null;
            // A plane that spawned on the runway (never landed) stays parked instead of taxiing to the repair zone.
            if (zone == null || !runway.IsFriendlyTo(team) || !HasTouchedDown)
                return null;
            float distance = Vector3.Dot(zone.transform.position - _position, heading);
            return distance >= 0f || zone.Contains(_position) ? distance : null; // past the middle but inside: stop now
        }

        /// <summary>
        /// Sweeps every plane collider along this step. Returns true if something solid is in the way: the plane
        /// crashed, or (too slow to crash, e.g. taxiing into a hangar) it stopped dead.
        /// </summary>
        bool SweepForCollisions(Vector3 step)
        {
            float distance = step.magnitude;
            if (distance < 0.0001f)
                return false;

            var hits = _rb.SweepTestAll(step / distance, distance, QueryTriggerInteraction.Collide);
            RaycastHit? solid = null;
            foreach (var hit in hits)
            {
                if (hit.collider.isTrigger)
                {
                    if (hit.collider.TryGetComponent(out RingGate gate))
                        gate.OnPlanePassed(this);
                    continue;
                }
                if (PlaneDebris.IsDebris(hit.collider))
                    continue;
                if (solid == null || hit.distance < solid.Value.distance)
                    solid = hit;
            }

            if (solid == null)
                return false;
            if (Velocity.magnitude < crashSpeed)
            {
                Speed = 0f;
                _velocity = Vector3.zero;
                return true;
            }

            // A wing or tail that clips something breaks off; then the rest of the plane may still hit it.
            if (TryClipPart(solid.Value, step / distance))
                return SweepForCollisions(step);

            // Hitting flat ground with the airframe while nearly level is a failed touchdown (for the HUD), not just a
            // crash: a belly landing without locked gear, or with it, a wingtip or tail striking first (past ~20°).
            // Setting down on the sea is always a ditching.
            if (_gear != null && solid.Value.normal.y >= minGroundNormal)
            {
                var info = JudgeTouchdown(_gear.IsLocked);
                if (landingRules.IsLandingAttempt(info.Pitch, info.Roll))
                    RecordTouchdown(new TouchdownInfo(TouchdownResult.Crashed,
                        IsWater(solid.Value.collider) ? TouchdownFault.Ditched
                            : _gear.IsLocked ? TouchdownFault.BadAngle : TouchdownFault.GearNotLocked,
                        info.Speed, info.SinkRate, info.Pitch, info.Roll));
            }
            Crash(solid.Value.point);
            return true;
        }

        /// <summary>
        /// If the collider that struck <paramref name="hit"/> belongs to a wing or stabilizer, rips that part off (and the
        /// other plane's part too, in a wing-on-wing clip) instead of crashing. Returns true if a part came off.
        /// </summary>
        bool TryClipPart(RaycastHit hit, Vector3 direction)
        {
            var part = StrikingPart(hit, direction);
            if (part == null || !part.CanDetach || part.IsDetached)
                return false;
            _health.ApplyDamage(part, float.MaxValue, hit.point, this);
            if (!part.IsDetached)
                return false; // invulnerable (just repaired): crashes as before

            if (PlanePart.TryGetFromCollider(hit.collider, out var other) && other.CanDetach)
            {
                var otherHealth = other.GetComponentInParent<PlaneHealth>();
                if (otherHealth != null && otherHealth != _health)
                    otherHealth.ApplyDamage(other, float.MaxValue, hit.point, this);
            }

            Speed *= 1f - clipSpeedLoss;
            _velocity *= 1f - clipSpeedLoss;
            if (part.partType is PlanePartType.WingLeft or PlanePartType.WingRight)
                _angularRates.y += (part.partType == PlanePartType.WingLeft ? -1f : 1f) * clipYawKick;
            return true;
        }

        /// <summary>
        /// Which of our parts made <paramref name="hit"/>. The sweep doesn't say which collider struck, so this takes the
        /// one nearest the contact point, measured from where the plane is now (the hit point minus the swept distance).
        /// </summary>
        PlanePart StrikingPart(RaycastHit hit, Vector3 direction)
        {
            if (_health == null || (hit.distance <= 0f && hit.point == Vector3.zero))
                return null; // started overlapping: no contact point to go on
            Vector3 contact = hit.point - direction * hit.distance;
            Collider nearest = null;
            float best = float.PositiveInfinity;
            foreach (var c in _colliders)
            {
                if (c == null || !c.enabled || !c.gameObject.activeInHierarchy || c.isTrigger)
                    continue;
                float d = (c.ClosestPoint(contact) - contact).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    nearest = c;
                }
            }
            return PlanePart.TryGetFromCollider(nearest, out var part) ? part : null;
        }

        /// <summary>Destroys the plane at <paramref name="point"/>. Every death goes through here; respawning is up to <see cref="PlaneRespawner"/>.</summary>
        public void Crash(Vector3 point, DeathCause cause = DeathCause.Collision)
        {
            if (!IsAlive)
                return;
            IsAlive = false;
            LastDeathCause = cause;
            IsBoosting = false;
            SetCollidersEnabled(false); // so debris doesn't bounce off the hidden wreck
            Crashed?.Invoke(this, point);
        }

        void SetCollidersEnabled(bool enabled)
        {
            foreach (var c in _colliders)
                if (c != null)
                    c.enabled = enabled;
        }
    }
}

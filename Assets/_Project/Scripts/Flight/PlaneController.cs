using System;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// Arcade, velocity-driven flight (not an aerodynamics sim). Rotation comes from input × class
    /// rates, velocity follows the nose with a little drift, and gravity only nudges speed.
    /// The whole simulation runs from <see cref="FlightInput"/> in FixedUpdate, so it is
    /// deterministic from inputs (good for bots now and server authority later).
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PlaneController : MonoBehaviour
    {
        [SerializeField] FlightStats stats;

        [Header("Crash")]
        [Tooltip("Any solid hit while faster than this destroys the plane.")]
        [SerializeField] float crashSpeed = 8f;

        [Header("Boundary turn-back")]
        [SerializeField] float boundarySteerSensitivity = 2.5f;
        [Tooltip("Seconds for the turn-back autopilot to take full control.")]
        [SerializeField] float boundaryTakeoverTime = 0.6f;

        Rigidbody _rb;
        Collider[] _colliders;
        StatModifierStack _modifiers;
        FlightInput _input;

        Vector3 _position;
        Quaternion _rotation;
        Vector3 _velocity;
        float _sinkSpeed;
        Vector3 _angularRates; // x = pitch (+ nose up), y = yaw (+ right), z = roll (+ right), deg/s
        float _boundaryOverride;
        float _boostCooldown;
        float _wobbleSeed;

        public event Action<PlaneController, Vector3> Crashed;
        public event Action<PlaneController> Respawned;
        public event Action<PlaneController> StatsChanged;

        public FlightStats Stats => stats;
        public StatModifierStack Modifiers => _modifiers ??= new StatModifierStack(BaseStat);
        public FlightInput CurrentInput => _input;
        public bool IsAlive { get; private set; } = true;
        public float Speed { get; private set; }
        public float Throttle { get; private set; }
        /// <summary>0..1 remaining boost.</summary>
        public float BoostMeter { get; private set; } = 1f;
        public bool IsBoosting { get; private set; }
        public bool IsOutOfBounds { get; private set; }
        public bool IsNearBoundary { get; private set; }
        public bool IsNearCeiling { get; private set; }
        /// <summary>0..1, how much lift the plane has (1 = flying normally).</summary>
        public float LiftFactor { get; private set; } = 1f;
        public Vector3 Velocity => _velocity + Vector3.down * _sinkSpeed;
        public float NormalizedSpeed => Speed / Mathf.Max(1f, Modifiers.Get(FlightStat.MaxSpeed));

        float BaseStat(FlightStat stat) => stats != null ? stats.GetBase(stat) : 0f;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _colliders = GetComponentsInChildren<Collider>();
            _rb.isKinematic = true;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            _wobbleSeed = UnityEngine.Random.value * 100f;
            ResetState(transform.position, transform.rotation, stats != null ? stats.cruiseThrottle : 0.6f);
        }

        /// <summary>Set by the player input, a bot brain, or the network each frame.</summary>
        public void SetInput(FlightInput input) => _input = input;

        public void SetStats(FlightStats newStats)
        {
            stats = newStats;
            Modifiers.SetBase(BaseStat);
            StatsChanged?.Invoke(this);
        }

        /// <summary>Puts the plane back in the air at cruise speed with a fresh plane's state.</summary>
        public void Respawn(Vector3 position, Quaternion rotation)
        {
            Modifiers.Clear();
            ResetState(position, rotation, stats.cruiseThrottle);
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
            BoostMeter = 1f;
            _boostCooldown = 0f;
            IsBoosting = false;
            _input = default;
        }

        void FixedUpdate()
        {
            if (!IsAlive || stats == null)
                return;

            float dt = Time.fixedDeltaTime;
            FlightInput input = ApplyBoundary(_input, dt);

            UpdateThrottleAndBoost(input, dt);
            UpdateSpeed(dt);
            UpdateRotation(input, dt);
            Vector3 step = UpdateVelocity(dt) * dt;

            if (SweepForCollisions(step))
                return;

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

            bool takeOver = IsOutOfBounds || aboveCeiling;
            _boundaryOverride = Mathf.MoveTowards(_boundaryOverride, takeOver ? 1f : 0f, dt / boundaryTakeoverTime);
            if (_boundaryOverride <= 0f)
                return input;

            Vector3 target;
            if (IsOutOfBounds)
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
                Throttle = Mathf.MoveTowards(Throttle, stats.cruiseThrottle, stats.throttleChangeRate * dt);
            else
                Throttle = Mathf.Clamp01(Throttle + input.Throttle * stats.throttleChangeRate * dt);

            // Need a little meter to start a boost, so tapping on empty doesn't flicker.
            bool wantsBoost = input.Boost && Modifiers.Get(FlightStat.Thrust) > 0f;
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
            float thrust = m.Get(FlightStat.Thrust);
            float maxSpeed = m.Get(FlightStat.MaxSpeed);
            float boostMax = maxSpeed * m.Get(FlightStat.BoostMultiplier);

            float target = Mathf.Lerp(m.Get(FlightStat.MinSpeed), maxSpeed, Throttle) * thrust;
            float accel = Speed < target ? m.Get(FlightStat.Acceleration) : stats.deceleration;
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

        void UpdateRotation(FlightInput input, float dt)
        {
            var m = Modifiers;
            Vector3 forward = _rotation * Vector3.forward;
            Vector3 up = _rotation * Vector3.up;
            Vector3 right = _rotation * Vector3.right;

            // Less control authority when slow.
            float authority = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(stats.stallSpeed * 0.5f, stats.stallSpeed * 1.3f, Speed));
            float horizontal = 1f - Mathf.Abs(forward.y); // stabilizers do little while pointing straight up/down
            float autoLevel = m.Get(FlightStat.AutoLevel) * horizontal;

            float pitchRate = m.Get(FlightStat.PitchRate);
            float rollRate = m.Get(FlightStat.RollRate);
            float yawRate = m.Get(FlightStat.YawRate);

            Vector3 target = new Vector3(
                Mathf.Clamp(input.Pitch, -1f, 1f) * pitchRate,
                Mathf.Clamp(input.Yaw, -1f, 1f) * yawRate,
                Mathf.Clamp(input.Roll, -1f, 1f) * rollRate);

            // Bank angle: 0 = wings level, + = right wing up, ±180 = inverted.
            float bank = Mathf.Atan2(right.y, up.y) * Mathf.Rad2Deg;

            if (Mathf.Abs(input.Roll) < 0.05f)
                target.z = Mathf.Clamp(bank * stats.autoLevelRoll * autoLevel, -rollRate, rollRate);

            if (Mathf.Abs(input.Pitch) < 0.05f)
            {
                float pitchAngle = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
                target.x = -pitchAngle * stats.autoLevelPitch * autoLevel * Mathf.Clamp01(up.y);
            }

            // Damage effects.
            target.z += m.Get(FlightStat.RollBias);
            float wobble = m.Get(FlightStat.Wobble);
            if (wobble > 0f)
            {
                float t = Time.time * 1.7f;
                target.x += (Mathf.PerlinNoise(_wobbleSeed, t) * 2f - 1f) * wobble;
                target.y += (Mathf.PerlinNoise(t, _wobbleSeed) * 2f - 1f) * wobble;
            }

            target *= authority;
            float k = 1f - Mathf.Exp(-m.Get(FlightStat.Responsiveness) * dt);
            _angularRates = Vector3.Lerp(_angularRates, target, k);

            // Local rotation: +X pitches nose down in Unity, -Z rolls right.
            _rotation *= Quaternion.Euler(-_angularRates.x * dt, _angularRates.y * dt, -_angularRates.z * dt);

            // Banking turns the nose (arcade "bank and it turns").
            float bankTurn = -Mathf.Sin(bank * Mathf.Deg2Rad) * m.Get(FlightStat.BankTurnRate) * authority * Mathf.Clamp01(up.y);
            _rotation = Quaternion.AngleAxis(bankTurn * dt, Vector3.up) * _rotation;

            // Not enough lift: the nose sags toward the ground.
            LiftFactor = Mathf.Clamp01(Speed / Mathf.Max(1f, stats.stallSpeed)) * m.Get(FlightStat.Lift);
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

            if (LiftFactor < 1f)
                _sinkSpeed += (1f - LiftFactor) * Physics.gravity.magnitude * dt;
            else
                _sinkSpeed = Mathf.MoveTowards(_sinkSpeed, 0f, 15f * dt);

            Vector3 move = _velocity + Vector3.down * _sinkSpeed;
            if (move.y > 0f)
                move.y *= m.Get(FlightStat.Climb);
            return move;
        }

        /// <summary>Sweeps every plane collider along this step. Returns true if the plane crashed.</summary>
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
                if (solid == null || hit.distance < solid.Value.distance)
                    solid = hit;
            }

            if (solid == null || Velocity.magnitude < crashSpeed)
                return false;

            Crash(solid.Value.point);
            return true;
        }

        public void Crash(Vector3 point)
        {
            if (!IsAlive)
                return;
            IsAlive = false;
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

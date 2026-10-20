using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Bots
{
    /// <summary>What a <see cref="BotPilot"/> is flying right now.</summary>
    public enum BotManeuver
    {
        /// <summary>Straight and level on the current heading.</summary>
        Cruise,
        /// <summary>Head for a point; on arrival, orbit it.</summary>
        FlyTo,
        /// <summary>Circle a point.</summary>
        Orbit,
        /// <summary>Chase a plane: aim at the gun lead point in range, else at an intercept point.</summary>
        Pursue,
        /// <summary>Hold a slot relative to a leader plane.</summary>
        FormUp,
        /// <summary>Fly an airfield's glide path down to its runway at approach speed, then idle so the auto-brake stops it.</summary>
        Land,
        /// <summary>Full power down the runway, rotate at flying speed and climb away straight ahead.</summary>
        TakeOff,
    }

    /// <summary>
    /// The hands on a bot's stick. It flies a <see cref="BotManeuver"/> and works the throttle, guns and
    /// buttons purely through <see cref="PlaneController.SetInput"/>, the same path as a player, so lost
    /// parts, damage states, the boundary turn-back and every weapon behave exactly as they do for players
    /// (and later the server can run bots as just another input source).
    /// Deciding <em>what</em> to fly (patrol, attack, evade, retreat) is the job of the behaviors on top
    /// (<see cref="BotBrain"/>); they give orders here and set the trigger properties each step.
    /// </summary>
    [DefaultExecutionOrder(-10)] // set input before PlaneController simulates
    [RequireComponent(typeof(PlaneController))]
    public class BotPilot : MonoBehaviour
    {
        [Header("Stick")]
        [Tooltip("Autopilot stick gain; higher = sharper corrections.")]
        [SerializeField] float steerSensitivity = 3f;
        [Tooltip("Off-target angle (deg) above which the bot banks to turn instead of yawing.")]
        [SerializeField] float aggressiveTurnAngle = 12f;
        [Tooltip("How fast the stick can move (full deflection per second; 0 = instant). Keeps bots from twitching.")]
        [SerializeField] float stickSlewRate = 5f;
        [Tooltip("Pursue: how far (deg) the aim wanders off the gun lead point. 0 = dead on.")]
        [SerializeField] float aimError;
        [Tooltip("Pursue: integral gain (per second) while tracking the lead point. Takes out the steady lag a plain " +
                 "proportional stick leaves behind a turning target.")]
        [SerializeField] float trackingIntegralGain = 6f;
        [Tooltip("Pursue: the integral term's limit (stick deflection).")]
        [SerializeField] float trackingIntegralLimit = 0.5f;

        [Header("Maneuvers")]
        [Tooltip("FlyTo counts as arrived inside this horizontal distance, then orbits the point.")]
        [SerializeField] float arriveRadius = 150f;
        [SerializeField] float orbitRadius = 300f;
        [Tooltip("Pursue: beyond this distance the bot goes to full speed to close in.")]
        [SerializeField] float closeInDistance = 600f;
        [Tooltip("Pursue: the gap (m) to settle at behind the target, just inside the guns' 350 m convergence.")]
        [SerializeField] float pursuitGap = 300f;
        [Tooltip("Pursue: extra speed (m/s) per meter of gap beyond that (negative inside it, to keep from overshooting).")]
        [SerializeField] float overtakePerMeter = 0.08f;
        [Tooltip("Pursue: the most speed over the target's (m/s) while closing in range.")]
        [SerializeField] float overtakeSpeed = 25f;
        [Tooltip("Land: aim this far (m) ahead along the glide path, far out. Shorter tracks it tighter but twitchier.")]
        [SerializeField] float glideLead = 250f;
        [Tooltip("Land: ...shrinking to this (m) near the runway.")]
        [SerializeField] float minGlideLead = 100f;
        [Tooltip("Land: bank to correct once the glide path is this far off the nose (deg); below it, yaw.")]
        [SerializeField] float landingTurnAngle = 8f;
        [Tooltip("Land: banked past this (deg), roll wings level before anything else.")]
        [SerializeField] float maxLandingBank = 35f;
        [Tooltip("Land: closer than this (m) to the aim point, keep the wings level (a banked touchdown is a crash).")]
        [SerializeField] float flareDistance = 200f;
        [Tooltip("Land: approach speed as a multiple of the class min speed (touching down over 1.6x bounces).")]
        [SerializeField] float approachSpeedFactor = 1.2f;
        [Tooltip("Land: extra approach speed (x min speed) per unit of lift lost to damage.")]
        [SerializeField] float damagedLiftSpeedBonus = 0.75f;
        [Tooltip("TakeOff: climb angle (deg) once the wheels are up.")]
        [SerializeField] float takeoffClimbAngle = 12f;

        PlaneController _plane;
        PlaneGuns _guns;
        Vector3 _destination, _orbitCenter, _slotOffset;
        float _orbitAltitude;
        bool _orbitClockwise;
        Airfield _field;
        Vector3 _takeoffHeading;
        float? _speedOverride;
        float _pitch, _roll, _yaw, _trackPitch, _trackYaw;
        bool _hasOverride;
        Vector3 _overridePoint;
        float? _overrideSpeed;
        float _overrideSensitivity = 1f;
        float _aimSeed;

        /// <summary>Every active bot, for debug views and tests.</summary>
        public static readonly System.Collections.Generic.List<BotPilot> All = new();

        public PlaneController Plane => _plane;
        public BotManeuver Maneuver { get; private set; } = BotManeuver.Cruise;
        /// <summary>FlyTo's goal, or the orbit center.</summary>
        public Vector3 Destination => Maneuver == BotManeuver.Orbit ? _orbitCenter : _destination;
        /// <summary>How wide an <see cref="Orbit"/> circles (the last radius given, or the default).</summary>
        public float OrbitRadius => orbitRadius;
        /// <summary>The plane being pursued or followed, or null.</summary>
        public PlaneController Target { get; private set; }
        /// <summary>Set when a FlyTo reaches its point (the bot then orbits it). Cleared by the next order.</summary>
        public bool HasArrived { get; private set; }
        /// <summary>Where the bot is pointing its nose this step (for debug drawing).</summary>
        public Vector3 SteerPoint { get; private set; }
        /// <summary>Speed (m/s) the throttle is working toward this step.</summary>
        public float TargetSpeed { get; private set; }
        /// <summary>Pursue: the gun lead point is in range this step.</summary>
        public bool HasGunSolution { get; private set; }
        /// <summary>The airfield being landed at, or null.</summary>
        public Airfield Field => Maneuver == BotManeuver.Land ? _field : null;

        // Buttons, held until the behavior on top changes them. Cleared when the plane dies.
        public bool Boost { get; set; }
        public bool FireGuns { get; set; }
        public bool FireRockets { get; set; }
        public bool FireMissile { get; set; }
        public bool DropBomb { get; set; }
        public bool UseAbility { get; set; }

        /// <summary>
        /// Speed to fly the final approach at: comfortably under the touchdown limit, comfortably over a stall. Faster
        /// with lift lost to damage (a wing gone: ~1.5x min speed), since only speed lets it hold the glide path against
        /// the extra sink.
        /// </summary>
        public float ApproachSpeed
        {
            get
            {
                if (_plane.Stats == null)
                    return 45f;
                float lostLift = 1f - Mathf.Clamp01(_plane.Modifiers.Get(FlightStat.Lift));
                return _plane.Stats.minSpeed * (approachSpeedFactor + damagedLiftSpeedBonus * lostLift);
            }
        }

        /// <summary>The class's cruise speed: where the throttle sits unless an order says otherwise.</summary>
        public float CruiseSpeed
        {
            get
            {
                var s = _plane.Stats;
                return s != null ? Mathf.Lerp(s.minSpeed, s.maxSpeed, s.cruiseThrottle) : 60f;
            }
        }

        // ------------------------------------------------------------------ orders

        /// <summary>Fly straight and level on the current heading, at <paramref name="speed"/> or cruise.</summary>
        public void Cruise(float? speed = null)
        {
            NewOrder(BotManeuver.Cruise, speed);
            _orbitAltitude = _plane.Position.y;
        }

        /// <summary>Head for <paramref name="point"/> (including its altitude), then orbit it.</summary>
        public void FlyTo(Vector3 point, float? speed = null)
        {
            NewOrder(BotManeuver.FlyTo, speed);
            _destination = point;
        }

        /// <summary>Circle <paramref name="center"/> at its altitude. Picks the turn direction that needs no reversal.</summary>
        public void Orbit(Vector3 center, float? radius = null, float? speed = null)
        {
            NewOrder(BotManeuver.Orbit, speed);
            _orbitCenter = center;
            _orbitAltitude = center.y;
            if (radius.HasValue)
                orbitRadius = radius.Value;
            Vector3 fromCenter = _plane.Position - center;
            Vector3 forward = _plane.Rotation * Vector3.forward;
            _orbitClockwise = Vector3.Cross(fromCenter, forward).y >= 0f;
        }

        /// <summary>Chase <paramref name="target"/>. Falls back to Cruise if it dies or is removed.</summary>
        public void Pursue(PlaneController target)
        {
            NewOrder(BotManeuver.Pursue, null);
            Target = target;
        }

        /// <summary>
        /// Hold a slot <paramref name="offset"/> from <paramref name="leader"/> in its level frame
        /// (x right, y up, z forward; e.g. (-40, 0, -40) is behind and to its left).
        /// </summary>
        public void FormUp(PlaneController leader, Vector3 offset)
        {
            NewOrder(BotManeuver.FormUp, null);
            Target = leader;
            _slotOffset = offset;
        }

        /// <summary>
        /// Land on <paramref name="field"/>'s runway: track its glide path (from wherever the plane is, turning onto the
        /// centerline) at <paramref name="speed"/> or <see cref="ApproachSpeed"/>. The gear comes down by itself near
        /// the ground. On the wheels the bot idles and keeps straight, so the auto-brake stops it on the repair zone's
        /// cross. Lining up first (an approach lane, a go-around) is the behaviors' job.
        /// </summary>
        public void Land(Airfield field, float? speed = null)
        {
            NewOrder(BotManeuver.Land, speed);
            _field = field;
        }

        /// <summary>
        /// Take off along the current heading: throttle up to cruise, pull the nose up at flying speed, then hold a
        /// steady climb straight ahead until the next order. Boost is the behaviors' call.
        /// </summary>
        public void TakeOff()
        {
            NewOrder(BotManeuver.TakeOff, null);
            Vector3 flat = Vector3.ProjectOnPlane(_plane.Rotation * Vector3.forward, Vector3.up);
            _takeoffHeading = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
        }

        /// <summary>
        /// Takes the stick for this physics step only: steer at <paramref name="steerPoint"/> instead of the order,
        /// which keeps running underneath (so the bot picks it back up when the override stops). Used by the behaviors
        /// for reflexes: pulling up from terrain and breaking from missiles. Call it every step it should last, before
        /// the pilot runs (<see cref="BotBrain"/> does).
        /// </summary>
        /// <param name="sensitivityScale">Multiplies the stick gain (above 1 = yank harder).</param>
        public void Override(Vector3 steerPoint, float? speed = null, float sensitivityScale = 1f)
        {
            _hasOverride = true;
            _overridePoint = steerPoint;
            _overrideSpeed = speed;
            _overrideSensitivity = sensitivityScale;
        }

        /// <summary>True on a step whose stick is overridden (see <see cref="Override"/>).</summary>
        public bool IsOverridden { get; private set; }

        /// <summary>
        /// The stick hand's share of a <see cref="BotSkill"/>: gain, slew rate and how much the aim wanders while
        /// tracking (<see cref="BotBrain"/> sets it from its difficulty).
        /// </summary>
        public void SetSkill(float sensitivity, float slewRate, float aimErrorDegrees)
        {
            steerSensitivity = sensitivity;
            stickSlewRate = slewRate;
            aimError = aimErrorDegrees;
        }

        public float SteerSensitivity => steerSensitivity;
        public float StickSlewRate => stickSlewRate;
        public float AimError => aimError;

        /// <summary>Lets go of every button (the stick keeps flying the current order).</summary>
        public void ReleaseButtons() => Boost = FireGuns = FireRockets = FireMissile = DropBomb = UseAbility = false;

        void NewOrder(BotManeuver maneuver, float? speed)
        {
            Maneuver = maneuver;
            _speedOverride = speed;
            Target = null;
            HasArrived = false;
        }

        // ------------------------------------------------------------------ flying

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _guns = GetComponent<PlaneGuns>();
            _aimSeed = Random.Range(0f, 1000f);
        }

        void OnEnable()
        {
            All.Add(this);
            _plane.Crashed += OnCrashed;
        }

        void OnDisable()
        {
            All.Remove(this);
            _plane.Crashed -= OnCrashed;
        }

        void Start()
        {
            if (Maneuver == BotManeuver.Cruise)
                _orbitAltitude = _plane.Position.y;
        }

        void OnCrashed(PlaneController plane, Vector3 point)
        {
            ReleaseButtons();
            _hasOverride = false;
            _pitch = _roll = _yaw = 0f;
        }

        void FixedUpdate()
        {
            if (!_plane.IsAlive)
                return;

            float dt = Time.fixedDeltaTime;
            Vector3 pos = _plane.Position;
            Vector3 forward = _plane.Rotation * Vector3.forward;
            float sensitivity = steerSensitivity;
            HasGunSolution = false;

            if ((Maneuver == BotManeuver.Pursue || Maneuver == BotManeuver.FormUp) &&
                (Target == null || !Target.isActiveAndEnabled || !Target.IsAlive))
                Cruise();
            if (Maneuver == BotManeuver.Land && (_field == null || !_field.isActiveAndEnabled))
                Cruise();

            Vector3 steer;
            float speed = _speedOverride ?? CruiseSpeed;
            float turnAngle = aggressiveTurnAngle;
            bool tracking = false;
            switch (Maneuver)
            {
                case BotManeuver.FlyTo:
                {
                    Vector3 flat = _destination - pos;
                    flat.y = 0f;
                    if (flat.magnitude <= arriveRadius)
                    {
                        float? keepSpeed = _speedOverride;
                        Vector3 point = _destination;
                        Orbit(point, speed: keepSpeed);
                        HasArrived = true;
                        goto case BotManeuver.Orbit;
                    }
                    steer = _destination;
                    break;
                }
                case BotManeuver.Orbit:
                    steer = BotSteering.OrbitPoint(pos, forward, _orbitCenter, orbitRadius, _orbitAltitude, _orbitClockwise);
                    break;
                case BotManeuver.Pursue:
                {
                    float distance = Vector3.Distance(pos, Target.Position);
                    if (_guns != null && _guns.TryGetLeadPoint(Target, out Vector3 lead, out _))
                    {
                        steer = lead;
                        if (aimError > 0f)
                        {
                            Vector2 wander = BotSteering.AimWander(_aimSeed, Time.fixedTime) * aimError;
                            steer = BotSteering.OffsetAim(pos, lead, _plane.Rotation * Vector3.up, wander.x, wander.y);
                        }
                        HasGunSolution = true;
                        sensitivity *= 1.5f; // fine tracking on the pipper
                        tracking = true;
                    }
                    else
                    {
                        steer = BotSteering.InterceptPoint(pos, _plane.Speed, Target.Position, Target.Velocity);
                    }
                    speed = distance > closeInDistance
                        ? _plane.Modifiers.Get(FlightStat.MaxSpeed)
                        : Target.Speed + BotSteering.Overtake(distance, pursuitGap, overtakePerMeter, overtakeSpeed);
                    break;
                }
                case BotManeuver.FormUp:
                {
                    Vector3 slot = BotSteering.SlotPosition(Target.Position, Target.Rotation, _slotOffset);
                    Vector3 leaderForward = Target.Rotation * Vector3.forward;
                    // Aim a little ahead of the slot along the leader's path, so the bot slides in instead of circling it.
                    steer = slot + Vector3.ProjectOnPlane(leaderForward, Vector3.up).normalized * 150f;
                    speed = BotSteering.SlotSpeed(pos, slot, leaderForward, Target.Speed);
                    break;
                }
                case BotManeuver.Land:
                {
                    var slope = _field.GlideSlope;
                    var guide = _field.GetApproach(pos, _plane.Velocity);
                    // Descend toward a carrot well ahead (smooth on the glide path), but turn toward one that gets closer
                    // near the runway, so a plane still off the centerline turns in harder.
                    float aimZ = slope.AimZ(_field.RunwaySize.y);
                    float lead = Mathf.Clamp(guide.Distance * 0.4f, minGlideLead, glideLead);
                    Vector3 far = _field.transform.TransformPoint(BotSteering.GlideCarrot(guide.Distance, glideLead, slope.angle, aimZ));
                    Vector3 near = _field.transform.TransformPoint(BotSteering.GlideCarrot(guide.Distance, lead, slope.angle, aimZ));
                    Vector3 carrot = BotSteering.SplitAim(pos, near, far);
                    // Bank to line up while there's room; near the runway, or banked steeply (a lost wing's roll can run
                    // away with it), wings level first and yaw for the corrections.
                    bool steepBank = Mathf.Abs((_plane.Rotation * Vector3.right).y) > Mathf.Sin(maxLandingBank * Mathf.Deg2Rad);
                    turnAngle = steepBank ? 90f : guide.Distance > flareDistance ? landingTurnAngle : aggressiveTurnAngle * 2f;
                    // Put the flight path, not the nose, on the carrot: a plane sinking from lost lift needs its nose
                    // up to hold the glide path, like a player flying the flight-path marker.
                    steer = _plane.IsGrounded ? carrot : BotSteering.FlightPathAim(pos, forward, _plane.Velocity, carrot);
                    // On the wheels: idle, so the auto-brake takes over and parks the plane in the repair zone.
                    speed = _plane.IsGrounded ? 0f : _speedOverride ?? ApproachSpeed;
                    break;
                }
                case BotManeuver.TakeOff:
                {
                    float climb = takeoffClimbAngle * Mathf.Deg2Rad;
                    steer = pos + (_takeoffHeading * Mathf.Cos(climb) + Vector3.up * Mathf.Sin(climb)) * 500f;
                    break;
                }
                default: // Cruise
                {
                    Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
                    if (flat.sqrMagnitude < 1e-4f)
                        flat = Vector3.forward;
                    steer = new Vector3(pos.x, _orbitAltitude, pos.z) + flat.normalized * 600f;
                    break;
                }
            }

            IsOverridden = _hasOverride;
            if (_hasOverride)
            {
                steer = _overridePoint;
                speed = _overrideSpeed ?? speed;
                sensitivity = steerSensitivity * _overrideSensitivity;
                HasGunSolution = false;
                _hasOverride = false;
                tracking = false;
            }
            UpdateTrackingIntegral(steer, tracking, turnAngle, dt);

            SteerPoint = steer;
            TargetSpeed = speed;
            _plane.SetInput(BuildInput(steer, speed, sensitivity, turnAngle, dt));
        }

        /// <summary>
        /// Integrates the pitch/yaw error while fine-tracking the lead point (inside the wings-level cone), so the stick
        /// settles on the steady deflection a turning target needs instead of trailing it by a couple of degrees.
        /// Anything else (a turn, an override, no solution) resets it.
        /// </summary>
        void UpdateTrackingIntegral(Vector3 steer, bool tracking, float turnAngle, float dt)
        {
            Vector3 to = steer - _plane.Position;
            Vector3 local = to.sqrMagnitude > 1e-4f ? Quaternion.Inverse(_plane.Rotation) * to.normalized : Vector3.forward;
            if (!tracking || local.z <= 0f || Vector3.Angle(Vector3.forward, local) > turnAngle)
            {
                _trackPitch = _trackYaw = 0f;
                return;
            }
            _trackPitch = Mathf.Clamp(_trackPitch + local.y * trackingIntegralGain * dt, -trackingIntegralLimit, trackingIntegralLimit);
            _trackYaw = Mathf.Clamp(_trackYaw + local.x * trackingIntegralGain * dt, -trackingIntegralLimit, trackingIntegralLimit);
        }

        FlightInput BuildInput(Vector3 steer, float speed, float sensitivity, float turnAngle, float dt)
        {
            FlightAutopilot.SteerTowards(_plane.Position, _plane.Rotation, steer, sensitivity, turnAngle,
                out float pitch, out float yaw, out float roll);
            pitch = Mathf.Clamp(pitch + _trackPitch, -1f, 1f);
            yaw = Mathf.Clamp(yaw + _trackYaw, -1f, 1f);
            _pitch = BotSteering.Slew(_pitch, pitch, stickSlewRate, dt);
            if (_plane.IsGrounded && Maneuver == BotManeuver.Land)
                _pitch = Mathf.Min(_pitch, 0f); // on the wheels after landing: no back-pressure, or it lifts off again
            _roll = BotSteering.Slew(_roll, roll, stickSlewRate, dt);
            _yaw = BotSteering.Slew(_yaw, yaw, stickSlewRate, dt);

            var m = _plane.Modifiers;
            float throttle = BotSteering.ThrottleCommand(_plane.Throttle, speed, m.Get(FlightStat.MinSpeed),
                m.Get(FlightStat.MaxSpeed), m.Get(FlightStat.Thrust), _plane.IsGrounded);

            return new FlightInput
            {
                Pitch = _pitch,
                Roll = _roll,
                Yaw = _yaw,
                Throttle = throttle,
                Boost = Boost,
                FireGuns = FireGuns,
                FireRockets = FireRockets,
                FireMissile = FireMissile,
                DropBomb = DropBomb,
                UseAbility = UseAbility,
            };
        }
    }
}

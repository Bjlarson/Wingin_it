using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Dev
{
    /// <summary>How a <see cref="DummyPilot"/> flies when it isn't evading or on a tow run.</summary>
    public enum DummyPattern
    {
        /// <summary>A steady circle around the center: the easiest target.</summary>
        Orbit,
        /// <summary>Long straight legs back and forth through the center, turning at each end: easy to line up from behind.</summary>
        Racetrack,
        /// <summary>A circle whose radius and altitude swing in and out: harder to keep the pipper on.</summary>
        Weave,
    }

    /// <summary>
    /// Prototype-only target drone until real bots exist (Phase 5). Flies a <see cref="DummyPattern"/>
    /// around a point, optionally breaks hard for a moment when another plane hits it, and can be
    /// sent on a straight "tow run" in front of the player (dev key T, see <see cref="DamageTester"/>).
    /// Steers with the same autopilot as mouse-aim, so lost parts affect it like any plane.
    /// </summary>
    [DefaultExecutionOrder(-10)] // set input before PlaneController simulates
    [RequireComponent(typeof(PlaneController))]
    public class DummyPilot : MonoBehaviour
    {
        [SerializeField] DummyPattern pattern = DummyPattern.Orbit;
        [Tooltip("Center of the pattern; its height is the cruise altitude.")]
        [SerializeField] Vector3 center;
        [Tooltip("Orbit / weave radius, or half the length of a racetrack leg.")]
        [SerializeField] float radius = 300f;
        [SerializeField] bool clockwise = true;
        [Tooltip("Racetrack only: compass heading (deg) of the legs.")]
        [SerializeField] float racetrackHeading;
        [Tooltip("How far around the circle (deg) the steering point leads the plane.")]
        [SerializeField] float leadAngle = 25f;
        [SerializeField] float steerSensitivity = 2f;

        [Header("Weave")]
        [SerializeField] float weaveRadiusSwing = 120f;
        [SerializeField] float weaveAltitudeSwing = 70f;
        [SerializeField] float weavePeriod = 9f;

        [Header("Evasion")]
        [Tooltip("Break hard (with boost) for a moment when another plane hits it.")]
        [SerializeField] bool evadeWhenHit;
        [SerializeField] float evadeDuration = 2.5f;
        [Tooltip("Seconds after a break before a hit can trigger another one.")]
        [SerializeField] float evadeCooldown = 4f;

        PlaneController _plane;
        PlaneHealth _health;
        int _racetrackEnd; // which end of the racetrack it's flying toward: 0 = +heading, 1 = -heading
        float _evadeTimeLeft, _evadeCooldownLeft;
        float _evadeSide = 1f;
        float _towTimeLeft;
        Vector3 _towDirection;
        float _towAltitude;

        /// <summary>Every active drone, so dev tools can find one without a scene search.</summary>
        public static readonly System.Collections.Generic.List<DummyPilot> All = new();

        public DummyPattern Pattern => pattern;
        public bool EvadeWhenHit { get => evadeWhenHit; set => evadeWhenHit = value; }
        public bool IsEvading => _evadeTimeLeft > 0f;
        public bool IsOnTowRun => _towTimeLeft > 0f;
        public PlaneController Plane => _plane;

        public void Configure(Vector3 orbitCenter, float orbitRadius, bool orbitClockwise) =>
            Configure(DummyPattern.Orbit, orbitCenter, orbitRadius, orbitClockwise, 0f, false);

        public void Configure(DummyPattern flightPattern, Vector3 patternCenter, float patternRadius, bool turnClockwise,
            float heading, bool evade)
        {
            pattern = flightPattern;
            center = patternCenter;
            radius = patternRadius;
            clockwise = turnClockwise;
            racetrackHeading = heading;
            evadeWhenHit = evade;
        }

        /// <summary>
        /// Puts the drone, fresh and fully repaired, at <paramref name="position"/> flying level along
        /// <paramref name="heading"/>, and holds that line for <paramref name="duration"/> seconds before
        /// it returns to its pattern. A tow run never evades, so it's the easy way to practice part shots.
        /// </summary>
        public void StartTowRun(Vector3 position, Vector3 heading, float duration)
        {
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.001f)
                heading = Vector3.forward;
            heading.Normalize();

            var respawner = GetComponent<PlaneRespawner>();
            if (respawner != null)
                respawner.CancelPendingRespawn();
            _plane.Respawn(position, Quaternion.LookRotation(heading));

            _towDirection = heading;
            _towAltitude = position.y;
            _towTimeLeft = duration;
            _evadeTimeLeft = 0f;
        }

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
        }

        void OnEnable()
        {
            All.Add(this);
            if (_health != null)
                _health.Damaged += OnDamaged;
            _plane.Crashed += OnCrashed;
        }

        void OnDisable()
        {
            All.Remove(this);
            if (_health != null)
                _health.Damaged -= OnDamaged;
            _plane.Crashed -= OnCrashed;
        }

        void OnCrashed(PlaneController plane, Vector3 point)
        {
            _evadeTimeLeft = _evadeCooldownLeft = _towTimeLeft = 0f;
        }

        void OnDamaged(PlaneHealth health, PartDamageInfo info)
        {
            if (!evadeWhenHit || IsOnTowRun || IsEvading || _evadeCooldownLeft > 0f || !_plane.IsAlive)
                return;
            // Only another plane's weapons count: dev tools and self-damage never make it flinch.
            var attacker = PlaneHealth.GetPlane(info.Source);
            if (attacker == null || attacker == _plane)
                return;

            // Break across the attacker's line of fire, toward whichever side it's already leaning.
            Vector3 right = _plane.Rotation * Vector3.right;
            Vector3 fromAttacker = _plane.Position - attacker.Position;
            float side = Vector3.Dot(right, fromAttacker);
            _evadeSide = Mathf.Abs(side) > 1f ? Mathf.Sign(side) : (Random.value < 0.5f ? -1f : 1f);
            _evadeTimeLeft = evadeDuration;
        }

        void FixedUpdate()
        {
            if (!_plane.IsAlive)
                return;

            float dt = Time.fixedDeltaTime;
            _evadeCooldownLeft = Mathf.Max(0f, _evadeCooldownLeft - dt);

            Vector3 pos = _plane.Position;
            Vector3 target;
            float sensitivity = steerSensitivity, aggressiveAngle = 15f;
            bool boost = false;

            if (_towTimeLeft > 0f)
            {
                _towTimeLeft -= dt;
                target = new Vector3(pos.x, _towAltitude, pos.z) + _towDirection * 600f;
            }
            else if (_evadeTimeLeft > 0f)
            {
                _evadeTimeLeft -= dt;
                if (_evadeTimeLeft <= 0f)
                    _evadeCooldownLeft = evadeCooldown;
                // A point off the wingtip and a little up: the autopilot banks hard and pulls.
                Vector3 right = _plane.Rotation * Vector3.right;
                Vector3 forward = _plane.Rotation * Vector3.forward;
                target = pos + (right * _evadeSide * 2f + forward * 0.5f + Vector3.up * 0.4f) * 200f;
                sensitivity = 4f;
                aggressiveAngle = 5f;
                boost = true;
            }
            else
            {
                target = PatternTarget(pos);
            }

            FlightAutopilot.SteerTowards(pos, _plane.Rotation, target, sensitivity, aggressiveAngle,
                out float pitch, out float yaw, out float roll);
            _plane.SetInput(new FlightInput { Pitch = pitch, Yaw = yaw, Roll = roll, AutoThrottle = true, Boost = boost });
        }

        Vector3 PatternTarget(Vector3 pos)
        {
            switch (pattern)
            {
                case DummyPattern.Racetrack:
                {
                    Vector3 axis = Quaternion.Euler(0f, racetrackHeading, 0f) * Vector3.forward;
                    Vector3 end = center + axis * (_racetrackEnd == 0 ? radius : -radius);
                    Vector3 flat = end - pos;
                    flat.y = 0f;
                    if (flat.magnitude < 120f)
                        _racetrackEnd = 1 - _racetrackEnd;
                    return end;
                }
                case DummyPattern.Weave:
                {
                    float phase = Time.time * Mathf.PI * 2f / Mathf.Max(1f, weavePeriod);
                    float r = radius + weaveRadiusSwing * Mathf.Sin(phase);
                    float y = center.y + weaveAltitudeSwing * Mathf.Sin(phase * 1.5f);
                    return OrbitPoint(pos, r, y);
                }
                default:
                    return OrbitPoint(pos, radius, center.y);
            }
        }

        Vector3 OrbitPoint(Vector3 pos, float orbitRadius, float altitude)
        {
            Vector3 fromCenter = pos - center;
            fromCenter.y = 0f;
            if (fromCenter.sqrMagnitude < 1f)
                fromCenter = _plane.Rotation * Vector3.forward;

            // A positive angle around +Y is clockwise seen from above.
            float angle = clockwise ? leadAngle : -leadAngle;
            Vector3 onCircle = Quaternion.AngleAxis(angle, Vector3.up) * fromCenter.normalized * orbitRadius;
            return new Vector3(center.x + onCircle.x, altitude, center.z + onCircle.z);
        }
    }
}

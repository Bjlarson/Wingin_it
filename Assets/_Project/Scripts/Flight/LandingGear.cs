using System;
using UnityEngine;

namespace WingingIt.Flight
{
    public enum GearState
    {
        /// <summary>Fully retracted.</summary>
        Up,
        /// <summary>Extending; not safe to touch down yet.</summary>
        Deploying,
        /// <summary>Fully out and locked: safe to land.</summary>
        Locked,
        /// <summary>Folding back up after a climb.</summary>
        Retracting,
    }

    /// <summary>
    /// Fully automatic landing gear (there is no gear button). It starts to extend when the plane is low and
    /// descending, takes <see cref="DeployTime"/> to lock, and folds away once the plane climbs back above
    /// <see cref="RetractHeight"/>. In between it stays where it was, so skimming low keeps the gear out.
    /// Extended gear adds drag (a lower top speed). Runs from the simulated pose in FixedUpdate, like
    /// <see cref="PlaneController"/>.
    /// </summary>
    [RequireComponent(typeof(PlaneController))]
    public class LandingGear : MonoBehaviour
    {
        [Header("Trigger")]
        [Tooltip("Gear starts to extend below this height above the ground (m)...")]
        [SerializeField] float deployHeight = 30f;
        [Tooltip("...while sinking faster than this (m/s).")]
        [SerializeField] float deployDescentSpeed = 0.5f;
        [Tooltip("Gear retracts above this height. A little above the deploy height so it doesn't flicker.")]
        [SerializeField] float retractHeight = 35f;

        [Header("Timing (s)")]
        [Tooltip("From fully up to locked.")]
        [SerializeField] float deployTime = 1.5f;
        [Tooltip("From locked to fully up.")]
        [SerializeField] float retractTime = 1.5f;

        [Header("Drag")]
        [Tooltip("Top speed multiplier while the gear is out at all.")]
        [Range(0f, 1f)] [SerializeField] float gearMaxSpeed = 0.9f;

        [Header("Wheels")]
        [Tooltip("How far the bottom of the wheels hangs below the plane's origin when locked, at visual scale 1 (m). " +
                 "Set by the scene builder from the gear legs.")]
        [SerializeField] float wheelDrop = 2.1f;

        const float ProbeRange = 500f;
        static readonly RaycastHit[] s_hits = new RaycastHit[16];

        PlaneController _plane;
        bool _wantsDown;
        bool _dragApplied;

        /// <summary>Raised whenever <see cref="State"/> changes (for the clunk sound, HUD flashes).</summary>
        public event Action<LandingGear, GearState> StateChanged;

        public GearState State { get; private set; } = GearState.Up;
        /// <summary>0 = fully up, 1 = fully out.</summary>
        public float Extension { get; private set; }
        public bool IsLocked => State == GearState.Locked;
        /// <summary>Height of the plane above whatever is below it (m); infinity if nothing is within range.</summary>
        public float HeightAboveGround { get; private set; } = float.PositiveInfinity;

        public float DeployHeight => deployHeight;
        public float RetractHeight => retractHeight;
        public float DeployTime => deployTime;
        public float RetractTime => retractTime;
        public float GearMaxSpeed => gearMaxSpeed;
        /// <summary>Height of the plane's origin above the ground when it sits on locked wheels (m), for this class's scale.</summary>
        public float WheelClearance => wheelDrop * (Plane.Stats != null ? Plane.Stats.visualScale : 1f);

        PlaneController Plane => _plane != null ? _plane : _plane = GetComponent<PlaneController>();

        void OnEnable() => Plane.Respawned += OnRespawned;
        void OnDisable() => Plane.Respawned -= OnRespawned;

        void OnRespawned(PlaneController plane) => Snap(plane.IsGrounded); // up in the air, locked on a runway spawn

        void FixedUpdate()
        {
            if (!Plane.IsAlive)
                return;
            HeightAboveGround = MeasureHeight(Plane.Position);
            Step(HeightAboveGround, Plane.Velocity.y, Time.fixedDeltaTime);
        }

        /// <summary>Advances the gear one step from the plane's height above ground and vertical speed (+ = up).</summary>
        public void Step(float heightAboveGround, float verticalSpeed, float dt)
        {
            if (heightAboveGround > retractHeight)
                _wantsDown = false;
            else if (heightAboveGround <= deployHeight && verticalSpeed < -deployDescentSpeed)
                _wantsDown = true;

            float time = _wantsDown ? deployTime : retractTime;
            Extension = Mathf.MoveTowards(Extension, _wantsDown ? 1f : 0f, dt / Mathf.Max(0.01f, time));
            Refresh();
        }

        /// <summary>Puts the gear fully up or fully locked at once (respawn, runway spawn, tests).</summary>
        public void Snap(bool down)
        {
            _wantsDown = down;
            Extension = down ? 1f : 0f;
            Refresh();
        }

        void Refresh()
        {
            var state = Extension >= 1f ? GearState.Locked
                : Extension <= 0f ? GearState.Up
                : _wantsDown ? GearState.Deploying : GearState.Retracting;

            bool drag = Extension > 0f;
            if (drag != _dragApplied)
            {
                _dragApplied = drag;
                var modifiers = Plane.Modifiers;
                modifiers.RemoveAllFromSource(this);
                if (drag)
                    modifiers.Add(FlightStat.MaxSpeed, ModifierOp.Multiply, gearMaxSpeed, this);
            }

            if (state != State)
            {
                State = state;
                StateChanged?.Invoke(this, state);
            }
        }

        /// <summary>Distance straight down to the nearest solid surface, ignoring planes and debris.</summary>
        static float MeasureHeight(Vector3 position) =>
            ProbeGround(position, out var hit) ? hit.distance : float.PositiveInfinity;

        /// <summary>
        /// The nearest solid surface straight below <paramref name="position"/> (within 500 m), ignoring planes and
        /// debris. What the gear and the wheels stand on.
        /// </summary>
        public static bool ProbeGround(Vector3 position, out RaycastHit ground)
        {
            int count = Physics.RaycastNonAlloc(position, Vector3.down, s_hits, ProbeRange,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            ground = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = s_hits[i];
                if ((found && hit.distance >= ground.distance) || PlaneDebris.IsDebris(hit.collider)
                    || hit.collider.GetComponentInParent<PlaneController>() != null)
                    continue;
                ground = hit;
                found = true;
            }
            return found;
        }
    }
}

using System;
using UnityEngine;
using WingingIt.Bots;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// Bailing out of a plane that's going down. Only a crippled plane (a part shot off or the body Critical, see
    /// <see cref="PlaneHealth.IsCrippled"/>) in the air can eject, so it's never a way out of a fair fight. Pressing
    /// <see cref="FlightInput.Eject"/> then fires the pilot out under a parachute (<see cref="EjectedPilot"/>) and ends
    /// the life with <see cref="DeathCause.Ejected"/>: whoever crippled the plane gets the kill as if it had crashed, the
    /// plane falls on as a wreck, and the respawn comes sooner (<see cref="PlaneRespawner.EjectRespawnDelay"/>) than
    /// riding it into the ground.
    /// </summary>
    [RequireComponent(typeof(PlaneController))]
    public class PlaneEjection : MonoBehaviour
    {
        [SerializeField] PilotFigure pilotPrefab;
        [SerializeField] PilotParachute parachutePrefab;
        [Tooltip("How fast the seat fires the pilot out, along the plane's up (m/s).")]
        [SerializeField] float ejectSpeed = 16f;
        [Tooltip("Where the pilot leaves the plane: this far above its origin, along its up (m, at visual scale 1).")]
        [SerializeField] float seatHeight = 1.2f;

        PlaneController _plane;
        PlaneHealth _health;
        PlaneVisuals _visuals;

        /// <summary>Raised when any plane's pilot ejects, after the plane is down.</summary>
        public static event Action<PlaneEjection, EjectedPilot> AnyEjected;

        public PlaneController Plane => _plane;
        /// <summary>The pilot from this plane's latest ejection, until it's gone.</summary>
        public EjectedPilot LastPilot { get; private set; }

        /// <summary>
        /// What the pilot wears: the player's own look (Settings, customised on the main menu's PILOT screen); every bot
        /// the default all-team look.
        /// </summary>
        public PilotLook Look => GetComponent<BotBrain>() == null ? GameSettings.Current.Data.PilotLook : PilotLook.Default;

        /// <summary>The plane is crippled and in the air: the eject button works (and shows on touch).</summary>
        public bool CanEject => CanEjectFrom(_plane.IsAlive, _plane.IsGrounded, _health != null && _health.IsCrippled);

        /// <summary>Only a live, crippled plane off the ground can eject.</summary>
        public static bool CanEjectFrom(bool alive, bool grounded, bool crippled) => alive && !grounded && crippled;

        public void SetPrefabs(PilotFigure pilot, PilotParachute parachute)
        {
            pilotPrefab = pilot;
            parachutePrefab = parachute;
        }

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
            _visuals = GetComponent<PlaneVisuals>();
        }

        void Update()
        {
            if (_plane.CurrentInput.Eject)
                TryEject();
        }

        /// <summary>Ejects if <see cref="CanEject"/>; false (and nothing happens) otherwise.</summary>
        public bool TryEject()
        {
            if (!CanEject)
                return false;
            float scale = _plane.Stats != null ? _plane.Stats.visualScale : 1f;
            Vector3 up = _plane.Rotation * Vector3.up;
            Vector3 seat = _plane.Position + up * (seatHeight * scale);
            Vector3 velocity = _plane.Velocity + up * ejectSpeed;
            Color team = _visuals != null ? _visuals.TeamColor : Color.white;
            if (pilotPrefab != null)
                LastPilot = EjectedPilot.Launch(pilotPrefab, parachutePrefab, seat, _plane.Rotation * Vector3.forward, velocity,
                    team, Look);
            _plane.Crash(_plane.Position, DeathCause.Ejected);
            AnyEjected?.Invoke(this, LastPilot);
            return true;
        }
    }
}

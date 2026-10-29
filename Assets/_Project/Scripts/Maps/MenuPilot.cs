using UnityEngine;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.Maps
{
    /// <summary>
    /// The pilot standing beside the plane on the menu stage, dressed in the player's <see cref="PilotLook"/> (redrawn the
    /// moment it changes, with TEAM in the player's team colour). While the PILOT screen is up (<see cref="Showcase"/>)
    /// it turns to face the camera, its canopy blooms open overhead and it reaches up to the risers
    /// (<see cref="PilotMove.Risers"/>), so every change shows on the figure itself.
    /// Runs on unscaled time: the menus freeze the world.
    /// </summary>
    [RequireComponent(typeof(PilotFigure))]
    public class MenuPilot : MonoBehaviour
    {
        [Tooltip("A parachute hung from the pilot's harness, hidden until the PILOT screen shows it.")]
        [SerializeField] PilotParachute parachute;
        [Tooltip("Degrees per second the pilot turns to face the camera and back.")]
        [SerializeField] float turnSpeed = 240f;

        static MenuPilot s_current;

        PilotFigure _figure;
        MatchManager _match;
        Quaternion _rest;
        PilotLook _shown;
        Color _shownTeam;
        bool _painted;
        float _bloom; // 0 = canopy packed away, 1 = fully open
        bool _showing;

        /// <summary>The menu pilot that's showing, or null.</summary>
        public static MenuPilot Current => s_current != null && s_current.isActiveAndEnabled ? s_current : null;

        public PilotFigure Figure => _figure;
        public PilotParachute Parachute => parachute;
        /// <summary>True while the PILOT screen is up: facing the camera under an open canopy.</summary>
        public bool Showcase { get; set; }
        /// <summary>Where the pilot stands (its feet).</summary>
        public Vector3 Position => transform.position;
        /// <summary>How open the canopy is (0..1).</summary>
        public float Bloom => _bloom;

        public void Configure(PilotParachute chute) => parachute = chute;

        void Awake()
        {
            _figure = GetComponent<PilotFigure>();
            if (_figure.Animator != null)
                _figure.Animator.updateMode = AnimatorUpdateMode.UnscaledTime; // the menus freeze the world
            _rest = transform.rotation;
            if (parachute != null)
                parachute.gameObject.SetActive(false);
        }

        void OnEnable() => s_current = this;

        void OnDisable()
        {
            if (s_current == this)
                s_current = null;
        }

        void LateUpdate()
        {
            Repaint();
            float dt = Time.unscaledDeltaTime;

            var view = Camera.main;
            Quaternion target = _rest;
            if (Showcase && view != null)
            {
                Vector3 flat = Vector3.ProjectOnPlane(view.transform.position - transform.position, Vector3.up);
                if (flat.sqrMagnitude > 1e-4f)
                    target = Quaternion.LookRotation(flat, Vector3.up);
            }
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * dt);

            if (Showcase != _showing)
            {
                _showing = Showcase;
                _figure.Play(Showcase ? PilotMove.Risers : PilotMove.Idle, 0.4f);
            }

            _bloom = Mathf.MoveTowards(_bloom, Showcase ? 1f : 0f, dt / EjectedPilot.ChuteOpenTime);
            if (parachute != null)
            {
                bool show = _bloom > 0f;
                if (parachute.gameObject.activeSelf != show)
                    parachute.gameObject.SetActive(show);
                parachute.SetOpen(_bloom, Time.unscaledTime);
            }
        }

        /// <summary>Paints the saved look whenever it, or the player's team colour, changes.</summary>
        void Repaint()
        {
            var look = GameSettings.Current.Data.PilotLook;
            Color team = TeamColor();
            if (_painted && look.Equals(_shown) && team == _shownTeam)
                return;
            look.ApplyTo(_figure, parachute, team);
            (_shown, _shownTeam, _painted) = (look, team, true);
        }

        /// <summary>The player's plane's team colour (the colour the plane in the hangar wears).</summary>
        Color TeamColor()
        {
            if (_match == null)
                _match = FindAnyObjectByType<MatchManager>();
            var player = _match != null ? _match.Player : null;
            var visuals = player != null ? player.GetComponent<PlaneVisuals>() : null;
            return visuals != null ? visuals.TeamColor : Color.white;
        }
    }
}

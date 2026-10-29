using Unity.Cinemachine;
using UnityEngine;
using WingingIt.Match;

namespace WingingIt.CameraSystem
{
    /// <summary>
    /// The main menu's camera: inside the home hangar it slowly circles the player's parked plane; on PLAY it eases
    /// out through the doors (<see cref="PullBack"/>, under 2 s, skippable) and holds outside, looking back in, for the
    /// game mode and class select screens. It drives the main camera directly (the Cinemachine brain is off meanwhile)
    /// on unscaled time, since the world is frozen in the menus. Once the match starts it hands back to the chase cam.
    /// </summary>
    public class MenuCamera : MonoBehaviour
    {
        [SerializeField] MatchManager match;
        [SerializeField] Camera view;

        [Header("Orbit (inside)")]
        [SerializeField] float orbitRadius = 12f;
        [SerializeField] float orbitHeight = 3.2f;
        [Tooltip("Seconds per lap.")]
        [SerializeField] float lapTime = 60f;
        [SerializeField] float fieldOfView = 55f;

        [Header("Outside")]
        [Tooltip("How far out in front of the doors the camera ends up.")]
        [SerializeField] float outsideDistance = 38f;
        [SerializeField] float outsideHeight = 5.5f;
        [SerializeField] float pullBackTime = 1.6f;

        [Header("Class select")]
        [Tooltip("Closer in for class select, so the highlighted plane reads above the cards.")]
        [SerializeField] float selectDistance = 26f;
        [Tooltip("Looks this far lower, putting the plane in the top half of the screen.")]
        [SerializeField] float selectLookDrop = 8f;
        [SerializeField] float selectEaseTime = 0.5f;

        [Header("Pilot (PILOT screen)")]
        [Tooltip("How far from the menu pilot the camera stands, out toward the doors.")]
        [SerializeField] float pilotDistance = 12f;
        [SerializeField] float pilotHeight = 2.6f;
        [Tooltip("How high on the pilot and its open canopy the camera looks.")]
        [SerializeField] float pilotLookHeight = 4.4f;
        [Tooltip("Looks this far to the pilot's left, putting it on the right of the screen beside the panel.")]
        [SerializeField] float pilotLookAside = 3.2f;
        [SerializeField] float pilotEaseTime = 0.8f;

        CinemachineBrain _brain;
        float _pilot;       // 0 = the orbit / outside, 1 = framing the menu pilot
        bool _showPilot;
        float _blend;       // 0 = orbiting inside, 1 = outside
        float _blendTarget; // where _blend is heading
        float _orbitAngle;  // degrees from the plane's nose, around it
        float _select;      // 0 = outside framing, 1 = class select framing
        bool _driving;

        public bool IsPulledBack => _blendTarget > 0.5f;
        /// <summary>True while easing in or out.</summary>
        public bool IsMoving => !Mathf.Approximately(_blend, _blendTarget) || !Mathf.Approximately(_pilot, _showPilot ? 1f : 0f);

        void Awake()
        {
            if (view == null)
                view = Camera.main;
            if (view != null)
                _brain = view.GetComponent<CinemachineBrain>();
            _orbitAngle = 140f; // start off the rear quarter: the plane's side and nose in view
        }

        /// <summary>PLAY: ease out through the hangar doors.</summary>
        public void PullBack() => _blendTarget = 1f;

        /// <summary>Back to the main menu: ease back in to the orbit.</summary>
        public void ReturnToHangar() => _blendTarget = 0f;

        /// <summary>The PILOT screen: swing round to face the menu pilot under its canopy (false: back to the orbit).</summary>
        public void ShowPilot(bool show) => _showPilot = show;

        /// <summary>Jumps to the end of the current move.</summary>
        public void Skip()
        {
            _blend = _blendTarget;
            _pilot = _showPilot ? 1f : 0f;
        }

        void LateUpdate()
        {
            var spot = match != null ? match.HangarSpot : null;
            bool menu = match != null && !match.IsDismissed && spot != null && view != null
                        && match.Phase is MatchPhase.Menu or MatchPhase.ClassSelect;
            if (menu != _driving)
            {
                _driving = menu;
                if (_brain != null)
                    _brain.enabled = !menu; // re-enabled, it cuts straight to the chase cam
                if (!menu && view != null)
                    view.fieldOfView = 60f;
            }
            if (!menu)
                return;
            if (match.Phase == MatchPhase.ClassSelect)
                _blendTarget = 1f; // Play Again lands here straight away

            float dt = Time.unscaledDeltaTime;
            _blend = Mathf.MoveTowards(_blend, _blendTarget, dt / Mathf.Max(0.01f, pullBackTime));
            var menuPilot = Maps.MenuPilot.Current;
            _pilot = Mathf.MoveTowards(_pilot, _showPilot && menuPilot != null ? 1f : 0f, dt / Mathf.Max(0.01f, pilotEaseTime));
            if (_blend <= 0f && _pilot <= 0f)
                _orbitAngle += 360f / Mathf.Max(1f, lapTime) * dt;

            // Polar path: swing round to the front first, then out through the doors, so the camera never cuts
            // through the plane or the hangar walls.
            float swing = Smooth(Mathf.Clamp01(_blend / 0.6f));
            float outward = Smooth(Mathf.Clamp01((_blend - 0.25f) / 0.75f));
            float angle = Mathf.LerpAngle(_orbitAngle, 0f, swing);
            float radius = Mathf.Lerp(orbitRadius, outsideDistance, outward);
            float height = Mathf.Lerp(orbitHeight, outsideHeight, outward);
            _select = Mathf.MoveTowards(_select, match.Phase == MatchPhase.ClassSelect ? 1f : 0f, dt / Mathf.Max(0.01f, selectEaseTime));
            float select = Smooth(_select);
            radius = Mathf.Lerp(radius, selectDistance, select);

            Vector3 pivot = match.Player != null ? match.Player.Position : spot.position;
            Vector3 nose = Vector3.ProjectOnPlane(spot.forward, Vector3.up).normalized;
            Vector3 look = pivot + Vector3.up * (Mathf.Lerp(1.2f, 2.5f, outward) - selectLookDrop * select);
            if (_pilot > 0f && menuPilot != null)
            {
                // Round the plane on the same polar path (never through it) to a spot out in front of the pilot,
                // toward the doors, looking up at it and its canopy.
                float p = Smooth(_pilot);
                Vector3 side = Vector3.ProjectOnPlane(menuPilot.Position - pivot, Vector3.up);
                // Mostly toward the doors: the side wall is only ~8 m past the pilot, and a wider angle put the camera
                // inside it.
                Vector3 toEye = (side.normalized + nose * 3f).normalized;
                Vector3 pilotEye = menuPilot.Position + toEye * pilotDistance;
                Vector3 offset = Vector3.ProjectOnPlane(pilotEye - pivot, Vector3.up);
                angle = Mathf.LerpAngle(angle, Vector3.SignedAngle(nose, offset, Vector3.up), p);
                radius = Mathf.Lerp(radius, offset.magnitude, p);
                height = Mathf.Lerp(height, pilotEye.y - pivot.y + pilotHeight, p);
                Vector3 left = Vector3.Cross(Vector3.up, toEye); // the camera's left (it looks along -toEye)
                look = Vector3.Lerp(look, menuPilot.Position + Vector3.up * pilotLookHeight + left * pilotLookAside, p);
            }
            Vector3 dir = Quaternion.AngleAxis(angle, Vector3.up) * nose;
            Vector3 eye = pivot + dir * radius + Vector3.up * height;
            view.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(look - eye, Vector3.up));
            view.fieldOfView = fieldOfView;
        }

        static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}

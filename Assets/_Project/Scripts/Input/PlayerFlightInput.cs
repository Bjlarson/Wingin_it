using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using WingingIt.CameraSystem;
using WingingIt.Flight;
using WingingIt.Settings;

namespace WingingIt.Input
{
    public enum ControlMode
    {
        /// <summary>Plane steers toward where the mouse points (War Thunder style). PC default.</summary>
        MouseAim,
        /// <summary>Stick-style: A/D roll, mouse Y or arrows pitch, Q/E yaw.</summary>
        Direct,
    }

    /// <summary>
    /// Reads the Flight action map (keyboard + mouse, gamepad, and on-screen touch controls, which
    /// act as a virtual gamepad) and turns it into <see cref="FlightInput"/> for the plane. Control mode, invert pitch
    /// and auto throttle come from <see cref="GameSettings"/> (M switches the mode and saves it). Disabled, it lets go of
    /// the stick: the plane flies on with neutral controls and its throttle where it was.
    /// </summary>
    [RequireComponent(typeof(PlaneController))]
    public class PlayerFlightInput : MonoBehaviour
    {
        [SerializeField] InputActionAsset actions;
        [SerializeField] FlightCameraRig cameraRig;
        [SerializeField] ControlMode controlMode = ControlMode.MouseAim;

        [Header("Direct mode")]
        [Tooltip("Push forward / up arrow pitches the nose down (flight-sim style).")]
        [SerializeField] bool invertPitch;
        [SerializeField] bool mousePitchInDirectMode = true;
        [SerializeField] float mousePitchSensitivity = 0.05f;

        [Header("Mouse aim")]
        [SerializeField] float aimSteerSensitivity = 4f;
        [Tooltip("Off-target angle (deg) above which the plane banks to turn instead of yawing.")]
        [SerializeField] float aggressiveTurnAngle = 12f;
        [Tooltip("How fast the aim point follows the nose while you fly with the keys (deg/s).")]
        [SerializeField] float keyOverrideRecenterSpeed = 150f;

        [Header("Touch")]
        [SerializeField] bool autoThrottleOnTouch = true;
        [Tooltip("On-screen stick travel (0-1, per axis) that does nothing, so a resting thumb doesn't steer.")]
        [SerializeField] float touchDeadzone = 0.05f;
        [Tooltip("0 = linear, 1 = cubic: finer aim near the middle of the stick, full rate at the edge.")]
        [SerializeField, Range(0f, 1f)] float touchExpo = 0.3f;
        [Tooltip("Turn rates follow the touch stick this much faster than the class's responsiveness.")]
        [SerializeField] float touchResponsiveness = 1.6f;

        PlaneController _plane;
        InputActionMap _map;
        InputAction _pitch, _roll, _yaw, _throttle, _boost, _mouseLook, _stickLook, _freeLook;
        InputAction _fireGuns, _fireMissile, _fireSecondary, _dropBomb, _ability, _toggleMode, _nextClass;
        bool _lastDeviceWasGamepad;
        bool _autoThrottle;
        float _touchSensitivity = 1f;
        ControlStyle _style;
        bool _manualThrottle; // touch: the throttle buttons took over from auto throttle, until respawn or the setting changes

        public event Action NextClassPressed;

        public ControlMode Mode => controlMode;
        public bool UsingGamepad => _lastDeviceWasGamepad;
        /// <summary>Set by <c>TouchControlsUI</c> while the on-screen controls are showing.</summary>
        public bool TouchControlsActive { get; set; }
        /// <summary>
        /// The on-screen stick, set by <c>TouchControlsUI</c>. Read raw, past the virtual gamepad's stick dead zone (made
        /// for worn thumbsticks, far too wide for a finger), and shaped by <see cref="ShapeTouchAxis"/> instead.
        /// </summary>
        public OnScreenStick TouchStick { get; set; }
        public bool MouseAimActive => controlMode == ControlMode.MouseAim && !_lastDeviceWasGamepad && !TouchControlsActive;

        // What's held right now, for the HUD's control readout.
        public bool FireGunsHeld => _fireGuns != null && _fireGuns.IsPressed();
        public bool FireMissileHeld => _fireMissile != null && _fireMissile.IsPressed();
        public bool FireSecondaryHeld => _fireSecondary != null && _fireSecondary.IsPressed();
        public bool DropBombHeld => _dropBomb != null && _dropBomb.IsPressed();
        public bool AbilityHeld => _ability != null && _ability.IsPressed();

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            if (actions == null)
            {
                Debug.LogError($"{name}: no InputActionAsset assigned to PlayerFlightInput.", this);
                enabled = false;
                return;
            }

            _map = actions.FindActionMap("Flight", throwIfNotFound: true);
            _pitch = _map.FindAction("Pitch", true);
            _roll = _map.FindAction("Roll", true);
            _yaw = _map.FindAction("Yaw", true);
            _throttle = _map.FindAction("Throttle", true);
            _boost = _map.FindAction("Boost", true);
            _mouseLook = _map.FindAction("MouseLook", true);
            _stickLook = _map.FindAction("StickLook", true);
            _freeLook = _map.FindAction("FreeLook", true);
            _fireGuns = _map.FindAction("FireGuns", true);
            _fireMissile = _map.FindAction("FireMissile", true);
            _fireSecondary = _map.FindAction("FireSecondary", true);
            _dropBomb = _map.FindAction("DropBomb", true);
            _ability = _map.FindAction("Ability", true);
            _toggleMode = _map.FindAction("ToggleControlMode", true);
            _nextClass = _map.FindAction("NextClass", true);
        }

        void OnEnable()
        {
            GameSettings.Current.Changed += ReadSettings;
            ReadSettings();
            if (_map == null)
                return;
            _map.actionTriggered += OnActionTriggered;
            _plane.Respawned += OnRespawned;
            _toggleMode.performed += OnToggleMode;
            _nextClass.performed += OnNextClass;
            _map.Enable();
        }

        void OnDisable()
        {
            GameSettings.Current.Changed -= ReadSettings;
            if (_plane != null)
            {
                _plane.SetInput(new FlightInput { AutoThrottle = _autoThrottle });
                _plane.ResponsivenessScale = 1f;
            }
            if (_map == null)
                return;
            _map.actionTriggered -= OnActionTriggered;
            _plane.Respawned -= OnRespawned;
            _toggleMode.performed -= OnToggleMode;
            _nextClass.performed -= OnNextClass;
            _map.Disable();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        void OnActionTriggered(InputAction.CallbackContext ctx)
        {
            if (ctx.phase != InputActionPhase.Performed || ctx.control == null)
                return;
            var device = ctx.control.device;
            if (device is Gamepad)
                _lastDeviceWasGamepad = true; // also the on-screen touch controls
            else if (device is Keyboard || device is Mouse)
                _lastDeviceWasGamepad = false;
        }

        void OnRespawned(PlaneController _) => _manualThrottle = false;

        void ReadSettings()
        {
            var s = GameSettings.Current.Data;
            controlMode = s.mouseAim ? ControlMode.MouseAim : ControlMode.Direct;
            invertPitch = s.invertPitch;
            _style = s.controlStyle;
            if (autoThrottleOnTouch != s.autoThrottle)
                _manualThrottle = false;
            autoThrottleOnTouch = s.autoThrottle;
            _touchSensitivity = s.touchSensitivity;
        }

        void OnToggleMode(InputAction.CallbackContext _) =>
            GameSettings.Current.Set(s => s.mouseAim = controlMode != ControlMode.MouseAim);

        void OnNextClass(InputAction.CallbackContext _) => NextClassPressed?.Invoke();

        void Update()
        {
            bool cursorLocked = UpdateCursorLock();

            float pitch = _pitch.ReadValue<float>() * (invertPitch ? -1f : 1f);
            float roll = _roll.ReadValue<float>();
            if (TouchControlsActive && TouchStick != null && TouchStick.control is InputControl<Vector2> stick)
            {
                Vector2 raw = stick.ReadUnprocessedValue();
                if (raw != Vector2.zero) // a gamepad paired to the phone still works while the thumb is off the stick
                {
                    pitch = ShapeTouchAxis(raw.y, touchDeadzone, touchExpo, _touchSensitivity) * (invertPitch ? -1f : 1f);
                    roll = ShapeTouchAxis(raw.x, touchDeadzone, touchExpo, _touchSensitivity);
                }
            }
            _plane.ResponsivenessScale = TouchControlsActive ? touchResponsiveness : 1f;
            float yaw = _yaw.ReadValue<float>();
            float throttle = _throttle.ReadValue<float>();
            if (TouchControlsActive && Mathf.Abs(throttle) >= 0.1f)
                _manualThrottle = true; // the on-screen throttle buttons: stay where they leave it
            Vector2 mouse = cursorLocked ? _mouseLook.ReadValue<Vector2>() : Vector2.zero;
            bool freeLook = _freeLook.IsPressed();

            var input = new FlightInput
            {
                Throttle = throttle,
                Boost = _boost.IsPressed(),
                // The click that captures the mouse shouldn't also fire.
                FireGuns = _fireGuns.IsPressed() && (cursorLocked || _fireGuns.activeControl?.device is not Mouse),
                FireRockets = _fireSecondary.IsPressed() && (cursorLocked || _fireSecondary.activeControl?.device is not Mouse),
                FireMissile = _fireMissile.IsPressed() && (cursorLocked || _fireMissile.activeControl?.device is not Mouse),
                DropBomb = _dropBomb.IsPressed(),
                UseAbility = _ability.IsPressed(),
                AutoThrottle = TouchControlsActive && autoThrottleOnTouch && !_manualThrottle,
                Style = ControlStyle.AutoLevel,
            };
            _autoThrottle = input.AutoThrottle;

            bool mouseAim = MouseAimActive && cameraRig != null;
            if (cameraRig != null)
            {
                cameraRig.MouseAimMode = mouseAim;
                cameraRig.FreeLookHeld = freeLook;
                cameraRig.SetStickLook(_stickLook.ReadValue<Vector2>());
            }

            if (mouseAim)
            {
                cameraRig.AddMouseDelta(invertPitch ? new Vector2(mouse.x, -mouse.y) : mouse);
                FlightAutopilot.SteerTowards(transform.position, transform.rotation, cameraRig.AimPoint,
                    aimSteerSensitivity, aggressiveTurnAngle, out float aimPitch, out float aimYaw, out float aimRoll);

                // Keys override the mouse per axis; the aim point drifts back to the nose meanwhile.
                bool pitchKey = Mathf.Abs(pitch) > 0.1f, rollKey = Mathf.Abs(roll) > 0.1f, yawKey = Mathf.Abs(yaw) > 0.1f;
                input.Pitch = pitchKey ? pitch : aimPitch;
                input.Roll = rollKey ? roll : aimRoll;
                input.Yaw = yawKey ? yaw : aimYaw;
                if (pitchKey || rollKey || yawKey)
                    cameraRig.RecenterAim(keyOverrideRecenterSpeed);
            }
            else
            {
                if (freeLook)
                    cameraRig?.AddMouseDelta(mouse);
                else if (mousePitchInDirectMode && !_lastDeviceWasGamepad)
                    pitch += mouse.y * mousePitchSensitivity * (invertPitch ? -1f : 1f);

                input.Style = _style;
                input.Pitch = Mathf.Clamp(pitch, -1f, 1f);
                input.Roll = roll;
                input.Yaw = yaw;
            }

            _plane.SetInput(input);
        }

        /// <summary>
        /// One on-screen stick axis (-1..1) to a control input: nothing inside <paramref name="deadzone"/>, then a blend
        /// of linear and cubic (<paramref name="expo"/>) up to full travel, times <paramref name="sensitivity"/>.
        /// </summary>
        public static float ShapeTouchAxis(float value, float deadzone, float expo, float sensitivity)
        {
            float a = Mathf.Abs(value);
            if (a <= deadzone)
                return 0f;
            float t = Mathf.Clamp01((a - deadzone) / Mathf.Max(0.0001f, 1f - deadzone));
            float curved = Mathf.Lerp(t, t * t * t, expo);
            return Mathf.Sign(value) * Mathf.Min(1f, curved * sensitivity);
        }

        /// <summary>Mouse is captured while flying with it. Esc releases it; clicking the game recaptures.</summary>
        bool UpdateCursorLock()
        {
            bool wantsMouse = !TouchControlsActive && !_lastDeviceWasGamepad &&
                              (controlMode == ControlMode.MouseAim || mousePitchInDirectMode);
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;

            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
                Cursor.lockState = CursorLockMode.None;
            else if (wantsMouse && mouse != null && mouse.leftButton.wasPressedThisFrame)
                Cursor.lockState = CursorLockMode.Locked;
            else if (!wantsMouse)
                Cursor.lockState = CursorLockMode.None;

            bool locked = Cursor.lockState == CursorLockMode.Locked;
            Cursor.visible = !locked;
            return locked;
        }
    }
}

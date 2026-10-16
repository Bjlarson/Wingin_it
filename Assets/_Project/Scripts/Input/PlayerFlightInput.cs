using System;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.CameraSystem;
using WingingIt.Flight;

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
    /// act as a virtual gamepad) and turns it into <see cref="FlightInput"/> for the plane.
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

        PlaneController _plane;
        InputActionMap _map;
        InputAction _pitch, _roll, _yaw, _throttle, _boost, _mouseLook, _stickLook, _freeLook;
        InputAction _fireGuns, _fireMissile, _fireSecondary, _dropBomb, _ability, _toggleMode, _nextClass;
        bool _lastDeviceWasGamepad;

        public event Action NextClassPressed;

        public ControlMode Mode => controlMode;
        public bool UsingGamepad => _lastDeviceWasGamepad;
        /// <summary>Set by <c>TouchControlsUI</c> while the on-screen controls are showing.</summary>
        public bool TouchControlsActive { get; set; }
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
            if (_map == null)
                return;
            _map.actionTriggered += OnActionTriggered;
            _toggleMode.performed += OnToggleMode;
            _nextClass.performed += OnNextClass;
            _map.Enable();
        }

        void OnDisable()
        {
            if (_map == null)
                return;
            _map.actionTriggered -= OnActionTriggered;
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

        void OnToggleMode(InputAction.CallbackContext _) =>
            controlMode = controlMode == ControlMode.MouseAim ? ControlMode.Direct : ControlMode.MouseAim;

        void OnNextClass(InputAction.CallbackContext _) => NextClassPressed?.Invoke();

        void Update()
        {
            bool cursorLocked = UpdateCursorLock();

            float pitch = _pitch.ReadValue<float>() * (invertPitch ? -1f : 1f);
            float roll = _roll.ReadValue<float>();
            float yaw = _yaw.ReadValue<float>();
            float throttle = _throttle.ReadValue<float>();
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
                AutoThrottle = TouchControlsActive && autoThrottleOnTouch && Mathf.Abs(throttle) < 0.1f,
            };

            bool mouseAim = MouseAimActive && cameraRig != null;
            if (cameraRig != null)
            {
                cameraRig.MouseAimMode = mouseAim;
                cameraRig.FreeLookHeld = freeLook;
                cameraRig.SetStickLook(_stickLook.ReadValue<Vector2>());
            }

            if (mouseAim)
            {
                cameraRig.AddMouseDelta(mouse);
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

                input.Pitch = Mathf.Clamp(pitch, -1f, 1f);
                input.Roll = roll;
                input.Yaw = yaw;
            }

            _plane.SetInput(input);
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

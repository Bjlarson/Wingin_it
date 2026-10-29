using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.Utilities;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Settings;

namespace WingingIt.UI
{
    /// <summary>
    /// Shows the on-screen stick and buttons on mobile, or after a touchscreen is touched.
    /// The controls are Input System OnScreen controls that feed a virtual gamepad, so they use
    /// the same actions as a real controller. F2 toggles them in the Editor for testing.
    /// Weapon buttons only show for the weapons the plane's class carries. Settings: Touch controls Auto (the above) /
    /// On / Off, and the button size.
    /// </summary>
    public class TouchControlsUI : MonoBehaviour
    {
        [SerializeField] GameObject controlsRoot;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] bool showOnDesktop;

        [Header("Loadout buttons")]
        [SerializeField] PlaneGuns guns;
        [SerializeField] GameObject fireButton;
        [SerializeField] PlaneRockets rockets;
        [SerializeField] GameObject rocketsButton;
        [SerializeField] PlaneMissiles missiles;
        [SerializeField] GameObject missileButton;
        [SerializeField] PlaneBombs bombs;
        [SerializeField] GameObject bombButton;
        [SerializeField] PlaneAbility ability;
        [SerializeField] GameObject abilityButton;
        [Tooltip("Hidden while the plane can't boost (Ram Royale, towing a banner, a dead engine).")]
        [SerializeField] GameObject boostButton;
        [Tooltip("Shows only while the plane can bail out (crippled, in the air). Hold it to eject.")]
        [SerializeField] PlaneEjection ejection;
        [SerializeField] GameObject ejectButton;
        [Tooltip("Throttle up / down: on the right edge, mirrored to the left with the left-hand throttle setting.")]
        [SerializeField] GameObject[] throttleButtons;

        readonly TouchOverlayState _state = new TouchOverlayState();
        IDisposable _anyButton;
        (RectTransform rt, Vector2 anchor, Vector2 pos)[] _throttleHome;
        // Each on-screen button presses the gamepad control its action is bound to, so it follows a rebind.
        (OnScreenButton button, InputAction action, int index)[] _bound;

        void Awake()
        {
            if (player != null && controlsRoot != null)
                player.TouchStick = controlsRoot.GetComponentInChildren<OnScreenStick>(true);
            _throttleHome = Array.ConvertAll(throttleButtons ?? Array.Empty<GameObject>(), b =>
            {
                var rt = (RectTransform)b.transform;
                return (rt, rt.anchorMin, rt.anchoredPosition);
            });
            FindBoundActions();
        }

        void FindBoundActions()
        {
            var map = player != null && player.Actions != null ? player.Actions.FindActionMap("Flight") : null;
            if (map == null || controlsRoot == null)
            {
                _bound = Array.Empty<(OnScreenButton, InputAction, int)>();
                return;
            }
            InputBindings.Apply(player.Actions); // the saved rebinds, in case the player's Awake hasn't run yet
            var bound = new System.Collections.Generic.List<(OnScreenButton, InputAction, int)>();
            foreach (var button in controlsRoot.GetComponentsInChildren<OnScreenButton>(true))
                foreach (var slot in InputBindings.Slots(map, InputBindings.GamepadGroup))
                    if (slot.Action.bindings[slot.Index].path == button.controlPath)
                    {
                        bound.Add((button, slot.Action, slot.Index));
                        break;
                    }
            _bound = bound.ToArray();
        }

        void OnEnable()
        {
            GameSettings.Current.Changed += ApplySize;
            ApplySize();
            _state.PadsChanged(HasRealGamepad());
            InputSystem.onDeviceChange += OnDeviceChange;
            // A pad that was already paired before the match only counts once it's used, so catch its first press too.
            _anyButton = InputSystem.onAnyButtonPress.Call(OnAnyButton);
        }

        void OnDisable()
        {
            GameSettings.Current.Changed -= ApplySize;
            InputSystem.onDeviceChange -= OnDeviceChange;
            _anyButton?.Dispose();
            _anyButton = null;
        }

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (!IsRealPad(device))
                return;
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Reconnected)
                _state.PadConnected();
            else if (change == InputDeviceChange.Removed || change == InputDeviceChange.Disconnected)
                _state.PadsChanged(HasRealGamepad());
        }

        void OnAnyButton(InputControl control)
        {
            if (IsRealPad(control.device))
                _state.PadUsed();
        }

        void ApplySize()
        {
            if (controlsRoot == null)
                return;
            var scale = Vector3.one * GameSettings.Current.Data.touchButtonSize;
            foreach (Transform child in controlsRoot.transform)
                child.localScale = scale; // each button grows around its own anchor

            bool left = GameSettings.Current.Data.touchThrottleLeft;
            foreach (var (rt, anchor, pos) in _throttleHome ?? Array.Empty<(RectTransform, Vector2, Vector2)>())
            {
                rt.anchorMin = rt.anchorMax = left ? new Vector2(1f - anchor.x, anchor.y) : anchor;
                rt.anchoredPosition = left ? new Vector2(-pos.x, pos.y) : pos;
            }

            foreach (var (button, action, index) in _bound ?? Array.Empty<(OnScreenButton, InputAction, int)>())
            {
                string path = action.bindings[index].effectivePath;
                if (button.controlPath == path)
                    continue;
                bool was = button.enabled;
                button.enabled = false; // re-registers the control on the virtual pad
                button.controlPath = path;
                button.enabled = was;
            }
        }

        void Update()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                _state.Touched();
            if (RealPadStickMoved())
                _state.PadUsed();

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
                showOnDesktop = !showOnDesktop;

            bool show = _state.Show(GameSettings.Current.Data.touchControls, Application.isMobilePlatform || showOnDesktop);
            if (player != null)
                player.TouchControlsActive = show;
            // Gesture-only play: touch input stays on, but the buttons and stick go away.
            bool showButtons = show && (player == null || !player.TouchGesturesActive);
            if (controlsRoot != null && controlsRoot.activeSelf != showButtons)
                controlsRoot.SetActive(showButtons);

            if (showButtons)
            {
                ShowIf(fireButton, guns == null || guns.IsEquipped);
                ShowIf(rocketsButton, rockets != null && rockets.IsEquipped);
                ShowIf(missileButton, missiles != null && missiles.IsEquipped);
                ShowIf(bombButton, bombs != null && bombs.IsEquipped);
                ShowIf(abilityButton, ability == null || ability.IsEquipped);
                ShowIf(boostButton, ability == null || ability.Plane == null || ability.Plane.CanBoost);
                ShowIf(ejectButton, ejection != null && ejection.CanEject);
            }
        }

        // The on-screen controls drive a virtual Gamepad, so only count pads the platform reported (native). Android
        // reports pads it has no layout for as joysticks, so those count too.
        static bool IsRealPad(InputDevice device) => device != null && device.native && (device is Gamepad || device is Joystick);

        static bool HasRealGamepad()
        {
            foreach (var device in InputSystem.devices)
                if (IsRealPad(device))
                    return true;
            return false;
        }

        static bool RealPadStickMoved()
        {
            foreach (var pad in Gamepad.all)
                if (pad.native && (pad.leftStick.ReadValue().sqrMagnitude > 0.25f || pad.rightStick.ReadValue().sqrMagnitude > 0.25f))
                    return true;
            foreach (var stick in Joystick.all)
                if (stick.native && stick.stick.ReadValue().sqrMagnitude > 0.25f)
                    return true;
            return false;
        }

        static void ShowIf(GameObject button, bool show)
        {
            if (button != null && button.activeSelf != show)
                button.SetActive(show);
        }
    }
}

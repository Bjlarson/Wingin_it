using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
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

        bool _touchSeen;

        void Awake()
        {
            if (player != null && controlsRoot != null)
                player.TouchStick = controlsRoot.GetComponentInChildren<OnScreenStick>(true);
        }

        void OnEnable()
        {
            GameSettings.Current.Changed += ApplySize;
            ApplySize();
        }

        void OnDisable() => GameSettings.Current.Changed -= ApplySize;

        void ApplySize()
        {
            if (controlsRoot == null)
                return;
            var scale = Vector3.one * GameSettings.Current.Data.touchButtonSize;
            foreach (Transform child in controlsRoot.transform)
                child.localScale = scale; // each button grows around its own anchor
        }

        void Update()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                _touchSeen = true;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
                showOnDesktop = !showOnDesktop;

            bool show = GameSettings.Current.Data.touchControls switch
            {
                TouchControlsMode.On => true,
                TouchControlsMode.Off => false,
                _ => !HasRealGamepad() && (Application.isMobilePlatform || _touchSeen || showOnDesktop),
            };
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
            }
        }

        // The on-screen controls drive a virtual Gamepad, so only count pads the platform reported (native).
        static bool HasRealGamepad()
        {
            foreach (var pad in Gamepad.all)
                if (pad.native)
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

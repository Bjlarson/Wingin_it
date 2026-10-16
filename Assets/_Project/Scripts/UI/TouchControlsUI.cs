using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Flight;
using WingingIt.Input;

namespace WingingIt.UI
{
    /// <summary>
    /// Shows the on-screen stick and buttons on mobile, or after a touchscreen is touched.
    /// The controls are Input System OnScreen controls that feed a virtual gamepad, so they use
    /// the same actions as a real controller. F2 toggles them in the Editor for testing.
    /// Weapon buttons only show for the weapons the plane's class carries.
    /// </summary>
    public class TouchControlsUI : MonoBehaviour
    {
        [SerializeField] GameObject controlsRoot;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] bool showOnDesktop;

        [Header("Loadout buttons")]
        [SerializeField] PlaneRockets rockets;
        [SerializeField] GameObject rocketsButton;
        [SerializeField] PlaneMissiles missiles;
        [SerializeField] GameObject missileButton;
        [SerializeField] PlaneBombs bombs;
        [SerializeField] GameObject bombButton;

        bool _touchSeen;

        void Update()
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                _touchSeen = true;

            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f2Key.wasPressedThisFrame)
                showOnDesktop = !showOnDesktop;

            bool show = Application.isMobilePlatform || _touchSeen || showOnDesktop;
            if (controlsRoot != null && controlsRoot.activeSelf != show)
                controlsRoot.SetActive(show);
            if (player != null)
                player.TouchControlsActive = show;

            if (show)
            {
                ShowIf(rocketsButton, rockets != null && rockets.IsEquipped);
                ShowIf(missileButton, missiles != null && missiles.IsEquipped);
                ShowIf(bombButton, bombs != null && bombs.IsEquipped);
            }
        }

        static void ShowIf(GameObject button, bool show)
        {
            if (button != null && button.activeSelf != show)
                button.SetActive(show);
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Input;

namespace WingingIt.UI
{
    /// <summary>
    /// Shows the on-screen stick and buttons on mobile, or after a touchscreen is touched.
    /// The controls are Input System OnScreen controls that feed a virtual gamepad, so they use
    /// the same actions as a real controller. F2 toggles them in the Editor for testing.
    /// </summary>
    public class TouchControlsUI : MonoBehaviour
    {
        [SerializeField] GameObject controlsRoot;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] bool showOnDesktop;

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
        }
    }
}

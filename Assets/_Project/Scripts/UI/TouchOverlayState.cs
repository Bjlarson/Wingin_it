using WingingIt.Settings;

namespace WingingIt.UI
{
    /// <summary>
    /// Which input the player is on, for <see cref="TouchControlsUI"/>: a real controller being connected or used hides
    /// the on-screen controls, touching the screen again (or the last controller going away) brings them back.
    /// </summary>
    public class TouchOverlayState
    {
        bool _padActive;
        bool _touchSeen;

        public bool PadActive => _padActive;

        public void PadConnected() => _padActive = true;
        public void PadUsed() => _padActive = true;

        /// <summary>Re-checks after a controller comes or goes: with none left, touch takes over again.</summary>
        public void PadsChanged(bool anyPad)
        {
            if (!anyPad)
                _padActive = false;
        }

        public void Touched()
        {
            _touchSeen = true;
            _padActive = false;
        }

        /// <param name="touchPlatform">A phone or tablet, or the Editor's F2 test toggle.</param>
        public bool Show(TouchControlsMode mode, bool touchPlatform) => mode switch
        {
            TouchControlsMode.On => true,
            TouchControlsMode.Off => false,
            _ => !_padActive && (touchPlatform || _touchSeen),
        };
    }
}

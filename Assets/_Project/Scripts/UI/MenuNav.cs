using UnityEngine;
using UnityEngine.InputSystem;

namespace WingingIt.UI
{
    /// <summary>The device the player last touched in the menus: picks the hint glyphs.</summary>
    public enum MenuDevice
    {
        Keyboard,
        Gamepad,
        Touch,
    }

    /// <summary>Focus and value maths for the menus. Pure, so it's EditMode-testable.</summary>
    public static class MenuMath
    {
        /// <summary>How long a newly shown panel takes to fade in (style guide: panel changes cross-fade in 120 ms).</summary>
        public const float PanelFadeTime = 0.12f;

        /// <summary>A panel's opacity <paramref name="age"/> seconds after it was shown: 0 → 1 over <see cref="PanelFadeTime"/>.</summary>
        public static float PanelFade(float age) => Mathf.Clamp01(age / PanelFadeTime);

        /// <summary>The row <paramref name="delta"/> steps from <paramref name="index"/>, wrapping around <paramref name="count"/> rows.</summary>
        public static int Step(int index, int delta, int count)
        {
            if (count <= 0)
                return 0;
            return ((index + delta) % count + count) % count;
        }

        /// <summary>A slider moved one <paramref name="notch"/> (in its own units), snapped to that grid from <paramref name="min"/> and clamped.</summary>
        public static float Nudge(float value, int dir, float min, float max, float notch)
        {
            if (notch <= 0f)
                return Mathf.Clamp(value, min, max);
            float n = Mathf.Round((value - min) / notch) + dir;
            return Mathf.Clamp(min + n * notch, min, max);
        }

        /// <summary>The 0-1 slider position of <paramref name="value"/>.</summary>
        public static float Fraction(float value, float min, float max) => max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;

        /// <summary>The menus are laid out for a virtual screen at least this wide and tall (16:9).</summary>
        public const float ReferenceWidth = 1920f, ReferenceHeight = 1080f;

        /// <summary>
        /// Where the menus draw: inside the screen's safe area (clear of a phone's notch and rounded corners), scaled so
        /// a 1920 × 1080 layout always fits: by height on wide screens (a 19.5:9 phone gets a wider virtual screen), by
        /// width on narrow ones (a 4:3 tablet gets a taller one). <paramref name="uiScale"/> above 1 is a smaller virtual
        /// screen. <paramref name="safe"/> is in Unity screen pixels (origin bottom left); the returned origin is GUI
        /// pixels (top left), the size is in virtual units.
        /// </summary>
        public static (Vector2 origin, float scale, float width, float height) Fit(float screenHeight, Rect safe, float uiScale)
        {
            if (safe.width <= 0f || safe.height <= 0f)
                safe = new Rect(0f, 0f, screenHeight * ReferenceWidth / ReferenceHeight, screenHeight);
            float scale = Mathf.Min(safe.height / ReferenceHeight, safe.width / ReferenceWidth) * Mathf.Max(0.01f, uiScale);
            var origin = new Vector2(safe.x, screenHeight - safe.yMax);
            return (origin, scale, safe.width / scale, safe.height / scale);
        }
    }

    /// <summary>
    /// One frame of menu input from the keyboard, gamepad and touch (IMGUI has no navigation of its own). Call
    /// <see cref="Read"/> once per Update; held directions repeat. Also tracks the last device used, so hints show the
    /// right glyphs, and whether the mouse moved (hover then moves the focus).
    /// </summary>
    public sealed class MenuNav
    {
        const float RepeatDelay = 0.4f, RepeatRate = 0.09f, StickThreshold = 0.5f;

        Vector2Int _held;
        float _nextRepeat;

        public bool Up, Down, Left, Right, Confirm, Back, PrevTab, NextTab, MenuButton;
        /// <summary>Back came from the gamepad's B (not Esc): in flight B is Boost, so it mustn't open the pause menu.</summary>
        public bool PadBack;
        public bool MouseMoved;
        public MenuDevice Device { get; private set; } = Application.isMobilePlatform ? MenuDevice.Touch : MenuDevice.Keyboard;

        public void Read()
        {
            Up = Down = Left = Right = Confirm = Back = PrevTab = NextTab = MenuButton = PadBack = false;
            var kb = Keyboard.current;
            var pad = Gamepad.current;
            var mouse = Mouse.current;
            MouseMoved = mouse != null && mouse.delta.ReadValue().sqrMagnitude > 0.5f;

            var dir = Vector2Int.zero;
            if (kb != null)
            {
                if (kb.upArrowKey.isPressed || kb.wKey.isPressed) dir.y += 1;
                if (kb.downArrowKey.isPressed || kb.sKey.isPressed) dir.y -= 1;
                if (kb.leftArrowKey.isPressed || kb.aKey.isPressed) dir.x -= 1;
                if (kb.rightArrowKey.isPressed || kb.dKey.isPressed) dir.x += 1;
                Confirm |= kb.enterKey.wasPressedThisFrame || kb.numpadEnterKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame;
                Back |= kb.escapeKey.wasPressedThisFrame; // also Android's back button
                PrevTab |= kb.qKey.wasPressedThisFrame;
                NextTab |= kb.eKey.wasPressedThisFrame;
                if (kb.anyKey.wasPressedThisFrame)
                    Device = MenuDevice.Keyboard;
            }
            if (pad != null)
            {
                var stick = pad.leftStick.ReadValue();
                if (pad.dpad.up.isPressed || stick.y > StickThreshold) dir.y += 1;
                if (pad.dpad.down.isPressed || stick.y < -StickThreshold) dir.y -= 1;
                if (pad.dpad.left.isPressed || stick.x < -StickThreshold) dir.x -= 1;
                if (pad.dpad.right.isPressed || stick.x > StickThreshold) dir.x += 1;
                Confirm |= pad.buttonSouth.wasPressedThisFrame;
                Back |= pad.buttonEast.wasPressedThisFrame;
                PadBack = pad.buttonEast.wasPressedThisFrame;
                PrevTab |= pad.leftShoulder.wasPressedThisFrame;
                NextTab |= pad.rightShoulder.wasPressedThisFrame;
                MenuButton |= pad.startButton.wasPressedThisFrame;
                if (pad.wasUpdatedThisFrame && (stick.sqrMagnitude > 0.25f || AnyButton(pad)))
                    Device = MenuDevice.Gamepad;
            }
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
                Device = MenuDevice.Touch;
            if (MouseMoved || (mouse != null && mouse.leftButton.wasPressedThisFrame))
                if (Device == MenuDevice.Touch && !Application.isMobilePlatform)
                    Device = MenuDevice.Keyboard;

            dir.x = Mathf.Clamp(dir.x, -1, 1);
            dir.y = Mathf.Clamp(dir.y, -1, 1);
            float now = Time.unscaledTime;
            bool fire = false;
            if (dir != _held)
            {
                _held = dir;
                fire = dir != Vector2Int.zero;
                _nextRepeat = now + RepeatDelay;
            }
            else if (dir != Vector2Int.zero && now >= _nextRepeat)
            {
                fire = true;
                _nextRepeat = now + RepeatRate;
            }
            if (fire)
            {
                Up = dir.y > 0;
                Down = dir.y < 0;
                Left = dir.x < 0;
                Right = dir.x > 0;
            }
        }

        static bool AnyButton(Gamepad pad) =>
            pad.buttonSouth.wasPressedThisFrame || pad.buttonEast.wasPressedThisFrame || pad.buttonNorth.wasPressedThisFrame ||
            pad.buttonWest.wasPressedThisFrame || pad.startButton.wasPressedThisFrame || pad.dpad.ReadValue() != Vector2.zero ||
            pad.leftShoulder.wasPressedThisFrame || pad.rightShoulder.wasPressedThisFrame;

        /// <summary>Clears this frame's presses (a screen used them, so the next one drawn mustn't act on them too).</summary>
        public void Consume() => Up = Down = Left = Right = Confirm = Back = PrevTab = NextTab = MenuButton = PadBack = false;

        /// <summary>The glyph for an action on the last device: <paramref name="key"/> on keyboard, <paramref name="button"/> on gamepad, none on touch.</summary>
        public string Glyph(string key, string button) => Device switch
        {
            MenuDevice.Gamepad => button,
            MenuDevice.Touch => null,
            _ => key,
        };
    }
}

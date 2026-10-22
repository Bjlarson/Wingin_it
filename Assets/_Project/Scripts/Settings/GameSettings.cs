using System;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Settings
{
    public enum SettingsTab
    {
        Controls,
        Graphics,
        Audio,
        Hud,
    }

    public enum TouchControlsMode
    {
        /// <summary>On for touch screens (or once one is touched), off otherwise.</summary>
        Auto,
        On,
        Off,
    }

    public enum WindowMode
    {
        Fullscreen,
        Windowed,
        Borderless,
    }

    public enum TeamColorMode
    {
        Default,
        /// <summary>Blue vs orange: a colorblind-safe pair.</summary>
        HighContrast,
    }

    /// <summary>
    /// Every per-device setting as one plain record, saved as a single JSON string. Field names are the save format:
    /// renaming one resets it to its default for existing players.
    /// </summary>
    [Serializable]
    public sealed class SettingsData
    {
        // Controls
        public bool mouseAim = true;
        /// <summary>Fly toward the mouse cursor or a held finger instead (mouse aim and direct mode are off while this is on).</summary>
        public bool followPointer;
        public float mouseAimSensitivity = 1f;
        public float freeLookSensitivity = 1f;
        /// <summary>Flips up/down on the keys, stick, touch stick and mouse (mouse aim included).</summary>
        public bool invertPitch;
        /// <summary>The chase camera is fixed to the plane's orientation, so it rolls and loops with the plane.</summary>
        public bool cameraLockedToPlane;
        /// <summary>What the stick means in Direct mode, on a pad and on touch: turn rate (auto-level or hold) or bank angle.</summary>
        public ControlStyle controlStyle = ControlStyle.AutoLevel;
        public TouchControlsMode touchControls = TouchControlsMode.Auto;
        public bool autoThrottle = true;
        /// <summary>Follow mode on touch with no on-screen buttons: hold fires guns, tap fires missile/bomb, double tap is the ability.</summary>
        public bool touchGesturesOnly;
        public float touchButtonSize = 1f;
        /// <summary>How hard the on-screen stick steers: scales the stick past its small dead zone.</summary>
        public float touchSensitivity = 1f;
        /// <summary>The player's rebound keys and buttons, as the Input System's binding-overrides JSON ("" = the shipped bindings).</summary>
        public string bindingOverrides = "";

        // Graphics
        /// <summary>Index into the quality levels; -1 = the platform's default level.</summary>
        public int quality = -1;
        /// <summary>0 x 0 = the display's native resolution.</summary>
        public int resolutionWidth;
        public int resolutionHeight;
        public WindowMode windowMode = WindowMode.Fullscreen;
        /// <summary>Frames per second; 0 = unlimited.</summary>
        public int frameRateCap = 144;
        public float renderScale = 1f;
        /// <summary>How much shadow detail: distance, map size and edge filtering. Lower on phones.</summary>
        public ShadowDetail shadows = ShadowDetail.High;
        public float fieldOfView = 60f;
        public bool speedFov = true;
        /// <summary>High dynamic range rendering. Off by default on phones, where it costs bandwidth for little gain.</summary>
        public bool hdr = true;
        /// <summary>A small frame rate / frame time readout in the top left corner.</summary>
        public bool showFps;

        // Audio (0-1)
        public float masterVolume = 0.8f;
        public float effectsVolume = 1f;
        public float alertsVolume = 1f;
        public float musicVolume = 0.6f;
        public bool muteInBackground = true;

        // HUD & accessibility
        public float hudScale = 1f;
        public float damageEffects = 1f;
        public bool reduceFlashing;
        public TeamColorMode teamColors = TeamColorMode.Default;
        /// <summary>The minimap turns with the plane (heading up) instead of keeping north up.</summary>
        public bool minimapHeadingUp;
        /// <summary>Dev builds only: the F1 help, debug overlay and dev keys.</summary>
        public bool devTools = true;

        /// <summary>
        /// Turns follow mode on or off. On a phone, follow flies on gestures alone with auto throttle; leaving follow
        /// brings the on-screen controls back and leaves the throttle setting as it is.
        /// </summary>
        public void SetFollowPointer(bool on, bool mobile)
        {
            followPointer = on;
            if (on)
                invertPitch = false;
            if (on && mobile)
            {
                touchGesturesOnly = true;
                autoThrottle = true;
            }
            else if (!on)
            {
                touchGesturesOnly = false;
            }
        }

        /// <summary>Picks mouse aim (true) or direct (false). Mouse aim turns invert pitch off.</summary>
        public void SetMouseAim(bool on)
        {
            mouseAim = on;
            if (on)
                invertPitch = false;
        }

        public SettingsData Clone() => (SettingsData)MemberwiseClone();
    }

    /// <summary>Where settings are kept. <see cref="PlayerPrefsSettingsStore"/> in the game; a fake in tests.</summary>
    public interface ISettingsStore
    {
        /// <summary>The saved JSON, or null if nothing's saved.</summary>
        string Load();
        void Save(string json);
    }

    public sealed class PlayerPrefsSettingsStore : ISettingsStore
    {
        public const string Key = "WingingIt.Settings";

        public string Load() => PlayerPrefs.HasKey(Key) ? PlayerPrefs.GetString(Key) : null;

        public void Save(string json)
        {
            PlayerPrefs.SetString(Key, json);
            PlayerPrefs.Save();
        }
    }

    /// <summary>
    /// The player's settings for this device (see Design/06-UI-UX/front-end-menus.md#settings): platform defaults,
    /// clamping, and save / load as one JSON string. Every change applies at once: <see cref="Set"/> clamps, saves and
    /// raises <see cref="Changed"/>; each system reads <see cref="Current"/> on start and on change. Nothing else reads
    /// PlayerPrefs. Pure apart from the store, so it's EditMode-testable.
    /// </summary>
    public sealed class GameSettings
    {
        public const float MinSensitivity = 0.25f, MaxSensitivity = 3f;
        public const float MinTouchButtonSize = 0.75f, MaxTouchButtonSize = 1.3f;
        public const float MinRenderScale = 0.5f, MaxRenderScale = 1f;
        public const float MinFov = 50f, MaxFov = 80f;
        public const float MinHudScale = 0.75f, MaxHudScale = 1.25f;
        public static readonly int[] DesktopFrameRates = { 30, 60, 120, 144, 0 };
        public static readonly int[] MobileFrameRates = { 30, 60, 90, 120 };

        static GameSettings s_instance;

        readonly ISettingsStore _store;
        SettingsData _data;

        public GameSettings(bool mobile, ISettingsStore store)
        {
            IsMobile = mobile;
            _store = store;
            _data = Defaults(mobile);
            string json = store?.Load();
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    var loaded = Defaults(mobile); // fields missing from an older save keep their defaults
                    JsonUtility.FromJsonOverwrite(json, loaded);
                    _data = loaded;
                }
                catch (ArgumentException)
                {
                    // corrupt save: keep the defaults
                }
            }
            Clamp(_data, mobile);
        }

        /// <summary>The game's settings, loaded from PlayerPrefs on first use.</summary>
        public static GameSettings Current => s_instance ??= new GameSettings(Application.isMobilePlatform, new PlayerPrefsSettingsStore());

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_instance = null;

        /// <summary>Swaps <see cref="Current"/> (tests: settings in memory, nothing written to PlayerPrefs). Null reloads the saved ones.</summary>
        public static void UseForTests(GameSettings settings) => s_instance = settings;

        /// <summary>Raised after every change (and after a tab reset).</summary>
        public event Action Changed;

        public bool IsMobile { get; }
        /// <summary>The live values. Read-only by convention: change them through <see cref="Set"/>.</summary>
        public SettingsData Data => _data;
        public int[] FrameRates => IsMobile ? MobileFrameRates : DesktopFrameRates;

        /// <summary>Changes settings: <paramref name="edit"/> writes to the live values, then they're clamped, saved and announced.</summary>
        public void Set(Action<SettingsData> edit)
        {
            edit(_data);
            Clamp(_data, IsMobile);
            Save();
            Changed?.Invoke();
        }

        /// <summary>Puts one tab back to the platform defaults.</summary>
        public void ResetTab(SettingsTab tab)
        {
            var d = Defaults(IsMobile);
            Set(s =>
            {
                switch (tab)
                {
                    case SettingsTab.Controls:
                        s.mouseAim = d.mouseAim;
                        s.followPointer = d.followPointer;
                        s.mouseAimSensitivity = d.mouseAimSensitivity;
                        s.freeLookSensitivity = d.freeLookSensitivity;
                        s.invertPitch = d.invertPitch;
                        s.cameraLockedToPlane = d.cameraLockedToPlane;
                        s.controlStyle = d.controlStyle;
                        s.touchControls = d.touchControls;
                        s.autoThrottle = d.autoThrottle;
                        s.touchGesturesOnly = d.touchGesturesOnly;
                        s.touchButtonSize = d.touchButtonSize;
                        s.touchSensitivity = d.touchSensitivity;
                        s.bindingOverrides = d.bindingOverrides;
                        break;
                    case SettingsTab.Graphics:
                        s.quality = d.quality;
                        s.resolutionWidth = d.resolutionWidth;
                        s.resolutionHeight = d.resolutionHeight;
                        s.windowMode = d.windowMode;
                        s.frameRateCap = d.frameRateCap;
                        s.renderScale = d.renderScale;
                        s.shadows = d.shadows;
                        s.fieldOfView = d.fieldOfView;
                        s.speedFov = d.speedFov;
                        s.hdr = d.hdr;
                        s.showFps = d.showFps;
                        break;
                    case SettingsTab.Audio:
                        s.masterVolume = d.masterVolume;
                        s.effectsVolume = d.effectsVolume;
                        s.alertsVolume = d.alertsVolume;
                        s.musicVolume = d.musicVolume;
                        s.muteInBackground = d.muteInBackground;
                        break;
                    case SettingsTab.Hud:
                        s.hudScale = d.hudScale;
                        s.damageEffects = d.damageEffects;
                        s.reduceFlashing = d.reduceFlashing;
                        s.teamColors = d.teamColors;
                        s.minimapHeadingUp = d.minimapHeadingUp;
                        s.devTools = d.devTools;
                        break;
                }
            });
        }

        void Save() => _store?.Save(JsonUtility.ToJson(_data));

        /// <summary>The out-of-the-box values for a phone or a PC.</summary>
        public static SettingsData Defaults(bool mobile)
        {
            var d = new SettingsData();
            if (mobile)
            {
                d.frameRateCap = 60;
                d.renderScale = 0.8f;
                d.hdr = false;
                d.shadows = ShadowDetail.Medium;
            }
            return d;
        }

        /// <summary>Pulls every value into its range; an unknown frame rate snaps to the nearest offered one.</summary>
        public static void Clamp(SettingsData s, bool mobile)
        {
            s.mouseAimSensitivity = Mathf.Clamp(s.mouseAimSensitivity, MinSensitivity, MaxSensitivity);
            s.freeLookSensitivity = Mathf.Clamp(s.freeLookSensitivity, MinSensitivity, MaxSensitivity);
            s.touchButtonSize = Mathf.Clamp(s.touchButtonSize, MinTouchButtonSize, MaxTouchButtonSize);
            s.touchSensitivity = Mathf.Clamp(s.touchSensitivity, MinSensitivity, MaxSensitivity);
            if (!Enum.IsDefined(typeof(ControlStyle), s.controlStyle))
                s.controlStyle = ControlStyle.AutoLevel;
            if (!Enum.IsDefined(typeof(TouchControlsMode), s.touchControls))
                s.touchControls = TouchControlsMode.Auto;

            s.quality = Mathf.Max(-1, s.quality);
            if (s.resolutionWidth <= 0 || s.resolutionHeight <= 0)
                s.resolutionWidth = s.resolutionHeight = 0;
            if (!Enum.IsDefined(typeof(WindowMode), s.windowMode))
                s.windowMode = WindowMode.Fullscreen;
            s.frameRateCap = NearestFrameRate(s.frameRateCap, mobile ? MobileFrameRates : DesktopFrameRates);
            s.renderScale = Mathf.Clamp(s.renderScale, MinRenderScale, MaxRenderScale);
            if (!Enum.IsDefined(typeof(ShadowDetail), s.shadows))
                s.shadows = ShadowDetail.High;
            s.fieldOfView = Mathf.Clamp(s.fieldOfView, MinFov, MaxFov);

            s.masterVolume = Mathf.Clamp01(s.masterVolume);
            s.effectsVolume = Mathf.Clamp01(s.effectsVolume);
            s.alertsVolume = Mathf.Clamp01(s.alertsVolume);
            s.musicVolume = Mathf.Clamp01(s.musicVolume);

            s.hudScale = Mathf.Clamp(s.hudScale, MinHudScale, MaxHudScale);
            s.damageEffects = Mathf.Clamp01(s.damageEffects);
            if (!Enum.IsDefined(typeof(TeamColorMode), s.teamColors))
                s.teamColors = TeamColorMode.Default;
        }

        static int NearestFrameRate(int fps, int[] offered)
        {
            if (fps <= 0)
                return Array.IndexOf(offered, 0) >= 0 ? 0 : offered[^1]; // unlimited, or the top one if not offered
            int best = offered[0];
            foreach (int o in offered)
                if (o > 0 && Mathf.Abs(o - fps) < Mathf.Abs(best - fps))
                    best = o;
            return best;
        }

        /// <summary>The dev overlay, F1 help and dev keys: dev builds only, and only while the Developer setting is on.</summary>
        public static bool DevToolsOn => Debug.isDebugBuild && Current._data.devTools;

        /// <summary>
        /// The on half of a blink: true for the first <paramref name="onFraction"/> of every <paramref name="period"/>,
        /// or always with Reduce flashing on (a steady light instead).
        /// </summary>
        public static bool Blink(float time, float period, float onFraction)
        {
            if (Current._data.reduceFlashing)
                return true;
            return Mathf.Repeat(time, period) < period * onFraction;
        }

        /// <summary>A pulse from 0 to 1 (for fades that throb); a steady <paramref name="steady"/> with Reduce flashing on.</summary>
        public static float Pulse(float pulse01, float steady = 1f) => Current._data.reduceFlashing ? steady : pulse01;

        // Team colours: the match's own pair by default, blue vs orange in high contrast.
        public static readonly Color HighContrastBlue = new(0.2f, 0.55f, 1f);
        public static readonly Color HighContrastOrange = new(1f, 0.6f, 0.1f);

        /// <summary><paramref name="normal"/>, or the high-contrast colour for <paramref name="team"/> 0 / 1 when that's on.</summary>
        public static Color TeamColor(int team, Color normal)
        {
            if (Current._data.teamColors != TeamColorMode.HighContrast)
                return normal;
            return team == 0 ? HighContrastBlue : team == 1 ? HighContrastOrange : normal;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Settings;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    /// <summary>
    /// The Settings screen (Design/06-UI-UX/front-end-menus.md#settings): four tabs (Controls, Graphics, Audio, HUD &amp;
    /// Accessibility), each a list of rows that apply and save the moment they change, plus RESET TAB. Opens from the
    /// main menu's gear and from the in-match menu. Call <see cref="HandleInput"/> from Update and <see cref="Draw"/>
    /// from OnGUI. Changing the resolution or window mode asks to KEEP it and reverts after 10 s otherwise.
    /// </summary>
    public sealed class SettingsPanel
    {
        const float RowH = 56f, PanelW = 1000f, Notch = 0.05f, KeepSeconds = 10f;
        static readonly string[] TabNames = { "CONTROLS", "GRAPHICS", "AUDIO", "HUD & ACCESSIBILITY" };

        sealed class Row
        {
            public string Label;
            public Func<string> Value;
            /// <summary>← / → (-1 / +1).</summary>
            public Action<int> Change;
            /// <summary>Slider position 0-1, or null for a non-slider.</summary>
            public Func<float> Fraction;
            public Action<float> SetFraction;
            /// <summary>Enter / click.</summary>
            public Action Press;
        }

        readonly GlassGUI _g;
        readonly InputActionAsset _actions;
        SettingsTab _tab;
        int _focus;
        bool _bindings;
        int _bindGroup;      // 0 = keyboard + mouse, 1 = gamepad
        int _bindFocus;
        int _bindScroll;
        InputActionRebindingExtensions.RebindingOperation _rebind;
        BindingSlot _rebinding;
        int _rebindEndFrame = -1; // the frame a rebind ended: its Esc / Start press mustn't also act as Back
        List<Row> _rows = new();
        bool _dragging;

        /// <summary>A slider is being dragged (the menus hold their UI scale until it lets go, so the panel doesn't move under the cursor).</summary>
        public bool Dragging => _dragging;

        // Resolution / window mode waiting for KEEP.
        float _keepUntil = -1f;
        (int w, int h, WindowMode mode) _previousDisplay;

        public SettingsPanel(GlassGUI g, InputActionAsset actions)
        {
            _g = g;
            _actions = actions;
            InputBindings.Apply(actions);
        }

        static GameSettings S => GameSettings.Current;
        static SettingsData D => GameSettings.Current.Data;
        bool KeepPending => _keepUntil >= 0f;

        public void Open()
        {
            _tab = SettingsTab.Controls;
            _focus = 0;
            _bindings = false;
            _rebind?.Cancel();
        }

        /// <summary>Keyboard / gamepad. True when the player backs out of Settings.</summary>
        public bool HandleInput(MenuNav nav)
        {
            if (KeepPending && Time.unscaledTime > _keepUntil)
                RevertDisplay();
            if (_bindings)
            {
                HandleBindingsInput(nav);
                return false;
            }
            if (nav.Back)
            {
                if (KeepPending)
                    RevertDisplay();
                return true;
            }
            if (nav.PrevTab || nav.NextTab)
                SwitchTab(MenuMath.Step((int)_tab, nav.NextTab ? 1 : -1, TabNames.Length));
            _rows = BuildRows();
            if (_rows.Count == 0)
                return false;
            if (nav.Up) _focus = MenuMath.Step(_focus, -1, _rows.Count);
            if (nav.Down) _focus = MenuMath.Step(_focus, 1, _rows.Count);
            _focus = Mathf.Clamp(_focus, 0, _rows.Count - 1);
            var row = _rows[_focus];
            if (nav.Left) row.Change?.Invoke(-1);
            if (nav.Right) row.Change?.Invoke(1);
            if (nav.Confirm) (row.Press ?? (() => row.Change?.Invoke(1)))();
            return false;
        }

        void SwitchTab(int tab)
        {
            if (KeepPending)
                KeepDisplay();
            _tab = (SettingsTab)tab;
            _focus = 0;
        }

        /// <summary>Draws the panel. True when the back arrow was clicked.</summary>
        public bool Draw(float width, float referenceHeight, MenuNav nav, float glow)
        {
            _rows = BuildRows();
            _focus = Mathf.Clamp(_focus, 0, Mathf.Max(0, _rows.Count - 1));
            // A long tab at a big UI scale shrinks to fit the screen.
            float fit = Mathf.Min(1f, (referenceHeight - 80f) / (96f + 64f + Mathf.Max(8, _rows.Count) * RowH + 70f));
            if (fit >= 1f)
                return DrawPanel(width, referenceHeight, nav, glow);
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.Scale(new Vector3(fit, fit, 1f));
            bool back = DrawPanel(width / fit, referenceHeight / fit, nav, glow);
            GUI.matrix = old;
            return back;
        }

        bool DrawPanel(float width, float referenceHeight, MenuNav nav, float glow)
        {
            float rowsH = Mathf.Max(8, _rows.Count) * RowH;
            var panel = Centered(width, PanelW, 96f + 64f + rowsH + 70f, Mathf.Max(40f, (referenceHeight - (96f + 64f + rowsH + 70f)) / 2f));
            _g.Panel(panel, glow);
            bool back = BackArrow(panel);
            _g.Tracked(new Rect(panel.x + 84f, panel.y + 24f, panel.width - 120f, 48f), _bindings ? "BINDINGS" : "SETTINGS", _g.Title, 5.6f, TextAnchor.MiddleLeft, Color.white);
            _g.Tracked(new Rect(panel.x + 36f, panel.y + 24f, panel.width - 72f, 48f), S.IsMobile ? "THIS PHONE" : "THIS DEVICE", _g.Hint, 2.4f, TextAnchor.MiddleRight, Steel);
            _g.Rule(new Rect(panel.x + 36f, panel.y + 80f, panel.width - 72f, 1f));

            if (_bindings)
            {
                DrawBindings(panel, nav);
                if (back && _rebind == null)
                    _bindings = false;
                return false;
            }

            DrawTabs(new Rect(panel.x + 36f, panel.y + 96f, panel.width - 72f, 48f), nav);
            float y = panel.y + 96f + 64f;
            for (int i = 0; i < _rows.Count; i++)
                DrawRow(new Rect(panel.x + 36f, y + i * RowH, panel.width - 72f, RowH), _rows[i], i, nav);
            if (KeepPending)
                _g.Tracked(new Rect(panel.x + 36f, panel.yMax - 40f, panel.width - 72f, 24f),
                    $"REVERTING DISPLAY IN {Mathf.CeilToInt(_keepUntil - Time.unscaledTime)}", _g.Hint, 1.7f, TextAnchor.MiddleLeft, Glow);
            else
                _g.Foot(panel, "CHANGES APPLY AND SAVE AT ONCE", Hint(nav, "Q / E  TABS   ESC  BACK", "LB / RB  TABS   B  BACK"));
            if (Event.current.type == EventType.MouseUp)
                _dragging = false;
            return back;
        }

        static string Hint(MenuNav nav, string keys, string pad) => nav.Glyph(keys, pad) ?? "";

        bool BackArrow(Rect panel)
        {
            var r = new Rect(panel.x + 20f, panel.y + 22f, 52f, 52f);
            bool hover = r.Contains(Event.current.mousePosition);
            _g.Tracked(r, "‹", _g.Title, 0f, TextAnchor.MiddleCenter, hover ? Color.white : Alpha(White, 0.7f), 1.2f);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        void DrawTabs(Rect r, MenuNav nav) => DrawTabStrip(r, TabNames, (int)_tab, SwitchTab, nav);

        void DrawTabStrip(Rect r, string[] names, int current, Action<int> select, MenuNav nav)
        {
            float w = r.width / names.Length;
            for (int i = 0; i < names.Length; i++)
            {
                var t = new Rect(r.x + i * w, r.y, w, r.height);
                bool on = current == i;
                bool hover = t.Contains(Event.current.mousePosition);
                if (on)
                {
                    _g.Fill(new Rect(t.x + 12f, t.yMax - 2f, t.width - 24f, 2f), Color.white);
                    _g.Fill(new Rect(t.x + 12f, t.yMax - 6f, t.width - 24f, 6f), Alpha(Glow, 0.08f));
                }
                _g.Tracked(t, names[i], _g.Hint, 2.6f, TextAnchor.MiddleCenter, on ? Color.white : Alpha(White, hover ? 0.9f : 0.55f));
                if (GUI.Button(t, GUIContent.none, GUIStyle.none))
                    select(i);
            }
            string prev = nav.Glyph("Q", "LB"), next = nav.Glyph("E", "RB");
            if (prev != null)
            {
                _g.Kbd(new Rect(r.x - 4f, r.y, 0f, r.height), prev);
                _g.Kbd(new Rect(r.xMax + 4f + _g.TrackedWidth(next, _g.Hint, 1.5f) + 16f, r.y, 0f, r.height), next);
            }
            _g.Fill(new Rect(r.x, r.yMax + 6f, r.width, 1f), Alpha(Color.white, 0.08f));
        }

        void DrawRow(Rect r, Row row, int index, MenuNav nav)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover && nav.MouseMoved && Event.current.type == EventType.Repaint)
                _focus = index;
            bool focused = index == _focus;
            if (index > 0)
                _g.Fill(new Rect(r.x, r.y, r.width, 1f), Alpha(Color.white, 0.05f));
            if (focused)
                _g.Lit(r, 1f, true);
            var tint = focused ? Color.white : Alpha(White, 0.85f);
            _g.Tracked(new Rect(r.x + 28f, r.y, r.width * 0.5f, r.height), row.Label, _g.Item, 2.6f, TextAnchor.MiddleLeft, tint, 0.9f);

            // value side: ‹ value › (sliders get a bar between)
            var valueArea = new Rect(r.x + r.width * 0.5f, r.y, r.width * 0.5f - 20f, r.height);
            string value = row.Value?.Invoke() ?? "";
            if (row.Change != null)
            {
                var left = new Rect(valueArea.x, r.y, 40f, r.height);
                var right = new Rect(valueArea.xMax - 40f, r.y, 40f, r.height);
                _g.Tracked(left, "‹", _g.Item, 0f, TextAnchor.MiddleCenter, Alpha(tint, focused ? 0.9f : 0.4f), 1.3f);
                _g.Tracked(right, "›", _g.Item, 0f, TextAnchor.MiddleCenter, Alpha(tint, focused ? 0.9f : 0.4f), 1.3f);
                if (GUI.Button(left, GUIContent.none, GUIStyle.none)) { _focus = index; row.Change(-1); }
                if (GUI.Button(right, GUIContent.none, GUIStyle.none)) { _focus = index; row.Change(1); }
                var mid = new Rect(left.xMax + 8f, r.y, right.x - left.xMax - 16f, r.height);
                if (row.Fraction != null)
                {
                    var bar = new Rect(mid.x, r.y + r.height / 2f - 2f, mid.width - 92f, 4f);
                    float f = row.Fraction();
                    _g.RoundFill(bar, 2f, Alpha(Color.white, 0.14f));
                    _g.RoundFill(new Rect(bar.x, bar.y, bar.width * f, bar.height), 2f, Alpha(Color.white, focused ? 0.95f : 0.6f));
                    _g.RoundFill(new Rect(bar.x + bar.width * f - 6f, bar.y - 6f, 12f, 16f), 3f, focused ? Color.white : Alpha(White, 0.75f));
                    _g.Tracked(new Rect(bar.xMax + 8f, r.y, 84f, r.height), value, _g.Numeral, 0f, TextAnchor.MiddleRight, tint, 0.7f);
                    var grab = new Rect(bar.x - 8f, r.y, bar.width + 16f, r.height);
                    var e = Event.current;
                    if (e.type == EventType.MouseDown && grab.Contains(e.mousePosition))
                    {
                        _dragging = true;
                        _focus = index;
                    }
                    if (_dragging && index == _focus && (e.type == EventType.MouseDown || e.type == EventType.MouseDrag))
                    {
                        row.SetFraction(Mathf.Clamp01((e.mousePosition.x - bar.x) / bar.width));
                        e.Use();
                    }
                }
                else
                {
                    _g.Tracked(mid, value, _g.Item, 2.2f, TextAnchor.MiddleCenter, tint, 0.85f);
                    if (GUI.Button(mid, GUIContent.none, GUIStyle.none)) { _focus = index; row.Change(1); }
                }
            }
            else
            {
                _g.Tracked(valueArea, value, _g.Item, 2.2f, TextAnchor.MiddleRight, Alpha(tint, 0.8f), 0.85f);
                if (row.Press != null && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    _focus = index;
                    row.Press();
                }
            }
        }

        // ---------- Rows ----------

        List<Row> BuildRows()
        {
            var rows = new List<Row>();
            if (KeepPending && _tab == SettingsTab.Graphics)
                rows.Add(new Row
                {
                    Label = "KEEP THIS DISPLAY?",
                    Value = () => $"KEEP  ({Mathf.CeilToInt(_keepUntil - Time.unscaledTime)})",
                    Press = KeepDisplay,
                });
            bool mobile = S.IsMobile;
            bool touchRows = mobile || D.touchControls == TouchControlsMode.On || Touchscreen.current != null;
            switch (_tab)
            {
                case SettingsTab.Controls:
                    rows.Add(Toggle("FOLLOW CURSOR / FINGER", () => D.followPointer, v => S.Set(s => s.SetFollowPointer(v, mobile))));
                    if (!mobile)
                    {
                        rows.Add(Choice("CONTROL MODE", new[] { "MOUSE AIM", "DIRECT" }, () => D.mouseAim ? 0 : 1, i => S.Set(s => s.SetMouseAim(i == 0))));
                        rows.Add(Slider("MOUSE AIM SENSITIVITY", () => D.mouseAimSensitivity, v => S.Set(s => s.mouseAimSensitivity = v),
                            GameSettings.MinSensitivity, GameSettings.MaxSensitivity, 0.05f, v => $"{v:0.00}×"));
                    }
                    rows.Add(Slider("FREE LOOK SENSITIVITY", () => D.freeLookSensitivity, v => S.Set(s => s.freeLookSensitivity = v),
                        GameSettings.MinSensitivity, GameSettings.MaxSensitivity, 0.05f, v => $"{v:0.00}×"));
                    rows.Add(Toggle("INVERT PITCH", () => D.invertPitch, v => S.Set(s => s.invertPitch = v)));
                    rows.Add(Choice("CAMERA", new[] { "LEVEL CHASE", "LOCKED TO PLANE" }, () => D.cameraLockedToPlane ? 1 : 0, i => S.Set(s => s.cameraLockedToPlane = i == 1)));
                    rows.Add(Choice("STICK STYLE", new[] { "AUTO-LEVEL", "HOLD", "BANK TURN" }, () => (int)D.controlStyle,
                        i => S.Set(s => s.controlStyle = (ControlStyle)i)));
                    rows.Add(Choice("TOUCH CONTROLS", new[] { "AUTO", "ON", "OFF" }, () => (int)D.touchControls, i => S.Set(s => s.touchControls = (TouchControlsMode)i)));
                    if (touchRows)
                    {
                        rows.Add(Toggle("AUTO THROTTLE (TOUCH)", () => D.autoThrottle, v => S.Set(s => s.autoThrottle = v)));
                        rows.Add(Toggle("TOUCH GESTURES ONLY (FOLLOW)", () => D.touchGesturesOnly, v => S.Set(s => s.touchGesturesOnly = v)));
                        rows.Add(Slider("TOUCH BUTTON SIZE", () => D.touchButtonSize, v => S.Set(s => s.touchButtonSize = v),
                            GameSettings.MinTouchButtonSize, GameSettings.MaxTouchButtonSize, Notch, v => $"{v * 100f:0}%"));
                        rows.Add(Choice("TOUCH THROTTLE", new[] { "RIGHT HAND", "LEFT HAND" }, () => D.touchThrottleLeft ? 1 : 0,
                            i => S.Set(s => s.touchThrottleLeft = i == 1)));
                        rows.Add(Slider("TOUCH STICK SENSITIVITY", () => D.touchSensitivity, v => S.Set(s => s.touchSensitivity = v),
                            GameSettings.MinSensitivity, GameSettings.MaxSensitivity, 0.05f, v => $"{v:0.00}×"));
                    }
                    // On a phone only a controller's buttons rebind (the touch buttons follow them).
                    if (_actions != null)
                        rows.Add(new Row { Label = mobile ? "CONTROLLER BINDINGS" : "KEY BINDINGS", Value = () => "REBIND  ›", Press = OpenBindings });
                    break;

                case SettingsTab.Graphics:
                    var names = QualitySettings.names;
                    rows.Add(Choice("QUALITY", ToUpper(names), () => D.quality >= 0 ? D.quality : QualitySettings.GetQualityLevel(), i => S.Set(s => s.quality = i)));
                    if (!mobile)
                    {
                        var res = Resolutions();
                        rows.Add(Choice("RESOLUTION", res.ConvertAll(r => $"{r.x} × {r.y}").ToArray(), () => ResolutionIndex(res),
                            i => ChangeDisplay(s => { s.resolutionWidth = res[i].x; s.resolutionHeight = res[i].y; })));
                        rows.Add(Choice("WINDOW MODE", new[] { "FULLSCREEN", "WINDOWED", "BORDERLESS" }, () => (int)D.windowMode,
                            i => ChangeDisplay(s => s.windowMode = (WindowMode)i)));
                    }
                    var rates = S.FrameRates;
                    rows.Add(Choice("FRAME RATE CAP", Array.ConvertAll(rates, f => f > 0 ? f.ToString() : "UNLIMITED"),
                        () => Mathf.Max(0, Array.IndexOf(rates, D.frameRateCap)), i => S.Set(s => s.frameRateCap = rates[i])));
                    rows.Add(Slider("RENDER SCALE", () => D.renderScale, v => S.Set(s => s.renderScale = v),
                        GameSettings.MinRenderScale, GameSettings.MaxRenderScale, Notch, v => $"{v * 100f:0}%"));
                    rows.Add(Choice("SHADOWS", new[] { "OFF", "LOW", "MEDIUM", "HIGH" }, () => (int)D.shadows, i => S.Set(s => s.shadows = (ShadowDetail)i)));
                    rows.Add(Choice("TREES", new[] { "LOW", "MEDIUM", "HIGH" }, () => (int)D.trees, i => S.Set(s => s.trees = (TreeDetail)i)));
                    rows.Add(Choice("RENDER DISTANCE", new[] { "LOW", "MEDIUM", "HIGH" }, () => (int)D.renderDistance,
                        i => S.Set(s => s.renderDistance = (RenderDistance)i)));
                    rows.Add(Slider("FIELD OF VIEW", () => D.fieldOfView, v => S.Set(s => s.fieldOfView = v),
                        GameSettings.MinFov, GameSettings.MaxFov, 1f, v => $"{v:0}°"));
                    rows.Add(Toggle("SPEED FOV EFFECT", () => D.speedFov, v => S.Set(s => s.speedFov = v)));
                    rows.Add(Toggle("HDR", () => D.hdr, v => S.Set(s => s.hdr = v)));
                    rows.Add(Toggle("SHOW FRAME RATE", () => D.showFps, v => S.Set(s => s.showFps = v)));
                    break;

                case SettingsTab.Audio:
                    rows.Add(Percent("MASTER", () => D.masterVolume, v => S.Set(s => s.masterVolume = v)));
                    rows.Add(Percent("EFFECTS", () => D.effectsVolume, v => S.Set(s => s.effectsVolume = v)));
                    rows.Add(Percent("ALERTS", () => D.alertsVolume, v => S.Set(s => s.alertsVolume = v)));
                    rows.Add(Percent("MUSIC", () => D.musicVolume, v => S.Set(s => s.musicVolume = v)));
                    if (!mobile)
                        rows.Add(Toggle("MUTE IN BACKGROUND", () => D.muteInBackground, v => S.Set(s => s.muteInBackground = v)));
                    break;

                case SettingsTab.Hud:
                    rows.Add(Slider("UI SCALE", () => D.hudScale, v => S.Set(s => s.hudScale = v),
                        GameSettings.MinHudScale, GameSettings.MaxHudScale, Notch, v => $"{v * 100f:0}%"));
                    rows.Add(Percent("DAMAGE SCREEN EFFECTS", () => D.damageEffects, v => S.Set(s => s.damageEffects = v)));
                    rows.Add(Toggle("REDUCE FLASHING", () => D.reduceFlashing, v => S.Set(s => s.reduceFlashing = v)));
                    rows.Add(Choice("TEAM COLORS", new[] { "DEFAULT", "HIGH CONTRAST" }, () => (int)D.teamColors, i => S.Set(s => s.teamColors = (TeamColorMode)i)));
                    rows.Add(Choice("MINIMAP", new[] { "NORTH UP", "HEADING UP" }, () => D.minimapHeadingUp ? 1 : 0, i => S.Set(s => s.minimapHeadingUp = i == 1)));
                    if (Debug.isDebugBuild)
                    {
                        rows.Add(Toggle("DEVELOPER TOOLS", () => D.devTools, v => S.Set(s => s.devTools = v)));
                        // Playtest A/B: applies from each plane's next spawn; the dev panel shows the average time to kill.
                        rows.Add(Slider("COMBAT TEST: PLANE HP", () => D.combatHealth, v => S.Set(s => s.combatHealth = v),
                            GameSettings.MinCombatHealth, 1f, 0.05f, v => $"{v * 100f:0}%"));
                        rows.Add(Toggle("COMBAT TEST: BIG HITBOXES", () => D.combatBigHitboxes, v => S.Set(s => s.combatBigHitboxes = v)));
                    }
                    break;
            }
            rows.Add(new Row { Label = "RESET TAB", Value = () => "DEFAULTS", Press = ResetTab });
            return rows;
        }

        void ResetTab()
        {
            if (_tab == SettingsTab.Graphics && !S.IsMobile)
                ChangeDisplay(_ => { });
            S.ResetTab(_tab);
            if (_tab == SettingsTab.Controls)
                InputBindings.Apply(_actions);
        }

        static Row Toggle(string label, Func<bool> get, Action<bool> set) => new()
        {
            Label = label,
            Value = () => get() ? "ON" : "OFF",
            Change = _ => set(!get()),
        };

        static Row Choice(string label, string[] options, Func<int> get, Action<int> set) => new()
        {
            Label = label,
            Value = () => options.Length > 0 ? options[Mathf.Clamp(get(), 0, options.Length - 1)] : "",
            Change = dir =>
            {
                if (options.Length > 1)
                    set(MenuMath.Step(Mathf.Clamp(get(), 0, options.Length - 1), dir, options.Length));
            },
        };

        static Row Slider(string label, Func<float> get, Action<float> set, float min, float max, float notch, Func<float, string> format) => new()
        {
            Label = label,
            Value = () => format(get()),
            Change = dir => set(MenuMath.Nudge(get(), dir, min, max, notch)),
            Fraction = () => MenuMath.Fraction(get(), min, max),
            SetFraction = f => set(MenuMath.Nudge(Mathf.Lerp(min, max, f), 0, min, max, notch)),
        };

        static Row Percent(string label, Func<float> get, Action<float> set) =>
            Slider(label, get, set, 0f, 1f, Notch, v => $"{v * 100f:0}%");

        static string[] ToUpper(string[] names) => Array.ConvertAll(names, n => n.ToUpperInvariant());

        // ---------- Display (PC) ----------

        static List<Vector2Int> Resolutions()
        {
            var list = new List<Vector2Int>();
            foreach (var r in Screen.resolutions)
            {
                var v = new Vector2Int(r.width, r.height);
                if (!list.Contains(v))
                    list.Add(v);
            }
            var native = new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            if (!list.Contains(native))
                list.Add(native);
            return list;
        }

        static int ResolutionIndex(List<Vector2Int> res)
        {
            var want = D.resolutionWidth > 0 ? new Vector2Int(D.resolutionWidth, D.resolutionHeight)
                : new Vector2Int(Screen.currentResolution.width, Screen.currentResolution.height);
            int i = res.IndexOf(want);
            return i >= 0 ? i : res.Count - 1;
        }

        /// <summary>A resolution or window mode change: applies now, reverts in 10 s unless the player keeps it.</summary>
        void ChangeDisplay(Action<SettingsData> edit)
        {
            if (!KeepPending)
            {
                _previousDisplay = (D.resolutionWidth, D.resolutionHeight, D.windowMode);
                _focus++; // the KEEP row appears on top: stay on the same setting
            }
            S.Set(edit);
            _keepUntil = Time.unscaledTime + KeepSeconds;
        }

        void KeepDisplay()
        {
            if (!KeepPending)
                return;
            _keepUntil = -1f;
            if (_tab == SettingsTab.Graphics)
                _focus = Mathf.Max(0, _focus - 1); // the KEEP row is gone
        }

        void RevertDisplay()
        {
            KeepDisplay();
            var (w, h, mode) = _previousDisplay;
            S.Set(s =>
            {
                s.resolutionWidth = w;
                s.resolutionHeight = h;
                s.windowMode = mode;
            });
        }

        // ---------- Bindings ----------

        const float BindRowH = 44f;
        static readonly string[] BindGroupNames = { "KEYBOARD + MOUSE", "GAMEPAD" };
        static readonly string[] MobileBindGroupNames = { "GAMEPAD" };

        string BindGroup => _bindGroup == 0 ? InputBindings.KeyboardGroup : InputBindings.GamepadGroup;

        InputActionMap FlightMap => _actions != null ? _actions.FindActionMap("Flight") : null;

        List<BindingSlot> BindSlots()
        {
            var map = FlightMap;
            return map != null ? InputBindings.Slots(map, BindGroup) : new List<BindingSlot>();
        }

        void OpenBindings()
        {
            _bindings = true;
            _bindFocus = 0;
            _bindScroll = 0;
            if (S.IsMobile)
                _bindGroup = 1;
        }

        void SetBindGroup(int group)
        {
            if (S.IsMobile)
                return; // a phone has no keyboard page
            _bindGroup = group;
            _bindFocus = 0;
            _bindScroll = 0;
        }

        /// <summary>Keyboard / gamepad on the bindings page. While a rebind waits, the rebinder owns the input.</summary>
        void HandleBindingsInput(MenuNav nav)
        {
            if (_rebind != null || Time.frameCount == _rebindEndFrame)
                return;
            if (nav.Back)
            {
                _bindings = false;
                return;
            }
            if (nav.PrevTab || nav.NextTab || nav.Left || nav.Right)
                SetBindGroup(1 - _bindGroup);
            var slots = BindSlots();
            int count = slots.Count + 1; // the last row resets the device's bindings
            if (nav.Up) _bindFocus = MenuMath.Step(_bindFocus, -1, count);
            if (nav.Down) _bindFocus = MenuMath.Step(_bindFocus, 1, count);
            _bindFocus = Mathf.Clamp(_bindFocus, 0, count - 1);
            if (nav.Confirm)
                ActivateBinding(slots, _bindFocus);
            else if (_bindFocus < slots.Count && (Keyboard.current?.deleteKey.wasPressedThisFrame ?? false))
            {
                InputBindings.Reset(slots[_bindFocus]);
                InputBindings.Save(_actions);
            }
        }

        void ActivateBinding(List<BindingSlot> slots, int index)
        {
            var map = FlightMap;
            if (map == null || _rebind != null)
                return;
            if (index >= slots.Count)
            {
                InputBindings.ResetGroup(map, BindGroup);
                InputBindings.Save(_actions);
                return;
            }
            _rebinding = slots[index];
            _rebind = InputBindings.Begin(_actions, map, BindGroup, _rebinding, _ =>
            {
                _rebind = null;
                _rebindEndFrame = Time.frameCount;
            });
        }

        void DrawBindings(Rect panel, MenuNav nav)
        {
            if (FlightMap == null)
                return;
            if (S.IsMobile)
                DrawTabStrip(new Rect(panel.x + 36f, panel.y + 96f, panel.width - 72f, 48f), MobileBindGroupNames, 0, _ => { }, nav);
            else
                DrawTabStrip(new Rect(panel.x + 36f, panel.y + 96f, panel.width - 72f, 48f), BindGroupNames, _bindGroup,
                    i => { if (_rebind == null) SetBindGroup(i); }, nav);

            var slots = BindSlots();
            int total = slots.Count + 1;
            float x = panel.x + 36f, w = panel.width - 72f, top = panel.y + 96f + 64f;
            int visible = Mathf.Max(1, Mathf.FloorToInt((panel.yMax - 80f - top) / BindRowH));
            _bindFocus = Mathf.Clamp(_bindFocus, 0, total - 1);

            var e = Event.current;
            if (e.type == EventType.ScrollWheel && panel.Contains(e.mousePosition))
            {
                _bindScroll += e.delta.y > 0f ? 1 : -1;
                e.Use();
            }
            _bindScroll = Mathf.Clamp(_bindScroll, 0, Mathf.Max(0, total - visible));
            if (_bindFocus < _bindScroll) _bindScroll = _bindFocus;
            if (_bindFocus >= _bindScroll + visible) _bindScroll = _bindFocus - visible + 1;

            bool waiting = _rebind != null;
            for (int row = 0; row < visible && _bindScroll + row < total; row++)
            {
                int index = _bindScroll + row;
                var r = new Rect(x, top + row * BindRowH, w - 14f, BindRowH);
                bool hover = r.Contains(e.mousePosition);
                if (hover && nav.MouseMoved && e.type == EventType.Repaint && !waiting)
                    _bindFocus = index;
                bool focused = index == _bindFocus;
                bool isReset = index >= slots.Count;
                bool capturing = waiting && !isReset && slots[index].Action == _rebinding.Action && slots[index].Index == _rebinding.Index;
                if (row > 0)
                    _g.Fill(new Rect(r.x, r.y, r.width, 1f), Alpha(Color.white, 0.05f));
                if (focused)
                    _g.Lit(r, 1f, true);
                var tint = focused ? Color.white : Alpha(White, 0.85f);
                string label = isReset ? "RESET " + BindGroupNames[_bindGroup] : slots[index].Label;
                string value = isReset ? "DEFAULTS" : capturing ? (_bindGroup == 0 ? "PRESS A KEY…" : "PRESS A BUTTON…") : slots[index].Display;
                _g.Tracked(new Rect(r.x + 28f, r.y, r.width * 0.5f, r.height), label, _g.Item, 2.2f, TextAnchor.MiddleLeft, tint, 0.8f);
                _g.Tracked(new Rect(r.x + r.width * 0.5f, r.y, r.width * 0.5f - 20f, r.height), value, _g.Item, 2.0f, TextAnchor.MiddleRight,
                    capturing ? Glow : Alpha(tint, 0.9f), 0.8f);
                if (!waiting && GUI.Button(r, GUIContent.none, GUIStyle.none))
                {
                    _bindFocus = index;
                    ActivateBinding(slots, index);
                }
            }

            if (total > visible) // scroll thumb
            {
                var track = new Rect(x + w - 6f, top, 3f, visible * BindRowH);
                _g.RoundFill(track, 1.5f, Alpha(Color.white, 0.1f));
                float f0 = _bindScroll / (float)total, f1 = (_bindScroll + visible) / (float)total;
                _g.RoundFill(new Rect(track.x, track.y + track.height * f0, track.width, track.height * (f1 - f0)), 1.5f, Alpha(Color.white, 0.5f));
            }

            if (waiting)
                _g.Foot(panel, _bindGroup == 0 ? "PRESS THE NEW KEY OR MOUSE BUTTON" : "PRESS THE NEW BUTTON",
                    Hint(nav, "ESC  CANCEL", "START  CANCEL"));
            else
                _g.Foot(panel, _bindGroup == 0 ? "A KEY ALREADY IN USE SWAPS" : "STICKS ARE FIXED  ·  A BUTTON ALREADY IN USE SWAPS",
                    Hint(nav, "ENTER  REBIND   DEL  RESET   Q / E  DEVICE   ESC  BACK", "A  REBIND   LB / RB  DEVICE   B  BACK"));
        }
    }
}

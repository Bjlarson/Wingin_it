using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Bots;
using WingingIt.CameraSystem;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.Settings;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    /// <summary>
    /// Every screen outside the cockpit, drawn with IMGUI over the <see cref="MatchManager"/>
    /// (Design/06-UI-UX/front-end-menus.md): the main menu over the player's plane in its hangar, the game mode cards,
    /// class select, the 3-2-1, the clock + team score bar in play, the in-match menu (Esc / Start /
    /// ☰, the match keeps running) with the live scoreboard, Settings, and the end-of-match scoreboard. In play it also
    /// draws the class picker (while dead, and parked + repaired at home with Tab or CHANGE PLANE). Everything works
    /// with mouse, keyboard (arrows, Enter, Esc, Q / E), gamepad (D-pad / stick, A, B, LB / RB) and touch, through
    /// <see cref="MenuNav"/>. Styled to Design/06-UI-UX/glass-menu-style-guide.html through <see cref="GlassGUI"/>.
    /// </summary>
    public class MatchScreens : MonoBehaviour
    {
        [SerializeField] MatchManager match;
        [Tooltip("Circles the parked plane in the hangar; pulls back through the doors on PLAY.")]
        [SerializeField] MenuCamera menuCamera;
        [Tooltip("For the read-only bindings list in Settings.")]
        [SerializeField] InputActionAsset actions;

        const float ReferenceHeight = 1080f;
        static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };

        /// <summary>Which screen of the menu phase is up.</summary>
        enum Front
        {
            Main,
            GameMode,
        }

        // Main menu rows (keyboard / gamepad focus): PLAY, then the gear and QUIT in the corner.
        const int MainPlay = 0, MainSettings = 1, MainQuit = 2;
        // In-match menu rows.
        const int MenuResume = 0, MenuSettings = 1, MenuLeave = 2;

        readonly GlassGUI _g = new();
        readonly MenuNav _nav = new();
        SettingsPanel _settings;
        bool _settingsOpen;
        Front _front;
        int _focus;
        int _mode;           // highlighted game mode card (index into GameModeInfo.All)
        List<int> _cards;    // the cards on the game mode screen (modes in development only in dev builds)
        int _modeRow;        // 0 = the card, 1 = its BOTS row
        int _highlight = -1; // highlighted class card on class select
        bool _leaveArmed;    // LEAVE MATCH pressed once: the next press confirms
        object _screen;
        float _shownAt;

        static bool CanQuit => !Application.isMobilePlatform && Application.platform != RuntimePlatform.WebGLPlayer;

        void Awake()
        {
            useGUILayout = false; // no GUILayout here: skips IMGUI's layout pass
            _settings = new SettingsPanel(_g, actions);
            _cards = GameModeInfo.MenuCards(Debug.isDebugBuild);
        }

        void OnDestroy() => _g.Dispose();

        // ---------- Input ----------

        void Update()
        {
            if (match == null || match.IsDismissed)
                return;
            _nav.Read();
            TrackScreen();

            if (_settingsOpen && match.Phase != MatchPhase.Menu && !match.MenuOpen)
                _settingsOpen = false; // time ran out with Settings open over the match
            if (_settingsOpen)
            {
                if (_settings.HandleInput(_nav))
                    CloseSettings();
                return;
            }

            switch (match.Phase)
            {
                case MatchPhase.Menu: MenuInput(); break;
                case MatchPhase.ClassSelect: ClassSelectInput(); break;
                case MatchPhase.Countdown:
                case MatchPhase.Playing: PlayInput(); break;
                case MatchPhase.Ended: ScoreboardInput(); break;
            }
        }

        /// <summary>What's on screen, to re-run the open glow when it changes.</summary>
        object ScreenKey() => (match.Phase, _front, _settingsOpen, match.MenuOpen, match.Picker);

        /// <summary>Notes when the screen changes, for the open glow and the fade-in. Checked in OnGUI too, so a screen
        /// opened by input this frame starts faded out rather than flashing in for a frame.</summary>
        void TrackScreen()
        {
            var screen = ScreenKey();
            if (Equals(screen, _screen))
                return;
            _screen = screen;
            _shownAt = Time.unscaledTime;
        }

        void MenuInput()
        {
            if (menuCamera != null && menuCamera.IsMoving && (_nav.Confirm || _nav.Back))
            {
                menuCamera.Skip(); // the pull-back is skippable
                return;
            }
            if (_front == Front.Main)
            {
                int rows = CanQuit ? 3 : 2;
                if (_nav.Up) _focus = MenuMath.Step(_focus, -1, rows);
                if (_nav.Down) _focus = MenuMath.Step(_focus, 1, rows);
                if (_nav.Confirm)
                    ActivateMain(_focus);
                return;
            }

            int pos = Mathf.Max(0, _cards.IndexOf(_mode));
            var mode = GameModeInfo.All[_cards[pos]];
            if (_nav.Up || _nav.Down)
                _modeRow = mode.HasBotDifficulty ? 1 - _modeRow : 0;
            int dir = _nav.Left ? -1 : _nav.Right ? 1 : 0;
            if (dir != 0)
            {
                if (_modeRow == 1 || _cards.Count == 1)
                    StepDifficulty(mode, dir);
                else
                    _mode = _cards[Mathf.Clamp(pos + dir, 0, _cards.Count - 1)];
            }
            if (_nav.Confirm)
                ConfirmMode();
            else if (_nav.Back)
                BackToMain();
        }

        void ActivateMain(int row)
        {
            switch (row)
            {
                case MainPlay:
                    _front = Front.GameMode;
                    _mode = Mathf.Max(0, match.ModeIndex);
                    _modeRow = 0;
                    if (menuCamera != null)
                        menuCamera.PullBack();
                    break;
                case MainSettings:
                    OpenSettings();
                    break;
                case MainQuit:
                    Quit();
                    break;
            }
        }

        void BackToMain()
        {
            _front = Front.Main;
            _focus = MainPlay;
            if (menuCamera != null)
                menuCamera.ReturnToHangar();
        }

        void ConfirmMode()
        {
            if (!GameModeInfo.All[_mode].IsPlayable)
                return; // in development: the card shows, PLAY doesn't
            _highlight = match.SelectedClass;
            match.SelectMode(_mode);
            match.OpenClassSelect();
        }

        void StepDifficulty(GameModeInfo mode, int dir) =>
            match.SetDifficulty(mode, (BotDifficulty)Mathf.Clamp((int)MatchManager.DifficultyFor(mode) + dir, 0, DifficultyNames.Length - 1));

        void ClassSelectInput()
        {
            int n = match.Classes.Count;
            if (_highlight < 0)
                _highlight = match.SelectedClass;
            if (_nav.Left) _highlight = MenuMath.Step(_highlight, -1, n);
            if (_nav.Right) _highlight = MenuMath.Step(_highlight, 1, n);
            match.PreviewClass(_highlight); // the highlighted plane waits in the hangar
            if (_nav.Back)
            {
                _front = Front.GameMode;
                match.BackToMenu();
            }
            else if (_nav.Confirm)
                match.SelectClass(_highlight);
            var keyboard = Keyboard.current;
            if (keyboard != null)
                for (int i = 0; i < n && i < 4; i++)
                    if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                        match.SelectClass(i);
        }

        void PlayInput()
        {
            bool toggle = _nav.Back || _nav.MenuButton;
            if (!match.MenuOpen)
            {
                if (toggle)
                {
                    // Esc closes an open airfield picker first; a second Esc opens the menu.
                    if (_nav.Back && match.Picker == ClassPicker.Airfield)
                        match.CloseAirfieldPicker();
                    else
                        OpenMenu();
                    return;
                }
                PickerInput();
                return;
            }

            if (_nav.Up || _nav.Down)
            {
                _focus = MenuMath.Step(_focus, _nav.Up ? -1 : 1, 3);
                _leaveArmed = false;
            }
            if (_nav.Confirm)
                ActivateMenu(_focus);
            else if (toggle)
            {
                if (_leaveArmed && !_nav.MenuButton)
                    _leaveArmed = false;
                else
                    match.CloseMenu();
            }
        }

        void OpenMenu()
        {
            match.OpenMenu();
            _focus = MenuResume;
            _leaveArmed = false;
        }

        void ActivateMenu(int row)
        {
            _focus = row;
            switch (row)
            {
                case MenuResume:
                    match.CloseMenu();
                    break;
                case MenuSettings:
                    _leaveArmed = false;
                    OpenSettings();
                    break;
                case MenuLeave:
                    if (_leaveArmed)
                        match.ReturnToMainMenu();
                    else
                        _leaveArmed = true;
                    break;
            }
        }

        void PickerInput()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null || match.Phase != MatchPhase.Playing)
                return;
            if (match.Picker == ClassPicker.None)
            {
                if (keyboard.tabKey.wasPressedThisFrame)
                    match.OpenAirfieldPicker();
                return;
            }
            if (match.Picker == ClassPicker.Airfield && keyboard.tabKey.wasPressedThisFrame)
            {
                match.CloseAirfieldPicker();
                return;
            }
            for (int i = 0; i < 4; i++)
                if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                    PickInPlay(i);
        }

        void PickInPlay(int index)
        {
            if (match.Picker == ClassPicker.Respawn)
                match.ChooseRespawnClass(index);
            else
                match.ChangeClassAtAirfield(index);
        }

        void ScoreboardInput()
        {
            if (_nav.Left || _nav.Right || _nav.Up || _nav.Down)
                _focus = 1 - Mathf.Clamp(_focus, 0, 1);
            if (_nav.Confirm)
            {
                if (_focus == 1)
                    match.ReturnToMainMenu();
                else
                    match.PlayAgain();
            }
            else if (_nav.Back)
                match.ReturnToMainMenu();
        }

        void OpenSettings()
        {
            _settings.Open();
            _settingsOpen = true;
        }

        void CloseSettings() => _settingsOpen = false;

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------- Drawing ----------

        void OnGUI()
        {
            if (match == null || match.IsDismissed)
                return;
            _g.EnsureStyles();
            TrackScreen();
            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            // A new screen fades in (the backdrop, score bar and menu button stay put; IMGUI can't keep the old one up).
            _g.Opacity = MenuMath.PanelFade(Time.unscaledTime - _shownAt);

            switch (match.Phase)
            {
                case MatchPhase.Menu:
                    if (_settingsOpen)
                        DrawSettings(width, true);
                    else if (_front == Front.Main)
                        DrawMainMenu(width);
                    else
                        DrawGameModes(width);
                    break;
                case MatchPhase.ClassSelect: DrawClassSelect(width); break;
                case MatchPhase.Countdown:
                case MatchPhase.Playing:
                    if (match.Phase == MatchPhase.Countdown)
                        DrawCountdown(width);
                    else if (match.Picker != ClassPicker.None)
                        DrawPicker(width);
                    else if (match.CanChangeClassAtAirfield && !match.MenuOpen)
                        DrawSwapPrompt(width);
                    _g.Opacity = 1f;
                    DrawScoreBar(width);
                    _g.Opacity = MenuMath.PanelFade(Time.unscaledTime - _shownAt);
                    if (match.MenuOpen)
                    {
                        if (_settingsOpen)
                            DrawSettings(width, true);
                        else
                            DrawInMatchMenu(width);
                    }
                    else if (ShowMenuButton)
                    {
                        _g.Opacity = 1f;
                        DrawMenuButton(width);
                    }
                    break;
                case MatchPhase.Ended: DrawScoreboard(width); break;
            }
            _g.Opacity = 1f;
        }

        /// <summary>The dark wash behind a screen, at full strength while the screen itself fades in.</summary>
        void Backdrop(float width)
        {
            float fade = _g.Opacity;
            _g.Opacity = 1f;
            _g.Fill(new Rect(0f, 0f, width, ReferenceHeight), Dim);
            _g.Opacity = fade;
        }

        /// <summary>Outer glow: 14% when a screen opens, settling to 7% over 180 ms.</summary>
        float OpenGlow()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _shownAt) / 0.18f);
            return Mathf.Lerp(GlowOpen, GlowRest, t);
        }

        /// <summary>Focus follows the mouse when it moves over a row.</summary>
        void HoverFocus(Rect r, int row, ref int focus)
        {
            if (_nav.MouseMoved && Event.current.type == EventType.Repaint && r.Contains(Event.current.mousePosition))
                focus = row;
        }

        /// <summary>The bottom hint strip, in the last device's glyphs (nothing on touch).</summary>
        void HintStrip(float width, string keys, string pad)
        {
            string text = _nav.Glyph(keys, pad);
            if (string.IsNullOrEmpty(text))
                return;
            _g.Shadowed(new Rect(0f, ReferenceHeight - 52f, width, 28f), text, _g.Hint, 3f, TextAnchor.MiddleCenter, Alpha(White, 0.75f));
        }

        void DrawSettings(float width, bool dim)
        {
            if (dim)
                Backdrop(width);
            if (_settings.Draw(width, ReferenceHeight, _nav, OpenGlow()))
                CloseSettings();
        }

        // ---------- Main menu ----------

        void DrawMainMenu(float width)
        {
            // Left third: the title, then a one-row glass panel. The hangar and the plane fill the rest.
            float x = Mathf.Max(60f, width * 0.08f);
            _g.Shadowed(new Rect(x, 300f, 700f, 90f), "WINGING IT", _g.Huge, 8f, TextAnchor.MiddleLeft, Color.white, 0.55f);
            _g.Shadowed(new Rect(x + 4f, 392f, 700f, 30f), "ARCADE DOGFIGHTS", _g.Hint, 6f, TextAnchor.MiddleLeft, Alpha(White, 0.8f));

            var panel = new Rect(x, 460f, 420f, 56f + 40f);
            _g.Panel(panel, OpenGlow());
            var play = new Rect(panel.x, panel.y + 20f, panel.width, 56f);
            HoverFocus(play, MainPlay, ref _focus);
            if (_g.Button(play, "PLAY", _nav.Glyph("ENTER", "A"), _focus == MainPlay, false, false))
                ActivateMain(MainPlay);

            // Corner: the gear (Settings) and, on PC, QUIT. The build version bottom left.
            var gear = new Rect(width - 92f, ReferenceHeight - 92f, 56f, 56f);
            HoverFocus(gear, MainSettings, ref _focus);
            if (IconButton(gear, _focus == MainSettings, DrawGear))
                ActivateMain(MainSettings);
            if (CanQuit)
            {
                var quit = new Rect(gear.x - 120f, gear.y, 108f, 56f);
                HoverFocus(quit, MainQuit, ref _focus);
                bool on = _focus == MainQuit || quit.Contains(Event.current.mousePosition);
                _g.Shadowed(quit, "QUIT", _g.Item, 3f, TextAnchor.MiddleCenter, on ? Color.white : Alpha(White, 0.6f), 0.85f);
                if (_focus == MainQuit)
                    _g.Fill(new Rect(quit.x + 24f, quit.yMax - 10f, quit.width - 48f, 2f), Color.white);
                if (GUI.Button(quit, GUIContent.none, GUIStyle.none))
                    ActivateMain(MainQuit);
            }
            _g.Shadowed(new Rect(28f, ReferenceHeight - 52f, 400f, 28f), $"V{Application.version}", _g.Hint, 2f, TextAnchor.MiddleLeft, Alpha(White, 0.5f));
            HintStrip(width, "ARROWS  MOVE    ENTER  SELECT", "D-PAD  MOVE    A  SELECT");
        }

        /// <summary>A small round glass button holding an icon. True when clicked.</summary>
        bool IconButton(Rect r, bool focused, System.Action<Rect, Color> icon)
        {
            bool hover = r.Contains(Event.current.mousePosition);
            _g.RoundFill(r, r.width / 2f, new Color(0.12f, 0.13f, 0.15f, 0.72f));
            _g.RoundStroke(r, r.width / 2f, focused ? 2f : 1f, Alpha(Color.white, focused ? 0.9f : hover ? 0.5f : 0.22f));
            icon(r, focused || hover ? Color.white : Alpha(White, 0.75f));
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>A cog: eight rounded teeth round a thick ring.</summary>
        void DrawGear(Rect r, Color c)
        {
            Vector2 center = r.center;
            for (int i = 0; i < 8; i++)
            {
                float a = i * Mathf.PI / 4f;
                var tooth = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 11.5f;
                _g.RoundFill(new Rect(tooth.x - 3.5f, tooth.y - 3.5f, 7f, 7f), 2f, c);
            }
            _g.RoundStroke(new Rect(center.x - 10f, center.y - 10f, 20f, 20f), 10f, 5f, c);
        }

        // ---------- Game mode select ----------

        const float CardW = 316f, CardH = 470f, CardGap = 24f;

        void DrawGameModes(float width)
        {
            _g.Fill(new Rect(0f, 0f, width, ReferenceHeight), Alpha(Dim, 0.25f));
            _g.Shadowed(new Rect(0f, 110f, width, 60f), "GAME MODE", _g.Title, 8f, TextAnchor.MiddleCenter, Color.white);

            int n = _cards.Count;
            float total = n * CardW + (n - 1) * CardGap;
            float s = Mathf.Min(1f, (width - 80f) / total); // narrow screens: shrink to fit
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3((width - total * s) / 2f, 230f, 0f), Quaternion.identity, new Vector3(s, s, 1f));
            float glow = OpenGlow();
            for (int k = 0; k < n; k++)
            {
                int i = _cards[k];
                var mode = GameModeInfo.All[i];
                var card = new Rect(k * (CardW + CardGap), 0f, CardW, CardH + 100f);
                if (_nav.MouseMoved && Event.current.type == EventType.Repaint && _mode != i && card.Contains(Event.current.mousePosition))
                {
                    _mode = i; // focus follows the mouse
                    _modeRow = 0;
                }
                bool selected = i == _mode;
                _g.Panel(card, selected ? Mathf.Max(glow, GlowSettle) : glow * 0.5f);

                var head = new Rect(card.x, card.y + 22f, card.width, 44f);
                if (selected && _modeRow == 0)
                    _g.Lit(head, 1f, true);
                string name = mode.Name.ToUpperInvariant();
                float fit = Mathf.Min(1f, (card.width - 56f) / _g.TrackedWidth(name, _g.Item, 3.4f)); // long names shrink
                _g.Tracked(new Rect(card.x + 28f, head.y, card.width - 56f, head.height), name, _g.Item, 3.4f, TextAnchor.MiddleLeft, Color.white, fit);
                _g.Fill(new Rect(card.x + 28f, head.yMax + 8f, card.width - 56f, 1f), Alpha(Color.white, 0.14f));

                DrawModeSketch(new Rect(card.x + 28f, head.yMax + 24f, card.width - 56f, 120f), mode.Sketch);
                GUI.color = _g.Faded(Body);
                GUI.Label(new Rect(card.x + 28f, head.yMax + 158f, card.width - 56f, 96f), mode.Pitch, _g.BodyText);
                GUI.color = Color.white;

                float y = head.yMax + 262f;
                Fact(card, y, "PLAYERS", mode.Players);
                Fact(card, y + 34f, "LENGTH", $"{Mathf.FloorToInt(mode.Length / 60f)}:{Mathf.RoundToInt(mode.Length % 60f):00}");

                if (mode.HasBotDifficulty)
                {
                    // BOTS, then the three levels as chips across the card; each mode keeps its own
                    _g.Tracked(new Rect(card.x + 28f, y + 70f, card.width - 56f, 26f), "BOTS", _g.Hint, 2.4f, TextAnchor.MiddleLeft, Steel);
                    var row = new Rect(card.x, y + 96f, card.width, 48f);
                    if (selected && _modeRow == 1)
                        _g.Lit(row, 1f, true);
                    var level = MatchManager.DifficultyFor(mode);
                    float bw = (card.width - 56f) / DifficultyNames.Length;
                    for (int d = 0; d < DifficultyNames.Length; d++)
                    {
                        var chip = new Rect(row.x + 28f + d * bw, row.y + 6f, bw - 8f, row.height - 12f);
                        bool on = (int)level == d;
                        if (on)
                            _g.RoundFill(chip, 6f, Alpha(Color.white, 0.14f));
                        _g.RoundStroke(chip, 6f, 1f, Alpha(Color.white, on ? 0.6f : 0.12f));
                        _g.Tracked(chip, DifficultyNames[d].ToUpperInvariant(), _g.Hint, 1.6f, TextAnchor.MiddleCenter, on ? Color.white : Alpha(White, 0.6f));
                        if (GUI.Button(chip, GUIContent.none, GUIStyle.none))
                        {
                            _mode = i;
                            _modeRow = 1;
                            match.SetDifficulty(mode, (BotDifficulty)d);
                        }
                    }
                }

                var play = new Rect(card.x, card.yMax - 78f, card.width, 56f);
                if (!mode.IsPlayable)
                    _g.Button(play, "IN DEVELOPMENT", null, false, true, true, TextAnchor.MiddleCenter);
                else if (_g.Button(play, "PLAY", _nav.Glyph("ENTER", "A"), selected && _modeRow == 0, false, true))
                {
                    _mode = i;
                    ConfirmMode();
                }
                if (GUI.Button(card, GUIContent.none, GUIStyle.none)) // a click or tap anywhere else on the card picks it
                {
                    _mode = i;
                    _modeRow = 0;
                }
            }

            if (BackButton(new Rect(total / 2f - 130f, CardH + 100f + 36f, 260f, 56f)))
                BackToMain();
            GUI.matrix = old;
            if (n == 1)
                HintStrip(width, "ARROWS  BOTS    ENTER  PLAY    ESC  BACK", "D-PAD  BOTS    A  PLAY    B  BACK");
            else
                HintStrip(width, "LEFT / RIGHT  MODE    UP / DOWN  BOTS    ENTER  PLAY    ESC  BACK", "D-PAD  MODE / BOTS    A  PLAY    B  BACK");
        }

        void Fact(Rect card, float y, string label, string value)
        {
            _g.Tracked(new Rect(card.x + 28f, y, card.width - 56f, 30f), label, _g.Hint, 2.4f, TextAnchor.MiddleLeft, Steel);
            _g.Tracked(new Rect(card.x + 28f, y, card.width - 56f, 30f), value, _g.Numeral, 1f, TextAnchor.MiddleRight, White, 0.7f);
        }

        /// <summary>A game mode card's picture: a simple top-down sketch in the team colours.</summary>
        void DrawModeSketch(Rect r, ModeSketch sketch)
        {
            _g.RoundFill(r, 8f, new Color(0f, 0f, 0f, 0.25f));
            Color west = match.TeamColor(0), east = match.TeamColor(1);
            switch (sketch)
            {
                case ModeSketch.Teams: // two lines of planes, nose to nose
                    Island(r);
                    for (int team = 0; team < 2; team++)
                        for (int k = 0; k < 4; k++)
                        {
                            float y = r.y + 26f + k * 24f;
                            float x = team == 0 ? r.x + 34f + (k % 2) * 16f : r.xMax - 34f - (k % 2) * 16f;
                            TinyPlane(new Vector2(x, y), team == 0 ? 90f : -90f, team == 0 ? west : east);
                        }
                    _g.Tracked(r, "VS", _g.Hint, 4f, TextAnchor.MiddleCenter, Alpha(White, 0.5f));
                    break;

                case ModeSketch.Banner: // East tows West's banner away from its empty stand, a West plane gives chase
                    Island(r);
                    BannerStand(new Vector2(r.x + 30f, r.yMax - 16f), west, false);
                    BannerStand(new Vector2(r.xMax - 30f, r.yMax - 16f), east, true);
                    var carrier = new Vector2(r.center.x + 34f, r.y + 40f); // heading home to East's stand
                    for (int d = 0; d < 6; d++) // the cable
                        _g.Fill(new Rect(carrier.x - 17f - d * 9f, carrier.y + 3f + d * 3f, 5f, 2f), Alpha(White, 0.55f));
                    _g.Fill(new Rect(carrier.x - 94f, carrier.y + 18f, 28f, 12f), west); // the banner
                    TinyPlane(carrier, 90f, east);
                    TinyPlane(carrier + new Vector2(-8f, -22f), 90f, east); // escort
                    TinyPlane(carrier + new Vector2(-114f, 50f), 90f, west); // chaser
                    break;

                case ModeSketch.Domination: // three fields A / B / C, one each and one neutral, a plane landing on B
                    Island(r);
                    string[] letters = { "A", "B", "C" };
                    Color[] owners = { west, Alpha(White, 0.45f), east };
                    for (int f = 0; f < 3; f++)
                    {
                        var c = new Vector2(r.x + r.width * (0.2f + 0.3f * f), r.center.y + (f == 1 ? -16f : 6f));
                        var field = new Rect(c.x - 22f, c.y - 22f, 44f, 44f);
                        _g.RoundFill(field, 22f, Alpha(owners[f], 0.22f));
                        _g.RoundStroke(field, 22f, 2f, owners[f]);
                        _g.Fill(new Rect(c.x - 16f, c.y + 28f, 32f, 3f), Alpha(White, 0.35f)); // its runway
                        _g.Tracked(field, letters[f], _g.Item, 0f, TextAnchor.MiddleCenter, Color.white, 0.8f);
                    }
                    TinyPlane(new Vector2(r.center.x - 40f, r.center.y + 16f), 90f, east);
                    break;

                case ModeSketch.RamRoyale: // a ring of planes of every colour, all pointing in
                    var mid = r.center;
                    float ring = r.height / 2f - 8f;
                    _g.RoundStroke(new Rect(mid.x - ring, mid.y - ring, ring * 2f, ring * 2f), ring, 2f, new Color(1f, 0.55f, 0.3f, 0.7f));
                    for (int p = 0; p < 8; p++)
                    {
                        float a = p * 45f + 20f;
                        var at = mid + (Vector2)(Quaternion.Euler(0f, 0f, a) * Vector3.up) * (ring - 16f);
                        TinyPlane(at, 180f - a, Color.HSVToRGB(p / 8f, 0.55f, 1f));
                    }
                    break;
            }
        }

        /// <summary>The faint island under a sketch.</summary>
        void Island(Rect r) =>
            _g.RoundFill(new Rect(r.x + r.width * 0.3f, r.y + r.height * 0.2f, r.width * 0.4f, r.height * 0.6f), 30f, Alpha(Color.white, 0.05f));

        /// <summary>A pole with its team's base, and the flag on it unless it has been taken.</summary>
        void BannerStand(Vector2 foot, Color team, bool flying)
        {
            _g.Fill(new Rect(foot.x - 1f, foot.y - 56f, 2f, 56f), Alpha(White, 0.7f));
            _g.Fill(new Rect(foot.x - 8f, foot.y - 2f, 16f, 3f), team);
            if (flying)
                _g.Fill(new Rect(foot.x - 27f, foot.y - 56f, 26f, 14f), team);
        }

        /// <summary>A tiny plane (fuselage, wings, tail) centred on <paramref name="at"/>, nose along <paramref name="heading"/> (0 = up, clockwise).</summary>
        void TinyPlane(Vector2 at, float heading, Color color)
        {
            var old = GUI.matrix;
            // turn about the plane's centre in the current (scaled) GUI space; RotateAroundPivot works in screen space
            GUI.matrix = old * Matrix4x4.TRS(at, Quaternion.Euler(0f, 0f, heading), Vector3.one) * Matrix4x4.Translate(-at);
            _g.Fill(new Rect(at.x - 2f, at.y - 8f, 4f, 16f), color);
            _g.Fill(new Rect(at.x - 8f, at.y - 4f, 16f, 4f), color);
            _g.Fill(new Rect(at.x - 4f, at.y + 6f, 8f, 2f), color);
            GUI.matrix = old;
        }

        bool BackButton(Rect r)
        {
            _g.Panel(r, GlowRest, false);
            return _g.Button(r, "BACK", _nav.Glyph("ESC", "B"), false, false, false, TextAnchor.MiddleCenter);
        }

        // ---------- Class select ----------

        /// <summary>Class select's cards: shorter than the in-play picker's, low on the screen, so the highlighted
        /// plane shows in the hangar above them (no dark wash either).</summary>
        const float SelectCardH = 420f;

        void DrawClassSelect(float width)
        {
            if (_highlight < 0)
                _highlight = match.SelectedClass;
            int n = match.Classes.Count;
            float cardsW = n * CardW + (n - 1) * CardGap;
            float s = Mathf.Min(1f, (width - 80f) / cardsW); // narrow screens: shrink to fit
            var old = GUI.matrix;
            float left = (width - cardsW * s) / 2f;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(left, 0f, 0f), Quaternion.identity, new Vector3(s, s, 1f));

            _g.Shadowed(new Rect(0f, 40f / s, cardsW, 60f), "CHOOSE YOUR PLANE", _g.Title, 8f, TextAnchor.MiddleCenter, Color.white);
            float top = (ReferenceHeight - 140f) / s - SelectCardH; // the bottom edge stays put as the cards shrink
            int picked = ClassCards(0f, top, SelectCardH, "FLY", _highlight, true);
            if (picked >= 0)
                match.SelectClass(picked);
            if (BackButton(new Rect(cardsW / 2f - 130f, top + SelectCardH + 18f, 260f, 56f)))
            {
                _front = Front.GameMode;
                match.BackToMenu();
            }
            GUI.matrix = old;
            HintStrip(width, "ARROWS  MOVE    ENTER  FLY    1 – 4  PICK    ESC  BACK", "D-PAD  MOVE    A  FLY    B  BACK");
        }

        /// <summary>
        /// In play: while dead, picks the class to respawn in (the timer keeps running); parked and repaired at home,
        /// swaps class on the spot. The world runs underneath.
        /// </summary>
        void DrawPicker(float width)
        {
            Backdrop(width);
            bool respawn = match.Picker == ClassPicker.Respawn;
            string title = respawn ? $"RESPAWN IN {Mathf.CeilToInt(match.RespawnTimeLeft)}" : "CHANGE PLANE";
            string hint = respawn ? "1 – 4 OR CLICK TO PICK YOUR NEXT PLANE" : $"AT {match.SwapAirfield?.DisplayName.ToUpperInvariant()}  ·  1 – 4 OR CLICK  ·  ESC OR TAB KEEP THIS ONE";
            _g.Tracked(new Rect(0f, 110f, width, 60f), title, _g.Title, 8f, TextAnchor.MiddleCenter, Color.white);
            _g.Tracked(new Rect(0f, 176f, width, 24f), hint, _g.Hint, 3.4f, TextAnchor.MiddleCenter, Steel);
            int n = match.Classes.Count;
            float cardsW = n * CardW + (n - 1) * CardGap;
            int picked = ClassCards((width - cardsW) / 2f, 250f, CardH, respawn ? "PICK" : "SWAP", match.SelectedClass, false);
            if (picked >= 0 && !match.MenuOpen)
                PickInPlay(picked);
            if (!respawn && !match.MenuOpen && _g.Button(new Rect(width / 2f - 130f, 250f + CardH + 36f, 260f, 56f), "KEEP", "ESC", false, false, false, TextAnchor.MiddleCenter))
                match.CloseAirfieldPicker();
        }

        /// <summary>Parked and repaired at home: a small clickable (and touchable) "change plane" prompt above the HUD's bottom edge.</summary>
        void DrawSwapPrompt(float width)
        {
            var r = new Rect(width / 2f - 170f, ReferenceHeight - 270f, 340f, 56f);
            _g.Panel(r, GlowRest, false);
            if (_g.Button(r, "CHANGE PLANE", "TAB", false, false, false, TextAnchor.MiddleCenter))
                match.OpenAirfieldPicker();
        }

        /// <summary>
        /// One card per class (stats, loadout, ability, how many teammates fly it) with a <paramref name="button"/> row.
        /// Returns the index whose button was pressed this frame, or -1. With <paramref name="hover"/>, the mouse moves
        /// the highlight.
        /// </summary>
        int ClassCards(float x0, float top, float height, string button, int selectedIndex, bool hover)
        {
            var classes = match.Classes;
            int n = classes.Count;
            int[] team = match.TeamComposition();
            float glow = OpenGlow();
            int pressed = -1;
            for (int i = 0; i < n; i++)
            {
                var stats = classes[i];
                if (stats == null)
                    continue;
                var card = new Rect(x0 + i * (CardW + CardGap), top, CardW, height);
                if (hover)
                    HoverFocus(card, i, ref _highlight);
                bool selected = i == selectedIndex;
                _g.Panel(card, selected ? Mathf.Max(glow, GlowSettle) : glow * 0.5f);

                var head = new Rect(card.x, card.y + 22f, card.width, 44f);
                if (selected)
                    _g.Lit(head, 1f, true);
                _g.Fill(new Rect(card.x + 28f, head.y + 17f, 10f, 10f), stats.bodyColor);
                _g.Tracked(new Rect(card.x + 50f, head.y, card.width - 100f, head.height), stats.displayName.ToUpperInvariant(), _g.Item, 2.8f,
                    TextAnchor.MiddleLeft, selected ? Color.white : Alpha(White, 0.88f));
                _g.Kbd(new Rect(card.xMax - 28f, head.y, 0f, head.height), (i + 1).ToString());
                _g.Fill(new Rect(card.x + 28f, head.yMax + 8f, card.width - 56f, 1f), Alpha(Color.white, 0.14f));

                GUI.color = _g.Faded(Body);
                GUI.Label(new Rect(card.x + 28f, head.yMax + 22f, card.width - 56f, 250f), Describe(stats), _g.BodyText);
                GUI.color = Color.white;

                int mates = i < team.Length ? team[i] : 0;
                _g.Tracked(new Rect(card.x + 28f, card.yMax - 124f, card.width - 56f, 30f), "ON YOUR TEAM", _g.Hint, 2.4f, TextAnchor.MiddleLeft, Steel);
                _g.Tracked(new Rect(card.x + 28f, card.yMax - 124f, card.width - 56f, 30f), mates.ToString(), _g.Numeral, 0f, TextAnchor.MiddleRight,
                    mates == 0 ? Alpha(White, 0.5f) : White, 0.75f);

                if (_g.Button(new Rect(card.x, card.yMax - 78f, card.width, 56f), button, null, selected, false, true))
                    pressed = i;
            }
            return pressed;
        }

        static string Describe(FlightStats s)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Speed   {s.minSpeed * 3.6f:0} – {s.maxSpeed * 3.6f:0} km/h");
            sb.AppendLine($"Turn      {s.pitchRate:0}°/s pitch, {s.rollRate:0}°/s roll");
            if (s.durability != null)
                sb.AppendLine($"Body     {s.durability.body:0} HP");
            sb.AppendLine();
            sb.AppendLine("Guns");
            if (s.rockets != null) sb.AppendLine("Rockets");
            if (s.missiles != null) sb.AppendLine("Homing missiles");
            if (s.bombs != null) sb.AppendLine("Bombs");
            if (s.ability != null)
            {
                sb.AppendLine();
                sb.AppendLine($"Ability: {s.ability.displayName}");
            }
            return sb.ToString();
        }

        // ---------- Countdown + in play ----------

        void DrawCountdown(float width)
        {
            int n = Mathf.CeilToInt(match.TimeLeft);
            var big = new Rect(0f, ReferenceHeight * 0.33f, width, 190f);
            string text = n > 0 ? n.ToString() : "GO";
            // faint halo: the guide keeps glows low and edge-only, so just a ring of offset copies
            foreach (var o in new[] { new Vector2(-3, 0), new Vector2(3, 0), new Vector2(0, -3), new Vector2(0, 3) })
                _g.Tracked(new Rect(big.x + o.x, big.y + o.y, big.width, big.height), text, _g.Huge, 10f, TextAnchor.MiddleCenter, Alpha(Glow, 0.10f));
            _g.Tracked(big, text, _g.Huge, 10f, TextAnchor.MiddleCenter, Color.white);
            _g.Tracked(new Rect(0f, big.yMax + 4f, width, 30f), "GET READY", _g.Hint, 5f, TextAnchor.MiddleCenter, Steel);
        }

        void DrawScoreBar(float width)
        {
            var bar = new Rect(width / 2f - 240f, 16f, 480f, 64f);
            _g.Panel(bar, GlowRest, false);
            if ((match.Mode.Widgets & MatchWidgets.TeamScores) != 0)
            {
                DrawTeamScore(new Rect(bar.x + 22f, bar.y, 150f, bar.height), 0, TextAnchor.MiddleLeft);
                DrawTeamScore(new Rect(bar.xMax - 172f, bar.y, 150f, bar.height), 1, TextAnchor.MiddleRight);
            }
            float t = match.Phase == MatchPhase.Countdown ? match.MatchLength : match.TimeLeft;
            bool pulse = match.Phase == MatchPhase.Playing && t <= 30f && GameSettings.Blink(Time.unscaledTime, 1f, 0.5f);
            _g.Tracked(new Rect(bar.x + 170f, bar.y, 140f, bar.height), Clock(t), _g.Numeral, 1.5f, TextAnchor.MiddleCenter,
                pulse ? Glow : Color.white);
        }

        void DrawTeamScore(Rect r, int team, TextAnchor anchor)
        {
            // team name in its colour (the one functional use of colour on the glass), score in white
            _g.Tracked(new Rect(r.x, r.y - 12f, r.width, r.height), match.TeamName(team).ToUpperInvariant(), _g.Hint, 3f, anchor, match.TeamColor(team));
            _g.Tracked(new Rect(r.x, r.y + 10f, r.width, r.height), match.Score.TeamScore(team).ToString(), _g.Numeral, 1f, anchor, Color.white);
        }

        static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>Touch screens get a ☰ button (top right, above the kill feed): there's no Esc.</summary>
        bool ShowMenuButton => Application.isMobilePlatform || Touchscreen.current != null || _nav.Device == MenuDevice.Touch;

        void DrawMenuButton(float width)
        {
            var r = new Rect(width - 96f, 196f, 72f, 64f);
            if (IconButton(new Rect(r.x + 4f, r.y, 64f, 64f), false, DrawBurger))
                OpenMenu();
        }

        void DrawBurger(Rect r, Color c)
        {
            for (int i = -1; i <= 1; i++)
                _g.RoundFill(new Rect(r.center.x - 13f, r.center.y + i * 8f - 1.5f, 26f, 3f), 1.5f, c);
        }

        // ---------- In-match menu ----------

        /// <summary>
        /// Esc / Start / ☰ in play: RESUME, SETTINGS, LEAVE MATCH (press twice), and the live scoreboard beside it.
        /// The match keeps running behind it.
        /// </summary>
        void DrawInMatchMenu(float width)
        {
            _g.Fill(new Rect(0f, 0f, width, ReferenceHeight), Alpha(Dim, 0.3f));
            // Below the minimap and score bar, above the instrument cluster.
            const float panelW = 460f, tableW = 1100f, gap = 28f;
            float total = panelW + gap + tableW;
            float s = Mathf.Min(1f, (width - 60f) / total);
            var old = GUI.matrix;
            float left = (width - total * s) / 2f;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(left, 300f, 0f), Quaternion.identity, new Vector3(s, s, 1f));

            var panel = new Rect(0f, 0f, panelW, 96f + 3f * 56f + 24f + 70f);
            _g.Panel(panel, OpenGlow());
            _g.Header(panel, "MATCH", match.Phase == MatchPhase.Countdown ? "STARTING" : $"{Clock(match.TimeLeft)} LEFT");
            string[] labels = { "RESUME", "SETTINGS", _leaveArmed ? "LEAVE? TEAM PLAYS ON WITH A BOT" : "LEAVE MATCH" };
            for (int i = 0; i < labels.Length; i++)
            {
                var row = new Rect(panel.x, panel.y + 96f + i * 56f, panel.width, 56f);
                HoverFocus(row, i, ref _focus);
                if (_g.Button(row, labels[i], i == MenuLeave && _leaveArmed ? _nav.Glyph("ENTER", "A") : null, i == _focus, false, i > 0))
                    ActivateMenu(i);
            }
            _g.Foot(panel, "THE MATCH KEEPS RUNNING", _nav.Glyph("ESC  RESUME", "B  RESUME") ?? "");

            var table = new Rect(panelW + gap, 0f, tableW, 96f + 8f * 42f + 40f);
            _g.Panel(table, GlowRest);
            string line = match.Mode.ResultLine(match.Score, match.TeamName);
            _g.Header(table, "SCOREBOARD", line.ToUpperInvariant());
            DrawPilotTables(table, table.y + 90f);
            GUI.matrix = old;
        }

        // ---------- Scoreboard ----------

        void DrawScoreboard(float width)
        {
            Backdrop(width);
            var panel = Centered(width, 1100f, 860f, 110f);
            _g.Panel(panel, OpenGlow());

            int winner = match.Winner;
            string result = winner == MatchScore.Draw ? "DRAW" : winner == match.PlayerTeam ? "VICTORY" : "DEFEAT";
            _g.Header(panel, result, "FINAL SCORE");
            string line = match.Mode.ResultLine(match.Score, match.TeamName);
            _g.Tracked(new Rect(panel.x, panel.y + 100f, panel.width, 44f), line.ToUpperInvariant(), _g.Numeral, 3f, TextAnchor.MiddleCenter, Color.white);
            DrawPilotTables(panel, panel.y + 170f);

            var again = new Rect(panel.x + panel.width / 2f - 400f, panel.yMax - 112f, 380f, 56f);
            var menu = new Rect(panel.x + panel.width / 2f + 20f, panel.yMax - 112f, 380f, 56f);
            HoverFocus(again, 0, ref _focus);
            HoverFocus(menu, 1, ref _focus);
            if (_g.Button(again, "PLAY AGAIN", _nav.Glyph("ENTER", "A"), _focus != 1, false, false, TextAnchor.MiddleCenter))
                match.PlayAgain();
            if (_g.Button(menu, "MAIN MENU", _nav.Glyph("ESC", "B"), _focus == 1, false, false, TextAnchor.MiddleCenter))
                match.ReturnToMainMenu();
            _g.Foot(panel, match.Mode.WinRule, "");
        }

        /// <summary>
        /// Lays <paramref name="columns"/> out from the right edge of <paramref name="row"/> (16 px in), drawing each
        /// one's text. Returns the width left for the name, which starts 16 px in from the left.
        /// </summary>
        static float DrawColumns(Rect row, IReadOnlyList<ScoreColumn> columns, Func<ScoreColumn, string> text, Action<ScoreColumn, Rect, string> draw)
        {
            float right = row.xMax - 16f;
            for (int i = columns.Count - 1; i >= 0; i--)
            {
                var c = columns[i];
                draw(c, new Rect(right - c.Width, row.y, c.Width, row.height), text(c));
                right -= c.Width + c.Gap;
            }
            return right - row.x - 10f;
        }

        /// <summary>Both teams' pilots side by side (the mode's columns, best first), from <paramref name="top"/> down.</summary>
        void DrawPilotTables(Rect panel, float top)
        {
            var mode = match.Mode;
            var columns = mode.Columns;
            float colW = (panel.width - 72f - 40f) / 2f;
            for (int team = 0; team < 2; team++)
            {
                float x = panel.x + 36f + team * (colW + 40f);
                var head = new Rect(x, top, colW, 34f);
                float nameW = DrawColumns(head, columns, c => c.Header, (_, r, text) =>
                    _g.Tracked(r, text, _g.Hint, 3.6f, TextAnchor.MiddleRight, Steel));
                _g.Tracked(new Rect(head.x + 16f, head.y, nameW, head.height), match.TeamName(team).ToUpperInvariant(), _g.Hint, 3.6f,
                    TextAnchor.MiddleLeft, match.TeamColor(team));
                _g.Fill(new Rect(x, head.yMax, colW, 1f), Alpha(Color.white, 0.14f));

                var pilots = new List<PilotScore>();
                foreach (var p in match.Score.Pilots)
                    if (p.Team == team)
                        pilots.Add(p);
                pilots.Sort(mode.CompareForScoreboard);
                float y = head.yMax + 2f;
                foreach (var p in pilots)
                {
                    if (y > head.yMax + 8f * 42f)
                        break;
                    bool you = p.Name == "You";
                    var row = new Rect(x, y, colW, 42f);
                    if (you)
                        _g.Lit(row, 1f, true);
                    var tint = you ? Color.white : Alpha(White, 0.88f);
                    var pilot = p;
                    float rowNameW = DrawColumns(row, columns, c => c.Value(pilot), (c, r, text) =>
                        _g.Tracked(r, text, _g.Numeral, 0f, TextAnchor.MiddleRight, Alpha(tint, c.Alpha), 0.75f));
                    _g.Tracked(new Rect(row.x + 16f, row.y, rowNameW, row.height), p.Name.ToUpperInvariant(), _g.Hint, 1.8f, TextAnchor.MiddleLeft, tint, 1f);
                    y += 42f;
                    _g.Fill(new Rect(x, y - 1f, colW, 1f), Alpha(Color.white, 0.05f));
                }
            }
        }
    }
}

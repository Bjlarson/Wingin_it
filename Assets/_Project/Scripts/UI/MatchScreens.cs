using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Bots;
using WingingIt.Flight;
using WingingIt.Match;

namespace WingingIt.UI
{
    /// <summary>
    /// Prototype match screens drawn with IMGUI, over the <see cref="MatchManager"/>: the pre-match lobby (both
    /// rosters, bot difficulty, Start), class select, the 3-2-1, the match clock + team score bar in play, and the
    /// end-of-match scoreboard. In play it also draws the class picker (while dead, and parked + repaired at home
    /// with Tab or the CHANGE PLANE button). Buttons work with mouse and touch; Enter / Esc and 1-4 work on the keyboard.
    /// Styled to Design/06-UI-UX/glass-menu-style-guide.html (dark tinted glass, DIN type, rail selection).
    /// </summary>
    public class MatchScreens : MonoBehaviour
    {
        [SerializeField] MatchManager match;

        const float ReferenceHeight = 1080f;
        const float Radius = 14f;
        const float GlowRest = 0.07f, GlowOpen = 0.14f, GlowSettle = 0.18f;

        // Palette from the style guide.
        static readonly Color Glow = new(0.863f, 0.922f, 1f);          // #DCEBFF, edges only
        static readonly Color Steel = new(0.541f, 0.573f, 0.612f);     // #8A929C secondary text
        static readonly Color Body = new(0.706f, 0.729f, 0.761f);      // #B4BAC2
        static readonly Color White = new(0.949f, 0.957f, 0.969f);     // #F2F4F7
        static readonly Color Dim = new(0f, 0f, 0f, 0.45f);
        static readonly string[] DifficultyNames = { "Easy", "Normal", "Hard" };

        GUIStyle _title, _huge, _item, _hint, _body, _numeral;
        Texture2D _white, _ramp;
        Font _din, _inter;
        readonly Dictionary<int, Texture2D> _glass = new();
        readonly Dictionary<(GUIStyle, int, char), float> _advance = new();
        MatchPhase _lastPhase = (MatchPhase)(-1);
        float _phaseShownAt;

        void Update()
        {
            var keyboard = Keyboard.current;
            if (match == null || keyboard == null)
                return;
            if (match.Phase != _lastPhase)
            {
                _lastPhase = match.Phase;
                _phaseShownAt = Time.unscaledTime;
            }
            switch (match.Phase)
            {
                case MatchPhase.Lobby:
                    if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                        match.OpenClassSelect();
                    break;
                case MatchPhase.ClassSelect:
                    if (keyboard.escapeKey.wasPressedThisFrame)
                        match.BackToLobby();
                    else if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                        match.SelectClass(match.SelectedClass);
                    for (int i = 0; i < 4; i++)
                        if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                            match.SelectClass(i);
                    break;
                case MatchPhase.Playing:
                    if (match.Picker == ClassPicker.None)
                    {
                        if (keyboard.tabKey.wasPressedThisFrame)
                            match.OpenAirfieldPicker();
                        break;
                    }
                    if (match.Picker == ClassPicker.Airfield && (keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame))
                    {
                        match.CloseAirfieldPicker();
                        break;
                    }
                    for (int i = 0; i < 4; i++)
                        if (keyboard[Key.Digit1 + i].wasPressedThisFrame)
                        {
                            if (match.Picker == ClassPicker.Respawn)
                                match.ChooseRespawnClass(i);
                            else
                                match.ChangeClassAtAirfield(i);
                        }
                    break;
                case MatchPhase.Ended:
                    if (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame)
                        match.PlayAgain();
                    break;
            }
        }

        void OnDestroy()
        {
            foreach (var t in _glass.Values)
                if (t != null)
                    Destroy(t);
            if (_ramp != null)
                Destroy(_ramp);
        }

        void OnGUI()
        {
            if (match == null || match.IsDismissed)
                return;
            EnsureStyles();
            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            switch (match.Phase)
            {
                case MatchPhase.Lobby: DrawLobby(width); break;
                case MatchPhase.ClassSelect: DrawClassSelect(width); break;
                case MatchPhase.Countdown: DrawCountdown(width); break;
                case MatchPhase.Playing:
                    if (match.Picker != ClassPicker.None)
                        DrawPicker(width);
                    else if (match.CanChangeClassAtAirfield)
                        DrawSwapPrompt(width);
                    DrawScoreBar(width);
                    break;
                case MatchPhase.Ended: DrawScoreboard(width); break;
            }
        }

        /// <summary>Outer glow: 14% when a screen opens, settling to 7% over 180 ms.</summary>
        float OpenGlow()
        {
            float t = Mathf.Clamp01((Time.unscaledTime - _phaseShownAt) / 0.18f);
            return Mathf.Lerp(GlowOpen, GlowRest, t);
        }

        // ---------- Lobby ----------

        void DrawLobby(float width)
        {
            Fill(new Rect(0f, 0f, width, ReferenceHeight), Dim);
            var panel = Centered(width, 1000f, 740f, 150f);
            Panel(panel, OpenGlow());
            Header(panel, "TEAMS", "8 V 8  ·  5:00");
            GUI.color = Body;
            GUI.Label(new Rect(panel.x + 36f, panel.y + 94f, panel.width - 72f, 28f), "Highest team score when time runs out wins. A tie is a draw.", _body);
            GUI.color = Color.white;

            float colW = (panel.width - 72f - 40f) / 2f;
            for (int team = 0; team < 2; team++)
            {
                float x = panel.x + 36f + team * (colW + 40f);
                var head = new Rect(x, panel.y + 140f, colW, 34f);
                Tracked(new Rect(head.x + 16f, head.y, head.width - 32f, head.height), match.TeamName(team).ToUpperInvariant(), _hint, 3.6f, TextAnchor.MiddleLeft, match.TeamColor(team));
                Fill(new Rect(x, head.yMax, colW, 1f), Alpha(Color.white, 0.14f));
                float y = head.yMax + 2f;
                foreach (string line in RosterLines(team))
                {
                    bool you = line == "You";
                    var row = new Rect(x, y, colW, 42f);
                    if (you)
                        Lit(row, 1f, true);
                    Tracked(new Rect(row.x + 16f, row.y, row.width - 32f, row.height), line.ToUpperInvariant(), _hint, 2.2f, TextAnchor.MiddleLeft,
                        you ? Color.white : Alpha(White, 0.88f), 1.2f);
                    y += 42f;
                    Fill(new Rect(x, y - 1f, colW, 1f), Alpha(Color.white, 0.05f));
                }
            }

            float by = panel.y + 590f;
            Tracked(new Rect(panel.x + 36f, by, 90f, 56f), "BOTS", _hint, 3.6f, TextAnchor.MiddleLeft, Steel);
            for (int d = 0; d < DifficultyNames.Length; d++)
            {
                var r = new Rect(panel.x + 130f + d * 150f, by, 140f, 56f);
                if (Item(r, DifficultyNames[d].ToUpperInvariant(), null, (int)match.Difficulty == d, false, false, TextAnchor.MiddleCenter))
                    match.Difficulty = (BotDifficulty)d;
            }
            if (Item(new Rect(panel.xMax - 320f, by, 284f, 56f), "START", "ENTER", true, false, false))
                match.OpenClassSelect();
            Foot(panel, "YOU FLY FOR WEST  ·  EMPTY SLOTS ARE BOTS", "ENTER  START");
        }

        /// <summary>The lobby's lines for a team: "You" if the player's on it, then each planned bot and its class.</summary>
        List<string> RosterLines(int team)
        {
            var lines = new List<string>();
            if (match.Player != null && match.PlayerTeam == team)
                lines.Add("You");
            var roster = match.Roster;
            if (roster == null)
                return lines;
            var planned = roster.PlannedBots(team);
            for (int i = 0; i < planned.Count; i++)
                lines.Add($"Bot {i + 1}  ·  {planned[i].displayName}");
            return lines;
        }

        // ---------- Class select ----------

        void DrawClassSelect(float width)
        {
            Fill(new Rect(0f, 0f, width, ReferenceHeight), Dim);
            Tracked(new Rect(0f, 110f, width, 60f), "CHOOSE YOUR PLANE", _title, 8f, TextAnchor.MiddleCenter, Color.white);
            Tracked(new Rect(0f, 176f, width, 24f), "1 – 4 OR CLICK  ·  ENTER KEEPS THE HIGHLIGHTED ONE  ·  ESC BACK", _hint, 3.4f, TextAnchor.MiddleCenter, Steel);
            int picked = ClassCards(width, 250f, "FLY", match.SelectedClass);
            if (picked >= 0)
                match.SelectClass(picked);
            if (Item(new Rect(width / 2f - 130f, 250f + CardH + 36f, 260f, 56f), "BACK", "ESC", false, false, false, TextAnchor.MiddleCenter))
                match.BackToLobby();
        }

        /// <summary>
        /// In play: while dead, picks the class to respawn in (the timer keeps running); parked and repaired at home,
        /// swaps class on the spot. The world runs underneath.
        /// </summary>
        void DrawPicker(float width)
        {
            Fill(new Rect(0f, 0f, width, ReferenceHeight), Dim);
            bool respawn = match.Picker == ClassPicker.Respawn;
            string title = respawn ? $"RESPAWN IN {Mathf.CeilToInt(match.RespawnTimeLeft)}" : "CHANGE PLANE";
            string hint = respawn ? "1 – 4 OR CLICK TO PICK YOUR NEXT PLANE" : $"AT {match.SwapAirfield?.DisplayName.ToUpperInvariant()}  ·  1 – 4 OR CLICK  ·  ESC OR TAB KEEP THIS ONE";
            Tracked(new Rect(0f, 110f, width, 60f), title, _title, 8f, TextAnchor.MiddleCenter, Color.white);
            Tracked(new Rect(0f, 176f, width, 24f), hint, _hint, 3.4f, TextAnchor.MiddleCenter, Steel);
            int picked = ClassCards(width, 250f, respawn ? "PICK" : "SWAP", match.SelectedClass);
            if (picked >= 0)
            {
                if (respawn)
                    match.ChooseRespawnClass(picked);
                else
                    match.ChangeClassAtAirfield(picked);
            }
            if (!respawn && Item(new Rect(width / 2f - 130f, 250f + CardH + 36f, 260f, 56f), "KEEP", "ESC", false, false, false, TextAnchor.MiddleCenter))
                match.CloseAirfieldPicker();
        }

        /// <summary>Parked and repaired at home: a small clickable (and touchable) "change plane" prompt above the HUD's bottom edge.</summary>
        void DrawSwapPrompt(float width)
        {
            var r = new Rect(width / 2f - 170f, ReferenceHeight - 200f, 340f, 56f);
            Panel(r, GlowRest, false);
            if (Item(r, "CHANGE PLANE", "TAB", false, false, false, TextAnchor.MiddleCenter))
                match.OpenAirfieldPicker();
        }

        const float CardH = 470f;

        /// <summary>
        /// One card per class (stats, loadout, ability, how many teammates fly it) with a <paramref name="button"/> row.
        /// Returns the index whose button was pressed this frame, or -1.
        /// </summary>
        int ClassCards(float width, float top, string button, int selectedIndex)
        {
            var classes = match.Classes;
            int n = classes.Count;
            int[] team = match.TeamComposition();
            const float cardW = 340f, gap = 36f;
            float x0 = (width - (n * cardW + (n - 1) * gap)) / 2f;
            float glow = OpenGlow();
            int pressed = -1;
            for (int i = 0; i < n; i++)
            {
                var stats = classes[i];
                if (stats == null)
                    continue;
                var card = new Rect(x0 + i * (cardW + gap), top, cardW, CardH);
                bool selected = i == selectedIndex;
                Panel(card, selected ? Mathf.Max(glow, GlowSettle) : glow * 0.5f);

                var head = new Rect(card.x, card.y + 22f, card.width, 44f);
                if (selected)
                    Lit(head, 1f, true);
                Fill(new Rect(card.x + 36f, head.y + 17f, 10f, 10f), stats.bodyColor);
                Tracked(new Rect(card.x + 58f, head.y, card.width - 110f, head.height), stats.displayName.ToUpperInvariant(), _item, 2.8f,
                    TextAnchor.MiddleLeft, selected ? Color.white : Alpha(White, 0.88f));
                Kbd(new Rect(card.xMax - 36f, head.y, 0f, head.height), (i + 1).ToString());
                Fill(new Rect(card.x + 36f, head.yMax + 8f, card.width - 72f, 1f), Alpha(Color.white, 0.14f));

                GUI.color = Body;
                GUI.Label(new Rect(card.x + 36f, head.yMax + 22f, card.width - 72f, 250f), Describe(stats), _body);
                GUI.color = Color.white;

                int mates = i < team.Length ? team[i] : 0;
                Tracked(new Rect(card.x + 36f, card.yMax - 124f, card.width - 72f, 30f), "ON YOUR TEAM", _hint, 2.4f, TextAnchor.MiddleLeft, Steel);
                Tracked(new Rect(card.x + 36f, card.yMax - 124f, card.width - 72f, 30f), mates.ToString(), _numeral, 0f, TextAnchor.MiddleRight,
                    mates == 0 ? Alpha(White, 0.5f) : White, 0.75f);

                if (Item(new Rect(card.x, card.yMax - 78f, card.width, 56f), button, null, selected, false, true))
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
                Tracked(new Rect(big.x + o.x, big.y + o.y, big.width, big.height), text, _huge, 10f, TextAnchor.MiddleCenter, Alpha(Glow, 0.10f));
            Tracked(big, text, _huge, 10f, TextAnchor.MiddleCenter, Color.white);
            Tracked(new Rect(0f, big.yMax + 4f, width, 30f), "GET READY", _hint, 5f, TextAnchor.MiddleCenter, Steel);
            DrawScoreBar(width);
        }

        void DrawScoreBar(float width)
        {
            var bar = new Rect(width / 2f - 240f, 16f, 480f, 64f);
            Panel(bar, GlowRest, false);
            DrawTeamScore(new Rect(bar.x + 22f, bar.y, 150f, bar.height), 0, TextAnchor.MiddleLeft);
            DrawTeamScore(new Rect(bar.xMax - 172f, bar.y, 150f, bar.height), 1, TextAnchor.MiddleRight);
            float t = match.Phase == MatchPhase.Countdown ? match.MatchLength : match.TimeLeft;
            bool pulse = match.Phase == MatchPhase.Playing && t <= 30f && Mathf.Repeat(Time.unscaledTime, 1f) < 0.5f;
            Tracked(new Rect(bar.x + 170f, bar.y, 140f, bar.height), Clock(t), _numeral, 1.5f, TextAnchor.MiddleCenter,
                pulse ? Glow : Color.white);
        }

        void DrawTeamScore(Rect r, int team, TextAnchor anchor)
        {
            // team name in its colour (the one functional use of colour on the glass), score in white
            Tracked(new Rect(r.x, r.y - 12f, r.width, r.height), match.TeamName(team).ToUpperInvariant(), _hint, 3f, anchor, match.TeamColor(team));
            Tracked(new Rect(r.x, r.y + 10f, r.width, r.height), match.Score.TeamScore(team).ToString(), _numeral, 1f, anchor, Color.white);
        }

        static string Clock(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        // ---------- Scoreboard ----------

        void DrawScoreboard(float width)
        {
            Fill(new Rect(0f, 0f, width, ReferenceHeight), Dim);
            var panel = Centered(width, 1100f, 860f, 110f);
            Panel(panel, OpenGlow());

            int winner = match.Score.Winner();
            string result = winner == MatchScore.Draw ? "DRAW" : winner == match.PlayerTeam ? "VICTORY" : "DEFEAT";
            Header(panel, result, "FINAL SCORE");
            string line = $"{match.TeamName(0)}  {match.Score.TeamScore(0)}  –  {match.Score.TeamScore(1)}  {match.TeamName(1)}";
            Tracked(new Rect(panel.x, panel.y + 100f, panel.width, 44f), line.ToUpperInvariant(), _numeral, 3f, TextAnchor.MiddleCenter, Color.white);

            float colW = (panel.width - 72f - 40f) / 2f;
            for (int team = 0; team < 2; team++)
            {
                float x = panel.x + 36f + team * (colW + 40f);
                var head = new Rect(x, panel.y + 170f, colW, 34f);
                Tracked(new Rect(head.x + 16f, head.y, head.width - 236f, head.height), match.TeamName(team).ToUpperInvariant(), _hint, 3.6f,
                    TextAnchor.MiddleLeft, match.TeamColor(team));
                Tracked(new Rect(head.xMax - 226f, head.y, 40f, head.height), "K", _hint, 3.6f, TextAnchor.MiddleRight, Steel);
                Tracked(new Rect(head.xMax - 186f, head.y, 40f, head.height), "A", _hint, 3.6f, TextAnchor.MiddleRight, Steel);
                Tracked(new Rect(head.xMax - 146f, head.y, 40f, head.height), "D", _hint, 3.6f, TextAnchor.MiddleRight, Steel);
                Tracked(new Rect(head.xMax - 96f, head.y, 80f, head.height), "SCORE", _hint, 3.6f, TextAnchor.MiddleRight, Steel);
                Fill(new Rect(x, head.yMax, colW, 1f), Alpha(Color.white, 0.14f));

                var pilots = new List<PilotScore>();
                foreach (var p in match.Score.Pilots)
                    if (p.Team == team)
                        pilots.Add(p);
                pilots.Sort(MatchScore.CompareForScoreboard);
                float y = head.yMax + 2f;
                foreach (var p in pilots)
                {
                    if (y > head.yMax + 8f * 42f)
                        break;
                    bool you = p.Name == "You";
                    var row = new Rect(x, y, colW, 42f);
                    if (you)
                        Lit(row, 1f, true);
                    var tint = you ? Color.white : Alpha(White, 0.88f);
                    Tracked(new Rect(row.x + 16f, row.y, row.width - 236f, row.height), p.Name.ToUpperInvariant(), _hint, 2.4f, TextAnchor.MiddleLeft, tint, 1.15f);
                    Tracked(new Rect(row.xMax - 226f, row.y, 40f, row.height), p.Kills.ToString(), _numeral, 0f, TextAnchor.MiddleRight, tint, 0.75f);
                    Tracked(new Rect(row.xMax - 186f, row.y, 40f, row.height), p.Assists.ToString(), _numeral, 0f, TextAnchor.MiddleRight, Alpha(tint, 0.75f), 0.75f);
                    Tracked(new Rect(row.xMax - 146f, row.y, 40f, row.height), p.Deaths.ToString(), _numeral, 0f, TextAnchor.MiddleRight, Alpha(tint, 0.6f), 0.75f);
                    Tracked(new Rect(row.xMax - 96f, row.y, 80f, row.height), p.Points.ToString(), _numeral, 0f, TextAnchor.MiddleRight, tint, 0.75f);
                    y += 42f;
                    Fill(new Rect(x, y - 1f, colW, 1f), Alpha(Color.white, 0.05f));
                }
            }

            if (Item(new Rect(panel.x + panel.width / 2f - 190f, panel.yMax - 112f, 380f, 56f), "PLAY AGAIN", "ENTER", true, false, false, TextAnchor.MiddleCenter))
                match.PlayAgain();
            Foot(panel, "WINNER IS THE HIGHEST TEAM SCORE", "ENTER  PLAY AGAIN");
        }

        // ---------- Glass components ----------

        /// <summary>Title row: big DIN title on the left, small steel caption on the right, then the fading rule.</summary>
        void Header(Rect panel, string title, string caption)
        {
            Tracked(new Rect(panel.x + 36f, panel.y + 24f, panel.width - 72f, 48f), title, _title, 5.6f, TextAnchor.MiddleLeft, Color.white);
            Tracked(new Rect(panel.x + 36f, panel.y + 24f, panel.width - 72f, 48f), caption, _hint, 2.4f, TextAnchor.MiddleRight, Steel);
            Rule(new Rect(panel.x + 36f, panel.y + 80f, panel.width - 72f, 1f));
        }

        void Foot(Rect panel, string left, string right)
        {
            Tracked(new Rect(panel.x + 36f, panel.yMax - 40f, panel.width * 0.6f, 24f), left, _hint, 1.7f, TextAnchor.MiddleLeft, Steel);
            Tracked(new Rect(panel.xMax - 36f - panel.width * 0.3f, panel.yMax - 40f, panel.width * 0.3f, 24f), right, _hint, 1.7f, TextAnchor.MiddleRight, Steel);
        }

        /// <summary>The tinted-glass body: soft drop shadow, a low edge glow, then the baked panel texture.</summary>
        void Panel(Rect r, float glow, bool shadow = true)
        {
            if (shadow)
                for (int i = 0; i < 7; i++)
                    RoundFill(Inflate(r, i * 5f - 6f, 26f), Radius + i * 5f, new Color(0f, 0f, 0f, 0.075f));
            for (int i = 0; i < 6; i++)
            {
                float k = 1f - i / 6f;
                RoundStroke(Inflate(r, 1f + i * 4.5f, 0f), Radius + i * 4.5f, 4.5f, Alpha(Glow, glow * k * 0.55f));
            }
            var c = GUI.color;
            GUI.color = Color.white;
            GUI.DrawTexture(r, GlassTexture(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height)));
            GUI.color = c;
        }

        /// <summary>A menu row: 56 px, DIN caps. Selected (or hovered) rows get the white rail and fading fill.</summary>
        bool Item(Rect r, string label, string kbd, bool selected, bool dim, bool divider, TextAnchor align = TextAnchor.MiddleLeft)
        {
            bool hover = !dim && r.Contains(Event.current.mousePosition);
            if (divider)
                Fill(new Rect(r.x, r.y, r.width, 1f), Alpha(Color.white, 0.05f));
            if (selected || hover)
                Lit(r, selected ? 1f : 0.55f, true);
            var tint = dim ? Alpha(White, 0.38f) : selected ? Color.white : Alpha(White, hover ? 1f : 0.88f);
            float pad = align == TextAnchor.MiddleLeft ? 36f : 12f;
            float kbdRoom = kbd != null && align != TextAnchor.MiddleLeft ? 70f : 0f;
            Tracked(new Rect(r.x + pad, r.y, r.width - pad * 2f - kbdRoom, r.height), label, _item, 3.0f, align, tint);
            if (kbd != null)
                Kbd(new Rect(r.xMax - 20f, r.y, 0f, r.height), kbd);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>The selection look: left-to-right white fade (11% to 2%) and the 2 px glowing rail.</summary>
        void Lit(Rect r, float strength, bool rail)
        {
            var c = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.11f * strength);
            GUI.DrawTexture(r, _ramp);
            GUI.color = c;
            if (rail)
            {
                var bar = new Rect(r.x, r.y + 10f, 2f, r.height - 20f);
                Fill(Inflate(bar, 9f, 0f), Alpha(Glow, 0.05f * strength));
                Fill(Inflate(bar, 5f, 0f), Alpha(Glow, 0.10f * strength));
                Fill(Inflate(bar, 2f, 0f), Alpha(Glow, 0.25f * strength));
                Fill(bar, Alpha(Color.white, strength));
            }
        }

        /// <summary>A key-cap hint with its right edge at the rect's x.</summary>
        void Kbd(Rect at, string text)
        {
            float w = TrackedWidth(text, _hint, 1.5f) + 16f;
            var box = new Rect(at.x - w, at.y + (at.height - 22f) / 2f, w, 22f);
            RoundStroke(box, 4f, 1f, Alpha(Color.white, 0.14f));
            Tracked(box, text, _hint, 1.5f, TextAnchor.MiddleCenter, Steel);
        }

        void Rule(Rect r)
        {
            var c = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, 0.28f);
            GUI.DrawTexture(r, _ramp);
            GUI.color = c;
        }

        // ---------- Text ----------

        float TrackedWidth(string text, GUIStyle style, float tracking, float scale = 1f)
        {
            int size = Mathf.RoundToInt(style.fontSize * scale);
            float w = 0f;
            foreach (char ch in text)
                w += Advance(style, size, ch) + tracking * scale;
            return Mathf.Max(0f, w - tracking * scale);
        }

        float Advance(GUIStyle style, int size, char ch)
        {
            if (_advance.TryGetValue((style, size, ch), out float w))
                return w;
            if (ch == ' ')
                w = size * 0.3f;
            else
            {
                int old = style.fontSize;
                style.fontSize = size;
                w = style.CalcSize(new GUIContent(ch.ToString())).x;
                style.fontSize = old;
            }
            _advance[(style, size, ch)] = w;
            return w;
        }

        /// <summary>
        /// Letter-spaced text (IMGUI has no tracking): each glyph is placed by hand. <paramref name="scale"/> scales the
        /// font size and tracking together, for sizes derived from a base style.
        /// </summary>
        void Tracked(Rect r, string text, GUIStyle style, float tracking, TextAnchor anchor, Color color, float scale = 1f)
        {
            int size = Mathf.RoundToInt(style.fontSize * scale);
            float total = TrackedWidth(text, style, tracking, scale);
            float x = anchor switch
            {
                TextAnchor.MiddleCenter or TextAnchor.UpperCenter => r.x + (r.width - total) / 2f,
                TextAnchor.MiddleRight or TextAnchor.UpperRight => r.xMax - total,
                _ => r.x,
            };
            var c = GUI.color;
            GUI.color = color;
            int oldSize = style.fontSize;
            style.fontSize = size;
            foreach (char ch in text)
            {
                float adv = Advance(style, size, ch);
                if (ch != ' ')
                    GUI.Label(new Rect(x, r.y, adv + 8f, r.height), ch.ToString(), style);
                x += adv + tracking * scale;
            }
            style.fontSize = oldSize;
            GUI.color = c;
        }

        // ---------- Drawing helpers ----------

        static Rect Centered(float width, float w, float h, float top) => new((width - w) / 2f, top, w, h);

        static Rect Inflate(Rect r, float by, float dy) => new(r.x - by, r.y - by + dy, r.width + by * 2f, r.height + by * 2f);

        static Color Alpha(Color c, float a) => new(c.r, c.g, c.b, a);

        void Fill(Rect r, Color color)
        {
            var c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, _white);
            GUI.color = c;
        }

        void RoundFill(Rect r, float radius, Color color) =>
            GUI.DrawTexture(r, _white, ScaleMode.StretchToFill, true, 0f, color, Vector4.zero, new Vector4(radius, radius, radius, radius));

        void RoundStroke(Rect r, float radius, float thickness, Color color) =>
            GUI.DrawTexture(r, _white, ScaleMode.StretchToFill, true, 0f, color, new Vector4(thickness, thickness, thickness, thickness),
                new Vector4(radius, radius, radius, radius));

        // ---------- Setup ----------

        void EnsureStyles()
        {
            if (_item != null)
                return;
            _white = Texture2D.whiteTexture;
            _ramp = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 64; i++)
                _ramp.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.15f, i / 63f)));
            _ramp.Apply();

            // DIN-like signage face: Bahnschrift on Windows, falling back through similar faces. Body face: Inter / Segoe UI.
            _din = Font.CreateDynamicFontFromOSFont(new[] { "Bahnschrift", "DIN Alternate", "D-DIN", "Segoe UI Semibold", "Arial" }, 28);
            _inter = Font.CreateDynamicFontFromOSFont(new[] { "Inter", "Segoe UI Variable", "Segoe UI", "Arial" }, 15);

            _title = Style(_din, 36, FontStyle.Bold);
            _item = Style(_din, 20, FontStyle.Normal);
            _hint = Style(_din, 15, FontStyle.Normal);
            _numeral = Style(_din, 28, FontStyle.Normal);
            _huge = Style(_din, 150, FontStyle.Bold);
            _body = Style(_inter, 17, FontStyle.Normal);
            _body.alignment = TextAnchor.UpperLeft;
            _body.wordWrap = true;
        }

        static GUIStyle Style(Font font, int size, FontStyle fontStyle)
        {
            var s = new GUIStyle(GUI.skin.label) { font = font, fontSize = size, fontStyle = fontStyle, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            s.normal.textColor = Color.white;
            s.hover.textColor = Color.white;
            return s;
        }

        /// <summary>
        /// Bakes the guide's glass panel 1:1: graphite tint, top sheen, two static reflection streaks (115°), the 160°
        /// body gradient, 1 px outer stroke, top bevel (+30% white), bottom bevel (-55% black) and rounded corners.
        /// IMGUI has no backdrop blur, so the tint is denser than the guide's 55% to keep text legible over the scene.
        /// </summary>
        Texture2D GlassTexture(int w, int h)
        {
            int key = w * 10000 + h;
            if (_glass.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var px = new Color[w * h];
            float r = Radius;
            // CSS gradient geometry (y down)
            Vector2 d160 = new(Mathf.Sin(160f * Mathf.Deg2Rad), -Mathf.Cos(160f * Mathf.Deg2Rad));
            Vector2 d115 = new(Mathf.Sin(115f * Mathf.Deg2Rad), -Mathf.Cos(115f * Mathf.Deg2Rad));
            float len160 = Mathf.Abs(w * d160.x) + Mathf.Abs(h * d160.y);
            float len115 = Mathf.Abs(w * d115.x) + Mathf.Abs(h * d115.y);
            for (int y = 0; y < h; y++)
            {
                int yy = h - 1 - y; // texture rows run bottom-up; the maths is top-down
                for (int x = 0; x < w; x++)
                {
                    float fx = x + 0.5f, fy = yy + 0.5f;
                    float qx = Mathf.Abs(fx - w / 2f) - (w / 2f - r), qy = Mathf.Abs(fy - h / 2f) - (h / 2f - r);
                    float dist = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
                    float cover = Mathf.Clamp01(0.5f - dist);
                    if (cover <= 0f)
                        continue;

                    float cr = 32f / 255f, cg = 35f / 255f, cb = 40f / 255f, ca = 0.68f;
                    float t160 = ((fx - w / 2f) * d160.x + (fy - h / 2f) * d160.y) / len160 + 0.5f;
                    float body = t160 < 0.38f ? Mathf.Lerp(0.10f, 0.025f, t160 / 0.38f) : t160 < 0.6f ? Mathf.Lerp(0.025f, 0f, (t160 - 0.38f) / 0.22f) : 0f;
                    float t115 = ((fx - w / 2f) * d115.x + (fy - h / 2f) * d115.y) / len115 + 0.5f;
                    float streak = Tri(t115, 0.26f, 0.33f, 0.41f, 0.075f) + Tri(t115, 0.58f, 0.62f, 0.66f, 0.035f);
                    float sx = (fx - 0.18f * w) / (1.2f * w), sy = fy / (0.9f * 0.46f * h);
                    float sheen = 0.09f * Mathf.Clamp01(1f - new Vector2(sx, sy).magnitude / 0.6f);

                    Over(ref cr, ref cg, ref cb, ref ca, 1f, 1f, 1f, body + streak + sheen);
                    if (dist > -1f)
                        Over(ref cr, ref cg, ref cb, ref ca, 1f, 1f, 1f, 0.14f);
                    bool straight = fx > r * 0.6f && fx < w - r * 0.6f;
                    if (yy == 1 && straight)
                        Over(ref cr, ref cg, ref cb, ref ca, 1f, 1f, 1f, 0.30f);
                    if (yy == h - 2 && straight)
                        Over(ref cr, ref cg, ref cb, ref ca, 0f, 0f, 0f, 0.55f);

                    px[y * w + x] = new Color(cr, cg, cb, ca * cover);
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            tex.SetPixels(px);
            tex.Apply(false, true);
            _glass[key] = tex;
            return tex;
        }

        static float Tri(float t, float a, float peak, float b, float amp) =>
            t <= a || t >= b ? 0f : amp * (t < peak ? (t - a) / (peak - a) : (b - t) / (b - peak));

        /// <summary>Source-over of a colour at <paramref name="oa"/> onto the running colour and alpha.</summary>
        static void Over(ref float r, ref float g, ref float b, ref float a, float sr, float sg, float sb, float oa)
        {
            if (oa <= 0f)
                return;
            float na = oa + a * (1f - oa);
            r = (sr * oa + r * a * (1f - oa)) / na;
            g = (sg * oa + g * a * (1f - oa)) / na;
            b = (sb * oa + b * a * (1f - oa)) / na;
            a = na;
        }
    }
}

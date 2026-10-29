using System;
using UnityEngine;
using WingingIt.Settings;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    /// <summary>
    /// The PILOT screen off the main menu (playtest G4, Design/06-UI-UX/front-end-menus.md#pilot): suit, helmet, canopy
    /// and trim colours and the canopy pattern, each a ‹ value › row that applies and saves at once, plus RESET. A glass
    /// panel on the left; the menu pilot shows every change on the right (<see cref="Maps.MenuPilot"/>). Call
    /// <see cref="HandleInput"/> from Update and <see cref="Draw"/> from OnGUI.
    /// </summary>
    public sealed class PilotPanel
    {
        const float RowH = 64f, PanelW = 640f, Chip = 26f;

        enum RowKind { Suit, Helmet, Canopy, Trim, Pattern, Reset }

        static readonly RowKind[] Rows = (RowKind[])Enum.GetValues(typeof(RowKind));
        static readonly string[] PatternNames = { "STRIPES", "QUARTERS", "CROWN", "SOLID" };

        readonly GlassGUI _g;
        int _focus;

        public PilotPanel(GlassGUI g) => _g = g;

        static GameSettings S => GameSettings.Current;
        static PilotLook Look => GameSettings.Current.Data.PilotLook;

        public void Open() => _focus = 0;

        /// <summary>Keyboard / gamepad. True when the player backs out.</summary>
        public bool HandleInput(MenuNav nav)
        {
            if (nav.Back)
                return true;
            if (nav.Up) _focus = MenuMath.Step(_focus, -1, Rows.Length);
            if (nav.Down) _focus = MenuMath.Step(_focus, 1, Rows.Length);
            if (nav.Left) Change(Rows[_focus], -1);
            if (nav.Right) Change(Rows[_focus], 1);
            if (nav.Confirm) Change(Rows[_focus], 1);
            return false;
        }

        /// <summary>Steps a row: the next / previous swatch or pattern; RESET puts the default look back.</summary>
        public static void Change(int row, int dir) => Change(Rows[row], dir);

        static void Change(RowKind row, int dir)
        {
            if (row == RowKind.Reset)
            {
                S.ResetPilotLook();
                return;
            }
            var look = Look;
            switch (row)
            {
                case RowKind.Suit: look.suit = PilotColors.Step(look.suit, dir); break;
                case RowKind.Helmet: look.helmet = PilotColors.Step(look.helmet, dir); break;
                case RowKind.Canopy: look.canopy = PilotColors.Step(look.canopy, dir); break;
                case RowKind.Trim: look.trim = PilotColors.Step(look.trim, dir); break;
                case RowKind.Pattern: look.pattern = (CanopyPattern)MenuMath.Step((int)look.pattern, dir, PatternNames.Length); break;
            }
            S.Set(s => s.PilotLook = look);
        }

        /// <summary>Draws the panel. True when the back arrow was clicked.</summary>
        public bool Draw(float width, float height, MenuNav nav, float glow, Color team)
        {
            float panelH = 96f + Rows.Length * RowH + 70f;
            var panel = new Rect(Mathf.Max(48f, width * 0.06f), Mathf.Max(40f, (height - panelH) / 2f), PanelW, panelH);
            _g.Panel(panel, glow);
            var backRect = new Rect(panel.x + 20f, panel.y + 22f, 52f, 52f);
            bool hover = backRect.Contains(Event.current.mousePosition);
            _g.Tracked(backRect, "‹", _g.Title, 0f, TextAnchor.MiddleCenter, hover ? Color.white : Alpha(White, 0.7f), 1.2f);
            bool back = GUI.Button(backRect, GUIContent.none, GUIStyle.none);
            _g.Tracked(new Rect(panel.x + 84f, panel.y + 24f, panel.width - 120f, 48f), "PILOT", _g.Title, 5.6f, TextAnchor.MiddleLeft, Color.white);
            _g.Rule(new Rect(panel.x + 36f, panel.y + 80f, panel.width - 72f, 1f));

            float y = panel.y + 96f;
            for (int i = 0; i < Rows.Length; i++)
                DrawRow(new Rect(panel.x + 36f, y + i * RowH, panel.width - 72f, RowH), i, nav, team);
            _g.Foot(panel, "CHANGES SAVE AT ONCE", nav.Glyph("ARROWS  CHANGE   ESC  BACK", "D-PAD  CHANGE   B  BACK") ?? "");
            return back;
        }

        void DrawRow(Rect r, int index, MenuNav nav, Color team)
        {
            var row = Rows[index];
            bool hover = r.Contains(Event.current.mousePosition);
            if (hover && nav.MouseMoved && Event.current.type == EventType.Repaint)
                _focus = index;
            bool focused = index == _focus;
            if (index > 0)
                _g.Fill(new Rect(r.x, r.y, r.width, 1f), Alpha(Color.white, 0.05f));
            if (focused)
                _g.Lit(r, 1f, true);
            var tint = focused ? Color.white : Alpha(White, 0.85f);
            _g.Tracked(new Rect(r.x + 28f, r.y, r.width * 0.4f, r.height), Label(row), _g.Item, 2.6f, TextAnchor.MiddleLeft, tint, 0.9f);

            var area = new Rect(r.x + r.width * 0.4f, r.y, r.width * 0.6f - 20f, r.height);
            if (row == RowKind.Reset)
            {
                _g.Tracked(area, "DEFAULTS", _g.Item, 2.2f, TextAnchor.MiddleRight, Alpha(tint, 0.8f), 0.85f);
                if (GUI.Button(r, GUIContent.none, GUIStyle.none)) { _focus = index; Change(row, 1); }
                return;
            }
            var left = new Rect(area.x, r.y, 40f, r.height);
            var right = new Rect(area.xMax - 40f, r.y, 40f, r.height);
            _g.Tracked(left, "‹", _g.Item, 0f, TextAnchor.MiddleCenter, Alpha(tint, focused ? 0.9f : 0.4f), 1.3f);
            _g.Tracked(right, "›", _g.Item, 0f, TextAnchor.MiddleCenter, Alpha(tint, focused ? 0.9f : 0.4f), 1.3f);
            if (GUI.Button(left, GUIContent.none, GUIStyle.none)) { _focus = index; Change(row, -1); }
            if (GUI.Button(right, GUIContent.none, GUIStyle.none)) { _focus = index; Change(row, 1); }
            var mid = new Rect(left.xMax + 8f, r.y, right.x - left.xMax - 16f, r.height);
            if (GUI.Button(mid, GUIContent.none, GUIStyle.none)) { _focus = index; Change(row, 1); }

            if (row == RowKind.Pattern)
            {
                _g.Tracked(mid, PatternNames[Mathf.Clamp((int)Look.pattern, 0, PatternNames.Length - 1)], _g.Item, 2.2f,
                    TextAnchor.MiddleCenter, tint, 0.85f);
                return;
            }
            // A colour chip, then the swatch's name.
            string id = Swatch(row);
            float textW = _g.TrackedWidth(PilotColors.Name(id), _g.Item, 2.2f, 0.85f);
            float start = mid.center.x - (Chip + 14f + textW) / 2f;
            var chip = new Rect(start, r.center.y - Chip / 2f, Chip, Chip);
            _g.RoundFill(chip, Chip / 2f, PilotColors.Resolve(id, team));
            _g.RoundStroke(chip, Chip / 2f, 1.5f, Alpha(Color.white, focused ? 0.9f : 0.5f));
            _g.Tracked(new Rect(chip.xMax + 14f, r.y, textW + 20f, r.height), PilotColors.Name(id), _g.Item, 2.2f,
                TextAnchor.MiddleLeft, tint, 0.85f);
        }

        static string Label(RowKind row) => row switch
        {
            RowKind.Suit => "FLIGHT SUIT",
            RowKind.Helmet => "HELMET",
            RowKind.Canopy => "CANOPY",
            RowKind.Trim => "CANOPY TRIM",
            RowKind.Pattern => "PATTERN",
            _ => "RESET PILOT",
        };

        static string Swatch(RowKind row) => row switch
        {
            RowKind.Suit => Look.suit,
            RowKind.Helmet => Look.helmet,
            RowKind.Canopy => Look.canopy,
            _ => Look.trim,
        };
    }
}

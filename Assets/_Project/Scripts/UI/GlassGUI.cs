using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.UI
{
    /// <summary>
    /// The glass look from Design/06-UI-UX/glass-menu-style-guide.html for IMGUI: dark tinted glass panels, DIN type
    /// with hand-placed letter spacing, the selection rail, key caps and rules. Shared by <see cref="MatchScreens"/> and
    /// <see cref="FlightHUD"/>. Call <see cref="EnsureStyles"/> inside OnGUI before drawing, and <see cref="Dispose"/> when done.
    /// </summary>
    public sealed class GlassGUI
    {
        public const float Radius = 14f;
        public const float GlowRest = 0.07f, GlowOpen = 0.14f, GlowSettle = 0.18f;

        // Palette from the style guide.
        public static readonly Color Glow = new(0.863f, 0.922f, 1f);          // #DCEBFF, edges only
        public static readonly Color Steel = new(0.541f, 0.573f, 0.612f);     // #8A929C secondary text
        public static readonly Color Body = new(0.706f, 0.729f, 0.761f);      // #B4BAC2
        public static readonly Color White = new(0.949f, 0.957f, 0.969f);     // #F2F4F7
        public static readonly Color Dim = new(0f, 0f, 0f, 0.45f);

        public GUIStyle Title, Huge, Item, Hint, BodyText, Numeral;
        /// <summary>Multiplies the alpha of everything drawn: 0..1, for fading a whole panel in.</summary>
        public float Opacity = 1f;
        Texture2D _white, _ramp;
        Font _din, _inter;
        readonly Dictionary<int, Texture2D> _glass = new();
        readonly Dictionary<(GUIStyle, int, char), float> _advance = new();
        readonly HashSet<(Font, int, FontStyle)> _warmed = new();

        /// <summary>Every glyph the HUD and menus draw, put in the font atlas together (see <see cref="Warm"/>).</summary>
        const string Charset = " !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~·–—°×";

        public void Dispose()
        {
            foreach (var t in _glass.Values)
                if (t != null)
                    Object.Destroy(t);
            _glass.Clear();
            if (_ramp != null)
                Object.Destroy(_ramp);
        }

        // ---------- Components ----------

        /// <summary>Title row: big DIN title on the left, small steel caption on the right, then the fading rule.</summary>
        public void Header(Rect panel, string title, string caption)
        {
            Tracked(new Rect(panel.x + 36f, panel.y + 24f, panel.width - 72f, 48f), title, Title, 5.6f, TextAnchor.MiddleLeft, Color.white);
            Tracked(new Rect(panel.x + 36f, panel.y + 24f, panel.width - 72f, 48f), caption, Hint, 2.4f, TextAnchor.MiddleRight, Steel);
            Rule(new Rect(panel.x + 36f, panel.y + 80f, panel.width - 72f, 1f));
        }

        public void Foot(Rect panel, string left, string right)
        {
            Tracked(new Rect(panel.x + 36f, panel.yMax - 40f, panel.width * 0.6f, 24f), left, Hint, 1.7f, TextAnchor.MiddleLeft, Steel);
            Tracked(new Rect(panel.xMax - 36f - panel.width * 0.3f, panel.yMax - 40f, panel.width * 0.3f, 24f), right, Hint, 1.7f, TextAnchor.MiddleRight, Steel);
        }

        /// <summary>The tinted-glass body: soft drop shadow, a low edge glow, then the baked panel texture.</summary>
        public void Panel(Rect r, float glow, bool shadow = true)
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
            GUI.color = Faded(Color.white);
            GUI.DrawTexture(r, GlassTexture(Mathf.RoundToInt(r.width), Mathf.RoundToInt(r.height)));
            GUI.color = c;
        }

        /// <summary>A menu row: 56 px, DIN caps. Selected (or hovered) rows get the white rail and fading fill.</summary>
        public bool Button(Rect r, string label, string kbd, bool selected, bool dim, bool divider, TextAnchor align = TextAnchor.MiddleLeft)
        {
            bool hover = !dim && r.Contains(Event.current.mousePosition);
            if (divider)
                Fill(new Rect(r.x, r.y, r.width, 1f), Alpha(Color.white, 0.05f));
            if (selected || hover)
                Lit(r, selected ? 1f : 0.55f, true);
            var tint = dim ? Alpha(White, 0.38f) : selected ? Color.white : Alpha(White, hover ? 1f : 0.88f);
            float pad = align == TextAnchor.MiddleLeft ? 36f : 12f;
            float kbdRoom = kbd != null && align != TextAnchor.MiddleLeft ? 70f : 0f;
            Tracked(new Rect(r.x + pad, r.y, r.width - pad * 2f - kbdRoom, r.height), label, Item, 3.0f, align, tint);
            if (kbd != null)
                Kbd(new Rect(r.xMax - 20f, r.y, 0f, r.height), kbd);
            return GUI.Button(r, GUIContent.none, GUIStyle.none);
        }

        /// <summary>The selection look: left-to-right white fade (11% to 2%) and the 2 px glowing rail.</summary>
        public void Lit(Rect r, float strength, bool rail)
        {
            var c = GUI.color;
            GUI.color = Faded(new Color(1f, 1f, 1f, 0.11f * strength));
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
        public void Kbd(Rect at, string text)
        {
            float w = TrackedWidth(text, Hint, 1.5f) + 16f;
            var box = new Rect(at.x - w, at.y + (at.height - 22f) / 2f, w, 22f);
            RoundStroke(box, 4f, 1f, Alpha(Color.white, 0.14f));
            Tracked(box, text, Hint, 1.5f, TextAnchor.MiddleCenter, Steel);
        }

        public void Rule(Rect r)
        {
            var c = GUI.color;
            GUI.color = Faded(new Color(1f, 1f, 1f, 0.28f));
            GUI.DrawTexture(r, _ramp);
            GUI.color = c;
        }

        // ---------- Text ----------

        public float TrackedWidth(string text, GUIStyle style, float tracking, float scale = 1f)
        {
            int size = Mathf.RoundToInt(style.fontSize * scale);
            float w = 0f;
            foreach (char ch in text)
                w += Advance(style, size, ch) + tracking * scale;
            return Mathf.Max(0f, w - tracking * scale);
        }

        static readonly Dictionary<char, string> Glyphs = new();

        /// <summary>
        /// <paramref name="ch"/> as a one-letter string, made once: every HUD letter is its own label, every frame, so
        /// a fresh string each time added up to thousands of small allocations a second (and garbage collector hitches).
        /// </summary>
        static string Glyph(char ch)
        {
            if (!Glyphs.TryGetValue(ch, out var s))
                Glyphs[ch] = s = ch.ToString();
            return s;
        }

        /// <summary>
        /// Puts the whole <see cref="Charset"/> in the dynamic font's atlas the first time a size is used. Asking for
        /// glyphs one at a time made Unity rebuild the atlas partway through drawing, and letters already laid out that
        /// frame then sampled the wrong spot ("COMBAT AREA" drew as "COMBCT CREC").
        /// </summary>
        void Warm(GUIStyle style, int size)
        {
            var font = style.font;
            if (font != null && font.dynamic && _warmed.Add((font, size, style.fontStyle)))
                font.RequestCharactersInTexture(Charset, size, style.fontStyle);
        }

        float Advance(GUIStyle style, int size, char ch)
        {
            if (_advance.TryGetValue((style, size, ch), out float w))
                return w;
            Warm(style, size);
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
        public void Tracked(Rect r, string text, GUIStyle style, float tracking, TextAnchor anchor, Color color, float scale = 1f)
        {
            int size = Mathf.RoundToInt(style.fontSize * scale);
            Warm(style, size);
            float total = TrackedWidth(text, style, tracking, scale);
            float x = anchor switch
            {
                TextAnchor.MiddleCenter or TextAnchor.UpperCenter or TextAnchor.LowerCenter => r.x + (r.width - total) / 2f,
                TextAnchor.MiddleRight or TextAnchor.UpperRight or TextAnchor.LowerRight => r.xMax - total,
                _ => r.x,
            };
            var c = GUI.color;
            GUI.color = Faded(color);
            int oldSize = style.fontSize;
            style.fontSize = size;
            foreach (char ch in text)
            {
                float adv = Advance(style, size, ch);
                if (ch != ' ')
                    GUI.Label(new Rect(x, r.y, adv + 8f, r.height), Glyph(ch), style);
                x += adv + tracking * scale;
            }
            style.fontSize = oldSize;
            GUI.color = c;
        }

        /// <summary>
        /// <see cref="Tracked"/> with a soft dark halo behind it, for text drawn straight over the scene (no panel):
        /// readable over bright sky and cloud.
        /// </summary>
        public void Shadowed(Rect r, string text, GUIStyle style, float tracking, TextAnchor anchor, Color color, float scale = 1f)
        {
            var shade = new Color(0f, 0f, 0f, 0.55f * color.a);
            Tracked(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, style, tracking, anchor, shade, scale);
            Tracked(r, text, style, tracking, anchor, color, scale);
        }

        // ---------- Drawing helpers ----------

        public static Rect Centered(float width, float w, float h, float top) => new((width - w) / 2f, top, w, h);

        public static Rect Inflate(Rect r, float by, float dy) => new(r.x - by, r.y - by + dy, r.width + by * 2f, r.height + by * 2f);

        public static Color Alpha(Color c, float a) => new(c.r, c.g, c.b, a);

        /// <summary><paramref name="c"/> with <see cref="Opacity"/> applied, for raw GUI draws.</summary>
        public Color Faded(Color c) => Opacity >= 1f ? c : new Color(c.r, c.g, c.b, c.a * Opacity);

        public void Fill(Rect r, Color color)
        {
            var c = GUI.color;
            GUI.color = Faded(color);
            GUI.DrawTexture(r, _white);
            GUI.color = c;
        }

        public void RoundFill(Rect r, float radius, Color color) =>
            GUI.DrawTexture(r, _white, ScaleMode.StretchToFill, true, 0f, Faded(color), Vector4.zero, new Vector4(radius, radius, radius, radius));

        public void RoundStroke(Rect r, float radius, float thickness, Color color) =>
            GUI.DrawTexture(r, _white, ScaleMode.StretchToFill, true, 0f, Faded(color), new Vector4(thickness, thickness, thickness, thickness),
                new Vector4(radius, radius, radius, radius));

        // ---------- Setup ----------

        public void EnsureStyles()
        {
            if (Item != null)
                return;
            _white = Texture2D.whiteTexture;
            _ramp = new Texture2D(64, 1, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            for (int i = 0; i < 64; i++)
                _ramp.SetPixel(i, 0, new Color(1f, 1f, 1f, Mathf.Lerp(1f, 0.15f, i / 63f)));
            _ramp.Apply();

            // DIN-like signage face: Bahnschrift on Windows, falling back through similar faces (Roboto Condensed on
            // Android). Body face: Inter / Segoe UI / Roboto.
            // A font file bundled at Resources/Fonts/Display.ttf / Body.ttf ships inside the build and wins over device fonts.
            _din = Resources.Load<Font>("Fonts/Display") ?? OSFont(28,"Bahnschrift", "DIN Alternate", "D-DIN", "Segoe UI Semibold", "Roboto Condensed", "Roboto", "Arial");
            _inter = Resources.Load<Font>("Fonts/Body") ?? OSFont(15,"Inter", "Segoe UI Variable", "Segoe UI", "Roboto", "Arial");

            Title = Style(_din, 36, FontStyle.Bold);
            Item = Style(_din, 20, FontStyle.Normal);
            Hint = Style(_din, 15, FontStyle.Normal);
            Numeral = Style(_din, 28, FontStyle.Normal);
            Huge = Style(_din, 150, FontStyle.Bold);
            BodyText = Style(_inter, 17, FontStyle.Normal);
            BodyText.alignment = TextAnchor.UpperLeft;
            BodyText.wordWrap = true;
            foreach (var style in new[] { Title, Item, Hint, Numeral, Huge, BodyText })
                Warm(style, style.fontSize);
        }

        /// <summary>
        /// The first of <paramref name="names"/> installed on this device. A dynamic font asked for names the OS lacks
        /// (Windows faces on Android) has no glyphs and draws nothing, so with no match fall back to Unity's built-in
        /// font, which every build includes.
        /// </summary>
        static Font OSFont(int size, params string[] names)
        {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), System.StringComparer.OrdinalIgnoreCase);
            foreach (var name in names)
                if (installed.Contains(name))
                    return Font.CreateDynamicFontFromOSFont(name, size);
            return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
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

using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.Match;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    // Capture the Banner: both banners' state under the score bar, a marker on each banner and the friendly carrier
    // (enemy carriers are radar contacts already, see PlaneAbility.IsRevealedTo), banners on the minimap, and callouts.
    public partial class FlightHUD
    {
        const float CalloutTime = 3.5f;
        const int CalloutMax = 3;

        struct Callout
        {
            public string Text;
            public HudText.Tone Tone;
            public float Time;
        }

        readonly List<Callout> _callouts = new();
        BannerWorld _bannerWorld;

        /// <summary>The callouts showing now, oldest first. For tests.</summary>
        public IEnumerable<string> CalloutTexts
        {
            get
            {
                foreach (var c in _callouts)
                    yield return c.Text;
            }
        }

        /// <summary>The match's banners, or null outside Capture the Banner. Follows the world across matches.</summary>
        BannerWorld Banners
        {
            get
            {
                var world = match != null && !match.IsDismissed ? match.Banners : null;
                if (world != _bannerWorld)
                {
                    if (_bannerWorld != null)
                        _bannerWorld.EventApplied -= OnBannerEvent;
                    _bannerWorld = world;
                    if (_bannerWorld != null)
                        _bannerWorld.EventApplied += OnBannerEvent;
                    _callouts.Clear();
                }
                return world;
            }
        }

        void OnBannerEvent(BannerEvent e)
        {
            if (plane == null)
                return;
            var pilot = e.Pilot as PlaneController;
            string text = HudText.BannerCallout(e.Kind, e.BannerTeam, e.PilotTeam, plane.Team,
                pilot != null ? PilotName(pilot) : null, e.PilotTeam >= 0 ? TeamName(e.PilotTeam) : null, out var tone);
            AddCallout(text, tone);
        }

        /// <summary>Stacks a mode callout under the center warning (the oldest goes past <see cref="CalloutMax"/>). Null is ignored.</summary>
        void AddCallout(string text, HudText.Tone tone)
        {
            if (text == null)
                return;
            _callouts.Add(new Callout { Text = text, Tone = tone, Time = Time.time });
            if (_callouts.Count > CalloutMax)
                _callouts.RemoveAt(0);
        }

        static Color ToneColor(HudText.Tone tone) => tone == HudText.Tone.Good ? Green : tone == HudText.Tone.Bad ? Red : White;

        void DrawBanners(float scale, float width)
        {
            var world = Banners;
            if (world == null || world.Board == null)
                return;
            DrawBannerBar(world, width);
            DrawBannerMarkers(world, scale, width);
        }

        string CarrierName(BannerBoard.Banner b) => b.Carrier is PlaneController c ? PilotName(c) : "?";

        /// <summary>Two chips under the score bar, one per banner: whose it is, then HOME / TAKEN BY x / DROPPED + timer.</summary>
        void DrawBannerBar(BannerWorld world, float width)
        {
            var board = world.Board;
            for (int team = 0; team < board.TeamCount && team < 2; team++)
            {
                var b = board.Get(team);
                bool ours = team == plane.Team;
                var chip = new Rect(team == 0 ? width / 2f - 240f : width / 2f + 4f, 88f, 236f, 42f);
                _g.Panel(chip, GlowRest, false);
                // Taken from us: red. Taken by us: green. A dropped one is a chance to return (ours) or grab (theirs).
                Color status = b.State == BannerState.Home ? Alpha(White, 0.7f)
                    : b.State == BannerState.Carried ? (ours ? Red : Green)
                    : Amber;
                if (b.State == BannerState.Carried && !Settings.GameSettings.Blink(Time.time, 1.2f, 0.7f))
                    status = Alpha(status, 0.55f);
                _g.Tracked(new Rect(chip.x + 12f, chip.y + 3f, chip.width - 24f, 16f), $"{TeamName(team)} BANNER", _g.Hint, 2f,
                    TextAnchor.MiddleLeft, TeamColor(team), 0.8f);
                _g.Tracked(new Rect(chip.x + 12f, chip.y + 19f, chip.width - 24f, 20f), HudText.BannerStatus(b.State, CarrierName(b), b.ReturnLeft),
                    _g.Hint, 1.5f, TextAnchor.MiddleLeft, status, 0.9f);
                if (b.State == BannerState.Dropped)
                {
                    var bar = new Rect(chip.x + 12f, chip.yMax - 5f, chip.width - 24f, 3f);
                    _g.Fill(bar, Alpha(Color.white, 0.12f));
                    _g.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(b.ReturnLeft / board.ReturnTime), bar.height), status);
                }
            }
            if (plane.IsAlive && plane.CarryingBanner)
                _g.Tracked(new Rect(0f, 134f, width, 18f), "TOWING THE BANNER  ·  SLOWER  ·  NO BOOST  ·  NO REPAIR  ·  FLY LOW THROUGH YOUR STAND",
                    _g.Hint, 2f, TextAnchor.MiddleCenter, Alpha(Amber, 0.95f), 0.8f);
        }

        /// <summary>
        /// A marker on each banner that is away from its stand, and on the enemy stand to go and take: over it in view,
        /// an edge arrow out of view. Your own banner at home needs none; the enemy carrier is already a radar contact.
        /// </summary>
        void DrawBannerMarkers(BannerWorld world, float scale, float width)
        {
            if (viewCamera == null || !plane.IsAlive)
                return;
            var board = world.Board;
            for (int team = 0; team < board.TeamCount; team++)
            {
                var b = board.Get(team);
                bool ours = team == plane.Team;
                if (ours && b.State == BannerState.Home)
                    continue;
                if (b.State == BannerState.Carried && b.Carrier == plane)
                    continue;
                // Their banner in the enemy's hands is ours to guard: mark the carrier; ours in their hands is the one to hunt.
                bool carried = b.State == BannerState.Carried && b.Carrier is PlaneController;
                Vector3 at = carried ? ((PlaneController)b.Carrier).Position : world.Cloth(team).Position;
                string label = b.State switch
                {
                    BannerState.Carried => ours ? "SHOOT THE CARRIER" : "ESCORT THE CARRIER",
                    BannerState.Dropped => ours ? "RETURN YOUR BANNER" : "TAKE THEIR BANNER",
                    _ => "TAKE THEIR BANNER",
                };
                Color color = Alpha(Color.Lerp(TeamColor(team), White, 0.35f), 0.9f);
                DrawEdgeMarker(at, $"{label}  {HudText.Distance(Vector3.Distance(plane.Position, at))}", color, scale, width);
            }
        }

        void DrawEdgeMarker(Vector3 world, string label, Color color, float scale, float width)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            var inset = new Rect(80f, 160f, width - 160f, _height - 160f - 230f);
            bool off = HudMath.OffScreen(new Vector2(sp.x, Screen.height - sp.y) / scale, sp.z > 0f, inset, out Vector2 at, out Vector2 dir);
            if (!off)
            {
                Diamond(at, 12f, color);
                _g.Shadowed(new Rect(at.x - 130f, at.y - 36f, 260f, 20f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.9f);
                return;
            }
            Arrow(at, dir, 20f, color);
            Vector2 textAt = at - dir * 40f;
            _g.Shadowed(new Rect(textAt.x - 130f, textAt.y - 10f, 260f, 20f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.9f);
        }

        /// <summary>Banners on the minimap: a team-coloured square per banner, pulsing ring once it is away from its stand.</summary>
        void DrawBannerMapMarks(Rect map, float turn)
        {
            var world = Banners;
            if (world == null || world.Board == null)
                return;
            var board = world.Board;
            for (int team = 0; team < board.TeamCount; team++)
            {
                var b = board.Get(team);
                Vector3 pos = b.State == BannerState.Carried && b.Carrier is PlaneController c ? c.Position
                    : b.State == BannerState.Dropped ? b.DropPosition : b.Stand;
                Vector2 p = HudMath.MapPoint(pos, _mapCenter, _mapRadius, map, turn);
                if (!map.Contains(p))
                    continue;
                Color color = TeamColor(team);
                float s = b.State == BannerState.Home ? 6f : 9f;
                _g.Fill(new Rect(p.x - s / 2f - 1.5f, p.y - s / 2f - 1.5f, s + 3f, s + 3f), Alpha(Color.black, 0.8f));
                _g.Fill(new Rect(p.x - s / 2f, p.y - s / 2f, s, s), color);
                if (b.State != BannerState.Home)
                {
                    float ring = 8f + 4f * Mathf.Repeat(Time.time * 1.5f, 1f);
                    _g.RoundStroke(new Rect(p.x - ring, p.y - ring, ring * 2f, ring * 2f), ring, 1.5f, Alpha(color, 0.85f));
                }
            }
        }

        /// <summary>Mode callouts (banners, fields) stacked under the center warning, fading in the last second.</summary>
        void DrawCallouts(float width)
        {
            for (int i = _callouts.Count - 1; i >= 0; i--)
                if (Time.time - _callouts[i].Time > CalloutTime)
                    _callouts.RemoveAt(i);
            float y = 192f;
            foreach (var c in _callouts)
            {
                float fade = Mathf.Clamp01(CalloutTime - (Time.time - c.Time));
                _g.Shadowed(new Rect(0f, y, width, 34f), c.Text, _g.Item, 3f, TextAnchor.MiddleCenter, Alpha(ToneColor(c.Tone), fade), 1.1f);
                y += 34f;
            }
        }
    }
}

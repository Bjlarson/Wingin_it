using UnityEngine;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    // Ram Royale: a marker on every plane in view in its pilot's colour and a big one on the nearest (an arrow on the
    // screen edge when it's out of view), INCOMING RAM when a plane is about to fly into us, every plane on the minimap,
    // and the ram flash. Any hard-edged arena also gets an arrow back to the middle while near its edge.
    // Planes left is on the score bar (MatchScreens, MatchWidgets.PlanesLeft).
    public partial class FlightHUD
    {
        const float RamFlashTime = 1.6f, RamEdgeFlashTime = 0.35f;
        const float PlaneMarkerRange = 1500f;
        const float IncomingTime = 2.5f, IncomingMiss = 30f, IncomingRange = 600f, IncomingOwnSpeed = 20f;

        string _ramFlash, _ramFlashLine;
        HudText.Tone _ramTone;
        float _ramTime = float.NegativeInfinity;

        /// <summary>True while a Ram Royale match is on.</summary>
        bool RamRoyale => match != null && !match.IsDismissed && match.Mode is RamRoyaleMode;

        /// <summary>The ram flash showing now ("RAMMED BOT 5!", "RAMMED BY BOT 5"), or null. For tests.</summary>
        public string RamFlashText => Time.time - _ramTime < RamFlashTime ? _ramFlash : null;

        /// <summary>The plane the nearest-plane marker is on this frame (Ram Royale only), or null. For tests.</summary>
        public PlaneController NearestPlane => plane != null && plane.IsAlive && RamRoyale ? Nearest() : null;

        void OnAnyRammed(RamInfo ram)
        {
            if (plane == null || (ram.Hitter != plane && ram.Victim != plane))
                return;
            bool hitter = ram.Hitter == plane;
            var other = hitter ? ram.Victim : ram.Hitter;
            float dealt = hitter ? ram.VictimDamage : ram.HitterDamage;
            float took = hitter ? ram.HitterDamage : ram.VictimDamage;
            string text = HudText.RamFlash(other != null ? PilotName(other) : null, dealt, took, hitter, out var tone);
            if (text == null)
                return;
            (_ramFlash, _ramFlashLine, _ramTone, _ramTime) = (text, HudText.RamDamage(dealt, took), tone, Time.time);
        }

        PlaneController Nearest()
        {
            PlaneController best = null;
            float bestSq = float.MaxValue;
            foreach (var other in PlaneController.All)
            {
                if (other == plane || !other.IsAlive)
                    continue;
                float sq = (other.Position - plane.Position).sqrMagnitude;
                if (sq < bestSq)
                    (best, bestSq) = (other, sq);
            }
            return best;
        }

        void DrawRamRoyale(float scale, float width)
        {
            DrawArenaEdge(scale, width);
            if (RamRoyale && plane.IsAlive && viewCamera != null)
            {
                DrawPlaneMarkers(scale, width);
                DrawIncomingRam(width);
            }
            DrawRamFlash(width);
        }

        /// <summary>
        /// Small corner brackets on every plane in view within <see cref="PlaneMarkerRange"/> (no gun brackets with the
        /// weapons off), in its pilot's colour. The nearest gets a ring, its name, distance and closing speed, or a big
        /// arrow on the screen edge pointing at it when it's out of view.
        /// </summary>
        void DrawPlaneMarkers(float scale, float width)
        {
            var nearest = Nearest();
            foreach (var other in PlaneController.All)
            {
                if (other == plane || other == nearest || !other.IsAlive)
                    continue;
                if (Vector3.Distance(plane.Position, other.Position) > PlaneMarkerRange || !ToScreen(other.Position, scale, out Vector2 p))
                    continue;
                Brackets(p, 12f, 5f, Alpha(TeamColor(other.Team), 0.7f));
            }
            if (nearest == null)
                return;

            Color color = Alpha(Color.Lerp(TeamColor(nearest.Team), Color.white, 0.2f), 0.95f);
            Vector3 rel = nearest.Position - plane.Position;
            float closing = HudMath.ClosingSpeed(rel, nearest.Velocity - plane.Velocity);
            string label = $"{PilotName(nearest)}  {HudText.Distance(rel.magnitude)}";

            Vector3 sp = viewCamera.WorldToScreenPoint(nearest.Position);
            var inset = new Rect(80f, 130f, width - 160f, _height - 130f - 230f);
            bool off = HudMath.OffScreen(new Vector2(sp.x, Screen.height - sp.y) / scale, sp.z > 0f, inset, out Vector2 at, out Vector2 dir);
            if (!off)
            {
                float pulse = 22f + 3f * Mathf.Sin(Time.time * 6f);
                Circle(at, pulse, 2.5f, color);
                Brackets(at, pulse + 8f, 8f, color, 2.5f);
                _g.Shadowed(new Rect(at.x - 160f, at.y - pulse - 30f, 320f, 20f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.9f);
                if (closing > 1f)
                    _g.Shadowed(new Rect(at.x - 160f, at.y + pulse + 10f, 320f, 18f), $"CLOSING {closing:0} M/S", _g.Hint, 2f,
                        TextAnchor.MiddleCenter, Alpha(White, 0.8f), 0.8f);
                return;
            }
            Arrow(at, dir, 34f, color);
            Vector2 textAt = at - dir * 46f;
            _g.Shadowed(new Rect(textAt.x - 140f, textAt.y - 10f, 280f, 20f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.9f);
        }

        /// <summary>
        /// INCOMING RAM when a plane flying at us (at least <see cref="IncomingOwnSpeed"/> of its own speed our way, so
        /// not one we're chasing down) will pass within <see cref="IncomingMiss"/> inside <see cref="IncomingTime"/> if
        /// both hold course, with an arrow round the crosshair pointing at it (the soonest one if several).
        /// </summary>
        void DrawIncomingRam(float width)
        {
            PlaneController threat = null;
            float soonest = IncomingTime;
            foreach (var other in PlaneController.All)
            {
                if (other == plane || !other.IsAlive)
                    continue;
                Vector3 rel = other.Position - plane.Position;
                if (rel.sqrMagnitude > IncomingRange * IncomingRange || Vector3.Dot(other.Velocity, -rel.normalized) < IncomingOwnSpeed)
                    continue;
                if (HudMath.ClosestApproach(rel, other.Velocity - plane.Velocity, out float t, out float miss) && miss < IncomingMiss && t < soonest)
                    (threat, soonest) = (other, t);
            }
            if (threat == null)
                return;
            Color color = GameSettings.Blink(Time.time, 0.3f, 0.6f) ? Amber : Color.white;
            _g.Shadowed(new Rect(0f, 196f, width, 48f), $"INCOMING RAM  ·  {PilotName(threat)}", _g.Title, 4f, TextAnchor.MiddleCenter, color, 0.8f);

            Vector3 sp = viewCamera.WorldToScreenPoint(threat.Position);
            float scale = Screen.height / ReferenceHeight * GameSettings.Current.Data.hudScale;
            Vector2 center = new(width * 0.5f, _height * 0.5f);
            Vector2 dir = new Vector2(sp.x, Screen.height - sp.y) / scale - center;
            if (sp.z < 0f)
                dir = -dir; // behind the camera the projection is mirrored
            if (dir.sqrMagnitude > 1f)
                Arrow(center + dir.normalized * 150f, dir, 34f, Alpha(Amber, 0.95f));
        }

        /// <summary>
        /// Near or past a hard edge (the Ram Royale arena): a red arrow round the crosshair pointing back to the middle,
        /// and the screen's rim glowing red, stronger the closer to the edge. The text line is <see cref="CenterWarning"/>.
        /// </summary>
        void DrawArenaEdge(float scale, float width)
        {
            var boundary = FlightBoundary.Active;
            if (boundary == null || !boundary.hardEdge || !plane.IsAlive || viewCamera == null || !(plane.IsNearBoundary || plane.IsOutOfBounds))
                return;
            float toEdge = boundary.radius - boundary.HorizontalDistance(plane.Position);
            float danger = plane.IsOutOfBounds ? 1f : Mathf.Clamp01(1f - toEdge / Mathf.Max(1f, boundary.warningMargin));
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * (plane.IsOutOfBounds ? 12f : 6f));
            RimGlow(width, Alpha(Red, (0.12f + 0.3f * danger) * pulse), 60f);

            Vector3 middle = boundary.Center;
            middle.y = plane.Position.y;
            Vector3 sp = viewCamera.WorldToScreenPoint(middle);
            Vector2 center = new(width * 0.5f, _height * 0.5f);
            Vector2 dir = new Vector2(sp.x, Screen.height - sp.y) / scale - center;
            if (sp.z < 0f)
                dir = -dir;
            if (dir.sqrMagnitude > 1f)
                Arrow(center + dir.normalized * 190f, dir, 44f, Alpha(Red, 0.6f + 0.4f * pulse));
        }

        /// <summary>
        /// The ram flash: the callout pops above the crosshair with the damage each side took under it, fading out over
        /// <see cref="RamFlashTime"/>. A ram we won also flashes the screen's rim gold; one we lost doesn't need to, the
        /// damage vignette (<see cref="DamageFeedback"/>) already flashes red.
        /// </summary>
        void DrawRamFlash(float width)
        {
            float age = Time.time - _ramTime;
            if (age >= RamFlashTime || _ramFlash == null)
                return;
            Color tone = _ramTone == HudText.Tone.Bad ? Red : Amber;
            if (_ramTone == HudText.Tone.Good && age < RamEdgeFlashTime)
                RimGlow(width, Alpha(Color.Lerp(tone, Color.white, 0.3f), 0.35f * (1f - age / RamEdgeFlashTime)), 70f);

            float fade = Mathf.Clamp01((RamFlashTime - age) / 0.4f);
            float pop = 1.35f - 0.35f * Mathf.Clamp01(age / 0.15f);
            float y = _height * 0.5f - 190f;
            _g.Shadowed(new Rect(0f, y, width, 60f), _ramFlash, _g.Title, 5f, TextAnchor.MiddleCenter, Alpha(tone, fade), pop);
            _g.Shadowed(new Rect(0f, y + 58f, width, 30f), _ramFlashLine, _g.Item, 2.4f, TextAnchor.MiddleCenter, Alpha(White, 0.92f * fade), 0.9f);
        }

        /// <summary>A glow round the screen's rim: soft bands fading in from each edge.</summary>
        void RimGlow(float width, Color color, float depth)
        {
            const int bands = 6;
            for (int i = 0; i < bands; i++)
            {
                float d = depth * (i + 1) / bands;
                var c = Alpha(color, color.a / bands);
                _g.Fill(new Rect(0f, 0f, width, d), c);
                _g.Fill(new Rect(0f, _height - d, width, d), c);
                _g.Fill(new Rect(0f, d, d, _height - d * 2f), c);
                _g.Fill(new Rect(width - d, d, d, _height - d * 2f), c);
            }
        }
    }
}

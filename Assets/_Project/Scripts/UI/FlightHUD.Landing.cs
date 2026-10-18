using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    // The landing assist and approach lanes (Design/06-UI-UX/hud.md#landing-assist-proposed).
    public partial class FlightHUD
    {
        static readonly Color LaneColor = new(0.55f, 0.95f, 1f, 0.9f);
        const float GlideRingSpacing = 150f;
        const int GlideRingsShown = 6;
        const float GlideRingMinAhead = 150f;
        const float GlideRingMaxRadius = 20f;

        // This frame's approach to the nearest friendly runway (default when there is none, or on the ground).
        Airfield _approachField;
        ApproachGuide _approach;

        void UpdateApproach()
        {
            _approachField = plane.IsAlive ? Airfield.NearestFriendly(plane.Position, plane.Team) : null;
            _approach = _approachField != null && !plane.IsGrounded
                ? _approachField.GetApproach(plane.Position, plane.Velocity) : default;
        }

        /// <summary>
        /// On approach to a friendly runway: rings along the glide path to fly through (green while on the path), a bar
        /// across the runway at the aim point, the repair zone outlined on the ground, and an ILS-style panel beside the
        /// crosshair. The zone outline stays up while rolling in after touchdown.
        /// </summary>
        void DrawLandingAssist(float scale, float width)
        {
            var field = _approachField;
            if (field == null || viewCamera == null)
                return;
            bool rollingIn = plane.IsGrounded && plane.HasTouchedDown && Airfield.RunwayUnder(plane.Position) == field
                && (repair == null || !repair.IsRepairing);
            if (_approach.OnApproach || rollingIn)
                DrawRepairZoneOutline(field.RepairZone, scale);
            if (!_approach.OnApproach)
                return;

            var guide = _approach;
            Color pathColor = guide.OnPath ? Green : Amber;

            // Rings ahead of us along the glide path, nearest brightest. Not too close or too big: a ring right in
            // front of the camera would scatter its dots over the whole screen.
            int first = Mathf.FloorToInt((guide.Distance - GlideRingMinAhead) / GlideRingSpacing);
            for (int k = first, shown = 0; k >= 1 && shown < GlideRingsShown; k--, shown++)
            {
                float d = k * GlideRingSpacing;
                float radius = Mathf.Min(field.GlideSlope.ToleranceAt(d), GlideRingMaxRadius);
                DrawGlideRing(field, field.GlidePathPoint(d), radius, Alpha(pathColor, Mathf.Lerp(0.95f, 0.3f, shown / (float)GlideRingsShown)), scale);
            }

            // Aim bar across the runway at the touchdown point.
            if (guide.Distance > 0f)
            {
                Vector3 aim = field.GlidePathPoint(0f) + Vector3.up * 0.3f;
                Vector3 half = field.transform.right * (field.RunwaySize.x * 0.5f);
                if (ToScreen(aim - half, scale, out Vector2 a) && ToScreen(aim + half, scale, out Vector2 b))
                {
                    DottedLine(a, b, Green, 4f);
                    _g.Shadowed(new Rect(b.x + 8f, b.y - 10f, 60f, 20f), "AIM", _g.Hint, 2f, TextAnchor.MiddleLeft, Green, 0.85f);
                }
            }

            DrawApproachPanel(guide, width);
        }

        /// <summary>
        /// On an approach lane into a friendly field (and not yet on its glide path, where the landing assist takes
        /// over): a diamond on the lane's next gate, labeled with the lane and how far is left to the final fix.
        /// </summary>
        void DrawApproachLane(float scale)
        {
            if (!plane.IsAlive || plane.IsGrounded || _approach.OnApproach || viewCamera == null)
                return;
            var lane = ApproachLane.FriendlyLaneAt(plane.Position, plane.Team);
            if (lane == null || !ToScreen(lane.NextWaypoint(plane.Position), scale, out Vector2 p))
                return;
            Diamond(p, 10f, LaneColor);
            _g.Shadowed(new Rect(p.x - 120f, p.y + 14f, 240f, 20f),
                $"{lane.DisplayName.ToUpperInvariant()}  {HudText.Distance(lane.RemainingDistance(plane.Position))}",
                _g.Hint, 2f, TextAnchor.MiddleCenter, LaneColor, 0.85f);
        }

        /// <summary>A glide-path ring standing across the runway direction at <paramref name="center"/>.</summary>
        void DrawGlideRing(Airfield field, Vector3 center, float radius, Color color, float scale)
        {
            Vector3 right = field.transform.right, up = field.transform.up;
            const int dots = 28;
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                if (ToScreen(center + (right * Mathf.Cos(a) + up * Mathf.Sin(a)) * radius, scale, out Vector2 p))
                    _g.Fill(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), color);
            }
        }

        /// <summary>Pulsing yellow outline of the repair zone on the ground, labeled REPAIR.</summary>
        void DrawRepairZoneOutline(RepairZone zone, float scale)
        {
            if (zone == null)
                return;
            float hx = zone.Size.x * 0.5f, hz = zone.Size.y * 0.5f;
            Vector3[] local = { new(-hx, 0.3f, -hz), new(hx, 0.3f, -hz), new(hx, 0.3f, hz), new(-hx, 0.3f, hz) };
            var corners = new Vector2[4];
            for (int i = 0; i < 4; i++)
                if (!ToScreen(zone.transform.TransformPoint(local[i]), scale, out corners[i]))
                    return;
            var color = Alpha(Amber, 0.55f + 0.4f * Mathf.Sin(Time.time * 5f));
            for (int i = 0; i < 4; i++)
                DottedLine(corners[i], corners[(i + 1) % 4], color, 4f);
            Vector2 mid = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            _g.Shadowed(new Rect(mid.x - 80f, mid.y - 10f, 160f, 20f), "REPAIR", _g.Hint, 3f, TextAnchor.MiddleCenter, color, 0.9f);
        }

        /// <summary>
        /// ILS-style glass panel right of the crosshair. The dot is where the glide path is (dot low = we're high, dot
        /// left = we're right of the centerline); the inner square is the on-path tolerance. Under it: what to do, the
        /// distance to the aim point, and the speed against the touchdown limit.
        /// </summary>
        void DrawApproachPanel(ApproachGuide guide, float width)
        {
            const float half = 46f, inner = 12f, panelW = 236f;
            var panel = new Rect(width * 0.5f + 150f, _height * 0.5f - 90f, panelW, 196f);
            _g.Panel(panel, GlowRest, false);
            var center = new Vector2(panel.center.x, panel.y + 16f + half);
            var box = new Rect(center.x - half, center.y - half, half * 2f, half * 2f);
            _g.RoundFill(box, 6f, new Color(0f, 0f, 0f, 0.3f));
            var axis = Alpha(Color.white, 0.25f);
            _g.Fill(new Rect(box.x, center.y - 0.5f, box.width, 1f), axis);
            _g.Fill(new Rect(center.x - 0.5f, box.y, 1f, box.height), axis);
            _g.RoundStroke(new Rect(center.x - inner, center.y - inner, inner * 2f, inner * 2f), 2f, 1f, Alpha(Color.white, 0.5f));

            Color pathColor = guide.OnPath ? Green : Amber;
            float maxOffset = half - 6f;
            float dx = Mathf.Clamp(-guide.Lateral / guide.Tolerance * inner, -maxOffset, maxOffset);
            float dy = Mathf.Clamp(guide.HeightError / guide.Tolerance * inner, -maxOffset, maxOffset);
            _g.RoundFill(new Rect(center.x + dx - 5f, center.y + dy - 5f, 10f, 10f), 5f, pathColor);

            float maxSpeed = MaxTouchdownSpeed;
            bool slowEnough = plane.Speed <= maxSpeed;
            var row = new Rect(panel.x, box.yMax + 8f, panel.width, 20f);
            _g.Tracked(row, HudText.ApproachHint(guide), _g.Hint, 2f, TextAnchor.MiddleCenter, pathColor, 0.9f);
            row.y += 20f;
            _g.Tracked(row, guide.Distance > 0f ? $"AIM  {HudText.Distance(guide.Distance)}" : "OVER RUNWAY", _g.Hint, 2f, TextAnchor.MiddleCenter, White, 0.85f);
            row.y += 20f;
            _g.Tracked(row, $"{(slowEnough ? "SPEED OK" : "SLOW DOWN")}  MAX {maxSpeed * 3.6f:0}", _g.Hint, 2f, TextAnchor.MiddleCenter,
                slowEnough ? Green : Amber, 0.85f);
        }
    }
}

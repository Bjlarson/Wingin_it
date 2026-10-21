using UnityEngine;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    // Airfield Domination: a chip per capture field under the score bar (letter in the owner's colour, capture progress,
    // TOWER DOWN), the hold-every-field countdown, a letter over each field in view and on the minimap, a capture ring
    // while parked in a zone, and callouts for captures, towers and holding every field.
    public partial class FlightHUD
    {
        const float FieldChipW = 168f, FieldChipGap = 8f;
        const int CaptureRingDots = 48;

        DominationWorld _fieldWorld;
        bool[] _towerWasUp;
        int _holdingAllWas = FieldBoard.Neutral;

        /// <summary>The match's capture fields, or null outside Airfield Domination. Follows the world across matches.</summary>
        DominationWorld Fields
        {
            get
            {
                var world = match != null && !match.IsDismissed ? match.Fields : null;
                if (world != _fieldWorld)
                {
                    if (_fieldWorld != null)
                        _fieldWorld.Captured -= OnFieldCaptured;
                    _fieldWorld = world;
                    _towerWasUp = null;
                    _holdingAllWas = FieldBoard.Neutral;
                    if (_fieldWorld != null)
                        _fieldWorld.Captured += OnFieldCaptured;
                    _callouts.Clear();
                }
                return world != null && world.Board != null ? world : null;
            }
        }

        string FieldLabel(DominationWorld world, int i) => HudText.FieldName(world.Fields[i].DisplayName);

        Color OwnerColor(int owner) => owner == FieldBoard.Neutral ? Alpha(Body, 0.85f) : TeamColor(owner);

        void OnFieldCaptured(FieldCapture c)
        {
            if (plane == null || _fieldWorld == null)
                return;
            bool you = false;
            foreach (var p in c.Pilots)
                you |= ReferenceEquals(p, plane);
            AddCallout(HudText.FieldCallout(FieldLabel(_fieldWorld, c.Field), c.Team, plane.Team, you, TeamName(c.Team), out var tone), tone);
        }

        /// <summary>Tower and hold-every-field changes since last frame: the board has no events for those, so watch them.</summary>
        void WatchFields(DominationWorld world)
        {
            var board = world.Board;
            if (_towerWasUp == null || _towerWasUp.Length != board.FieldCount)
            {
                _towerWasUp = new bool[board.FieldCount];
                for (int i = 0; i < board.FieldCount; i++)
                    _towerWasUp[i] = board.Get(i).TowerUp;
            }
            for (int i = 0; i < board.FieldCount; i++)
            {
                var f = board.Get(i);
                if (f.TowerUp == _towerWasUp[i])
                    continue;
                _towerWasUp[i] = f.TowerUp;
                AddCallout(HudText.TowerCallout(FieldLabel(world, i), f.Owner, plane.Team, !f.TowerUp, out var tone), tone);
            }

            var mode = world.Mode;
            if (mode.HoldingAll != _holdingAllWas)
            {
                _holdingAllWas = mode.HoldingAll;
                if (mode.HoldingAll != FieldBoard.Neutral)
                    AddCallout(HudText.HoldAllCallout(mode.HoldingAll, plane.Team, TeamName(mode.HoldingAll),
                        mode.HoldAllToWin - mode.HoldingAllTime, out var tone), tone);
            }
        }

        void DrawFields(float scale, float width)
        {
            var world = Fields;
            if (world == null)
                return;
            WatchFields(world);
            DrawFieldBar(world, width);
            DrawFieldMarkers(world, scale, width);
            DrawCaptureRing(world, width);
        }

        /// <summary>
        /// A chip per capture field under the score bar: its letter in the owner's colour, its name, and its state
        /// (owner / capture % / CONTESTED / TOWER DOWN) with a bar in the capturing team's colour. Under them, the
        /// hold-every-field countdown while one team has them all.
        /// </summary>
        void DrawFieldBar(DominationWorld world, float width)
        {
            var board = world.Board;
            int n = board.FieldCount;
            float x = width / 2f - (n * FieldChipW + (n - 1) * FieldChipGap) / 2f;
            for (int i = 0; i < n; i++, x += FieldChipW + FieldChipGap)
            {
                var f = board.Get(i);
                var chip = new Rect(x, 88f, FieldChipW, 42f);
                _g.Panel(chip, GlowRest, false);
                Color owner = OwnerColor(f.Owner);
                bool towerDown = f.Owner != FieldBoard.Neutral && !f.TowerUp;
                _g.Shadowed(new Rect(chip.x + 6f, chip.y, 30f, chip.height), HudText.FieldLetter(i), _g.Title, 0f, TextAnchor.MiddleCenter, owner, 0.7f);

                var text = new Rect(chip.x + 40f, chip.y, chip.width - 48f, chip.height);
                _g.Tracked(new Rect(text.x, chip.y + 3f, text.width, 16f), FieldLabel(world, i), _g.Hint, 1.5f, TextAnchor.MiddleLeft, Alpha(White, 0.7f), 0.75f);
                string status = HudText.FieldStatus(f.Owner != FieldBoard.Neutral ? TeamName(f.Owner) : null, f.Contested,
                    f.CapturingTeam >= 0 ? TeamName(f.CapturingTeam) : null, f.Progress, towerDown);
                Color statusColor = f.Contested ? Amber
                    : f.CapturingTeam >= 0 && f.Progress > 0f ? TeamColor(f.CapturingTeam)
                    : towerDown ? Red : owner;
                if ((f.Contested || towerDown) && !Settings.GameSettings.Blink(Time.time, 1.2f, 0.7f))
                    statusColor = Alpha(statusColor, 0.55f);
                _g.Tracked(new Rect(text.x, chip.y + 19f, text.width, 20f), status, _g.Hint, 1.5f, TextAnchor.MiddleLeft, statusColor, 0.9f);

                if (f.CapturingTeam >= 0 && f.Progress > 0f)
                {
                    var bar = new Rect(chip.x + 8f, chip.yMax - 5f, chip.width - 16f, 3f);
                    _g.Fill(bar, Alpha(Color.white, 0.12f));
                    _g.Fill(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(f.Progress), bar.height), TeamColor(f.CapturingTeam));
                }
            }

            var mode = world.Mode;
            if (mode.HoldingAll != FieldBoard.Neutral)
            {
                bool ours = mode.HoldingAll == plane.Team;
                int left = Mathf.CeilToInt(Mathf.Max(0f, mode.HoldAllToWin - mode.HoldingAllTime));
                string line = ours ? $"WE HOLD EVERY FIELD  ·  WIN IN {left} S" : $"{TeamName(mode.HoldingAll)} HOLDS EVERY FIELD  ·  THEY WIN IN {left} S";
                Color c = ours ? Green : Red;
                if (!ours && !Settings.GameSettings.Blink(Time.time, 1f, 0.6f))
                    c = Alpha(c, 0.6f);
                _g.Tracked(new Rect(0f, 134f, width, 18f), line, _g.Hint, 2f, TextAnchor.MiddleCenter, c, 0.8f);
            }
        }

        /// <summary>
        /// A letter over each capture field in view, in the owner's colour, with its distance (and TOWER DOWN). In view
        /// only: the minimap and the RETURN TO BASE arrow already point the way. None on the ground or on approach.
        /// </summary>
        void DrawFieldMarkers(DominationWorld world, float scale, float width)
        {
            if (viewCamera == null || !plane.IsAlive || plane.IsGrounded || _approach.OnApproach)
                return;
            var board = world.Board;
            for (int i = 0; i < board.FieldCount; i++)
            {
                var field = world.Fields[i];
                if (field == null || !field.IsOpen)
                    continue;
                Vector3 zone = field.RepairZone != null ? field.RepairZone.transform.position : field.transform.position;
                Vector3 sp = viewCamera.WorldToScreenPoint(zone + Vector3.up * 30f);
                if (sp.z <= 0f)
                    continue;
                Vector2 at = new Vector2(sp.x, Screen.height - sp.y) / scale;
                if (at.x < 60f || at.x > width - 60f || at.y < 160f || at.y > _height - 230f)
                    continue;
                var f = board.Get(i);
                Color color = Alpha(OwnerColor(f.Owner), 0.9f);
                Circle(at, 15f, 2f, color);
                _g.Shadowed(new Rect(at.x - 15f, at.y - 15f, 30f, 30f), HudText.FieldLetter(i), _g.Item, 0f, TextAnchor.MiddleCenter, color, 1f);
                string label = HudText.Distance(Vector3.Distance(plane.Position, zone));
                if (f.Owner != FieldBoard.Neutral && !f.TowerUp)
                    label += "  ·  TOWER DOWN";
                _g.Shadowed(new Rect(at.x - 130f, at.y + 18f, 260f, 18f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.8f);
            }
        }

        /// <summary>The capture field whose zone the player is landed in, or -1.</summary>
        int PlayerCaptureField(DominationWorld world)
        {
            if (!plane.IsAlive || !plane.IsGrounded)
                return -1;
            for (int i = 0; i < world.Fields.Count; i++)
            {
                var zone = world.Fields[i] != null ? world.Fields[i].RepairZone : null;
                if (zone != null && world.Fields[i].IsOpen && zone.Contains(plane.Position))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Landed in a capture field's zone: a ring round the screen centre filling with the capture (the capturing
        /// team's colour), the field's letter inside, and what's happening under it. Tells you to stop if still rolling.
        /// </summary>
        void DrawCaptureRing(DominationWorld world, float width)
        {
            int i = PlayerCaptureField(world);
            if (i < 0)
                return;
            var f = world.Board.Get(i);
            Vector2 c = new(width * 0.5f, _height * 0.5f - 40f);
            const float radius = 62f;
            bool undoing = f.CapturingTeam >= 0 && f.CapturingTeam != plane.Team && f.Progress > 0f;
            Color fill = f.Contested ? Amber : f.CapturingTeam >= 0 ? TeamColor(f.CapturingTeam) : TeamColor(plane.Team);
            float progress = f.CapturingTeam >= 0 ? Mathf.Clamp01(f.Progress) : f.Owner == plane.Team ? 1f : 0f;
            int lit = Mathf.RoundToInt(progress * CaptureRingDots);
            for (int d = 0; d < CaptureRingDots; d++)
            {
                float a = d / (float)CaptureRingDots * Mathf.PI * 2f; // clockwise from the top
                Vector2 p = c + new Vector2(Mathf.Sin(a), -Mathf.Cos(a)) * radius;
                bool on = d < lit;
                float s = on ? 6f : 4f;
                _g.RoundFill(new Rect(p.x - s / 2f, p.y - s / 2f, s, s), s / 2f, on ? fill : Alpha(Color.white, 0.25f));
            }
            _g.Shadowed(new Rect(c.x - 40f, c.y - 30f, 80f, 60f), HudText.FieldLetter(i), _g.Title, 0f, TextAnchor.MiddleCenter, OwnerColor(f.Owner), 1.1f);

            bool parked = repair != null ? plane.Speed <= repair.Rules.stoppedSpeed && !plane.IsReversing : plane.Speed <= 0.5f;
            string hint = !parked ? "STOP IN THE ZONE TO CAPTURE"
                : HudText.CaptureHint(plane.Team, f.Owner, f.CapturingTeam, f.Progress, f.Contested, f.CapturingTeam >= 0 ? TeamName(f.CapturingTeam) : null);
            Color hintColor = f.Contested || !parked ? Amber : undoing ? TeamColor(f.CapturingTeam) : Alpha(White, 0.92f);
            _g.Shadowed(new Rect(0f, c.y + radius + 10f, width, 22f), hint, _g.Hint, 2f, TextAnchor.MiddleCenter, hintColor, 1f);
            if (parked && f.Owner != FieldBoard.Neutral && f.Owner != plane.Team && f.TowerUp && !f.Contested)
                _g.Shadowed(new Rect(0f, c.y + radius + 32f, width, 18f), "TOWER STANDING: 3× SLOWER  ·  BOMB IT FOR A FAST CAPTURE",
                    _g.Hint, 2f, TextAnchor.MiddleCenter, Alpha(Amber, 0.85f), 0.8f);
        }

        /// <summary>Capture fields on the minimap: the letter beside each in the owner's colour, a pulsing ring while it's being taken.</summary>
        void DrawFieldMapMarks(Rect map, float turn)
        {
            var world = Fields;
            if (world == null)
                return;
            for (int i = 0; i < world.Board.FieldCount; i++)
            {
                var field = world.Fields[i];
                if (field == null || !field.IsOpen)
                    continue;
                Vector2 p = HudMath.MapPoint(field.transform.position, _mapCenter, _mapRadius, map, turn);
                if (!map.Contains(p))
                    continue;
                var f = world.Board.Get(i);
                _g.Shadowed(new Rect(p.x - 10f, p.y - 24f, 20f, 16f), HudText.FieldLetter(i), _g.Hint, 0f, TextAnchor.MiddleCenter, OwnerColor(f.Owner), 0.95f);
                if (f.Contested || (f.CapturingTeam >= 0 && f.Progress > 0f))
                {
                    Color ring = f.Contested ? Amber : TeamColor(f.CapturingTeam);
                    float r = 8f + 4f * Mathf.Repeat(Time.time * 1.5f, 1f);
                    _g.RoundStroke(new Rect(p.x - r, p.y - r, r * 2f, r * 2f), r, 1.5f, Alpha(ring, 0.85f));
                }
            }
        }
    }
}

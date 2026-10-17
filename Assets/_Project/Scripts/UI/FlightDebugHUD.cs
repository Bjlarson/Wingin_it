using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Settings;
using WingingIt.World;

namespace WingingIt.UI
{
    /// <summary>
    /// Prototype overlay drawn with IMGUI, beside the real <see cref="FlightHUD"/>: the landing assist (glide-path rings,
    /// aim bar, repair zone outline, ILS box), approach lane gates, the cloud wash, center messages (death, boundary, AA
    /// fire, touchdown, repair), the F1 help and a few dev readouts. Still to be moved into the real HUD. Damage feedback
    /// (vignette, alarm, RETURN TO BASE) is <see cref="DamageFeedback"/>.
    /// </summary>
    public class FlightDebugHUD : MonoBehaviour
    {
        [SerializeField] PlaneController plane;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] PlaneRespawner respawner;
        [SerializeField] PlaneHealth health;
        [SerializeField] PlaneRepair repair;
        [SerializeField] Camera viewCamera;
        [SerializeField] bool showHelp = true;

        GUIStyle _label, _labelCentered, _title, _big, _small, _tiny, _tinyCentered;
        const float ReferenceHeight = 1080f;
        const float TouchdownMessageTime = 2.5f;
        const float RepairMessageTime = 2.5f;

        float _touchdownTime = float.NegativeInfinity;
        float _repairEndTime = float.NegativeInfinity;
        bool _repairCompleted;
        // This frame's landing approach to the nearest friendly runway (default when there is none).
        Airfield _approachField;
        ApproachGuide _approach;

        void OnEnable()
        {
            if (plane != null)
                plane.TouchedDown += OnTouchedDown;
            if (repair != null)
            {
                repair.Completed += OnRepairCompleted;
                repair.Cancelled += OnRepairCancelled;
            }
        }

        void OnDisable()
        {
            if (plane != null)
                plane.TouchedDown -= OnTouchedDown;
            if (repair != null)
            {
                repair.Completed -= OnRepairCompleted;
                repair.Cancelled -= OnRepairCancelled;
            }
        }

        void OnTouchedDown(PlaneController _, TouchdownInfo info) => _touchdownTime = Time.time;

        void OnRepairCompleted(PlaneRepair _) => (_repairEndTime, _repairCompleted) = (Time.time, true);
        void OnRepairCancelled(PlaneRepair _) => (_repairEndTime, _repairCompleted) = (Time.time, false);

        static string FaultText(TouchdownFault fault) => fault switch
        {
            TouchdownFault.GearNotLocked => "GEAR NOT DOWN",
            TouchdownFault.BadAngle => "BAD ANGLE",
            TouchdownFault.SinkTooFast => "DESCENDING TOO FAST",
            TouchdownFault.HardSink => "SINKING TOO FAST",
            TouchdownFault.TooFast => "TOO FAST",
            TouchdownFault.Ditched => "DITCHED IN THE SEA",
            _ => "",
        };

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame && GameSettings.DevToolsOn)
                showHelp = !showHelp;
        }

        void OnGUI()
        {
            if (plane == null)
                return;
            EnsureStyles();

            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;

            DrawCloudWhiteout(width);
            UpdateApproach();
            DrawLandingAssist(scale, width);
            DrawApproachLane(scale);
            bool dev = GameSettings.DevToolsOn;
            if (dev)
                DrawDevPanel();
            DrawWarnings(width);
            if (showHelp && dev)
                DrawHelp();
        }

        /// <summary>
        /// Bottom left, dev info the real HUD doesn't show: the control mode and held buttons, ring progress, the nearest
        /// landmark, the nearest friendly field (with the auto-brake stop distance) and who its AA is shooting at.
        /// </summary>
        void DrawDevPanel()
        {
            var lines = new List<string>();
            string mode = player == null ? "AI"
                : player.TouchControlsActive ? "Touch"
                : player.UsingGamepad ? "Gamepad"
                : player.Mode == ControlMode.MouseAim ? "Mouse aim" : "Direct";
            string weapons = player == null ? "" :
                $"{(player.FireGunsHeld ? " [GUNS]" : "")}{(player.FireMissileHeld ? " [MSL]" : "")}" +
                $"{(player.FireSecondaryHeld ? " [SEC]" : "")}{(player.DropBombHeld ? " [BOMB]" : "")}" +
                $"{(player.AbilityHeld ? " [ABILITY]" : "")}";
            lines.Add($"Controls: {mode}{weapons}{(showHelp ? "" : "    F1  help")}");
            if (RingGate.TotalCount > 0)
                lines.Add($"Rings {RingGate.PassedCount}/{RingGate.TotalCount}");
            var landmark = Landmark.Nearest(plane.transform.position);
            if (landmark != null)
            {
                Vector3 d = landmark.transform.position - plane.transform.position;
                lines.Add($"Near {landmark.DisplayName} ({landmark.Region}) {new Vector2(d.x, d.z).magnitude / 1000f:0.0} km");
            }
            var home = Airfield.NearestFriendly(plane.transform.position, plane.Team);
            if (home != null)
            {
                float km = Vector3.Distance(plane.transform.position, home.transform.position) / 1000f;
                bool inZone = home.RepairZone != null && home.RepairZone.Contains(plane.transform.position);
                string stop = plane.IsAutoBraking && plane.AutoBrakeStopDistance is float stopAt && plane.Speed >= 0.1f
                    ? $"  zone stop {Mathf.Max(0f, stopAt):0} m" : "";
                lines.Add($"{home.DisplayName} {km:0.0} km{(inZone ? "  [REPAIR ZONE]" : "")}{stop}");
                string engaging = HomeAAEngaging(home);
                if (engaging != null)
                    lines.Add($"Home AA engaging {engaging}");
            }

            const float lineHeight = 22f;
            float bottom = player != null && player.TouchControlsActive ? ReferenceHeight - 440f : ReferenceHeight - 24f; // above the touch stick
            float x = 30f, y = bottom - lines.Count * lineHeight;
            Box(new Rect(x - 10f, y - 6f, 400f, lines.Count * lineHeight + 12f), new Color(0f, 0f, 0f, 0.3f));
            foreach (var line in lines)
            {
                GUI.Label(new Rect(x, y - 2f, 390f, 24f), line, _tiny);
                y += lineHeight;
            }
        }

        static readonly Color LandingOk = new(0.4f, 1f, 0.45f);
        static readonly Color LandingWarn = new(1f, 0.85f, 0.2f);
        const float GlideRingSpacing = 150f;
        const int GlideRingsShown = 6;
        const float GlideRingMinAhead = 150f;
        const float GlideRingMaxRadius = 20f;

        /// <summary>The fastest touchdown that still lands cleanly for our class.</summary>
        float MaxTouchdownSpeed => plane.Stats != null ? plane.LandingRules.MaxTouchdownSpeed(plane.Stats.minSpeed) : float.MaxValue;

        void UpdateApproach()
        {
            _approachField = plane.IsAlive ? Airfield.NearestFriendly(plane.Position, plane.Team) : null;
            _approach = _approachField != null && !plane.IsGrounded
                ? _approachField.GetApproach(plane.Position, plane.Velocity) : default;
        }

        /// <summary>
        /// Landing assist on approach to a friendly runway: rings along the glide path to fly through (green while on
        /// the path), a bar across the runway at the aim point, the repair zone outlined on the ground, and an ILS-style
        /// box beside the crosshair. The zone outline stays up while rolling in after touchdown.
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
            Color pathColor = guide.OnPath ? LandingOk : LandingWarn;

            // Rings ahead of us along the glide path, nearest brightest. Not too close or too big: a ring right in
            // front of the camera would scatter its dots over the whole screen.
            int first = Mathf.FloorToInt((guide.Distance - GlideRingMinAhead) / GlideRingSpacing);
            for (int k = first, shown = 0; k >= 1 && shown < GlideRingsShown; k--, shown++)
            {
                float d = k * GlideRingSpacing;
                var color = pathColor;
                color.a = Mathf.Lerp(0.95f, 0.3f, shown / (float)GlideRingsShown);
                float radius = Mathf.Min(field.GlideSlope.ToleranceAt(d), GlideRingMaxRadius);
                DrawGlideRing(field, field.GlidePathPoint(d), radius, color, scale);
            }

            // Aim bar across the runway at the touchdown point.
            if (guide.Distance > 0f)
            {
                Vector3 aim = field.GlidePathPoint(0f) + Vector3.up * 0.3f;
                Vector3 half = field.transform.right * (field.RunwaySize.x * 0.5f);
                if (ToScreen(aim - half, scale, out Vector2 a) && ToScreen(aim + half, scale, out Vector2 b))
                {
                    DrawDottedLine(a, b, LandingOk, 4f);
                    var c = GUI.color;
                    GUI.color = LandingOk;
                    GUI.Label(new Rect(b.x + 6f, b.y - 11f, 60f, 22f), "AIM", _tiny);
                    GUI.color = c;
                }
            }

            DrawApproachBox(guide, width);
        }

        static readonly Color LaneColor = new(0.55f, 0.95f, 1f, 0.9f);

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
            const float r = 9f;
            DrawDottedLine(p + new Vector2(0f, -r), p + new Vector2(r, 0f), LaneColor, 3f);
            DrawDottedLine(p + new Vector2(r, 0f), p + new Vector2(0f, r), LaneColor, 3f);
            DrawDottedLine(p + new Vector2(0f, r), p + new Vector2(-r, 0f), LaneColor, 3f);
            DrawDottedLine(p + new Vector2(-r, 0f), p + new Vector2(0f, -r), LaneColor, 3f);
            var c = GUI.color;
            GUI.color = LaneColor;
            GUI.Label(new Rect(p.x - 110f, p.y + r + 2f, 220f, 22f),
                $"{lane.DisplayName.ToUpperInvariant()}  {lane.RemainingDistance(plane.Position) / 1000f:0.0} km", _tinyCentered);
            GUI.color = c;
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
                    Box(new Rect(p.x - 2f, p.y - 2f, 4f, 4f), color);
            }
        }

        /// <summary>Pulsing yellow outline of the repair zone on the ground.</summary>
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
            var color = new Color(1f, 0.85f, 0.15f, 0.55f + 0.4f * Mathf.Sin(Time.time * 5f));
            for (int i = 0; i < 4; i++)
                DrawDottedLine(corners[i], corners[(i + 1) % 4], color, 4f);
            Vector2 mid = (corners[0] + corners[1] + corners[2] + corners[3]) * 0.25f;
            var c = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(mid.x - 60f, mid.y - 11f, 120f, 22f), "REPAIR", _tinyCentered);
            GUI.color = c;
        }

        /// <summary>
        /// ILS-style box right of the crosshair. The dot is where the glide path is (dot low = we're high, dot left =
        /// we're right of the centerline); the inner square is the on-path tolerance. Under it: what to do, the distance
        /// to the aim point, and the speed against the touchdown limit.
        /// </summary>
        void DrawApproachBox(ApproachGuide guide, float width)
        {
            const float half = 50f, inner = 12f;
            var center = new Vector2(width * 0.5f + 230f, ReferenceHeight * 0.5f - 20f);
            var box = new Rect(center.x - half, center.y - half, half * 2f, half * 2f);
            Box(box, new Color(0f, 0f, 0f, 0.35f));
            var axis = new Color(1f, 1f, 1f, 0.3f);
            Box(new Rect(box.x, center.y - 0.5f, box.width, 1f), axis);
            Box(new Rect(center.x - 0.5f, box.y, 1f, box.height), axis);
            var tolerance = new Color(1f, 1f, 1f, 0.5f);
            Box(new Rect(center.x - inner, center.y - inner, inner * 2f, 1f), tolerance);
            Box(new Rect(center.x - inner, center.y + inner, inner * 2f, 1f), tolerance);
            Box(new Rect(center.x - inner, center.y - inner, 1f, inner * 2f), tolerance);
            Box(new Rect(center.x + inner, center.y - inner, 1f, inner * 2f + 1f), tolerance);

            Color pathColor = guide.OnPath ? LandingOk : LandingWarn;
            float maxOffset = half - 6f;
            float dx = Mathf.Clamp(-guide.Lateral / guide.Tolerance * inner, -maxOffset, maxOffset);
            float dy = Mathf.Clamp(guide.HeightError / guide.Tolerance * inner, -maxOffset, maxOffset);
            Box(new Rect(center.x + dx - 5f, center.y + dy - 5f, 10f, 10f), pathColor);

            string vertical = guide.IsHigh ? $"HIGH {guide.HeightError:0} m" : guide.IsLow ? $"LOW {-guide.HeightError:0} m" : null;
            string lateral = guide.IsOffCenter ? (guide.Lateral > 0f ? "FLY LEFT" : "FLY RIGHT") : null;
            string hint = vertical == null && lateral == null ? "ON GLIDE PATH"
                : vertical != null && lateral != null ? $"{vertical}  {lateral}" : vertical ?? lateral;

            float maxSpeed = MaxTouchdownSpeed;
            bool slowEnough = plane.Speed <= maxSpeed;
            var rect = new Rect(center.x - 130f, box.yMax + 4f, 260f, 22f);
            var c = GUI.color;
            GUI.color = pathColor;
            GUI.Label(rect, hint, _tinyCentered);
            rect.y += 20f;
            GUI.color = Color.white;
            GUI.Label(rect, guide.Distance > 0f ? $"AIM {guide.Distance:0} m" : "OVER RUNWAY", _tinyCentered);
            rect.y += 20f;
            GUI.color = slowEnough ? LandingOk : LandingWarn;
            GUI.Label(rect, $"{(slowEnough ? "SPEED OK" : "SLOW DOWN")}  (max {maxSpeed * 3.6f:0} km/h)", _tinyCentered);
            GUI.color = c;
        }

        /// <summary>The planes our home airfield's AA guns are shooting at, or null if none.</summary>
        static string HomeAAEngaging(Airfield home)
        {
            string names = null;
            foreach (var gun in AAGun.All)
            {
                if (gun.Airfield != home || gun.Target == null || !gun.IsFiring)
                    continue;
                string n = gun.Target.name;
                if (names == null)
                    names = n;
                else if (!names.Contains(n))
                    names += $", {n}";
            }
            return names;
        }

        bool IsUnderAAFire()
        {
            foreach (var gun in AAGun.All)
                if (gun.Target == plane && gun.IsFiring)
                    return true;
            return false;
        }

        void DrawWarnings(float width)
        {
            string warning = null;
            if (!plane.IsAlive)
            {
                string death = plane.LastDeathCause == DeathCause.ShotDown ? "SHOT DOWN" : "CRASHED";
                if (plane.LastDeathCause == DeathCause.Collision && plane.HasTouchedDown
                    && plane.LastTouchdown.Result == TouchdownResult.Crashed)
                    death += $": {FaultText(plane.LastTouchdown.Fault)}";
                var kill = health != null ? health.LastKill : default;
                string killer = kill.Killer != null ? kill.Killer.name : kill.Source is AAGun ? "AA" : null;
                if (killer != null)
                    death += plane.LastDeathCause == DeathCause.ShotDown ? $" BY {killer.ToUpperInvariant()}"
                        : $"  ({killer} gets the kill)";
                warning = respawner != null ? $"{death}  —  respawn in {respawner.RespawnTimeLeft:0.0}" : death;
            }
            else if (plane.IsOutOfBounds)
                warning = "LEAVING COMBAT AREA — TURNING BACK";
            else if (plane.IsNearBoundary)
                warning = "COMBAT AREA EDGE";
            else if (plane.IsNearCeiling)
                warning = "ALTITUDE LIMIT";
            else if (plane.LiftFactor < 0.95f)
                warning = "LOW SPEED";
            else if (IsUnderAAFire())
                warning = "AA FIRE — GET OUT FAST OR LOW";

            if (warning != null)
            {
                var c = GUI.color;
                GUI.color = Mathf.Repeat(Time.time, 0.8f) < 0.55f ? new Color(1f, 0.35f, 0.25f) : Color.white;
                GUI.Label(new Rect(0f, 140f, width, 50f), warning, _big);
                GUI.color = c;
            }

            // Touchdown result, briefly: green for a clean landing, yellow for a bounce (crashes show above).
            if (plane.IsAlive && plane.HasTouchedDown && Time.time - _touchdownTime < TouchdownMessageTime)
            {
                var td = plane.LastTouchdown;
                string text = td.Result == TouchdownResult.Landed ? $"LANDED   {td.SinkRate:0.0} m/s sink"
                    : $"BOUNCED: {FaultText(td.Fault)}   (-{plane.LandingRules.bounceDamage * 100f:0}% body)";
                var c = GUI.color;
                GUI.color = td.Result == TouchdownResult.Landed ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.85f, 0.2f);
                GUI.Label(new Rect(0f, 195f, width, 50f), text, _big);
                GUI.color = c;
            }

            // Runway spawn: parked at the threshold, never landed yet.
            if (plane.IsAlive && plane.IsGrounded && !plane.HasTouchedDown && plane.Speed < 0.1f)
            {
                var c = GUI.color;
                GUI.color = new Color(0.4f, 1f, 0.45f);
                GUI.Label(new Rect(0f, 195f, width, 50f), "READY FOR TAKEOFF", _big);
                GUI.color = c;
                GUI.Label(new Rect(0f, 245f, width, 28f), "Throttle up (W) or boost, then pull up at flying speed", _labelCentered);
            }

            DrawRepair(width);

            if (player != null && player.enabled && !player.TouchControlsActive && !player.UsingGamepad &&
                Cursor.lockState != CursorLockMode.Locked)
                GUI.Label(new Rect(0f, ReferenceHeight * 0.5f + 60f, width, 30f), "Click to capture the mouse  (Esc releases it)", _label);
        }

        /// <summary>The repair timer while parked in our zone, then a short REPAIRED / REPAIR CANCELLED flash.</summary>
        void DrawRepair(float width)
        {
            if (repair == null || !plane.IsAlive)
                return;
            var c = GUI.color;
            if (repair.IsRepairing)
            {
                GUI.color = new Color(0.45f, 0.9f, 1f);
                GUI.Label(new Rect(0f, 195f, width, 50f), $"REPAIRING ({repair.Tier.ToString().ToUpperInvariant()})   {repair.TimeLeft:0.0} s", _big);
                GUI.color = c;
                Bar(new Rect(width * 0.5f - 150f, 250f, 300f, 16f), repair.Progress, new Color(0.45f, 0.9f, 1f), "");
                if (health != null && health.IsInvulnerable)
                {
                    GUI.color = new Color(1f, 0.82f, 0.25f);
                    GUI.Label(new Rect(0f, 270f, width, 28f), "SHIELDED: INVULNERABLE UNTIL REPAIRED", _labelCentered);
                    GUI.color = c;
                }
            }
            else if (Time.time - _repairEndTime < RepairMessageTime)
            {
                GUI.color = _repairCompleted ? new Color(0.4f, 1f, 0.45f) : new Color(1f, 0.85f, 0.2f);
                GUI.Label(new Rect(0f, 195f, width, 50f), _repairCompleted ? "REPAIRED" : "REPAIR CANCELLED: LEFT THE ZONE", _big);
                GUI.color = c;
            }
        }

        /// <summary>
        /// Flying through cloud: the screen washes out white the deeper the camera is in, and a label says whether the
        /// plane is hidden (locks, missiles and AA can't see it from any side) or only in the edge of the cloud.
        /// </summary>
        void DrawCloudWhiteout(float width)
        {
            Vector3 eye = viewCamera != null ? viewCamera.transform.position : plane.transform.position;
            float immersion = CloudCover.Immersion(eye);
            if (immersion > 0f)
                Box(new Rect(0f, 0f, width, ReferenceHeight), new Color(0.95f, 0.96f, 0.98f, Mathf.Lerp(0.35f, 0.85f, Mathf.Clamp01(immersion * 2f))));

            if (!plane.IsAlive || !CloudCover.IsInside(plane.transform.position))
                return;
            bool hidden = CloudCover.IsHidden(plane.transform.position);
            var c = GUI.color;
            GUI.color = hidden ? new Color(0.2f, 0.35f, 0.6f) : new Color(0.35f, 0.4f, 0.5f);
            GUI.Label(new Rect(width * 0.5f - 300f, ReferenceHeight * 0.5f + 70f, 600f, 30f),
                hidden ? "IN CLOUD: HIDDEN FROM LOCKS AND AA" : "CLOUD EDGE: STILL VISIBLE", _tinyCentered);
            GUI.color = c;
        }

        void DrawHelp()
        {
            string help =
                "F1  hide help\n" +
                "Mouse  aim (mouse-aim) / pitch (direct)\n" +
                "W/S  throttle    A/D  roll    Q/E  yaw    Arrows  pitch/roll\n" +
                "Shift  boost    C  free look    M  switch mouse-aim / direct\n" +
                "1-4 or Tab  switch class    F2  touch controls\n" +
                "5-0  damage body / L wing / R wing / H stab / V stab / engine\n" +
                "Backspace  restore parts\n" +
                "Space / LMB  guns (put the + on the red pipper)\n" +
                "R / MMB  rockets, Heavy/Support (orange diamond = lead)\n" +
                "RMB  missile, Fighter/Interceptor (hold on the nose to lock)\n" +
                "K  nearest plane fires a missile at you (turn hard to dodge)\n" +
                "T  a target drone flies straight ahead of you for 20 s\n" +
                "L  final approach to your runway (gear down, idle)\n" +
                "    approach: fly through the rings, box dot = glide path\n" +
                $"N  spawn: {(LevelSettings.CurrentSpawnMode == SpawnMode.Air ? "AIR" : "RUNWAY")} (switch + respawn; W/Shift to take off)\n" +
                $"J  your home AA shoots at you: {(AAGun.DevHostilePlane == plane ? "ON" : "OFF")}\n" +
                $"Y  drones break when hit: {(DamageTester.DronesEvade ? "ON" : "OFF")}\n" +
                "B  bomb, Heavy (ground circle = where it lands)\n" +
                "F  ability: Fighter barrel roll (breaks locks + missiles),\n" +
                "    Interceptor afterburner, Heavy armor shield, Support repair aura\n" +
                "Green diamonds = enemies on your Support's radar (sees through clouds)\n" +
                "Clouds: fly deep inside to break locks, missiles and AA";
            var rect = new Rect(20f, 300f, 620f, 555f); // under the minimap
            Box(rect, new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f), help, _small);
        }

        bool ToScreen(Vector3 world, float scale, out Vector2 p)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            p = new Vector2(sp.x, Screen.height - sp.y) / scale;
            return sp.z > 0f;
        }

        /// <summary>IMGUI has no line primitive, so this draws dots.</summary>
        static void DrawDottedLine(Vector2 a, Vector2 b, Color color, float dotSize = 2f)
        {
            int dots = Mathf.Min(40, Mathf.FloorToInt(Vector2.Distance(a, b) / 8f));
            for (int i = 1; i < dots; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)dots);
                Box(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), color);
            }
        }

        void Bar(Rect r, float value, Color color, string label)
        {
            GUI.Label(new Rect(r.x, r.y - 5f, 50f, 24f), label, _small);
            var bar = new Rect(r.x + 50f, r.y, r.width, r.height);
            Box(bar, new Color(1f, 1f, 1f, 0.15f));
            Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(value), bar.height), color);
        }

        static void Box(Rect r, Color color)
        {
            var c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = c;
        }

        void EnsureStyles()
        {
            if (_label != null)
                return;
            _label = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold };
            _label.normal.textColor = Color.white;
            _labelCentered = new GUIStyle(_label) { alignment = TextAnchor.UpperCenter };
            _title = new GUIStyle(_label) { fontSize = 34 };
            _big = new GUIStyle(_title) { alignment = TextAnchor.UpperCenter };
            _small = new GUIStyle(_label) { fontSize = 18, fontStyle = FontStyle.Normal };
            _tiny = new GUIStyle(_small) { fontSize = 15 };
            _tinyCentered = new GUIStyle(_tiny) { alignment = TextAnchor.MiddleCenter };
        }
    }
}

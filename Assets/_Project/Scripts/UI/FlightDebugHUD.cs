using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.CameraSystem;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.World;

namespace WingingIt.UI
{
    /// <summary>
    /// Prototype HUD drawn with IMGUI: speed, throttle, boost, gun heat, rocket, missile and bomb ammo, the class ability,
    /// the landing gear light, part HP, warnings, reticles, lead pippers on other planes, radar contacts, missile lock and incoming-missile
    /// warnings, the bomb impact preview, the landing assist, and a kill feed.
    /// The real HUD (Phase 6) replaces this.
    /// </summary>
    public class FlightDebugHUD : MonoBehaviour
    {
        [SerializeField] PlaneController plane;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] FlightCameraRig cameraRig;
        [SerializeField] PlaneRespawner respawner;
        [SerializeField] PlaneHealth health;
        [SerializeField] PlaneGuns guns;
        [SerializeField] PlaneRockets rockets;
        [SerializeField] PlaneMissiles missiles;
        [SerializeField] PlaneBombs bombs;
        [SerializeField] PlaneAbility ability;
        [SerializeField] LandingGear gear;
        [SerializeField] PlaneRepair repair;
        [SerializeField] Camera viewCamera;
        [SerializeField] bool showHelp = true;

        GUIStyle _label, _labelCentered, _title, _big, _small, _tiny, _tinyNoWrap, _tinyCentered;
        const float ReferenceHeight = 1080f;
        // Top of the flight panel above the screen bottom: room for guns, up to two loadout weapons and the ability.
        const float FlightPanelTop = 334f;
        const float HitMarkerTime = 0.15f;

        const float KillFeedTime = 8f;
        const float TouchdownMessageTime = 2.5f;
        const float RepairMessageTime = 2.5f;
        const int KillFeedMax = 5;

        float _lastHitTime = -1f;
        bool _lastHitDestroyedPart;
        readonly List<(string text, bool involvesUs, float time)> _killFeed = new();
        float _touchdownTime = float.NegativeInfinity;
        float _repairEndTime = float.NegativeInfinity;
        bool _repairCompleted;
        // This frame's landing approach to the nearest friendly runway (default when there is none).
        Airfield _approachField;
        ApproachGuide _approach;

        void OnEnable()
        {
            if (guns != null)
                guns.HitPlane += OnHitPlane;
            if (rockets != null)
                rockets.HitPlane += OnRocketHit;
            if (missiles != null)
                missiles.HitPlane += OnMissileHit;
            if (bombs != null)
            {
                bombs.HitPlane += OnBombHit;
                bombs.HitGroundTarget += OnBombHitGround;
            }
            PlaneHealth.AnyKilled += OnAnyKilled;
            GroundTarget.AnyDestroyed += OnGroundTargetDestroyed;
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
            if (guns != null)
                guns.HitPlane -= OnHitPlane;
            if (rockets != null)
                rockets.HitPlane -= OnRocketHit;
            if (missiles != null)
                missiles.HitPlane -= OnMissileHit;
            if (bombs != null)
            {
                bombs.HitPlane -= OnBombHit;
                bombs.HitGroundTarget -= OnBombHitGround;
            }
            PlaneHealth.AnyKilled -= OnAnyKilled;
            GroundTarget.AnyDestroyed -= OnGroundTargetDestroyed;
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

        void OnAnyKilled(KillInfo kill)
        {
            string victim = kill.Victim.name;
            string killer = kill.Killer != null ? kill.Killer.name : kill.Source != null ? kill.Source.name : null;
            string text = killer == null ? $"{victim}  crashed"
                : kill.Cause == DeathCause.ShotDown ? $"{killer}  shot down  {victim}"
                : $"{victim}  crashed  (kill: {killer})";
            AddToKillFeed(text, kill.Victim.Plane == plane || kill.Killer == plane);
        }

        void OnGroundTargetDestroyed(GroundTarget target, Object source)
        {
            string by = source != null ? source.name : "?";
            AddToKillFeed($"{by}  destroyed  {target.DisplayName}", source is PlaneController p && p == plane);
        }

        void AddToKillFeed(string text, bool involvesUs)
        {
            _killFeed.Add((text, involvesUs, Time.time));
            if (_killFeed.Count > KillFeedMax)
                _killFeed.RemoveAt(0);
        }

        void OnHitPlane(PlaneGuns _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);
        void OnRocketHit(PlaneRockets _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);
        void OnMissileHit(PlaneMissiles _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);
        void OnBombHit(PlaneBombs _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);

        void OnBombHitGround(PlaneBombs _, GroundTarget target, float damage)
        {
            _lastHitTime = Time.time;
            _lastHitDestroyedPart = target.IsDestroyed;
        }

        void ShowHitMarker(PartDamageInfo info)
        {
            _lastHitTime = Time.time;
            _lastHitDestroyedPart = info.Part.IsDestroyed;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard != null && keyboard.f1Key.wasPressedThisFrame)
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
            DrawCriticalVignette(width);
            DrawReticles(scale);
            DrawTargets(scale, width);
            DrawMissileLock(scale);
            DrawBombImpact(scale);
            UpdateApproach();
            DrawLandingAssist(scale, width);
            DrawApproachLane(scale);
            DrawFlightPanel();
            DrawDamagePanel();
            DrawWarnings(width);
            DrawMissileWarnings(scale, width);
            DrawKillFeed();
            if (showHelp)
                DrawHelp(width);
        }

        void DrawFlightPanel()
        {
            float x = 30f, y = ReferenceHeight - FlightPanelTop;
            Box(new Rect(x - 12f, y - 12f, 380f, FlightPanelTop - 15f), new Color(0f, 0f, 0f, 0.35f));

            string cls = plane.Stats != null ? plane.Stats.displayName : "?";
            var title = new GUIContent(cls.ToUpperInvariant());
            GUI.Label(new Rect(x, y, 360f, 40f), title, _title);
            if (ability != null && ability.IsEquipped)
            {
                float after = _title.CalcSize(title).x + 8f;
                GUI.Label(new Rect(x + after, y + 13f, 360f - after, 24f), ability.Stats.displayName, _tinyNoWrap);
            }
            y += 44f;
            // Landing: speed turns green once slow enough for a clean touchdown, yellow while too fast.
            bool landing = plane.IsAlive && !plane.IsGrounded && (_approach.OnApproach || (gear != null && gear.State != GearState.Up));
            var c = GUI.color;
            if (landing)
                GUI.color = plane.Speed <= MaxTouchdownSpeed ? LandingOk : LandingWarn;
            GUI.Label(new Rect(x, y, 360f, 28f), $"SPD {plane.Speed * 3.6f:0} km/h    ALT {plane.transform.position.y:0} m", _label);
            GUI.color = c;
            y += 34f;
            Bar(new Rect(x, y, 260f, 16f), plane.Throttle, new Color(0.9f, 0.9f, 0.9f), "THR");
            if (gear != null)
                DrawGearLight(new Rect(x + 300f, y - 3f, 90f, 22f));
            y += 26f;
            Bar(new Rect(x, y, 260f, 16f), plane.BoostMeter,
                plane.IsBoosting ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.75f, 1f), "BST");
            // Under the gear light while it's out: vertical speed, the number the landing rules judge most.
            if (plane.IsGrounded) // auto-brake at idle: BRAKE while slowing, PARKED once stopped
                GUI.Label(new Rect(x + 300f, y - 5f, 70f, 24f),
                    !plane.IsAutoBraking ? "GROUND" : plane.Speed < 0.1f ? "PARKED" : "BRAKE", _small);
            else if (gear != null && gear.State != GearState.Up)
            {
                GUI.color = SinkColor(-plane.Velocity.y);
                GUI.Label(new Rect(x + 300f, y - 5f, 70f, 24f), $"V/S {plane.Velocity.y:+0.0;-0.0}", _small);
                GUI.color = c;
            }
            y += 26f;
            if (guns != null)
            {
                bool flash = (guns.IsOverheated || guns.IsJammed) && Mathf.Repeat(Time.time, 0.4f) < 0.2f;
                Color heat = guns.IsOverheated ? (flash ? Color.white : new Color(1f, 0.25f, 0.2f))
                    : Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.1f), guns.Heat);
                Bar(new Rect(x, y, 260f, 16f), guns.Heat, heat, "GUN");
                string gunState = guns.IsJammed ? "JAM" : guns.IsOverheated ? "HOT" : $"{guns.ActiveGunCount}/{guns.GunCount}";
                GUI.Label(new Rect(x + 318f, y - 5f, 60f, 24f), gunState, _small);
            }
            if (rockets != null && rockets.IsEquipped) // only the weapons this class carries
            {
                y += 26f;
                bool empty = rockets.Ammo == 0;
                Color rkt = empty ? new Color(0.5f, 0.5f, 0.5f) : new Color(1f, 0.6f, 0.2f);
                Bar(new Rect(x, y, 260f, 16f), (float)rockets.Ammo / Mathf.Max(1, rockets.MaxAmmo), rkt, "RKT");
                string rktState = empty ? "EMPTY" : rockets.ActiveRailCount == 0 ? "NO RAIL" : $"{rockets.Ammo}/{rockets.MaxAmmo}";
                GUI.Label(new Rect(x + 318f, y - 5f, 70f, 24f), rktState, _small);
            }
            if (missiles != null && missiles.IsEquipped) // only the weapons this class carries
            {
                y += 26f;
                bool empty = missiles.Ammo == 0;
                Color msl = empty ? new Color(0.5f, 0.5f, 0.5f) : missiles.IsLocked ? new Color(1f, 0.3f, 0.25f) : new Color(1f, 0.85f, 0.3f);
                Bar(new Rect(x, y, 260f, 16f), (float)missiles.Ammo / Mathf.Max(1, missiles.MaxAmmo), msl, "MSL");
                string mslState = empty ? "EMPTY" : missiles.IsLocked ? "LOCK" : $"{missiles.Ammo}/{missiles.MaxAmmo}";
                GUI.Label(new Rect(x + 318f, y - 5f, 70f, 24f), mslState, _small);
            }
            if (bombs != null && bombs.IsEquipped) // only the weapons this class carries
            {
                y += 26f;
                bool empty = bombs.Ammo == 0;
                Color bmb = empty ? new Color(0.5f, 0.5f, 0.5f) : new Color(0.8f, 0.85f, 0.95f);
                Bar(new Rect(x, y, 260f, 16f), (float)bombs.Ammo / Mathf.Max(1, bombs.MaxAmmo), bmb, "BMB");
                GUI.Label(new Rect(x + 318f, y - 5f, 70f, 24f), empty ? "EMPTY" : $"{bombs.Ammo}/{bombs.MaxAmmo}", _small);
            }
            if (ability != null && ability.IsEquipped)
            {
                y += 26f;
                // Active: a bright bar draining with the effect. Recharging: a grey bar filling up. Ready: full.
                Color abl = ability.IsActive ? new Color(0.45f, 1f, 0.55f)
                    : ability.IsReady ? new Color(0.35f, 0.85f, 0.75f) : new Color(0.5f, 0.5f, 0.5f);
                Bar(new Rect(x, y, 260f, 16f), ability.Charge, abl, "ABL");
                string ablState = ability.IsActive ? "ON" : ability.IsReady ? "READY" : $"{ability.CooldownLeft:0}s";
                GUI.Label(new Rect(x + 318f, y - 5f, 70f, 24f), ablState, _small);
            }
            y += 30f;

            string mode = player == null ? "AI"
                : player.TouchControlsActive ? "Touch"
                : player.UsingGamepad ? "Gamepad"
                : player.Mode == ControlMode.MouseAim ? "Mouse aim" : "Direct";
            string weapons = player == null ? "" :
                $"{(player.FireGunsHeld ? " [GUNS]" : "")}{(player.FireMissileHeld ? " [MSL]" : "")}" +
                $"{(player.FireSecondaryHeld ? " [SEC]" : "")}{(player.DropBombHeld ? " [BOMB]" : "")}" +
                $"{(player.AbilityHeld ? " [ABILITY]" : "")}";
            GUI.Label(new Rect(x, y, 360f, 24f), $"Controls: {mode}{weapons}", _small);
            y += 24f;
            if (RingGate.TotalCount > 0)
            {
                GUI.Label(new Rect(x, y, 360f, 24f), $"Rings {RingGate.PassedCount}/{RingGate.TotalCount}", _small);
                y += 24f;
            }
            var landmark = Landmark.Nearest(plane.transform.position);
            if (landmark != null)
            {
                Vector3 d = landmark.transform.position - plane.transform.position;
                float km = new Vector2(d.x, d.z).magnitude / 1000f;
                GUI.Label(new Rect(x, y, 360f, 24f), $"Near {landmark.DisplayName} ({landmark.Region}) {km:0.0} km", _small);
                y += 24f;
            }
            var home = Airfield.NearestFriendly(plane.transform.position, plane.Team);
            if (home != null)
            {
                float km = Vector3.Distance(plane.transform.position, home.transform.position) / 1000f;
                bool inZone = home.RepairZone != null && home.RepairZone.Contains(plane.transform.position);
                string stop = plane.IsAutoBraking && plane.AutoBrakeStopDistance is float d && plane.Speed >= 0.1f
                    ? $"  zone stop {Mathf.Max(0f, d):0} m" : "";
                GUI.Label(new Rect(x, y, 360f, 24f), $"{home.DisplayName} {km:0.0} km{(inZone ? "  [REPAIR ZONE]" : "")}{stop}", _small);
                y += 24f;
                string engaging = HomeAAEngaging(home);
                if (engaging != null)
                    GUI.Label(new Rect(x, y, 360f, 24f), $"Home AA engaging {engaging}", _small);
            }
        }

        static readonly Color LandingOk = new(0.4f, 1f, 0.45f);
        static readonly Color LandingWarn = new(1f, 0.85f, 0.2f);
        static readonly Color LandingBad = new(1f, 0.3f, 0.2f);
        const float GlideRingSpacing = 150f;
        const int GlideRingsShown = 6;
        const float GlideRingMinAhead = 150f;
        const float GlideRingMaxRadius = 20f;

        /// <summary>The fastest touchdown that still lands cleanly for our class.</summary>
        float MaxTouchdownSpeed => plane.Stats != null ? plane.LandingRules.MaxTouchdownSpeed(plane.Stats.minSpeed) : float.MaxValue;

        /// <summary>Green below the bounce limit, yellow for a bounce, red for a crash.</summary>
        Color SinkColor(float sinkRate) => sinkRate <= plane.LandingRules.bounceSinkRate ? LandingOk
            : sinkRate <= plane.LandingRules.crashSinkRate ? LandingWarn : LandingBad;

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

        /// <summary>Red = up, yellow = deploying (flashing) or retracting, green = locked and safe to land.</summary>
        void DrawGearLight(Rect r)
        {
            bool flash = gear.State == GearState.Deploying && Mathf.Repeat(Time.time, 0.3f) < 0.15f;
            Color light = gear.State switch
            {
                GearState.Locked => new Color(0.3f, 1f, 0.35f),
                GearState.Up => new Color(0.9f, 0.2f, 0.15f),
                _ => flash ? new Color(0.5f, 0.45f, 0.1f) : new Color(1f, 0.85f, 0.15f),
            };
            Box(new Rect(r.x, r.y + 3f, 16f, 16f), light);
            GUI.Label(new Rect(r.x + 22f, r.y - 2f, r.width - 22f, r.height + 4f), "GEAR", _small);
        }

        static readonly (PlanePartType type, string label)[] PartLabels =
        {
            (PlanePartType.Body, "BODY"),
            (PlanePartType.WingLeft, "L WING"),
            (PlanePartType.WingRight, "R WING"),
            (PlanePartType.HorizontalStabilizer, "H STAB"),
            (PlanePartType.VerticalStabilizer, "V STAB"),
            (PlanePartType.Engine, "ENGINE"),
        };

        void DrawDamagePanel()
        {
            if (health == null)
                return;
            const float rowHeight = 24f;
            int rows = PartLabels.Length + 1;
            float x = 30f, y = ReferenceHeight - FlightPanelTop - 24f - rows * rowHeight;
            Box(new Rect(x - 12f, y - 12f, 380f, rows * rowHeight + 12f), new Color(0f, 0f, 0f, 0.35f));

            var state = health.BodyState;
            Color stateColor = state switch
            {
                BodyDamageState.Healthy => new Color(0.4f, 0.9f, 0.45f),
                BodyDamageState.Damaged => new Color(1f, 0.8f, 0.2f),
                _ => new Color(1f, 0.3f, 0.2f),
            };
            var c = GUI.color;
            GUI.color = stateColor;
            string sputter = plane.IsSputtering ? "   (engine sputtering)" : "";
            string shield = health.DamageTakenMultiplier < 1f ? $"   SHIELD -{(1f - health.DamageTakenMultiplier) * 100f:0}%" : "";
            GUI.Label(new Rect(x, y - 3f, 360f, 24f), $"CONDITION  {state.ToString().ToUpperInvariant()}{sputter}{shield}", _small);
            GUI.color = c;
            y += rowHeight;

            foreach (var (type, label) in PartLabels)
            {
                var part = health.GetPart(type);
                if (part == null)
                    continue;
                float hp = part.NormalizedHealth;
                Color color = part.IsDestroyed ? new Color(0.5f, 0.5f, 0.5f)
                    : hp > 0.6f ? new Color(0.4f, 0.9f, 0.45f)
                    : hp > 0.25f ? new Color(1f, 0.8f, 0.2f)
                    : new Color(1f, 0.3f, 0.2f);
                GUI.Label(new Rect(x, y - 3f, 90f, 24f), label, _small);
                var bar = new Rect(x + 90f, y, 180f, 14f);
                Box(bar, new Color(1f, 1f, 1f, 0.15f));
                Box(new Rect(bar.x, bar.y, bar.width * hp, bar.height), color);
                string status = !part.IsDestroyed ? $"{part.Health:0}/{part.MaxHealth:0}"
                    : part.CanDetach ? "LOST" : type == PlanePartType.Engine ? "DEAD" : "0";
                GUI.Label(new Rect(bar.xMax + 8f, y - 3f, 90f, 24f), status, _small);
                y += rowHeight;
            }
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
            else if (health != null && health.BodyState == BodyDamageState.Critical)
                warning = "CRITICAL DAMAGE — RETURN TO BASE";

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

            if (player != null && !player.TouchControlsActive && !player.UsingGamepad &&
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

        /// <summary>Placeholder for the Phase 6 kill feed: the last few deaths, top-left, ours highlighted.</summary>
        void DrawKillFeed()
        {
            _killFeed.RemoveAll(k => Time.time - k.time > KillFeedTime);
            float y = 20f;
            foreach (var (text, involvesUs, time) in _killFeed)
            {
                float alpha = Mathf.Clamp01((KillFeedTime - (Time.time - time)) / 1f);
                var rect = new Rect(18f, y, 520f, 28f);
                Box(rect, new Color(0f, 0f, 0f, 0.35f * alpha));
                var c = GUI.color;
                GUI.color = involvesUs ? new Color(1f, 0.85f, 0.3f, alpha) : new Color(1f, 1f, 1f, alpha);
                GUI.Label(new Rect(rect.x + 10f, rect.y + 2f, rect.width - 20f, rect.height), text, _small);
                GUI.color = c;
                y += 32f;
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

        /// <summary>Placeholder for the Phase 6 damage vignette: pulsing red screen edges while Critical.</summary>
        void DrawCriticalVignette(float width)
        {
            if (health == null || !plane.IsAlive || health.BodyState != BodyDamageState.Critical)
                return;
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 6f);
            const int bands = 6;
            const float bandSize = 18f;
            for (int i = 0; i < bands; i++)
            {
                var color = new Color(0.9f, 0.05f, 0.03f, (0.1f + 0.12f * pulse) * (1f - i / (float)bands));
                float o = i * bandSize;
                Box(new Rect(o, o, width - 2f * o, bandSize), color);
                Box(new Rect(o, ReferenceHeight - o - bandSize, width - 2f * o, bandSize), color);
                Box(new Rect(o, o + bandSize, bandSize, ReferenceHeight - 2f * (o + bandSize)), color);
                Box(new Rect(width - o - bandSize, o + bandSize, bandSize, ReferenceHeight - 2f * (o + bandSize)), color);
            }
        }

        void DrawHelp(float width)
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
            var rect = new Rect(width - 640f, 20f, 620f, 555f);
            Box(rect, new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(new Rect(rect.x + 12f, rect.y + 8f, rect.width - 24f, rect.height - 16f), help, _small);
        }

        void DrawReticles(float scale)
        {
            if (viewCamera == null || cameraRig == null || !plane.IsAlive)
                return;

            // Where the nose is pointing (always) and where the mouse is aiming (mouse-aim only).
            DrawMarker(cameraRig.BoresightPoint, scale, 10f, new Color(1f, 1f, 1f, 0.9f), cross: true);
            if (player != null && player.MouseAimActive)
                DrawMarker(cameraRig.AimPoint, scale, 18f, new Color(0.4f, 1f, 0.5f, 0.9f), cross: false);
        }

        /// <summary>
        /// A red pipper showing where to aim for every plane in gun range (plus an orange diamond for rockets),
        /// a bracket on the plane itself, and a readout of the target nearest the nose. A hit marker flashes on the
        /// boresight when our bullets or rockets connect.
        /// </summary>
        void DrawTargets(float scale, float width)
        {
            if (viewCamera == null || guns == null || !plane.IsAlive)
                return;

            var pipperColor = new Color(1f, 0.3f, 0.25f, 0.95f);
            Vector3 nose = plane.transform.forward;
            float bestAngle = float.MaxValue;
            PlaneController target = null;

            foreach (var other in PlaneController.All)
            {
                if (other == plane || !other.IsAlive)
                    continue;
                // Radar contact (placeholder for the minimap): enemies a Support on our team can see, at any range.
                if (PlaneAbility.IsRevealedTo(other, plane.Team) && ToScreen(other.transform.position, scale, out Vector2 contact))
                {
                    var radarColor = new Color(0.4f, 1f, 0.5f, 0.9f);
                    DrawDiamond(contact, 11f, radarColor);
                    var c = GUI.color;
                    GUI.color = radarColor;
                    GUI.Label(new Rect(contact.x - 60f, contact.y + 12f, 120f, 22f),
                        $"{Vector3.Distance(plane.transform.position, other.transform.position):0} m", _tinyCentered);
                    GUI.color = c;
                }
                // A plane behind or deep inside a cloud can't be seen: no bracket, pipper or readout (radar still sees it).
                if (CloudCover.Blocks(plane.transform.position, other.transform.position))
                    continue;
                if (rockets != null && rockets.Ammo > 0 && rockets.TryGetLeadPoint(other, out Vector3 rocketLead, out _) &&
                    ToScreen(rocketLead, scale, out Vector2 rocketPip))
                    DrawDiamond(rocketPip, 7f, new Color(1f, 0.6f, 0.2f, 0.95f));
                if (!guns.TryGetLeadPoint(other, out Vector3 lead, out float distance))
                    continue;
                if (!ToScreen(lead, scale, out Vector2 pip) || !ToScreen(other.transform.position, scale, out Vector2 body))
                    continue;

                DrawMarker(other.transform.position, scale, 14f, new Color(1f, 1f, 1f, 0.5f), cross: false);
                DrawDottedLine(body, pip, new Color(1f, 0.3f, 0.25f, 0.35f));
                DrawRing(pip, 9f, pipperColor);
                GUI.Label(new Rect(body.x + 18f, body.y - 22f, 120f, 22f), $"{distance:0} m", _tiny);

                float angle = Vector3.Angle(nose, lead - plane.transform.position);
                if (angle < bestAngle)
                {
                    bestAngle = angle;
                    target = other;
                }
            }

            if (target != null)
                DrawTargetReadout(target, width);

            Vector3 boresight = cameraRig != null ? cameraRig.BoresightPoint : plane.transform.position + nose * 600f;
            if (Time.time - _lastHitTime < HitMarkerTime && ToScreen(boresight, scale, out Vector2 center))
            {
                var c = _lastHitDestroyedPart ? new Color(1f, 0.3f, 0.2f) : Color.white;
                const float o = 12f, s = 5f;
                foreach (var d in HitMarkerCorners)
                {
                    Box(new Rect(center.x + d.x * o - s * 0.5f, center.y + d.y * o - s * 0.5f, s, s), c);
                    Box(new Rect(center.x + d.x * (o + s) - s * 0.5f, center.y + d.y * (o + s) - s * 0.5f, s, s), c);
                }
            }
        }

        /// <summary>
        /// Missile lock: a bracket on the plane we're locking that closes in as the lock builds, then turns red
        /// with a LOCK tag once a missile can fire.
        /// </summary>
        void DrawMissileLock(float scale)
        {
            if (viewCamera == null || missiles == null || !plane.IsAlive || missiles.LockTarget == null)
                return;
            if (!ToScreen(missiles.LockTarget.transform.position, scale, out Vector2 p))
                return;
            bool locked = missiles.IsLocked;
            float size = Mathf.Lerp(46f, 20f, missiles.LockProgress);
            Color color = locked ? new Color(1f, 0.2f, 0.15f, 0.95f) : new Color(1f, 0.85f, 0.3f, 0.9f);
            const float t = 3f, arm = 12f;
            foreach (var d in HitMarkerCorners)
            {
                float cx = p.x + d.x * size, cy = p.y + d.y * size;
                Box(new Rect(d.x < 0f ? cx : cx - arm, cy - t * 0.5f, arm, t), color);
                Box(new Rect(cx - t * 0.5f, d.y < 0f ? cy : cy - arm, t, arm), color);
            }
            var c = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(p.x - 60f, p.y + size + 4f, 120f, 22f),
                locked ? "LOCK" : $"{missiles.LockProgress * 100f:0}%", _tinyCentered);
            GUI.color = c;
        }

        /// <summary>
        /// Placeholder for the Phase 6 missile warning: "being locked" (slow amber), "locked" (red), and an incoming
        /// missile (fast red flash, distance, an arrow around the screen center pointing at it, and an evade bar that
        /// fills while you turn hard enough to shake it). No sound yet.
        /// </summary>
        void DrawMissileWarnings(float scale, float width)
        {
            if (!plane.IsAlive)
                return;
            var threat = PlaneMissiles.GetThreat(plane);
            string text;
            Color color;
            if (threat.Incoming > 0)
            {
                string count = threat.Incoming > 1 ? $" x{threat.Incoming}" : "";
                text = $"MISSILE{count}  {threat.NearestDistance:0} m  —  BREAK HARD";
                color = Mathf.Repeat(Time.time, 0.25f) < 0.15f ? new Color(1f, 0.2f, 0.15f) : Color.white;
            }
            else if (threat.Locked)
            {
                text = "LOCKED ON";
                color = new Color(1f, 0.25f, 0.2f);
            }
            else if (threat.BeingLocked)
            {
                text = "LOCK WARNING";
                color = Mathf.Repeat(Time.time, 0.6f) < 0.35f ? new Color(1f, 0.8f, 0.25f) : new Color(1f, 0.8f, 0.25f, 0.35f);
            }
            else
                return;

            var c = GUI.color;
            GUI.color = color;
            GUI.Label(new Rect(0f, 196f, width, 50f), text, _big);
            GUI.color = c;

            if (threat.Incoming > 0 && threat.EvadeProgress > 0f)
            {
                // How close the hard turn is to shaking the missile off.
                var bar = new Rect(width * 0.5f - 120f, 250f, 240f, 12f);
                Box(bar, new Color(1f, 1f, 1f, 0.15f));
                Box(new Rect(bar.x, bar.y, bar.width * threat.EvadeProgress, bar.height), new Color(0.4f, 0.9f, 0.45f));
                GUI.Label(new Rect(bar.x, bar.yMax + 2f, bar.width, 22f), "EVADING", _tinyCentered);
            }
            if (threat.Incoming > 0 && viewCamera != null)
                DrawThreatArrow(threat.NearestPosition, scale, width, color);
        }

        /// <summary>An arrow on a ring around the screen center pointing toward a world point (even one behind us).</summary>
        void DrawThreatArrow(Vector3 world, float scale, float width, Color color)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            Vector2 center = new Vector2(width * 0.5f, ReferenceHeight * 0.5f);
            Vector2 dir = new Vector2(sp.x, Screen.height - sp.y) / scale - center;
            if (sp.z < 0f)
                dir = -dir; // behind the camera the projection is mirrored
            if (dir.sqrMagnitude < 1f)
                return;
            dir.Normalize();
            Vector2 side = new Vector2(-dir.y, dir.x);
            const float radius = 170f, length = 34f;
            for (float r = 0f; r <= length; r += 3f)
            {
                float half = (length - r) * 0.45f; // wide at the base, a point at the tip
                Vector2 a = center + dir * (radius + r);
                for (float o = -half; o <= half; o += 3f)
                {
                    Vector2 q = a + side * o;
                    Box(new Rect(q.x - 1.5f, q.y - 1.5f, 3f, 3f), color);
                }
            }
        }

        /// <summary>
        /// Bomb impact preview: the blast circle drawn flat on whatever the bomb would hit, in perspective, with a
        /// center cross and the seconds to impact. Turns red when a ground target sits inside the blast.
        /// </summary>
        void DrawBombImpact(float scale)
        {
            if (viewCamera == null || bombs == null || !plane.IsAlive || bombs.Ammo == 0)
                return;
            if (!bombs.TryPredictImpact(out var impact))
                return;

            float radius = bombs.Stats.splashRadius;
            bool onTarget = false;
            foreach (var target in GroundTarget.All)
                if (!target.IsDestroyed && target.DistanceTo(impact.Point) <= radius)
                {
                    onTarget = true;
                    break;
                }
            Color color = onTarget ? new Color(1f, 0.3f, 0.2f, 0.95f) : new Color(1f, 1f, 1f, 0.85f);

            // A circle of dots lying on the surface it would hit.
            Vector3 n = impact.Normal.sqrMagnitude > 0.01f ? impact.Normal : Vector3.up;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.forward).normalized;
            Vector3 v = Vector3.Cross(n, u);
            Vector3 lift = n * 0.3f; // just above the surface
            const int dots = 36;
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                Vector3 p = impact.Point + lift + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius;
                if (ToScreen(p, scale, out Vector2 sp))
                    Box(new Rect(sp.x - 2f, sp.y - 2f, 4f, 4f), color);
            }
            if (ToScreen(impact.Point + lift, scale, out Vector2 center))
            {
                Box(new Rect(center.x - 8f, center.y - 1f, 16f, 2f), color);
                Box(new Rect(center.x - 1f, center.y - 8f, 2f, 16f), color);
                var c = GUI.color;
                GUI.color = color;
                GUI.Label(new Rect(center.x - 60f, center.y + 10f, 120f, 22f), $"{impact.Time:0.0} s", _tinyCentered);
                GUI.color = c;
            }
        }

        static readonly Vector2[] HitMarkerCorners =
            { new Vector2(-1f, -1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f) };

        void DrawTargetReadout(PlaneController target, float width)
        {
            var targetHealth = target.GetComponent<PlaneHealth>();
            string cls = target.Stats != null ? target.Stats.displayName : target.name;
            string text = $"TARGET  {cls.ToUpperInvariant()}";
            if (targetHealth != null)
            {
                foreach (var (type, label) in PartLabels)
                {
                    var part = targetHealth.GetPart(type);
                    if (part == null)
                        continue;
                    string hp = part.IsDestroyed ? (part.CanDetach ? "LOST" : "0") : $"{part.NormalizedHealth * 100f:0}%";
                    text += $"   {label} {hp}";
                }
            }
            var rect = new Rect(width * 0.5f - 460f, 86f, 920f, 30f);
            Box(rect, new Color(0f, 0f, 0f, 0.35f));
            GUI.Label(rect, text, _tinyCentered);
        }

        bool ToScreen(Vector3 world, float scale, out Vector2 p)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            p = new Vector2(sp.x, Screen.height - sp.y) / scale;
            return sp.z > 0f;
        }

        static void DrawRing(Vector2 p, float size, Color color)
        {
            const float t = 3f;
            Box(new Rect(p.x - size, p.y - size, size * 2f, t), color);
            Box(new Rect(p.x - size, p.y + size - t, size * 2f, t), color);
            Box(new Rect(p.x - size, p.y - size, t, size * 2f), color);
            Box(new Rect(p.x + size - t, p.y - size, t, size * 2f), color);
            Box(new Rect(p.x - 1.5f, p.y - 1.5f, 3f, 3f), color);
        }

        /// <summary>A hollow diamond of small squares (the rocket pipper).</summary>
        static void DrawDiamond(Vector2 p, float size, Color color)
        {
            const int steps = 5;
            for (int i = 0; i < steps; i++)
            {
                float t = i / (float)steps;
                float a = size * t, b = size * (1f - t);
                foreach (var d in HitMarkerCorners)
                    Box(new Rect(p.x + d.x * a - 1.5f, p.y + d.y * b - 1.5f, 3f, 3f), color);
            }
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

        void DrawMarker(Vector3 world, float scale, float size, Color color, bool cross)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            if (sp.z <= 0f)
                return;
            Vector2 p = new Vector2(sp.x, Screen.height - sp.y) / scale;
            if (cross)
            {
                Box(new Rect(p.x - size, p.y - 1f, size * 2f, 2f), color);
                Box(new Rect(p.x - 1f, p.y - size, 2f, size * 2f), color);
            }
            else
            {
                float t = 2f;
                Box(new Rect(p.x - size, p.y - size, size * 2f, t), color);
                Box(new Rect(p.x - size, p.y + size - t, size * 2f, t), color);
                Box(new Rect(p.x - size, p.y - size, t, size * 2f), color);
                Box(new Rect(p.x + size - t, p.y - size, t, size * 2f), color);
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
            _tinyNoWrap = new GUIStyle(_tiny) { wordWrap = false };
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using WingingIt.CameraSystem;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Maps;
using WingingIt.Match;
using WingingIt.Settings;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    /// <summary>
    /// The player's flight HUD (Design/06-UI-UX/hud.md), drawn with IMGUI in the glass style: a bottom instrument
    /// cluster (damage diagram, speed / altitude / throttle / boost / gear, guns, ammo, ability), the crosshair, gun and
    /// rocket pippers, the missile lock bracket and incoming-missile warning, the bomb impact preview, a north-up minimap,
    /// an arrow to the nearest friendly repair zone, score popups and the kill feed; the landing assist and approach lanes
    /// (FlightHUD.Landing.cs); center messages, the status toast and the cloud wash (FlightHUD.Messages.cs). The match
    /// clock and team score are <see cref="MatchScreens"/>; the dev readouts and F1 help are <see cref="FlightDebugHUD"/>.
    /// With touch controls on, the cluster sits in the gap between the stick and the buttons.
    /// </summary>
    public partial class FlightHUD : MonoBehaviour
    {
        [SerializeField] PlaneController plane;
        [SerializeField] PlayerFlightInput player;
        [SerializeField] FlightCameraRig cameraRig;
        [SerializeField] PlaneHealth health;
        [SerializeField] PlaneGuns guns;
        [SerializeField] PlaneRockets rockets;
        [SerializeField] PlaneMissiles missiles;
        [SerializeField] PlaneBombs bombs;
        [SerializeField] PlaneAbility ability;
        [SerializeField] LandingGear gear;
        [SerializeField] PlaneRepair repair;
        [SerializeField] PlaneRespawner respawner;
        [SerializeField] Camera viewCamera;
        [Tooltip("The match, for score popups and team colours. Empty in the sandbox.")]
        [SerializeField] MatchManager match;

        const float ReferenceHeight = 1080f;
        float _height = ReferenceHeight; // the virtual screen height this frame (1080 at 100% HUD scale)
        const float HitMarkerTime = 0.15f;
        const float KillFeedTime = 8f;
        const int KillFeedMax = 5;
        const float PopupTime = 1.8f;
        const int PopupMax = 4;
        const float MapSize = 260f, MapPad = 10f;
        const int MapResolution = 512;

        static readonly Color Red = new(1f, 0.3f, 0.25f);
        static readonly Color Amber = new(1f, 0.82f, 0.3f);
        static readonly Color Green = new(0.4f, 1f, 0.45f);
        static readonly Color Shield = new(1f, 0.82f, 0.25f);
        static readonly Color[] FallbackTeamColors = { new(0.35f, 0.6f, 1f), new(1f, 0.38f, 0.32f) };

        readonly GlassGUI _g = new();
        Texture2D _arrow;
        RenderTexture _map;
        RenderTexture _mapView; // heading up: the photo turned to the plane's heading, redrawn every frame
        float _mapTurn;         // how far the map is turned (clockwise degrees; 0 = north up)
        Vector2 _mapCenter;
        float _mapRadius;
        bool _mapStale;         // the mode's pieces changed: photograph the level again
        Color _mapSea = DefaultMapSea;
        ModePieceBuilder _pieces;
        MapMinimap _mapSettings; // the map's minimap settings, null in a level with no map definition
        MatchScore _score;
        Rect _clusterRect; // where the cluster was drawn this frame, in HUD space

        float _lastHitTime = -1f;
        bool _lastHitDestroyedPart;

        struct FeedLine
        {
            public string Left, Verb, Right;
            public Color LeftColor, RightColor;
            public bool InvolvesUs;
            public float Time;
        }

        struct Popup
        {
            public string Text;
            public Color Color;
            public float Scale;
            public float Time;
        }

        readonly List<FeedLine> _feed = new();
        readonly List<Popup> _popups = new();

        /// <summary>What the kill feed shows now, newest last ("YOU DOWNED EAST FIGHTER 2"). For tests.</summary>
        public IEnumerable<string> KillFeedLines
        {
            get
            {
                foreach (var l in _feed)
                    yield return l.Verb == null ? l.Left : $"{l.Left} {l.Verb} {l.Right}";
            }
        }

        /// <summary>The level photographed from above for the minimap; null until the HUD first shows, or with no boundary.</summary>
        public Texture Minimap => _map;

        /// <summary>The popups showing now ("+50  KILL", "WING SHOT OFF!"), oldest first. For tests.</summary>
        public IEnumerable<string> PopupTexts
        {
            get
            {
                foreach (var p in _popups)
                    yield return p.Text;
            }
        }

        // Subscribed for the whole life of the HUD, not just while it shows: the match hides it while the class picker
        // is up (dead), and the kill feed should still have the kills from that time.
        void Awake()
        {
            useGUILayout = false; // no GUILayout here: skips IMGUI's layout pass
            _feedback = GetComponent<DamageFeedback>();
            if (plane != null)
                plane.TouchedDown += OnTouchedDown;
            if (repair != null)
            {
                repair.Completed += OnRepairCompleted;
                repair.Cancelled += OnRepairCancelled;
            }
            if (guns != null)
                guns.HitPlane += OnHitPlane;
            if (rockets != null)
            {
                rockets.HitPlane += OnRocketHit;
                rockets.HitGroundTarget += OnRocketHitGround;
            }
            if (missiles != null)
                missiles.HitPlane += OnMissileHit;
            _pieces = match != null ? match.Pieces : null;
            if (_pieces != null)
                _pieces.Changed += OnPiecesChanged;
            if (bombs != null)
            {
                bombs.HitPlane += OnBombHit;
                bombs.HitGroundTarget += OnBombHitGround;
            }
            PlaneHealth.AnyKilled += OnAnyKilled;
            PlaneHealth.AnyPartDestroyed += OnAnyPartDestroyed;
            GroundTarget.AnyDestroyed += OnGroundTargetDestroyed;
            PlaneController.AnyRammed += OnAnyRammed;
        }

        void Start()
        {
            FollowScore();
            _mapSettings = MapSettings();
            RenderMap();
        }

        void OnDestroy()
        {
            if (plane != null)
                plane.TouchedDown -= OnTouchedDown;
            if (repair != null)
            {
                repair.Completed -= OnRepairCompleted;
                repair.Cancelled -= OnRepairCancelled;
            }
            if (guns != null)
                guns.HitPlane -= OnHitPlane;
            if (rockets != null)
            {
                rockets.HitPlane -= OnRocketHit;
                rockets.HitGroundTarget -= OnRocketHitGround;
            }
            if (missiles != null)
                missiles.HitPlane -= OnMissileHit;
            if (_pieces != null)
                _pieces.Changed -= OnPiecesChanged;
            if (bombs != null)
            {
                bombs.HitPlane -= OnBombHit;
                bombs.HitGroundTarget -= OnBombHitGround;
            }
            PlaneHealth.AnyKilled -= OnAnyKilled;
            PlaneHealth.AnyPartDestroyed -= OnAnyPartDestroyed;
            GroundTarget.AnyDestroyed -= OnGroundTargetDestroyed;
            PlaneController.AnyRammed -= OnAnyRammed;
            if (_score != null)
                _score.Awarded -= OnAwarded;
            _g.Dispose();
            if (_arrow != null)
                Destroy(_arrow);
            if (_map != null)
            {
                _map.Release();
                Destroy(_map);
            }
            if (_mapView != null)
            {
                _mapView.Release();
                Destroy(_mapView);
            }
        }

        /// <summary>Listens to the match's score (picking a game mode makes a fresh one).</summary>
        void FollowScore()
        {
            var score = match != null ? match.Score : null;
            if (score == _score)
                return;
            if (_score != null)
                _score.Awarded -= OnAwarded;
            _score = score;
            if (_score != null)
                _score.Awarded += OnAwarded;
        }

        void LateUpdate()
        {
            FollowScore();
            RenderMap(); // again only if a mode's arena moved the boundary or its pieces changed
            _mapTurn = GameSettings.Current.Data.minimapHeadingUp && plane != null && plane.IsAlive ? -HudMath.Heading(plane.transform.forward) : 0f;
            if (_mapTurn != 0f && _map != null)
                TurnMap(_mapTurn);
        }

        // ---------- Events ----------

        void OnHitPlane(PlaneGuns _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);
        void OnRocketHit(PlaneRockets _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);
        void OnMissileHit(PlaneMissiles _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);
        void OnBombHit(PlaneBombs _, PlaneHealth target, PartDamageInfo info) => ShowHitMarker(info);

        void OnRocketHitGround(PlaneRockets _, GroundTarget target, float damage) => ShowGroundHitMarker(target);
        void OnBombHitGround(PlaneBombs _, GroundTarget target, float damage) => ShowGroundHitMarker(target);

        void ShowGroundHitMarker(GroundTarget target)
        {
            _lastHitTime = Time.time;
            _lastHitDestroyedPart = target.IsDestroyed;
        }

        void ShowHitMarker(PartDamageInfo info)
        {
            _lastHitTime = Time.time;
            _lastHitDestroyedPart = info.Part.IsDestroyed;
        }

        void OnAwarded(ScoreAward award)
        {
            if (plane == null || !ReferenceEquals(award.Pilot, plane))
                return;
            bool kill = award.Kind == ScoreKind.Kill;
            AddPopup(HudText.Award(award), kill ? Color.white : Alpha(White, 0.88f), kill ? 1.5f : 1.2f);
        }

        void OnAnyPartDestroyed(PlaneHealth victim, PartDamageInfo info)
        {
            if (plane == null || victim.Plane == plane || PlaneHealth.GetPlane(info.Source) != plane)
                return;
            string text = HudText.PartLost(info.Part.partType, rammed: plane.WeaponsOff); // weapons off: only a ram takes parts
            if (text != null)
                AddPopup(text, Amber, 1.2f);
        }

        void AddPopup(string text, Color color, float scale)
        {
            _popups.Add(new Popup { Text = text, Color = color, Scale = scale, Time = Time.time });
            if (_popups.Count > PopupMax)
                _popups.RemoveAt(0);
        }

        void OnAnyKilled(KillInfo kill)
        {
            if (kill.Victim == null)
                return;
            var victim = kill.Victim.Plane;
            var line = new FeedLine { Time = Time.time };
            string victimName = PilotName(victim);
            Color victimColor = TeamColor(victim.Team);
            if (kill.Killer != null)
                (line.Left, line.LeftColor) = (PilotName(kill.Killer), TeamColor(kill.Killer.Team));
            else if (kill.Source is AAGun aa)
                (line.Left, line.LeftColor) = ($"{TeamName(aa.Team)} AA", TeamColor(aa.Team));
            if (line.Left != null)
                (line.Verb, line.Right, line.RightColor) = (kill.Cause == DeathCause.Rammed ? "RAMMED" : "DOWNED", victimName, victimColor);
            else
                (line.Left, line.LeftColor) = ($"{victimName} CRASHED", victimColor);
            line.InvolvesUs = victim == plane || kill.Killer == plane;
            AddFeed(line);
        }

        void OnGroundTargetDestroyed(GroundTarget target, Object source)
        {
            var by = source as PlaneController;
            string name = by != null ? PilotName(by) : source != null ? source.name.ToUpperInvariant() : "?";
            AddFeed(new FeedLine
            {
                Left = name, LeftColor = by != null ? TeamColor(by.Team) : White,
                Verb = "DESTROYED", Right = target.DisplayName.ToUpperInvariant(), RightColor = White,
                InvolvesUs = by == plane, Time = Time.time,
            });
        }

        void AddFeed(FeedLine line)
        {
            _feed.Add(line);
            if (_feed.Count > KillFeedMax)
                _feed.RemoveAt(0);
        }

        string PilotName(PlaneController p) => p == plane ? "YOU" : match != null ? match.PilotName(p).ToUpperInvariant() : p.name.ToUpperInvariant();

        string TeamName(int team) => (match != null ? match.TeamName(team) : team == 0 ? "West" : "East").ToUpperInvariant();

        Color TeamColor(int team) => match != null ? match.TeamColor(team)
            : team >= 0 && team < FallbackTeamColors.Length ? FallbackTeamColors[team] : Body;

        // ---------- Drawing ----------

        void OnGUI()
        {
            if (plane == null)
                return;
            // Only draws: every touch or mouse event would otherwise redraw the whole thing once more this frame.
            if (Event.current.type != EventType.Repaint)
                return;
            _g.EnsureStyles();
            EnsureArrow();
            // HUD scale: a bigger scale is a smaller virtual screen, so everything anchored to its edges stays put.
            float scale = Screen.height / ReferenceHeight * GameSettings.Current.Data.hudScale;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            _height = Screen.height / scale;
            float width = Screen.width / scale;

            DrawCloudWash(width);
            UpdateApproach();
            DrawLandingAssist(scale, width);
            DrawApproachLane(scale);
            DrawTeamMarkers(scale);
            DrawTargets(scale, width);
            DrawMissileLock(scale);
            DrawBombImpact(scale);
            DrawCrosshair(scale);
            DrawHomeMarker(scale, width);
            DrawMissileWarning(scale, width);
            DrawRamRoyale(scale, width);
            var widgets = match != null && !match.IsDismissed && match.Mode != null ? match.Mode.Widgets : MatchWidgets.All;
            if ((widgets & MatchWidgets.ScorePopups) != 0)
                DrawPopups(width);
            DrawMinimap();
            DrawBanners(scale, width);
            DrawFields(scale, width);
            DrawCallouts(width);
            if ((widgets & MatchWidgets.KillFeed) != 0)
                DrawKillFeed(width);
            DrawCluster(width);
            DrawCenterWarning(width);
            DrawEjectHint(width);
            DrawStatusToast(width);
            DrawCloudStatus(width);
            DrawMouseCaptureHint(width);
        }

        bool Crippled => health != null && (health.BodyState != BodyDamageState.Healthy || health.LostPartsMask != 0);

        // ---------- Bottom cluster ----------

        static readonly Vector2 ClusterSize = new(804f, 176f);
        const float DiagramW = 176f, PanelW = 300f, Gap = 14f;

        void DrawCluster(float width)
        {
            bool touch = player != null && player.TouchControlsActive;
            var r = HudMath.ClusterRect(width, _height, ClusterSize, touch, 20f, out float s);
            _clusterRect = r;
            var old = GUI.matrix;
            GUI.matrix = old * Matrix4x4.TRS(new Vector3(r.x, r.y, 0f), Quaternion.identity, new Vector3(s, s, 1f));
            DrawDamageDiagram(new Rect(0f, 0f, DiagramW, ClusterSize.y));
            DrawFlightPanel(new Rect(DiagramW + Gap, 0f, PanelW, ClusterSize.y));
            DrawWeaponsPanel(new Rect(DiagramW + Gap * 2f + PanelW, 0f, PanelW, ClusterSize.y));
            GUI.matrix = old;
        }

        /// <summary>
        /// Top-down plane, nose up, each part coloured by its HP (green / yellow / red), lost parts as grey outlines.
        /// The body flashes while Critical; a gold ring while the repair shield is up.
        /// </summary>
        void DrawDamageDiagram(Rect p)
        {
            _g.Panel(p, GlowRest, false);
            if (health == null)
                return;
            float cx = p.center.x, top = p.y + 18f;
            bool flash = health.BodyState == BodyDamageState.Critical && GameSettings.Blink(Time.time, 0.5f, 0.5f);

            DrawPart(PlanePartType.HorizontalStabilizer, new Rect(cx - 34f, top + 104f, 68f, 14f), 4f, false);
            DrawPart(PlanePartType.WingLeft, new Rect(cx - 72f, top + 44f, 62f, 26f), 6f, false);
            DrawPart(PlanePartType.WingRight, new Rect(cx + 10f, top + 44f, 62f, 26f), 6f, false);
            DrawPart(PlanePartType.Body, new Rect(cx - 10f, top + 20f, 20f, 100f), 9f, flash);
            DrawPart(PlanePartType.Engine, new Rect(cx - 13f, top, 26f, 20f), 8f, false);
            // The fin, seen from above: a thin dark-edged blade along the tail.
            _g.RoundFill(new Rect(cx - 4f, top + 84f, 8f, 46f), 3f, new Color(0f, 0f, 0f, 0.6f));
            DrawPart(PlanePartType.VerticalStabilizer, new Rect(cx - 2.5f, top + 86f, 5f, 42f), 2f, false);

            if (health.IsInvulnerable || health.DamageTakenMultiplier < 1f)
                _g.RoundStroke(new Rect(cx - 80f, top - 8f, 160f, 146f), 40f, 2f, Alpha(Shield, 0.6f + 0.3f * Mathf.Sin(Time.time * 6f)));

            string state = health.IsInvulnerable ? "SHIELDED" : health.BodyState.ToString().ToUpperInvariant();
            Color stateColor = health.IsInvulnerable ? Shield : HudMath.BodyStateColor(health.BodyState);
            _g.Tracked(new Rect(p.x, p.yMax - 30f, p.width, 20f), state, _g.Hint, 2.4f, TextAnchor.MiddleCenter, stateColor, 0.9f);
        }

        void DrawPart(PlanePartType type, Rect r, float radius, bool flash)
        {
            var part = health.GetPart(type);
            if (part == null)
                return;
            Color c = HudMath.PartColor(part.NormalizedHealth, part.IsDestroyed);
            if (part.IsDestroyed && part.CanDetach)
            {
                _g.RoundStroke(r, radius, 1.5f, Alpha(c, 0.8f)); // gone: just where it was
                return;
            }
            _g.RoundFill(r, radius, flash ? Alpha(c, 0.35f) : Alpha(c, 0.9f));
        }

        void DrawFlightPanel(Rect p)
        {
            _g.Panel(p, GlowRest, false);
            float x = p.x + 20f, right = p.xMax - 20f;

            // Speed: green when slow enough to land and on approach (or gear out), yellow when too fast.
            Color speedColor = Color.white;
            if (plane.IsAlive && !plane.IsGrounded && IsLanding)
                speedColor = plane.Speed <= MaxTouchdownSpeed ? Green : Amber;
            var speedText = $"{plane.Speed * 3.6f:0}";
            float sw = _g.TrackedWidth(speedText, _g.Numeral, 1f, 1.6f);
            _g.Tracked(new Rect(x, p.y + 10f, 160f, 56f), speedText, _g.Numeral, 1f, TextAnchor.MiddleLeft, speedColor, 1.6f);
            _g.Tracked(new Rect(x + sw + 8f, p.y + 26f, 60f, 30f), "KM/H", _g.Hint, 2f, TextAnchor.MiddleLeft, Steel, 0.9f);
            _g.Tracked(new Rect(right - 120f, p.y + 12f, 120f, 22f), "ALT", _g.Hint, 2.4f, TextAnchor.MiddleRight, Steel, 0.9f);
            _g.Tracked(new Rect(right - 120f, p.y + 32f, 120f, 30f), $"{plane.Position.y:0} M", _g.Numeral, 1f, TextAnchor.MiddleRight, Color.white, 0.8f);

            float y = p.y + 82f;
            Meter(new Rect(x, y, 196f, 20f), "THR", plane.Throttle, Alpha(White, 0.9f));
            if (gear != null)
                DrawGearLight(new Rect(right - 64f, y, 64f, 20f));
            y += 28f;
            if (!plane.AbilitiesOff)
                Meter(new Rect(x, y, 196f, 20f), "BST", plane.BoostMeter, plane.IsBoosting ? new Color(1f, 0.55f, 0.1f) : new Color(0.3f, 0.75f, 1f));
            // Beside it: vertical speed while the gear is out, the ground state on the runway.
            string vs = null;
            Color vsColor = Steel;
            if (plane.IsGrounded)
                vs = !plane.IsAutoBraking ? "GROUND" : plane.Speed < 0.1f ? "PARKED" : "BRAKE";
            else if (gear != null && gear.State != GearState.Up)
            {
                float sink = -plane.Velocity.y;
                vs = $"V/S {plane.Velocity.y:+0;-0}";
                vsColor = sink <= plane.LandingRules.bounceSinkRate ? Green : sink <= plane.LandingRules.crashSinkRate ? Amber : Red;
            }
            if (vs != null)
                _g.Tracked(new Rect(right - 80f, y, 80f, 20f), vs, _g.Hint, 1.5f, TextAnchor.MiddleRight, vsColor, 0.85f);
            y += 34f;
            string cls = plane.Stats != null ? plane.Stats.displayName.ToUpperInvariant() : "";
            _g.Tracked(new Rect(x, y, p.width - 40f, 20f), cls, _g.Hint, 3f, TextAnchor.MiddleLeft, Steel, 0.9f);
            if (plane.LiftFactor < 0.95f && plane.IsAlive && !plane.IsGrounded)
                _g.Tracked(new Rect(x, y, p.width - 40f, 20f), "LOW SPEED", _g.Hint, 2f, TextAnchor.MiddleRight,
                    GameSettings.Blink(Time.time, 0.6f, 0.5833f) ? Red : Alpha(Red, 0.4f), 0.9f);
        }

        void DrawWeaponsPanel(Rect p)
        {
            _g.Panel(p, GlowRest, false);
            float x = p.x + 20f, y = p.y + 18f;
            const float row = 34f, meterW = 170f;
            if (guns != null && guns.IsEquipped)
            {
                bool flash = (guns.IsOverheated || guns.IsJammed) && GameSettings.Blink(Time.time, 0.4f, 0.5f);
                Color heat = guns.IsOverheated ? (flash ? Color.white : Red) : Color.Lerp(new Color(1f, 0.85f, 0.4f), new Color(1f, 0.35f, 0.1f), guns.Heat);
                Meter(new Rect(x, y, meterW, 20f), "GUN", guns.Heat, heat);
                string state = guns.IsJammed ? "JAM" : guns.IsOverheated ? "HOT" : $"{guns.ActiveGunCount}/{guns.GunCount}";
                Count(new Rect(p.xMax - 90f, y, 70f, 20f), state, guns.IsJammed || guns.IsOverheated ? Red : Color.white);
                y += row;
            }
            if (rockets != null && rockets.IsEquipped)
            {
                bool empty = rockets.Ammo == 0;
                Meter(new Rect(x, y, meterW, 20f), "RKT", (float)rockets.Ammo / Mathf.Max(1, rockets.MaxAmmo), empty ? Steel : new Color(1f, 0.6f, 0.2f));
                Count(new Rect(p.xMax - 90f, y, 70f, 20f), empty ? "EMPTY" : rockets.ActiveRailCount == 0 ? "NO RAIL" : $"{rockets.Ammo}", empty ? Steel : Color.white);
                y += row;
            }
            if (missiles != null && missiles.IsEquipped)
            {
                bool empty = missiles.Ammo == 0;
                Color c = empty ? Steel : missiles.IsLocked ? Red : Amber;
                Meter(new Rect(x, y, meterW, 20f), "MSL", (float)missiles.Ammo / Mathf.Max(1, missiles.MaxAmmo), c);
                Count(new Rect(p.xMax - 90f, y, 70f, 20f), empty ? "EMPTY" : missiles.IsLocked ? "LOCK" : $"{missiles.Ammo}", empty ? Steel : missiles.IsLocked ? Red : Color.white);
                y += row;
            }
            if (bombs != null && bombs.IsEquipped)
            {
                bool empty = bombs.Ammo == 0;
                Meter(new Rect(x, y, meterW, 20f), "BMB", (float)bombs.Ammo / Mathf.Max(1, bombs.MaxAmmo), empty ? Steel : new Color(0.8f, 0.85f, 0.95f));
                Count(new Rect(p.xMax - 90f, y, 70f, 20f), empty ? "EMPTY" : $"{bombs.Ammo}", empty ? Steel : Color.white);
                y += row;
            }
            if (ability != null && ability.IsEquipped)
            {
                // Active: a bright bar draining with the effect. Recharging: a grey bar filling up. Ready: full.
                Color c = ability.IsActive ? new Color(0.45f, 1f, 0.55f) : ability.IsReady ? new Color(0.35f, 0.85f, 0.75f) : Steel;
                Meter(new Rect(x, y, meterW, 20f), "ABL", ability.Charge, c);
                string state = ability.IsActive ? "ON" : ability.IsReady ? "READY" : $"{ability.CooldownLeft:0}s";
                Count(new Rect(p.xMax - 90f, y, 70f, 20f), state, ability.IsActive || ability.IsReady ? c : Color.white);
                _g.Tracked(new Rect(x + 50f, y + 16f, meterW, 16f), ability.Stats.displayName.ToUpperInvariant(), _g.Hint, 1.6f,
                    TextAnchor.MiddleLeft, Steel, 0.7f);
            }
        }

        /// <summary>A label and a thin bar: "THR ▬▬▬▬▬───".</summary>
        void Meter(Rect r, string label, float value, Color color)
        {
            _g.Tracked(new Rect(r.x, r.y, 44f, r.height), label, _g.Hint, 2f, TextAnchor.MiddleLeft, Steel, 0.9f);
            var bar = new Rect(r.x + 50f, r.y + r.height / 2f - 4f, r.width - 50f, 8f);
            _g.RoundFill(bar, 3f, Alpha(Color.white, 0.10f));
            float v = Mathf.Clamp01(value);
            if (v > 0.001f)
                _g.RoundFill(new Rect(bar.x, bar.y, Mathf.Max(6f, bar.width * v), bar.height), 3f, color);
        }

        void Count(Rect r, string text, Color color) => _g.Tracked(r, text, _g.Numeral, 0.5f, TextAnchor.MiddleRight, color, 0.68f);

        /// <summary>Red = up, yellow = deploying (flashing) or retracting, green = locked and safe to land.</summary>
        void DrawGearLight(Rect r)
        {
            bool flash = gear.State == GearState.Deploying && GameSettings.Blink(Time.time, 0.3f, 0.5f);
            Color light = gear.State switch
            {
                GearState.Locked => Green,
                GearState.Up => new Color(0.9f, 0.2f, 0.15f),
                _ => flash ? new Color(0.5f, 0.45f, 0.1f) : Amber,
            };
            _g.RoundFill(new Rect(r.x, r.y + 4f, 12f, 12f), 6f, light);
            _g.Tracked(new Rect(r.x + 18f, r.y, r.width - 18f, r.height), "GEAR", _g.Hint, 1.6f, TextAnchor.MiddleLeft, Steel, 0.85f);
        }

        bool IsLanding => (gear != null && gear.State != GearState.Up) || _approach.OnApproach;

        /// <summary>The fastest touchdown that still lands cleanly for our class.</summary>
        float MaxTouchdownSpeed => plane.Stats != null ? plane.LandingRules.MaxTouchdownSpeed(plane.Stats.minSpeed) : float.MaxValue;

        // ---------- Aiming ----------

        /// <summary>Boresight cross where the nose points (gapped, with a center dot) and the mouse-aim circle.</summary>
        void DrawCrosshair(float scale)
        {
            if (viewCamera == null || cameraRig == null || !plane.IsAlive)
                return;
            if (ToScreen(cameraRig.BoresightPoint, scale, out Vector2 c))
            {
                var col = new Color(1f, 1f, 1f, 0.92f);
                var shade = new Color(0f, 0f, 0f, 0.4f);
                const float gap = 5f, len = 10f, t = 2f;
                foreach (var (dx, dy) in new[] { (1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f) })
                {
                    var tick = dx != 0f
                        ? new Rect(c.x + (dx > 0f ? gap : -gap - len), c.y - t / 2f, len, t)
                        : new Rect(c.x - t / 2f, c.y + (dy > 0f ? gap : -gap - len), t, len);
                    _g.Fill(new Rect(tick.x + 1f, tick.y + 1f, tick.width, tick.height), shade);
                    _g.Fill(tick, col);
                }
                _g.Fill(new Rect(c.x - 1f, c.y - 1f, 2f, 2f), col);
            }
            if (player != null && player.MouseAimActive && ToScreen(cameraRig.AimPoint, scale, out Vector2 aim))
                Circle(aim, 16f, 2f, new Color(0.4f, 1f, 0.5f, 0.85f));
        }

        /// <summary>
        /// For every visible plane in gun range: a bracket on it, a red pipper where to aim, the distance; an orange
        /// diamond for rockets; green diamonds for radar contacts (any range). A readout of the target nearest the nose
        /// under the score bar, and a hit marker on the boresight when our shots connect.
        /// </summary>
        void DrawTargets(float scale, float width)
        {
            if (viewCamera == null || guns == null || !guns.IsEquipped || !plane.IsAlive)
                return;

            Vector3 nose = plane.transform.forward;
            float bestAngle = float.MaxValue;
            PlaneController target = null;
            foreach (var other in PlaneController.All)
            {
                if (other == plane || !other.IsAlive)
                    continue;
                if (PlaneAbility.IsRevealedTo(other, plane.Team) && ToScreen(other.transform.position, scale, out Vector2 contact))
                {
                    Triangle(contact, 11f, new Color(0.4f, 1f, 0.5f, 0.9f)); // radar only reveals enemies
                    _g.Shadowed(new Rect(contact.x - 60f, contact.y + 12f, 120f, 18f), HudText.Distance(Vector3.Distance(plane.Position, other.Position)),
                        _g.Hint, 1f, TextAnchor.MiddleCenter, new Color(0.4f, 1f, 0.5f, 0.9f), 0.8f);
                }
                // A plane behind or deep inside a cloud can't be seen: no bracket, pipper or readout (radar still sees it).
                if (CloudCover.Blocks(plane.transform.position, other.transform.position))
                    continue;
                if (rockets != null && rockets.Ammo > 0 && rockets.TryGetLeadPoint(other, out Vector3 rocketLead, out _) &&
                    ToScreen(rocketLead, scale, out Vector2 rocketPip))
                    Diamond(rocketPip, 7f, new Color(1f, 0.6f, 0.2f, 0.95f));
                if (!guns.TryGetLeadPoint(other, out Vector3 lead, out float distance))
                    continue;
                if (!ToScreen(lead, scale, out Vector2 pip) || !ToScreen(other.transform.position, scale, out Vector2 body))
                    continue;

                bool enemy = other.Team != plane.Team;
                Brackets(body, 14f, 6f, enemy ? new Color(1f, 1f, 1f, 0.6f) : Alpha(TeamColor(other.Team), 0.6f));
                if (enemy)
                {
                    DottedLine(body, pip, Alpha(Red, 0.35f));
                    Circle(pip, 9f, 2f, Alpha(Red, 0.95f));
                    _g.Fill(new Rect(pip.x - 1.5f, pip.y - 1.5f, 3f, 3f), Red);
                }
                _g.Shadowed(new Rect(body.x + 18f, body.y - 24f, 120f, 18f), HudText.Metres(distance), _g.Hint, 1f, TextAnchor.MiddleLeft, Alpha(White, 0.85f), 0.8f);

                float angle = Vector3.Angle(nose, lead - plane.transform.position);
                if (enemy && angle < bestAngle)
                    (bestAngle, target) = (angle, other);
            }
            if (target != null)
                DrawTargetReadout(target, width);

            Vector3 boresight = cameraRig != null ? cameraRig.BoresightPoint : plane.transform.position + nose * 600f;
            if (Time.time - _lastHitTime < HitMarkerTime && ToScreen(boresight, scale, out Vector2 center))
            {
                var c = _lastHitDestroyedPart ? Red : Color.white;
                foreach (var (dx, dy) in Corners)
                {
                    var a = center + new Vector2(dx, dy) * 9f;
                    var b = center + new Vector2(dx, dy) * 19f;
                    DottedLine(a - (b - a) * 0.2f, b, c, 3f);
                }
            }
        }

        static readonly (float, float)[] Corners = { (-1f, -1f), (1f, -1f), (-1f, 1f), (1f, 1f) };

        const float TeamMarkerRange = 2000f, TeamMarkerLift = 26f;
        static readonly Color FriendGrey = new(0.72f, 0.74f, 0.76f, 0.85f);

        /// <summary>
        /// Friend or foe at a glance, over every plane in sight within <see cref="TeamMarkerRange"/>: a red triangle
        /// (point down) over an enemy, a grey square over a teammate. In a free for all everyone is an enemy.
        /// </summary>
        void DrawTeamMarkers(float scale)
        {
            if (viewCamera == null || !plane.IsAlive)
                return;
            foreach (var other in PlaneController.All)
            {
                if (other == plane || !other.IsAlive)
                    continue;
                if ((other.Position - plane.Position).sqrMagnitude > TeamMarkerRange * TeamMarkerRange)
                    continue;
                if (CloudCover.Blocks(plane.transform.position, other.transform.position) || !ToScreen(other.Position, scale, out Vector2 p))
                    continue;
                var at = p + new Vector2(0f, -TeamMarkerLift);
                if (other.Team != plane.Team)
                    Triangle(at, 7f, Alpha(Red, 0.9f), solid: true);
                else
                    Square(at, 6f, FriendGrey);
            }
        }

        static readonly (PlanePartType type, string label)[] PartLabels =
        {
            (PlanePartType.Body, "BODY"),
            (PlanePartType.WingLeft, "L WING"),
            (PlanePartType.WingRight, "R WING"),
            (PlanePartType.HorizontalStabilizer, "TAIL"),
            (PlanePartType.VerticalStabilizer, "FIN"),
            (PlanePartType.Engine, "ENGINE"),
        };

        /// <summary>The target nearest the nose: its class and what's left of each part, under the score bar.</summary>
        void DrawTargetReadout(PlaneController target, float width)
        {
            var targetHealth = target.GetComponent<PlaneHealth>();
            string cls = target.Stats != null ? target.Stats.displayName.ToUpperInvariant() : PilotName(target);
            string text = cls;
            if (targetHealth != null)
                foreach (var (type, label) in PartLabels)
                {
                    var part = targetHealth.GetPart(type);
                    if (part == null)
                        continue;
                    string hp = part.IsDestroyed ? (part.CanDetach ? "LOST" : "DEAD") : $"{part.NormalizedHealth * 100f:0}%";
                    text += $"   {label} {hp}";
                }
            _g.Shadowed(new Rect(0f, 92f, width, 22f), text, _g.Hint, 1.8f, TextAnchor.MiddleCenter, Alpha(White, 0.88f), 0.95f);
        }

        /// <summary>A bracket closing in on the plane we're locking, red with LOCK once a missile can fire.</summary>
        void DrawMissileLock(float scale)
        {
            if (viewCamera == null || missiles == null || !plane.IsAlive || missiles.LockTarget == null)
                return;
            if (!ToScreen(missiles.LockTarget.transform.position, scale, out Vector2 p))
                return;
            bool locked = missiles.IsLocked;
            float size = Mathf.Lerp(46f, 20f, missiles.LockProgress);
            Color color = locked ? new Color(1f, 0.2f, 0.15f, 0.95f) : Alpha(Amber, 0.9f);
            Brackets(p, size, 12f, color, 3f);
            _g.Shadowed(new Rect(p.x - 60f, p.y + size + 4f, 120f, 20f), locked ? "LOCK" : $"{missiles.LockProgress * 100f:0}%",
                _g.Hint, 2f, TextAnchor.MiddleCenter, color);
        }

        /// <summary>
        /// Bomb impact preview: the blast circle drawn flat on whatever the bomb would hit, in perspective, with a
        /// center cross and the seconds to impact. Red when a ground target sits inside the blast.
        /// </summary>
        void DrawBombImpact(float scale)
        {
            if (viewCamera == null || bombs == null || !plane.IsAlive || bombs.Ammo == 0)
                return;
            if (!bombs.TryPredictImpact(out var impact))
                return;

            float radius = bombs.Stats.splashRadius;
            bool onTarget = false;
            foreach (var t in GroundTarget.All)
                if (!t.IsDestroyed && t.DistanceTo(impact.Point) <= radius)
                {
                    onTarget = true;
                    break;
                }
            Color color = onTarget ? Alpha(Red, 0.95f) : new Color(1f, 1f, 1f, 0.85f);

            Vector3 n = impact.Normal.sqrMagnitude > 0.01f ? impact.Normal : Vector3.up;
            Vector3 u = Vector3.Cross(n, Mathf.Abs(n.y) < 0.95f ? Vector3.up : Vector3.forward).normalized;
            Vector3 v = Vector3.Cross(n, u);
            Vector3 lift = n * 0.3f;
            const int dots = 36;
            for (int i = 0; i < dots; i++)
            {
                float a = i * Mathf.PI * 2f / dots;
                if (ToScreen(impact.Point + lift + (u * Mathf.Cos(a) + v * Mathf.Sin(a)) * radius, scale, out Vector2 sp))
                    _g.Fill(new Rect(sp.x - 2f, sp.y - 2f, 4f, 4f), color);
            }
            if (ToScreen(impact.Point + lift, scale, out Vector2 center))
            {
                _g.Fill(new Rect(center.x - 8f, center.y - 1f, 16f, 2f), color);
                _g.Fill(new Rect(center.x - 1f, center.y - 8f, 2f, 16f), color);
                _g.Shadowed(new Rect(center.x - 60f, center.y + 10f, 120f, 20f), $"{impact.Time:0.0} S", _g.Hint, 1.5f, TextAnchor.MiddleCenter, color);
            }
        }

        /// <summary>
        /// Lock warning: "LOCK WARNING" (slow amber) while someone builds a lock, "LOCKED ON" (red), and for an incoming
        /// missile a fast red flash with the distance, an arrow around the crosshair pointing at it (even behind), and
        /// an EVADING bar that fills while the turn is hard enough to shake it.
        /// </summary>
        void DrawMissileWarning(float scale, float width)
        {
            if (!plane.IsAlive)
                return;
            var threat = PlaneMissiles.GetThreat(plane);
            string text;
            Color color;
            if (threat.Incoming > 0)
            {
                string count = threat.Incoming > 1 ? $" X{threat.Incoming}" : "";
                text = $"MISSILE{count}  {threat.NearestDistance:0} M  —  BREAK HARD";
                color = GameSettings.Blink(Time.time, 0.25f, 0.6f) ? new Color(1f, 0.2f, 0.15f) : Color.white;
            }
            else if (threat.Locked)
            {
                text = "LOCKED ON";
                color = new Color(1f, 0.25f, 0.2f);
            }
            else if (threat.BeingLocked)
            {
                text = "LOCK WARNING";
                color = GameSettings.Blink(Time.time, 0.6f, 0.5833f) ? Amber : Alpha(Amber, 0.35f);
            }
            else
                return;

            _g.Shadowed(new Rect(0f, 196f, width, 48f), text, _g.Title, 4f, TextAnchor.MiddleCenter, color, 0.9f);
            if (threat.Incoming > 0 && threat.EvadeProgress > 0f)
            {
                var bar = new Rect(width * 0.5f - 120f, 250f, 240f, 8f);
                _g.RoundFill(bar, 3f, Alpha(Color.white, 0.15f));
                _g.RoundFill(new Rect(bar.x, bar.y, Mathf.Max(6f, bar.width * threat.EvadeProgress), bar.height), 3f, Green);
                _g.Shadowed(new Rect(bar.x, bar.yMax + 4f, bar.width, 18f), "EVADING", _g.Hint, 3f, TextAnchor.MiddleCenter, Green, 0.85f);
            }
            if (threat.Incoming > 0 && viewCamera != null)
            {
                Vector3 sp = viewCamera.WorldToScreenPoint(threat.NearestPosition);
                Vector2 center = new(width * 0.5f, _height * 0.5f);
                Vector2 dir = new Vector2(sp.x, Screen.height - sp.y) / scale - center;
                if (sp.z < 0f)
                    dir = -dir; // behind the camera the projection is mirrored
                if (dir.sqrMagnitude > 1f)
                    Arrow(center + dir.normalized * 180f, dir, 40f, color);
            }
        }

        // ---------- Home marker ----------

        /// <summary>
        /// The nearest friendly repair zone: a marker over it with its name and distance when it's in view, else an
        /// arrow on the screen edge pointing the way. Bright and pulsing once the plane is damaged or missing a part.
        /// Hidden on approach (the landing assist takes over) and on the ground.
        /// </summary>
        void DrawHomeMarker(float scale, float width)
        {
            if (viewCamera == null || !plane.IsAlive || plane.IsGrounded)
                return;
            var field = _approachField;
            if (field == null || _approach.OnApproach)
                return;
            Vector3 zone = field.RepairZone != null ? field.RepairZone.transform.position : field.transform.position;
            float distance = Vector3.Distance(plane.Position, zone);
            bool urgent = Crippled;
            Color color = urgent ? Alpha(Color.Lerp(TeamColor(plane.Team), Color.white, 0.25f), 0.75f + 0.25f * Mathf.Sin(Time.time * 6f))
                : Alpha(White, 0.55f);
            string label = urgent ? $"REPAIR  {HudText.Distance(distance)}" : $"{field.DisplayName.ToUpperInvariant()}  {HudText.Distance(distance)}";

            Vector3 sp = viewCamera.WorldToScreenPoint(zone);
            var inset = new Rect(80f, 130f, width - 160f, _height - 130f - 230f);
            bool off = HudMath.OffScreen(new Vector2(sp.x, Screen.height - sp.y) / scale, sp.z > 0f, inset, out Vector2 at, out Vector2 dir);
            if (!off)
            {
                Arrow(at + new Vector2(0f, -12f), Vector2.up, 18f, color); // a chevron pointing down at the zone
                _g.Shadowed(new Rect(at.x - 120f, at.y - 46f, 240f, 20f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.9f);
                return;
            }
            Arrow(at, dir, urgent ? 30f : 22f, color);
            Vector2 textAt = at - dir * 42f;
            _g.Shadowed(new Rect(textAt.x - 120f, textAt.y - 10f, 240f, 20f), label, _g.Hint, 2f, TextAnchor.MiddleCenter, color, 0.9f);
        }

        // ---------- Minimap ----------

        /// <summary>
        /// Photographs the level once from straight above, north up, out to the flight boundary (times the map's
        /// <see cref="MapMinimap.margin"/>): terrain, sea, city and airfields, without clouds, fog or planes. A map with a
        /// baked <see cref="MapMinimap.image"/> uses that instead while its own boundary is in force. Needs a
        /// <see cref="FlightBoundary"/>; without one there's no minimap. Taken again when a mode's arena changes the
        /// boundary (a smaller map for a smaller arena) or the mode's pieces change (other airfields).
        /// </summary>
        void RenderMap()
        {
            var boundary = FlightBoundary.Active;
            if (boundary == null)
                return;
            var settings = _mapSettings;
            var center = new Vector2(boundary.transform.position.x, boundary.transform.position.z);
            float radius = boundary.radius * (settings?.margin ?? DefaultMapMargin);
            if (_map != null && !_mapStale && center == _mapCenter && Mathf.Approximately(radius, _mapRadius))
                return;
            if (_map != null)
            {
                _map.Release();
                Destroy(_map);
            }
            _mapStale = false;
            _mapCenter = center;
            _mapRadius = radius;
            _mapSea = settings?.sea ?? DefaultMapSea;
            _map = new RenderTexture(MapResolution, MapResolution, 24) { name = "Minimap" };
            if (settings?.image != null && !boundary.IsOverridden)
            {
                Graphics.Blit(settings.image, _map);
                return;
            }

            var hidden = new List<Renderer>();
            foreach (var cloud in FindObjectsByType<CloudBank>(FindObjectsSortMode.None))
                Hide(cloud.GetComponentsInChildren<Renderer>(), hidden);
            foreach (var p in PlaneController.All)
                Hide(p.GetComponentsInChildren<Renderer>(), hidden);
            bool fog = RenderSettings.fog;
            RenderSettings.fog = false;

            var go = new GameObject("Minimap Capture");
            var cam = go.AddComponent<Camera>();
            cam.enabled = false;
            cam.orthographic = true;
            cam.orthographicSize = _mapRadius;
            cam.aspect = 1f;
            cam.nearClipPlane = 1f;
            cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = _mapSea;
            cam.transform.SetPositionAndRotation(new Vector3(_mapCenter.x, 3000f, _mapCenter.y), Quaternion.Euler(90f, 0f, 0f));
            cam.targetTexture = _map;
            cam.Render();
            cam.targetTexture = null;
            Destroy(go);

            RenderSettings.fog = fog;
            foreach (var r in hidden)
                if (r != null)
                    r.enabled = true;
        }

        static readonly Color DefaultMapSea = new(0.12f, 0.3f, 0.42f);
        const float DefaultMapMargin = 1.06f;

        /// <summary>The minimap settings of the map being played: the match's layout map, else the loaded map scene's; null in a test level.</summary>
        MapMinimap MapSettings()
        {
            var map = _pieces != null && _pieces.Map != null ? _pieces.Map : MapScene.CurrentMap;
            return map != null ? map.minimap : null;
        }

        void OnPiecesChanged()
        {
            _mapStale = true;
            _mapSettings = MapSettings();
        }

        /// <summary>Redraws the level photo into <see cref="_mapView"/> turned clockwise by <paramref name="degrees"/>, sea round the corners.</summary>
        void TurnMap(float degrees)
        {
            if (_mapView == null)
                _mapView = new RenderTexture(MapResolution, MapResolution, 0) { name = "Minimap View" };
            var old = RenderTexture.active;
            RenderTexture.active = _mapView;
            GL.Clear(false, true, _mapSea);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0f, MapResolution, MapResolution, 0f); // y down, like GUI space
            var pivot = new Vector3(MapResolution * 0.5f, MapResolution * 0.5f, 0f);
            GL.MultMatrix(Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-pivot));
            Graphics.DrawTexture(new Rect(0f, 0f, MapResolution, MapResolution), _map);
            GL.PopMatrix();
            RenderTexture.active = old;
        }

        static void Hide(Renderer[] renderers, List<Renderer> hidden)
        {
            foreach (var r in renderers)
                if (r.enabled)
                {
                    r.enabled = false;
                    hidden.Add(r);
                }
        }

        /// <summary>
        /// Top-left, north up (or turned to our heading, by setting): the level photo, the boundary, airfields (team
        /// colours; home fields outlined), friendly planes, enemies on our Support's radar (every plane in Ram Royale),
        /// and us (white, bigger).
        /// </summary>
        void DrawMinimap()
        {
            if (_map == null)
                return;
            var panel = new Rect(20f, 20f, MapSize, MapSize);
            _g.Panel(panel, GlowRest, false);
            var map = new Rect(panel.x + MapPad, panel.y + MapPad, MapSize - MapPad * 2f, MapSize - MapPad * 2f);
            float turn = _mapTurn;
            GUI.DrawTexture(map, turn != 0f && _mapView != null ? _mapView : _map, ScaleMode.StretchToFill, false, 0f, new Color(0.78f, 0.8f, 0.84f, 1f), Vector4.zero, new Vector4(8f, 8f, 8f, 8f));

            var boundary = FlightBoundary.Active;
            if (boundary != null)
            {
                float r = boundary.radius / (_mapRadius * 2f) * map.width;
                Vector2 c = HudMath.MapPoint(boundary.transform.position, _mapCenter, _mapRadius, map, turn);
                _g.RoundStroke(new Rect(c.x - r, c.y - r, r * 2f, r * 2f), r, 1f, Alpha(Color.white, 0.3f));
            }

            var home = plane.IsAlive ? Airfield.NearestFriendly(plane.Position, plane.Team) : null;
            foreach (var field in Airfield.All)
            {
                if (!field.IsOpen)
                    continue;
                Vector2 p = HudMath.MapPoint(field.transform.position, _mapCenter, _mapRadius, map, turn);
                Color c = field.IsNeutral ? Alpha(Body, 0.8f) : TeamColor(field.Team);
                float s = field.IsHome ? 11f : 9f;
                if (field.IsHome)
                    _g.Fill(new Rect(p.x - s / 2f - 1.5f, p.y - s / 2f - 1.5f, s + 3f, s + 3f), Alpha(Color.white, 0.9f));
                _g.Fill(new Rect(p.x - s / 2f, p.y - s / 2f, s, s), c);
                if (field == home && Crippled)
                {
                    float ring = 10f + 4f * Mathf.Repeat(Time.time * 1.5f, 1f);
                    _g.RoundStroke(new Rect(p.x - ring, p.y - ring, ring * 2f, ring * 2f), ring, 1.5f, Alpha(Color.white, 0.8f));
                }
            }

            DrawBannerMapMarks(map, turn);
            DrawFieldMapMarks(map, turn);
            bool ramRoyale = RamRoyale; // a small arena with nowhere to hide: everyone shows
            foreach (var other in PlaneController.All)
            {
                if (other == plane || !other.IsAlive)
                    continue;
                bool friend = other.Team == plane.Team;
                if (!friend && !ramRoyale && !PlaneAbility.IsRevealedTo(other, plane.Team))
                    continue;
                Vector2 p = HudMath.MapPoint(other.Position, _mapCenter, _mapRadius, map, turn);
                if (map.Contains(p))
                    MapArrow(p, HudMath.Heading(other.transform.forward) + turn, 11f, TeamColor(other.Team));
            }

            if (plane.IsAlive)
            {
                Vector2 me = HudMath.MapPoint(plane.Position, _mapCenter, _mapRadius, map, turn);
                float heading = HudMath.Heading(plane.transform.forward) + turn;
                MapArrow(me, heading, 19f, new Color(0f, 0f, 0f, 0.7f));
                MapArrow(me, heading, 15f, Color.white);
            }
            if (turn == 0f)
                _g.Tracked(new Rect(map.x + 6f, map.y + 4f, 20f, 16f), "N", _g.Hint, 0f, TextAnchor.MiddleLeft, Alpha(White, 0.7f), 0.8f);
            else
            {
                // Heading up: N rides the rim where north is.
                Vector2 n = map.center + HudMath.Rotate(new Vector2(0f, -(map.width * 0.5f - 12f)), turn);
                _g.Shadowed(new Rect(n.x - 10f, n.y - 8f, 20f, 16f), "N", _g.Hint, 0f, TextAnchor.MiddleCenter, Alpha(White, 0.85f), 0.8f);
            }
        }

        void MapArrow(Vector2 at, float headingDeg, float size, Color color) => Rotated(_arrow, new Rect(at.x - size / 2f, at.y - size / 2f, size, size), headingDeg, color);

        // ---------- Feeds ----------

        /// <summary>Top right, newest at the bottom: "KILLER  DOWNED  VICTIM" in team colours; ours lit with the rail.</summary>
        void DrawKillFeed(float width)
        {
            _feed.RemoveAll(l => Time.time - l.Time > KillFeedTime);
            // With touch controls on, below the touch class-switch button in the top right corner.
            float y = player != null && player.TouchControlsActive ? 280f : 20f;
            const float h = 34f, pad = 14f, tracking = 1.6f, size = 0.95f;
            foreach (var l in _feed)
            {
                float alpha = Mathf.Clamp01((KillFeedTime - (Time.time - l.Time)) / 1f);
                float wl = _g.TrackedWidth(l.Left, _g.Hint, tracking, size);
                float wv = l.Verb != null ? _g.TrackedWidth(l.Verb, _g.Hint, tracking, size * 0.8f) + 20f : 0f;
                float wr = l.Right != null ? _g.TrackedWidth(l.Right, _g.Hint, tracking, size) : 0f;
                float w = wl + wv + wr + pad * 2f;
                var row = new Rect(width - 20f - w, y, w, h);
                _g.RoundFill(row, 6f, new Color(0f, 0f, 0f, 0.45f * alpha));
                if (l.InvolvesUs)
                    _g.Lit(row, alpha, true);
                float x = row.x + pad;
                _g.Tracked(new Rect(x, y, wl, h), l.Left, _g.Hint, tracking, TextAnchor.MiddleLeft, Alpha(l.LeftColor, alpha), size);
                x += wl + 10f;
                if (l.Verb != null)
                {
                    _g.Tracked(new Rect(x, y, wv, h), l.Verb, _g.Hint, tracking, TextAnchor.MiddleLeft, Alpha(Steel, alpha), size * 0.8f);
                    x += wv - 10f;
                    _g.Tracked(new Rect(x, y, wr, h), l.Right, _g.Hint, tracking, TextAnchor.MiddleLeft, Alpha(l.RightColor, alpha), size);
                }
                y += h + 6f;
            }
        }

        /// <summary>Under the crosshair: "+50  KILL", "+20  ASSIST", "WING SHOT OFF!", each rising and fading out.</summary>
        void DrawPopups(float width)
        {
            _popups.RemoveAll(p => Time.time - p.Time > PopupTime);
            float baseY = _height * 0.5f + 120f;
            for (int i = _popups.Count - 1, slot = 0; i >= 0; i--, slot++)
            {
                var p = _popups[i];
                float age = Time.time - p.Time;
                float rise = Mathf.Min(age / 0.25f, 1f) * 14f + age * 10f;
                float alpha = Mathf.Clamp01(age / 0.12f) * Mathf.Clamp01((PopupTime - age) / 0.45f);
                var r = new Rect(0f, baseY - slot * 36f - rise, width, 34f);
                _g.Shadowed(r, p.Text, _g.Item, 3f, TextAnchor.MiddleCenter, Alpha(p.Color, alpha), p.Scale);
            }
        }

        // ---------- Primitives ----------

        bool ToScreen(Vector3 world, float scale, out Vector2 p)
        {
            Vector3 sp = viewCamera.WorldToScreenPoint(world);
            p = new Vector2(sp.x, Screen.height - sp.y) / scale;
            return sp.z > 0f;
        }

        void Circle(Vector2 c, float radius, float thickness, Color color) =>
            _g.RoundStroke(new Rect(c.x - radius, c.y - radius, radius * 2f, radius * 2f), radius, thickness, color);

        /// <summary>Four corner brackets around <paramref name="c"/>.</summary>
        void Brackets(Vector2 c, float size, float arm, Color color, float t = 2f)
        {
            foreach (var (dx, dy) in Corners)
            {
                float cx = c.x + dx * size, cy = c.y + dy * size;
                _g.Fill(new Rect(dx < 0f ? cx : cx - arm, cy - t * 0.5f, arm, t), color);
                _g.Fill(new Rect(cx - t * 0.5f, dy < 0f ? cy : cy - arm, t, arm), color);
            }
        }

        /// <summary>A hollow diamond: the rocket pipper and radar contacts.</summary>
        void Diamond(Vector2 p, float size, Color color)
        {
            var top = p + new Vector2(0f, -size);
            var right = p + new Vector2(size, 0f);
            var bottom = p + new Vector2(0f, size);
            var left = p + new Vector2(-size, 0f);
            DottedLine(top, right, color, 2.5f, 3f);
            DottedLine(right, bottom, color, 2.5f, 3f);
            DottedLine(bottom, left, color, 2.5f, 3f);
            DottedLine(left, top, color, 2.5f, 3f);
        }

        /// <summary>A triangle pointing down at <paramref name="p"/>'s plane: the enemy marker. Solid, or a dotted outline.</summary>
        void Triangle(Vector2 p, float size, Color color, bool solid = false)
        {
            var left = p + new Vector2(-size, -size * 0.6f);
            var right = p + new Vector2(size, -size * 0.6f);
            var tip = p + new Vector2(0f, size * 0.9f);
            if (solid)
            {
                // Rows of fill, narrowing to the tip.
                for (float y = left.y; y <= tip.y; y += 1f)
                {
                    float half = size * (tip.y - y) / (tip.y - left.y);
                    _g.Fill(new Rect(p.x - half, y, half * 2f, 1f), color);
                }
                return;
            }
            DottedLine(left, right, color, 2.5f, 3f);
            DottedLine(right, tip, color, 2.5f, 3f);
            DottedLine(tip, left, color, 2.5f, 3f);
        }

        /// <summary>A hollow square centred on <paramref name="p"/>: the teammate marker.</summary>
        void Square(Vector2 p, float half, Color color, float t = 2.5f)
        {
            _g.Fill(new Rect(p.x - half, p.y - half, half * 2f, t), color);
            _g.Fill(new Rect(p.x - half, p.y + half - t, half * 2f, t), color);
            _g.Fill(new Rect(p.x - half, p.y - half, t, half * 2f), color);
            _g.Fill(new Rect(p.x + half - t, p.y - half, t, half * 2f), color);
        }

        /// <summary>IMGUI has no line primitive, so this draws dots.</summary>
        void DottedLine(Vector2 a, Vector2 b, Color color, float dotSize = 2f, float spacing = 8f)
        {
            int dots = Mathf.Min(40, Mathf.Max(2, Mathf.FloorToInt(Vector2.Distance(a, b) / spacing)));
            for (int i = 0; i <= dots; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, i / (float)dots);
                _g.Fill(new Rect(p.x - dotSize * 0.5f, p.y - dotSize * 0.5f, dotSize, dotSize), color);
            }
        }

        /// <summary>A filled arrowhead centered on <paramref name="at"/>, pointing along <paramref name="dir"/> (GUI space).</summary>
        void Arrow(Vector2 at, Vector2 dir, float size, Color color)
        {
            float angle = Mathf.Atan2(dir.x, -dir.y) * Mathf.Rad2Deg; // 0 = up the screen, clockwise
            Rotated(_arrow, new Rect(at.x - size / 2f, at.y - size / 2f, size, size), angle, color);
        }

        /// <summary>Draws <paramref name="tex"/> rotated clockwise by <paramref name="degrees"/> about the rect's center.</summary>
        static void Rotated(Texture tex, Rect r, float degrees, Color color)
        {
            var old = GUI.matrix;
            var pivot = new Vector3(r.center.x, r.center.y, 0f);
            GUI.matrix = old * Matrix4x4.Translate(pivot) * Matrix4x4.Rotate(Quaternion.Euler(0f, 0f, degrees)) * Matrix4x4.Translate(-pivot);
            var c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex);
            GUI.color = c;
            GUI.matrix = old;
        }

        /// <summary>A soft-edged white triangle pointing up (tinted when drawn).</summary>
        void EnsureArrow()
        {
            if (_arrow != null)
                return;
            const int n = 64;
            _arrow = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            Vector2 tip = new(0.5f, 0.06f), backL = new(0.1f, 0.94f), backR = new(0.9f, 0.94f);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    var p = new Vector2((x + 0.5f) / n, 1f - (y + 0.5f) / n); // y down, as drawn
                    float d = Mathf.Max(EdgeDistance(p, tip, backR), Mathf.Max(EdgeDistance(p, backR, backL), EdgeDistance(p, backL, tip)));
                    px[y * n + x] = new Color(1f, 1f, 1f, Mathf.Clamp01(0.5f - d * n));
                }
            _arrow.SetPixels(px);
            _arrow.Apply(false, true);
        }

        /// <summary>Signed distance from <paramref name="p"/> to the edge a→b: negative inside a clockwise outline (y down).</summary>
        static float EdgeDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 e = b - a;
            Vector2 normal = new Vector2(e.y, -e.x).normalized;
            return Vector2.Dot(p - a, normal);
        }
    }
}

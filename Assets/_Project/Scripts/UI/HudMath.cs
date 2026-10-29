using UnityEngine;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.World;

namespace WingingIt.UI
{
    /// <summary>
    /// Pure layout and projection maths for <see cref="FlightHUD"/>, in GUI space (y down, 1080 px tall), so it is
    /// EditMode-testable.
    /// </summary>
    public static class HudMath
    {
        /// <summary>
        /// Room the touch controls take at the bottom of the screen (left: the stick, right: the weapon buttons), in
        /// reference pixels. Matches the layout FlightTestSceneBuilder gives the touch canvas.
        /// </summary>
        public const float TouchStickWidth = 420f, TouchButtonsWidth = 690f;

        /// <summary>
        /// Where the bottom instrument cluster goes: centered at the bottom, or with touch controls on, centered in the
        /// gap between the stick and the buttons and shrunk (never below half size) if the gap is too narrow.
        /// </summary>
        public static Rect ClusterRect(float screenWidth, float screenHeight, Vector2 size, bool touch, float margin, out float scale)
        {
            float left = touch ? TouchStickWidth : 0f;
            float right = screenWidth - (touch ? TouchButtonsWidth : 0f);
            float room = Mathf.Max(0f, right - left - margin * 2f);
            scale = Mathf.Clamp(room / size.x, 0.5f, 1f);
            float w = size.x * scale, h = size.y * scale;
            float x = (left + right - w) / 2f;
            return new Rect(x, screenHeight - margin - h, w, h);
        }

        /// <summary>
        /// For a world point projected to <paramref name="screenPoint"/> (GUI space; <paramref name="inFront"/> false if
        /// it's behind the camera): false if it shows inside <paramref name="inset"/>, so it can be marked where it is.
        /// Otherwise true, with <paramref name="edge"/> on the inset's border in its direction from the inset's center
        /// and <paramref name="direction"/> pointing that way. A point straight behind points down.
        /// </summary>
        public static bool OffScreen(Vector2 screenPoint, bool inFront, Rect inset, out Vector2 edge, out Vector2 direction)
        {
            Vector2 center = inset.center;
            Vector2 d = screenPoint - center;
            if (inFront && inset.Contains(screenPoint))
            {
                edge = screenPoint;
                direction = d.sqrMagnitude > 1e-6f ? d.normalized : Vector2.up;
                return false;
            }
            if (!inFront)
                d = -d; // behind the camera the projection is mirrored
            if (d.sqrMagnitude < 1e-6f)
                d = Vector2.up; // dead astern: point down (GUI y is down)
            direction = d.normalized;
            float tx = Mathf.Abs(direction.x) > 1e-6f ? inset.width * 0.5f / Mathf.Abs(direction.x) : float.MaxValue;
            float ty = Mathf.Abs(direction.y) > 1e-6f ? inset.height * 0.5f / Mathf.Abs(direction.y) : float.MaxValue;
            edge = center + direction * Mathf.Min(tx, ty);
            return true;
        }

        /// <summary>
        /// Where two planes come closest if both hold their course: <paramref name="relativePosition"/> and
        /// <paramref name="relativeVelocity"/> are the other plane's minus ours. False when they're already opening (the
        /// closest is now: <paramref name="time"/> 0, <paramref name="miss"/> the distance).
        /// </summary>
        public static bool ClosestApproach(Vector3 relativePosition, Vector3 relativeVelocity, out float time, out float miss)
        {
            float vv = relativeVelocity.sqrMagnitude;
            float t = vv > 1e-6f ? -Vector3.Dot(relativePosition, relativeVelocity) / vv : 0f;
            if (t <= 0f)
            {
                time = 0f;
                miss = relativePosition.magnitude;
                return false;
            }
            time = t;
            miss = (relativePosition + relativeVelocity * t).magnitude;
            return true;
        }

        /// <summary>How fast two planes close on each other (m/s, negative while they pull apart).</summary>
        public static float ClosingSpeed(Vector3 relativePosition, Vector3 relativeVelocity) =>
            relativePosition.sqrMagnitude > 1e-6f ? -Vector3.Dot(relativePosition.normalized, relativeVelocity) : 0f;

        /// <summary><see cref="MapPoint(Vector3, Vector2, float, Rect)"/> on a map turned clockwise by <paramref name="rotationDeg"/>
        /// about its center (heading up: minus the plane's heading).</summary>
        public static Vector2 MapPoint(Vector3 world, Vector2 mapCenter, float mapRadius, Rect map, float rotationDeg)
        {
            Vector2 north = MapPoint(world, mapCenter, mapRadius, map);
            return map.center + Rotate(north - map.center, rotationDeg);
        }

        /// <summary>Turns a GUI-space offset (y down) clockwise by <paramref name="degrees"/>.</summary>
        public static Vector2 Rotate(Vector2 v, float degrees)
        {
            float r = degrees * Mathf.Deg2Rad, c = Mathf.Cos(r), s = Mathf.Sin(r);
            return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
        }

        /// <summary>
        /// North-up minimap: world <paramref name="world"/> on a map of radius <paramref name="mapRadius"/> around
        /// <paramref name="mapCenter"/> (world XZ) drawn in <paramref name="map"/>. East is right, north is up.
        /// </summary>
        public static Vector2 MapPoint(Vector3 world, Vector2 mapCenter, float mapRadius, Rect map)
        {
            float u = (world.x - mapCenter.x) / (mapRadius * 2f) + 0.5f;
            float v = (world.z - mapCenter.y) / (mapRadius * 2f) + 0.5f;
            return new Vector2(map.x + u * map.width, map.yMax - v * map.height);
        }

        /// <summary>Compass heading of <paramref name="forward"/> in degrees clockwise from north (+Z), 0..360.</summary>
        public static float Heading(Vector3 forward) => Mathf.Repeat(Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg, 360f);

        static readonly Color Good = new(0.4f, 0.9f, 0.45f);
        static readonly Color Hurt = new(1f, 0.8f, 0.2f);
        static readonly Color Bad = new(1f, 0.3f, 0.2f);
        static readonly Color Gone = new(0.42f, 0.44f, 0.47f);

        /// <summary>The damage diagram's colour for a part: green above 60%, yellow above 25%, then red; grey when lost or dead.</summary>
        public static Color PartColor(float normalizedHealth, bool destroyed) =>
            destroyed ? Gone : normalizedHealth > 0.6f ? Good : normalizedHealth > 0.25f ? Hurt : Bad;

        public static Color BodyStateColor(BodyDamageState state) => state switch
        {
            BodyDamageState.Healthy => Good,
            BodyDamageState.Damaged => Hurt,
            _ => Bad,
        };
    }

    /// <summary>How loudly <see cref="DamageFeedback"/> tells the pilot they're hurt.</summary>
    public enum DamageAlert
    {
        None,
        /// <summary>Smoky screen edges and a warning tone.</summary>
        Warning,
        /// <summary>Red vignette, alarm and RETURN TO BASE.</summary>
        Critical,
    }

    /// <summary>Pure rules for <see cref="DamageFeedback"/>, so they're EditMode-testable.</summary>
    public static class DamageFeedbackMath
    {
        public const float HitFlashTime = 0.35f;

        /// <summary>
        /// Follows the repair tiers, so the feedback matches how long the fix will take: a Heavy repair (body Damaged, a
        /// stabilizer lost, engine dead) warns, a Critical one (body Critical or a wing lost) sounds the alarm.
        /// </summary>
        public static DamageAlert Alert(BodyDamageState body, int lostPartsMask) => RepairRules.GetTier(body, lostPartsMask) switch
        {
            RepairTier.Critical => DamageAlert.Critical,
            RepairTier.Heavy => DamageAlert.Warning,
            _ => DamageAlert.None,
        };

        /// <summary>
        /// The alarm and the RETURN TO BASE prompt: Critical, alive and in the air, but not on approach to a friendly
        /// runway (the landing assist has the pilot's attention there; they're already going home).
        /// </summary>
        public static bool ShouldReturnToBase(DamageAlert alert, bool alive, bool grounded, bool onApproach) =>
            alert == DamageAlert.Critical && alive && !grounded && !onApproach;

        /// <summary>
        /// Opacity of the screen-edge overlay: a slow drift of smoke when Warning, a heartbeat pulse (two beats then a
        /// rest, every 0.9 s) when Critical.
        /// </summary>
        public static float VignetteAlpha(DamageAlert alert, float time)
        {
            switch (alert)
            {
                case DamageAlert.Warning:
                    return 0.3f + 0.08f * Mathf.Sin(time * 1.3f);
                case DamageAlert.Critical:
                    float t = Mathf.Repeat(time, 0.9f);
                    float beat = Mathf.Max(Pulse(t, 0f), 0.6f * Pulse(t, 0.22f));
                    return 0.4f + 0.35f * beat;
                default:
                    return 0f;
            }
        }

        static float Pulse(float t, float at) => t < at ? 0f : Mathf.Exp(-(t - at) * 14f);

        /// <summary>How strong a red flash a hit gets: bigger hits flash harder, a part shot off flashes fully.</summary>
        public static float HitStrength(float amount, float bodyMaxHealth, bool partDestroyed) =>
            partDestroyed ? 1f : Mathf.Clamp(0.25f + amount / Mathf.Max(1f, bodyMaxHealth) * 2.5f, 0.25f, 0.75f);

        /// <summary>The hit flash fading out over <see cref="HitFlashTime"/> seconds.</summary>
        public static float HitFlashAlpha(float age, float strength) =>
            age < 0f || age >= HitFlashTime ? 0f : strength * (1f - age / HitFlashTime);
    }

    /// <summary>What the HUD's popups and kill feed say.</summary>
    public static class HudText
    {
        /// <summary>"+50  KILL", "+20  ASSIST", "+100  CONVOY DESTROYED".</summary>
        public static string Award(ScoreAward award) => $"+{award.Points}  {award.Label.ToUpperInvariant()}";

        /// <summary>
        /// We took a part off an enemy: "WING SHOT OFF!", or "WING KNOCKED OFF!" when it was a ram. Null for the body
        /// (that's the kill).
        /// </summary>
        public static string PartLost(PlanePartType type, bool rammed = false)
        {
            string how = rammed ? "KNOCKED OFF!" : "SHOT OFF!";
            return type switch
            {
                PlanePartType.WingLeft or PlanePartType.WingRight => $"WING {how}",
                PlanePartType.HorizontalStabilizer => $"TAIL {how}",
                PlanePartType.VerticalStabilizer => $"FIN {how}",
                PlanePartType.Engine => "ENGINE KNOCKED OUT!",
                _ => null,
            };
        }

        /// <summary>Tone of a banner callout: good news for the viewer's team, bad news, or neither.</summary>
        public enum Tone { Neutral, Good, Bad }

        /// <summary>
        /// What a banner event says to a pilot on <paramref name="viewerTeam"/>: "WEST HAS YOUR BANNER", "CAPTURED!".
        /// <paramref name="pilot"/> is the carrier or returner's name ("YOU", a callsign), or null.
        /// </summary>
        public static string BannerCallout(BannerEventKind kind, int bannerTeam, int pilotTeam, int viewerTeam, string pilot, string pilotTeamName,
            out Tone tone)
        {
            bool ours = bannerTeam == viewerTeam;
            tone = Tone.Neutral;
            switch (kind)
            {
                case BannerEventKind.Grabbed:
                    tone = ours ? Tone.Bad : Tone.Good;
                    return ours ? $"{pilotTeamName} HAS YOUR BANNER" : pilot == "YOU" ? "YOU HAVE THE BANNER: RUN IT HOME" : $"{pilot} HAS THE BANNER";
                case BannerEventKind.Dropped:
                    tone = ours ? Tone.Good : Tone.Bad;
                    return ours ? "CARRIER DOWN: YOUR BANNER IS LOOSE" : "BANNER CARRIER LOST";
                case BannerEventKind.ReturnedByDefender:
                    tone = ours ? Tone.Good : Tone.Bad;
                    return ours ? "BANNER RETURNED" : "ENEMY RETURNED THEIR BANNER";
                case BannerEventKind.ReturnedByTimeout:
                    tone = Tone.Neutral;
                    return ours ? "YOUR BANNER IS BACK HOME" : "THEIR BANNER IS BACK HOME";
                case BannerEventKind.Captured:
                    tone = pilotTeam == viewerTeam ? Tone.Good : Tone.Bad;
                    return pilotTeam == viewerTeam ? "CAPTURED!" : $"{pilotTeamName} CAPTURED YOUR BANNER";
            }
            return null;
        }

        /// <summary>One banner's state for the top bar: "HOME", "TAKEN BY EAST FIGHTER 2", "DROPPED  12 S".</summary>
        public static string BannerStatus(BannerState state, string carrier, float returnLeft) => state switch
        {
            BannerState.Carried => $"TAKEN BY {carrier}",
            BannerState.Dropped => $"DROPPED  {Mathf.CeilToInt(Mathf.Max(0f, returnLeft))} S",
            _ => "HOME",
        };

        /// <summary>A capture field's letter by board index: A, B, C …</summary>
        public static string FieldLetter(int field) => ((char)('A' + field)).ToString();

        /// <summary>A field's name for the HUD: "Central Airfield" → "CENTRAL".</summary>
        public static string FieldName(string displayName)
        {
            string name = (displayName ?? "").ToUpperInvariant().Trim();
            return name.EndsWith(" AIRFIELD") ? name.Substring(0, name.Length - " AIRFIELD".Length) : name;
        }

        /// <summary>
        /// One capture field's state for the top bar: "CONTESTED", "EAST 45%" (a capture under way), "TOWER DOWN", or
        /// the owner ("WEST", "NEUTRAL"). <paramref name="ownerName"/> is null for a neutral field.
        /// </summary>
        public static string FieldStatus(string ownerName, bool contested, string capturingName, float progress, bool towerDown)
        {
            if (contested)
                return "CONTESTED";
            if (capturingName != null && progress > 0f)
                return $"{capturingName} {Mathf.FloorToInt(Mathf.Clamp01(progress) * 100f)}%";
            if (ownerName != null && towerDown)
                return "TOWER DOWN";
            return ownerName ?? "NEUTRAL";
        }

        /// <summary>
        /// What a field changing hands says to a pilot on <paramref name="viewerTeam"/>: "YOU CAPTURED CENTRAL",
        /// "WE CAPTURED CENTRAL", "CENTRAL CAPTURED BY EAST".
        /// </summary>
        public static string FieldCallout(string field, int team, int viewerTeam, bool youCaptured, string teamName, out Tone tone)
        {
            bool ours = team == viewerTeam;
            tone = ours ? Tone.Good : Tone.Bad;
            return youCaptured ? $"YOU CAPTURED {field}" : ours ? $"WE CAPTURED {field}" : $"{field} CAPTURED BY {teamName}";
        }

        /// <summary>
        /// An owned field's control tower fell or came back: "CENTRAL TOWER DOWN: NO AA OR REPAIR" for the owner,
        /// "CENTRAL TOWER DOWN: FULL-SPEED CAPTURE" for everyone else. Null on a neutral field (its tower does nothing).
        /// </summary>
        public static string TowerCallout(string field, int owner, int viewerTeam, bool down, out Tone tone)
        {
            tone = Tone.Neutral;
            if (owner < 0)
                return null;
            bool ours = owner == viewerTeam;
            tone = ours == down ? Tone.Bad : Tone.Good;
            if (down)
                return ours ? $"{field} TOWER DOWN: NO AA OR REPAIR" : $"{field} TOWER DOWN: FULL-SPEED CAPTURE";
            return ours ? $"{field} TOWER REBUILT" : $"ENEMY TOWER AT {field} REBUILT";
        }

        /// <summary>A team now holds every capture field: "WE HOLD EVERY FIELD: 30 S TO WIN" / "EAST HOLDS EVERY FIELD: TAKE ONE BACK".</summary>
        public static string HoldAllCallout(int team, int viewerTeam, string teamName, float toWin, out Tone tone)
        {
            bool ours = team == viewerTeam;
            tone = ours ? Tone.Good : Tone.Bad;
            return ours ? $"WE HOLD EVERY FIELD: {Mathf.CeilToInt(toWin)} S TO WIN" : $"{teamName} HOLDS EVERY FIELD: TAKE ONE BACK";
        }

        /// <summary>
        /// Under the capture ring while parked in a capture field's zone: what's happening to it from where we sit.
        /// "CONTESTED: ENEMY ON THE FIELD", "CAPTURING 45%", "UNDOING EAST 30%", "HELD BY YOUR TEAM".
        /// </summary>
        public static string CaptureHint(int viewerTeam, int owner, int capturingTeam, float progress, bool contested, string capturingName)
        {
            if (contested)
                return "CONTESTED: ENEMY ON THE FIELD";
            int pct = Mathf.FloorToInt(Mathf.Clamp01(progress) * 100f);
            if (capturingTeam >= 0 && capturingTeam != viewerTeam && progress > 0f)
                return $"UNDOING {capturingName} {pct}%";
            if (owner == viewerTeam)
                return "HELD BY YOUR TEAM";
            return $"CAPTURING {pct}%";
        }

        /// <summary>A distance for a marker: metres up close, then kilometres to one decimal.</summary>
        public static string Distance(float metres) => metres < 1000f ? Metres(metres) : Kilometres(metres);

        /// <summary>"640 M": whole metres. Made once per value, as the HUD labels every plane in sight every frame.</summary>
        public static string Metres(float metres)
        {
            double n = System.Math.Round((double)metres, System.MidpointRounding.AwayFromZero);
            if (n < 0 || n >= s_metres.Length)
                return $"{metres:0} M";
            int i = (int)n;
            return s_metres[i] ??= $"{i} M";
        }

        /// <summary>"2.4 KM", cached like <see cref="Metres"/>.</summary>
        static string Kilometres(float metres)
        {
            double tenths = System.Math.Round((double)(metres / 1000f) * 10.0, System.MidpointRounding.AwayFromZero);
            if (tenths < 0 || tenths >= s_kilometres.Length)
                return $"{metres / 1000f:0.0} KM";
            int i = (int)tenths;
            return s_kilometres[i] ??= $"{i / 10}.{i % 10} KM";
        }

        static readonly string[] s_metres = new string[10000];
        static readonly string[] s_kilometres = new string[1000];

        /// <summary>Why a touchdown went wrong: "GEAR NOT DOWN", "TOO FAST".</summary>
        public static string Fault(TouchdownFault fault) => fault switch
        {
            TouchdownFault.GearNotLocked => "GEAR NOT DOWN",
            TouchdownFault.BadAngle => "BAD ANGLE",
            TouchdownFault.SinkTooFast => "DESCENDING TOO FAST",
            TouchdownFault.HardSink => "SINKING TOO FAST",
            TouchdownFault.TooFast => "TOO FAST",
            TouchdownFault.Ditched => "DITCHED IN THE SEA",
            _ => "",
        };

        /// <summary>
        /// "SHOT DOWN BY EAST FIGHTER 2", "CRASHED: TOO FAST", "CRASHED  (WEST AA GETS THE KILL)". The crash fault is
        /// given when the plane died landing badly; the killer when someone gets the credit.
        /// </summary>
        public static string Death(bool shotDown, TouchdownFault? crashFault, string killer)
        {
            string death = shotDown ? "SHOT DOWN" : "CRASHED";
            if (!shotDown && crashFault is TouchdownFault fault)
                death += $": {Fault(fault)}";
            if (killer != null)
                death += shotDown ? $" BY {killer}" : $"  ({killer} GETS THE KILL)";
            return death;
        }

        /// <summary>Planes still flying out of everyone in the match: "7 / 16".</summary>
        public static string PlanesLeft(int flying, int total) => $"{Mathf.Max(0, flying)} / {Mathf.Max(0, total)}";

        /// <summary>
        /// The ram flash for a contact we were in: "RAMMED BOT 5!" when we flew into them (<paramref name="weHit"/>: the
        /// plane that flies in takes the smaller share, though a part with little HP left can take less than that) or did
        /// at least as much as we took, "RAMMED BY BOT 5" when they flew into us and we came off worse. Null for a
        /// harmless bump (nobody took anything).
        /// </summary>
        public static string RamFlash(string other, float dealt, float took, bool weHit, out Tone tone)
        {
            tone = Tone.Neutral;
            if (dealt <= 0f && took <= 0f)
                return null;
            other ??= "?";
            if (weHit || dealt >= took)
            {
                tone = Tone.Good;
                return $"RAMMED {other}!";
            }
            tone = Tone.Bad;
            return $"RAMMED BY {other}";
        }

        /// <summary>Under the ram flash: "120 DEALT  ·  36 TAKEN".</summary>
        public static string RamDamage(float dealt, float took) => $"{Mathf.RoundToInt(dealt)} DEALT  ·  {Mathf.RoundToInt(took)} TAKEN";

        /// <summary>Near a hard edge: "ARENA EDGE  120 M  ·  TURN BACK".</summary>
        public static string ArenaEdge(float metresToEdge) => $"ARENA EDGE  {Distance(Mathf.Max(0f, metresToEdge))}  ·  TURN BACK";

        /// <summary>The death line after a ram: "RAMMED BY BOT 5", or "RAMMED" with nobody to credit.</summary>
        public static string Rammed(string killer) => killer != null ? $"RAMMED BY {killer}" : "RAMMED";

        /// <summary>"EJECTED  (RED 3 GETS THE KILL)": bailing out of a crippled plane still gives whoever crippled it the kill.</summary>
        public static string Ejected(string killer) => killer != null ? $"EJECTED  ({killer} GETS THE KILL)" : "EJECTED";

        /// <summary>"HOLD X TO EJECT", or just "HOLD TO EJECT" with no key to name.</summary>
        public static string EjectHint(string key) => string.IsNullOrEmpty(key) ? "HOLD TO EJECT" : $"HOLD {key.ToUpperInvariant()} TO EJECT";

        /// <summary>What to do on the glide path: "ON GLIDE PATH", "HIGH 12 M", "LOW 8 M  FLY LEFT".</summary>
        public static string ApproachHint(ApproachGuide guide)
        {
            string vertical = guide.IsHigh ? $"HIGH {guide.HeightError:0} M" : guide.IsLow ? $"LOW {-guide.HeightError:0} M" : null;
            string lateral = guide.IsOffCenter ? (guide.Lateral > 0f ? "FLY LEFT" : "FLY RIGHT") : null;
            return vertical == null && lateral == null ? "ON GLIDE PATH"
                : vertical != null && lateral != null ? $"{vertical}  {lateral}" : vertical ?? lateral;
        }
    }
}

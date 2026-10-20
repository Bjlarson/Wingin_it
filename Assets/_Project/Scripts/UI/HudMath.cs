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

        /// <summary>Our shot took a part off an enemy: "WING SHOT OFF!". Null for the body (that's the kill).</summary>
        public static string PartLost(PlanePartType type) => type switch
        {
            PlanePartType.WingLeft or PlanePartType.WingRight => "WING SHOT OFF!",
            PlanePartType.HorizontalStabilizer => "TAIL SHOT OFF!",
            PlanePartType.VerticalStabilizer => "FIN SHOT OFF!",
            PlanePartType.Engine => "ENGINE KNOCKED OUT!",
            _ => null,
        };

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

        /// <summary>A distance for a marker: metres up close, then kilometres to one decimal.</summary>
        public static string Distance(float metres) => metres < 1000f ? $"{metres:0} M" : $"{metres / 1000f:0.0} KM";

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

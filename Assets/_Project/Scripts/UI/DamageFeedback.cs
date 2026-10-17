using UnityEngine;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.Settings;
using WingingIt.World;
using static WingingIt.UI.GlassGUI;

namespace WingingIt.UI
{
    /// <summary>
    /// How hurt the player is, felt without reading the HUD (Design/06-UI-UX/hud.md#damage-feedback). Warning (a Heavy
    /// repair: body Damaged, a stabilizer lost or the engine dead): drifting smoke at the screen edges and a two-beep
    /// tone. Critical (body Critical or a wing lost): a red heartbeat vignette, a looping alarm and RETURN TO BASE with
    /// the nearest friendly field above the instrument cluster. Every hit flashes the screen edges red. Lives on the
    /// Flight HUD object, so it hides (and the alarm stops) with it. Settings: Damage screen effects scales the smoke,
    /// vignette and hit flash (the alarm and RETURN TO BASE always stay), Reduce flashing holds the heartbeat steady,
    /// and the tones play at the Alerts volume.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class DamageFeedback : MonoBehaviour
    {
        [SerializeField] PlaneController plane;
        [SerializeField] PlaneHealth health;
        [SerializeField] PlayerFlightInput player;
        [Range(0f, 1f)] [SerializeField] float volume = 0.35f;

        const float ReferenceHeight = 1080f;
        static readonly Color Smoke = new(0.2f, 0.2f, 0.21f);
        static readonly Color Blood = new(0.85f, 0.04f, 0.03f);
        static readonly Color Red = new(1f, 0.3f, 0.25f);
        static readonly Vector2 ClusterSize = new(804f, 176f); // FlightHUD's bottom cluster

        readonly GlassGUI _g = new();
        AudioSource _source;
        AudioClip _warningClip, _alarmClip;
        Texture2D _smoke, _edge;
        DamageAlert _alert;
        float _hitTime = float.NegativeInfinity, _hitStrength;

        /// <summary>The current alert level (None while dead).</summary>
        public DamageAlert Alert => _alert;

        /// <summary>The Critical alarm is sounding (and RETURN TO BASE is up).</summary>
        public bool AlarmPlaying => _source != null && _source.isPlaying && _source.clip == _alarmClip;

        /// <summary>RETURN TO BASE is on screen this frame.</summary>
        public bool ShowingReturnToBase { get; private set; }

        /// <summary>How many warning tones have played. For tests.</summary>
        public int WarningTones { get; private set; }

        void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0f;
            _source.ignoreListenerPause = true;
            _warningClip = HudTones.Warning();
            _alarmClip = HudTones.Alarm();
        }

        void OnEnable()
        {
            if (health != null)
            {
                health.Damaged += OnDamaged;
                health.PartDestroyed += OnPartDestroyed;
            }
            // Pick up the current state silently: coming back from the picker shouldn't beep for old damage.
            _alert = CurrentAlert();
        }

        void OnDisable()
        {
            if (health != null)
            {
                health.Damaged -= OnDamaged;
                health.PartDestroyed -= OnPartDestroyed;
            }
            if (_source != null)
                _source.Stop();
            ShowingReturnToBase = false;
        }

        void OnDestroy()
        {
            _g.Dispose();
            Destroy(_warningClip);
            Destroy(_alarmClip);
            if (_smoke != null)
                Destroy(_smoke);
            if (_edge != null)
                Destroy(_edge);
        }

        void OnDamaged(PlaneHealth _, PartDamageInfo info)
        {
            if (info.Amount <= 0f)
                return;
            var body = health.GetPart(PlanePartType.Body);
            float strength = DamageFeedbackMath.HitStrength(info.Amount, body != null ? body.MaxHealth : 100f, false);
            Flash(strength);
        }

        void OnPartDestroyed(PlaneHealth _, PartDamageInfo info)
        {
            if (info.Part.partType == PlanePartType.Body)
                return; // that's the death
            Flash(1f);
            // A part gone is news even if we were already warned; the alarm covers it when it makes us Critical.
            _alert = CurrentAlert();
            if (_alert != DamageAlert.Critical)
                PlayWarning();
        }

        void Flash(float strength)
        {
            // A new hit never weakens a flash still fading in from a bigger one.
            float now = DamageFeedbackMath.HitFlashAlpha(Time.time - _hitTime, _hitStrength);
            _hitStrength = Mathf.Max(strength, now);
            _hitTime = Time.time;
        }

        DamageAlert CurrentAlert() => plane == null || health == null || !plane.IsAlive ? DamageAlert.None
            : DamageFeedbackMath.Alert(health.BodyState, health.LostPartsMask);

        void Update()
        {
            var alert = CurrentAlert();
            if (alert == DamageAlert.Warning && _alert == DamageAlert.None)
                PlayWarning();
            _alert = alert;

            bool onApproach = false;
            if (plane != null && plane.IsAlive && !plane.IsGrounded)
            {
                var field = Airfield.NearestFriendly(plane.Position, plane.Team);
                onApproach = field != null && field.GetApproach(plane.Position, plane.Velocity).OnApproach;
            }
            ShowingReturnToBase = plane != null && DamageFeedbackMath.ShouldReturnToBase(alert, plane.IsAlive, plane.IsGrounded, onApproach);

            if (ShowingReturnToBase && !AlarmPlaying)
            {
                _source.clip = _alarmClip;
                _source.loop = true;
                _source.volume = volume * 0.8f * GameSettings.Current.Data.alertsVolume;
                _source.Play();
            }
            else if (!ShowingReturnToBase && AlarmPlaying)
                _source.Stop();
        }

        void PlayWarning()
        {
            WarningTones++;
            if (!AlarmPlaying)
                _source.PlayOneShot(_warningClip, volume * GameSettings.Current.Data.alertsVolume);
        }

        // ---------- Drawing ----------

        void OnGUI()
        {
            if (plane == null || health == null)
                return;
            GUI.depth = 1; // behind the HUD
            _g.EnsureStyles();
            EnsureTextures();
            float scale = Screen.height / ReferenceHeight;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float width = Screen.width / scale;
            var screen = new Rect(0f, 0f, width, ReferenceHeight);

            var settings = GameSettings.Current.Data;
            float strength = settings.damageEffects;
            float a = DamageFeedbackMath.VignetteAlpha(_alert, Time.time);
            if (_alert == DamageAlert.Critical && settings.reduceFlashing)
                a = 0.55f; // a steady glow instead of the heartbeat
            a *= strength;
            if (_alert == DamageAlert.Warning)
            {
                // Two copies of the smoke breathing out of step, one a little larger, so the edge seems to drift.
                float k = 0.5f + 0.5f * Mathf.Sin(Time.time * 0.7f);
                Draw(_smoke, screen, Alpha(Smoke, a * (0.55f + 0.45f * k)));
                Draw(_smoke, Inflate(screen, 40f, 0f), Alpha(Smoke, a * (1f - 0.45f * k)));
            }
            else if (_alert == DamageAlert.Critical)
            {
                Draw(_smoke, screen, Alpha(Smoke, 0.3f * strength));
                Draw(_edge, screen, Alpha(Blood, a));
            }

            float hit = plane.IsAlive ? DamageFeedbackMath.HitFlashAlpha(Time.time - _hitTime, _hitStrength) * strength : 0f;
            if (hit > 0f)
                Draw(_edge, screen, Alpha(Blood, hit * 0.8f));

            if (ShowingReturnToBase)
                DrawReturnToBase(width);
        }

        /// <summary>Above the instrument cluster: flashing RETURN TO BASE, then where the nearest repair is.</summary>
        void DrawReturnToBase(float width)
        {
            bool touch = player != null && player.TouchControlsActive;
            // FlightHUD's cluster, drawn at its HUD scale, brought into this overlay's coordinates
            float hs = GameSettings.Current.Data.hudScale;
            var c = HudMath.ClusterRect(width / hs, ReferenceHeight / hs, ClusterSize, touch, 20f, out _);
            var cluster = new Rect(c.x * hs, c.y * hs, c.width * hs, c.height * hs);
            float y = cluster.y - 78f;
            Color color = GameSettings.Blink(Time.time, 0.8f, 0.6875f) ? Red : Color.white;
            _g.Shadowed(new Rect(cluster.x - 200f, y, cluster.width + 400f, 44f), "RETURN TO BASE", _g.Title, 6f, TextAnchor.MiddleCenter, color, 0.85f);

            var field = Airfield.NearestFriendly(plane.Position, plane.Team);
            if (field == null)
                return;
            Vector3 zone = field.RepairZone != null ? field.RepairZone.transform.position : field.transform.position;
            string where = $"REPAIR AT {field.DisplayName.ToUpperInvariant()}  {HudText.Distance(Vector3.Distance(plane.Position, zone))}";
            _g.Shadowed(new Rect(cluster.x - 200f, y + 42f, cluster.width + 400f, 22f), where, _g.Hint, 2.4f, TextAnchor.MiddleCenter, Alpha(White, 0.9f));
        }

        static void Draw(Texture tex, Rect r, Color color)
        {
            var c = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(r, tex, ScaleMode.StretchToFill);
            GUI.color = c;
        }

        /// <summary>
        /// Two white masks, opaque at the screen edges and clear in the middle (an ellipse, so the corners are thickest):
        /// a clean one for the red, and one broken up by noise for the smoke.
        /// </summary>
        void EnsureTextures()
        {
            if (_edge != null)
                return;
            _edge = Mask(false);
            _smoke = Mask(true);
        }

        static Texture2D Mask(bool noisy)
        {
            const int n = 256;
            var tex = new Texture2D(n, n, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp, filterMode = FilterMode.Bilinear };
            var px = new Color[n * n];
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float u = (x + 0.5f) / n * 2f - 1f, v = (y + 0.5f) / n * 2f - 1f;
                    float d = Mathf.Sqrt(u * u + v * v) / Mathf.Sqrt(2f); // 0 center, 1 corner
                    float inner = 0.5f, outer = 0.95f;
                    if (noisy)
                    {
                        float noise = Mathf.PerlinNoise(x * 0.035f + 11f, y * 0.035f + 7f) * 0.65f + Mathf.PerlinNoise(x * 0.09f, y * 0.09f) * 0.35f;
                        inner += (noise - 0.5f) * 0.3f;
                    }
                    float a = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, outer, d));
                    px[y * n + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels(px);
            tex.Apply(false, true);
            return tex;
        }
    }

    /// <summary>Placeholder cockpit sounds, synthesized until the audio pass (Phase 11).</summary>
    public static class HudTones
    {
        const int Rate = 44100;

        /// <summary>Two short high beeps.</summary>
        public static AudioClip Warning()
        {
            var data = new float[(int)(Rate * 0.3f)];
            Tone(data, 0f, 0.09f, 1047f);
            Tone(data, 0.15f, 0.09f, 1047f);
            return Clip("Damage Warning", data);
        }

        /// <summary>A hi-lo warble, 0.8 s, meant to loop.</summary>
        public static AudioClip Alarm()
        {
            var data = new float[(int)(Rate * 0.8f)];
            Tone(data, 0f, 0.36f, 880f);
            Tone(data, 0.4f, 0.36f, 660f);
            return Clip("Critical Alarm", data);
        }

        /// <summary>A soft square-ish tone (fundamental + 3rd harmonic) with 8 ms fades so it doesn't click.</summary>
        static void Tone(float[] data, float start, float length, float freq)
        {
            int from = (int)(start * Rate), count = (int)(length * Rate);
            const float fade = 0.008f * Rate;
            for (int i = 0; i < count && from + i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, i / fade, (count - i) / fade);
                float s = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.3f * Mathf.Sin(6f * Mathf.PI * freq * t);
                data[from + i] += s * 0.6f * env;
            }
        }

        static AudioClip Clip(string name, float[] data)
        {
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}

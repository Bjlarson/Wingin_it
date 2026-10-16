using System;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// Repairs a plane that stops on its wheels inside a friendly <see cref="RepairZone"/>. The tier is fixed when the
    /// repair starts (<see cref="RepairRules.GetTier"/>), and once its timer runs out every part, all HP and all ammo
    /// come back (<see cref="PlaneHealth.RestoreAll"/>). Leaving the zone first, or taking off, cancels it: no partial
    /// repair. A finished plane has to leave the zone before it can repair again. While the timer runs the plane is
    /// invulnerable (<see cref="PlaneHealth.IsInvulnerable"/>) and wears a gold bubble so attackers don't waste ammo.
    /// </summary>
    [RequireComponent(typeof(PlaneController), typeof(PlaneHealth))]
    public class PlaneRepair : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] RepairRules rules = new();
        [Tooltip("Transparent unlit material for the invulnerability bubble (tinted by bubbleColor).")]
        [SerializeField] Material bubbleMaterial;
        [SerializeField] Color bubbleColor = new Color(1f, 0.82f, 0.25f, 0.22f);

        GameObject _bubble;
        Renderer _bubbleRenderer;
        MaterialPropertyBlock _bubbleBlock;
        PlaneController _plane;
        PlaneHealth _health;
        bool _finishedHere; // repaired in the current zone: wait until the plane leaves it

        public event Action<PlaneRepair> Started;
        public event Action<PlaneRepair> Completed;
        /// <summary>Left the zone or took off before the timer ran out. Nothing was repaired.</summary>
        public event Action<PlaneRepair> Cancelled;

        public RepairRules Rules => rules;
        public bool IsRepairing { get; private set; }
        /// <summary>The tier of the current (or most recent) repair.</summary>
        public RepairTier Tier { get; private set; }
        /// <summary>Total seconds the current (or most recent) repair takes.</summary>
        public float Duration { get; private set; }
        public float Elapsed { get; private set; }
        public float TimeLeft => IsRepairing ? Mathf.Max(0f, Duration - Elapsed) : 0f;
        /// <summary>0..1 through the current repair.</summary>
        public float Progress => Duration > 0f ? Mathf.Clamp01(Elapsed / Duration) : 1f;
        /// <summary>The zone the plane is repairing in, or null.</summary>
        public RepairZone Zone { get; private set; }
        /// <summary>The invulnerability bubble, created on the first repair (null before).</summary>
        public GameObject Bubble => _bubble;

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
        }

        void OnEnable()
        {
            _plane.Crashed += OnPlaneReset;
            _plane.Respawned += OnPlaneReset;
        }

        void OnDisable()
        {
            _plane.Crashed -= OnPlaneReset;
            _plane.Respawned -= OnPlaneReset;
            Stop();
        }

        void OnPlaneReset(PlaneController _) => Stop();
        void OnPlaneReset(PlaneController _, Vector3 __) => Stop();

        void Stop()
        {
            SetRepairing(false);
            Zone = null;
            _finishedHere = false;
        }

        /// <summary>The repair timer and invulnerability always start and end together.</summary>
        void SetRepairing(bool repairing)
        {
            IsRepairing = repairing;
            if (_health != null)
                _health.IsInvulnerable = repairing;
            SetBubbleVisible(repairing);
        }

        void FixedUpdate()
        {
            var zone = _plane.IsAlive && _plane.IsGrounded ? FriendlyZoneAt(_plane.Position, _plane.Team) : null;

            if (IsRepairing)
            {
                if (zone != Zone)
                {
                    SetRepairing(false);
                    Zone = null;
                    Cancelled?.Invoke(this);
                    return;
                }
                Elapsed += Time.fixedDeltaTime;
                if (Elapsed >= Duration)
                    Complete();
                return;
            }

            if (zone == null)
                _finishedHere = false;
            else if (!_finishedHere && _plane.Speed <= rules.stoppedSpeed)
                Begin(zone);
        }

        void Begin(RepairZone zone)
        {
            Tier = RepairRules.GetTier(_health.BodyState, _health.LostPartsMask);
            Duration = rules.GetDuration(Tier);
            Elapsed = 0f;
            Zone = zone;
            SetRepairing(true);
            Started?.Invoke(this);
            if (Duration <= 0f)
                Complete();
        }

        void Complete()
        {
            SetRepairing(false);
            _finishedHere = true;
            Elapsed = Duration;
            _health.RestoreAll(); // parts, HP, kill credit; weapons restock on Restored
            Completed?.Invoke(this);
        }

        // ------------------------------------------------------------------ visuals (placeholder)

        void SetBubbleVisible(bool visible)
        {
            if (visible && _bubble == null)
            {
                _bubble = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                _bubble.name = "Repair Shield";
                DestroyImmediate(_bubble.GetComponent<Collider>()); // never a hitbox or an obstacle
                _bubble.transform.SetParent(transform, false);
                _bubbleRenderer = _bubble.GetComponent<Renderer>();
                if (bubbleMaterial != null)
                    _bubbleRenderer.sharedMaterial = bubbleMaterial;
                _bubbleRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                _bubbleBlock = new MaterialPropertyBlock();
            }
            if (_bubble == null)
                return;
            float scale = _plane != null && _plane.Stats != null ? _plane.Stats.visualScale : 1f;
            _bubble.transform.localScale = Vector3.one * 11f * scale; // a little bigger than the Armor Shield
            _bubble.SetActive(visible);
        }

        void LateUpdate()
        {
            if (_bubble == null || !_bubble.activeSelf)
                return;
            // A slow shimmer that quickens as the repair nears the end.
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * Mathf.Lerp(3f, 9f, Progress));
            var color = bubbleColor;
            color.a *= Mathf.Lerp(0.6f, 1.3f, pulse);
            _bubbleBlock.SetColor(BaseColorId, color);
            _bubbleRenderer.SetPropertyBlock(_bubbleBlock);
        }

        /// <summary>The repair zone of an airfield <paramref name="team"/> can repair at that contains <paramref name="position"/>, or null.</summary>
        public static RepairZone FriendlyZoneAt(Vector3 position, int team)
        {
            foreach (var field in Airfield.All)
                if (field.IsFriendlyTo(team) && field.RepairZone != null && field.RepairZone.Contains(position))
                    return field.RepairZone;
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Runs the class's special ability (<see cref="FlightStats.ability"/>): pressing <see cref="FlightInput.UseAbility"/>
    /// starts it, it lasts its duration, then recharges for its cooldown. Dying ends it early; respawning or changing
    /// class makes it ready again. Also answers radar queries: Support planes reveal nearby enemies to their team.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so a timed effect (a barrel roll) gets all its steps before it ends
    [RequireComponent(typeof(PlaneController))]
    public class PlaneAbility : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<PlaneAbility> _all = new();

        [Tooltip("Transparent unlit material for the shield bubble and the repair aura.")]
        [SerializeField] Material shieldMaterial;
        [Tooltip("Glow for the afterburner flame.")]
        [SerializeField] Material flameMaterial;
        [SerializeField] Color shieldColor = new Color(0.45f, 0.75f, 1f, 0.28f);
        [SerializeField] Color auraColor = new Color(0.35f, 1f, 0.45f, 0.12f);

        PlaneController _plane;
        AbilityStats _active; // the asset that started the running effect (survives a class change)
        bool _triggerLatched;
        GameObject _shield, _aura, _flame;

        /// <summary>Raised when the ability starts.</summary>
        public event Action<PlaneAbility> Activated;
        /// <summary>Raised when the effect ends (timed out, died, respawned or changed class).</summary>
        public event Action<PlaneAbility> Ended;

        public PlaneController Plane => _plane;
        public PlaneHealth Health { get; private set; }
        /// <summary>This class's ability, or null if it has none.</summary>
        public AbilityStats Stats => _plane.Stats != null ? _plane.Stats.ability : null;
        public bool IsEquipped => Stats != null;
        public bool IsActive => _active != null;
        public float ActiveTimeLeft { get; private set; }
        public float CooldownLeft { get; private set; }
        public bool IsReady => IsEquipped && !IsActive && CooldownLeft <= 0f;
        /// <summary>0..1 recharge progress (1 = ready). While active, the fraction of the effect left.</summary>
        public float Charge
        {
            get
            {
                var s = Stats;
                if (s == null)
                    return 0f;
                if (IsActive)
                    return Mathf.Clamp01(ActiveTimeLeft / _active.duration);
                return s.cooldown <= 0f ? 1f : 1f - Mathf.Clamp01(CooldownLeft / s.cooldown);
            }
        }

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            Health = GetComponent<PlaneHealth>();
        }

        void OnEnable()
        {
            _all.Add(this);
            _plane.Crashed += OnCrashed;
            _plane.Respawned += OnPlaneReset;
            _plane.StatsChanged += OnPlaneReset;
        }

        void OnDisable()
        {
            _all.Remove(this);
            _plane.Crashed -= OnCrashed;
            _plane.Respawned -= OnPlaneReset;
            _plane.StatsChanged -= OnPlaneReset;
            EndEffect();
        }

        void OnDestroy()
        {
            foreach (var go in new[] { _shield, _aura, _flame })
                if (go != null)
                    Destroy(go);
        }

        void OnCrashed(PlaneController _, Vector3 point) => EndEffect();

        void OnPlaneReset(PlaneController _)
        {
            EndEffect();
            CooldownLeft = 0f;
        }

        /// <summary>Starts the ability if it's ready. For the player and bots alike (they press the button); tests call it directly.</summary>
        public bool TryActivate()
        {
            var s = Stats;
            if (s == null || !_plane.IsAlive || !IsReady || !s.CanActivate(this))
                return false;
            _active = s;
            ActiveTimeLeft = s.duration;
            s.Begin(this);
            Activated?.Invoke(this);
            return true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;

            // One activation per press, like missiles.
            bool held = _plane.IsAlive && _plane.CurrentInput.UseAbility;
            bool pressed = held && !_triggerLatched;
            _triggerLatched = held;
            if (pressed)
                TryActivate();

            if (IsActive)
            {
                _active.Tick(this, dt);
                ActiveTimeLeft -= dt;
                if (ActiveTimeLeft <= 0f)
                    EndEffect();
            }
            else if (CooldownLeft > 0f)
            {
                CooldownLeft = Mathf.Max(0f, CooldownLeft - dt);
            }
        }

        void EndEffect()
        {
            if (_active == null)
                return;
            var ended = _active;
            _active = null;
            ActiveTimeLeft = 0f;
            CooldownLeft = ended.cooldown;
            ended.End(this);
            Ended?.Invoke(this);
        }

        // ------------------------------------------------------------------ radar

        /// <summary>
        /// Whether <paramref name="target"/> shows up on <paramref name="team"/>'s radar: a living Support on that
        /// team (other than the target) is within its radar range. Passive, always on.
        /// </summary>
        public static bool IsRevealedTo(PlaneController target, int team)
        {
            if (target == null || !target.IsAlive || target.Team == team)
                return false;
            foreach (var a in _all)
            {
                if (a._plane.Team != team || !a._plane.IsAlive || a.Stats is not RepairAuraAbility aura || aura.radarRange <= 0f)
                    continue;
                if ((a._plane.Position - target.Position).sqrMagnitude <= aura.radarRange * aura.radarRange)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ visuals (placeholder)

        public void SetShieldVisible(bool visible)
        {
            if (visible && _shield == null)
                _shield = Bubble("Armor Shield", shieldMaterial, shieldColor, PrimitiveType.Sphere);
            if (_shield != null)
            {
                float size = 10f * (_plane.Stats != null ? _plane.Stats.visualScale : 1f);
                _shield.transform.localScale = Vector3.one * size;
                _shield.SetActive(visible);
            }
        }

        /// <summary>A flat disc the size of the aura, kept level, so the pilot sees it from the chase camera too.</summary>
        public void SetAuraVisible(bool visible, float radius)
        {
            if (visible && _aura == null)
                _aura = Bubble("Repair Aura", shieldMaterial, auraColor, PrimitiveType.Cylinder);
            if (_aura != null)
            {
                _aura.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
                _aura.SetActive(visible);
            }
        }

        public void SetFlameVisible(bool visible)
        {
            if (visible && _flame == null)
            {
                _flame = Bubble("Afterburner", flameMaterial, new Color(1f, 0.6f, 0.15f), PrimitiveType.Capsule);
                _flame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            }
            if (_flame != null)
            {
                float s = _plane.Stats != null ? _plane.Stats.visualScale : 1f;
                _flame.transform.localPosition = new Vector3(0f, 0f, -4.6f * s);
                _flame.transform.localScale = new Vector3(0.8f, 1.6f, 0.8f) * s;
                _flame.SetActive(visible);
            }
        }

        GameObject Bubble(string name, Material material, Color color, PrimitiveType shape)
        {
            var go = GameObject.CreatePrimitive(shape);
            go.name = name;
            DestroyImmediate(go.GetComponent<Collider>()); // never a hitbox or an obstacle, not even for this frame
            go.transform.SetParent(transform, false);
            var r = go.GetComponent<Renderer>();
            if (material != null)
                r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
            go.SetActive(false);
            return go;
        }

        void LateUpdate()
        {
            // The aura disc stays level and centered on the plane.
            if (_aura != null && _aura.activeSelf)
                _aura.transform.rotation = Quaternion.identity;
        }
    }
}

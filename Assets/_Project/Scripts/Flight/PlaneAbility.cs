using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Effects;

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

        [Tooltip("Transparent unlit material for the repair aura, and the shield bubble without the VFX pack.")]
        [SerializeField] Material shieldMaterial;
        [Tooltip("Glow for the placeholder afterburner flame, without the VFX pack.")]
        [SerializeField] Material flameMaterial;
        [SerializeField] Color shieldColor = new Color(0.45f, 0.75f, 1f, 0.28f);
        [SerializeField] Color auraColor = new Color(0.35f, 1f, 0.45f, 0.12f);

        PlaneController _plane;
        AbilityStats _active; // the asset that started the running effect (survives a class change)
        bool _triggerLatched;
        GameObject _shield, _aura, _flame;
        Vector3 _flameScale;
        float _shieldSize, _shieldPulse;

        /// <summary>Raised when the ability starts.</summary>
        public event Action<PlaneAbility> Activated;
        /// <summary>Raised when the effect ends (timed out, died, respawned or changed class).</summary>
        public event Action<PlaneAbility> Ended;

        public PlaneController Plane => _plane;
        public PlaneHealth Health { get; private set; }
        /// <summary>
        /// The match's stand-in for the class's ability (Ram Royale gives Support a Ram Boost), or null for the class's
        /// own. Set by the match when the plane spawns; cleared on respawn or a class change.
        /// </summary>
        public AbilityStats Override { get; set; }
        /// <summary>This plane's ability (the <see cref="Override"/>, else the class's), or null if it has none.</summary>
        public AbilityStats Stats => Override != null ? Override : _plane.Stats != null ? _plane.Stats.ability : null;
        /// <summary>The ability whose effect is running, or null.</summary>
        public AbilityStats ActiveStats => _active;
        /// <summary>Has an ability it may use: one is set, and the match hasn't switched abilities off (<see cref="PlaneController.AbilitiesOff"/>).</summary>
        public bool IsEquipped => Stats != null && !_plane.AbilitiesOff;
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
            if (Health != null)
                Health.Damaged += OnDamaged;
        }

        void OnDisable()
        {
            _all.Remove(this);
            _plane.Crashed -= OnCrashed;
            _plane.Respawned -= OnPlaneReset;
            _plane.StatsChanged -= OnPlaneReset;
            if (Health != null)
                Health.Damaged -= OnDamaged;
            EndEffect();
        }

        void OnDestroy()
        {
            foreach (var go in new[] { _shield, _aura, _flame })
                if (go != null)
                    Destroy(go);
        }

        void OnCrashed(PlaneController _, Vector3 point) => EndEffect();

        /// <summary>A hit on the shield ripples it and sparkles where it struck.</summary>
        void OnDamaged(PlaneHealth _, PartDamageInfo info)
        {
            if (_shield == null || !_shield.activeSelf)
                return;
            _shieldPulse = 1f;
            Vector3 center = _shield.transform.position;
            Vector3 onBubble = center + (info.Point - center).normalized * (_shieldSize * 0.5f);
            Vfx.Play(VfxKind.ShieldHit, onBubble);
        }

        void OnPlaneReset(PlaneController _)
        {
            EndEffect();
            Override = null;
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
        /// team (other than the target) is within its radar range, or the target is towing a banner (Capture the Banner:
        /// the carrier shows to every enemy the whole time). Passive, always on.
        /// </summary>
        public static bool IsRevealedTo(PlaneController target, int team)
        {
            if (target == null || !target.IsAlive || target.Team == team)
                return false;
            if (target.CarryingBanner)
                return true;
            foreach (var a in _all)
            {
                if (a._plane.Team != team || !a._plane.IsAlive || a.Stats is not RepairAuraAbility aura || aura.radarRange <= 0f)
                    continue;
                if ((a._plane.Position - target.Position).sqrMagnitude <= aura.radarRange * aura.radarRange)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ visuals

        /// <summary>The faceted bubble from the VFX pack (a plain sphere without it), 10 m across at class scale 1.</summary>
        public void SetShieldVisible(bool visible)
        {
            var library = Vfx.Library;
            if (visible && _shield == null)
                _shield = library != null && library.shieldMesh != null
                    ? MeshBubble("Armor Shield", library.shieldMesh, library.shieldMaterial, shieldColor)
                    : Bubble("Armor Shield", shieldMaterial, shieldColor, PrimitiveType.Sphere);
            if (_shield != null)
            {
                _shieldSize = 10f * (_plane.Stats != null ? _plane.Stats.visualScale : 1f);
                _shieldPulse = 0f;
                _shield.transform.localScale = Vector3.one * _shieldSize;
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

        /// <summary>A jagged flame (the VFX pack's; a glowing capsule without it) roaring from the tail, flickering.</summary>
        public void SetFlameVisible(bool visible)
        {
            var library = Vfx.Library;
            bool mesh = library != null && library.afterburnerMesh != null;
            if (visible && _flame == null)
            {
                if (mesh)
                {
                    _flame = MeshBubble("Afterburner", library.afterburnerMesh, library.afterburnerMaterial, Color.white);
                }
                else
                {
                    _flame = Bubble("Afterburner", flameMaterial, new Color(1f, 0.6f, 0.15f), PrimitiveType.Capsule);
                    _flame.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                }
            }
            if (_flame != null)
            {
                float s = _plane.Stats != null ? _plane.Stats.visualScale : 1f;
                // The mesh starts at the nozzle and runs 1 m back; the capsule is centred.
                _flame.transform.localPosition = new Vector3(0f, 0f, (mesh ? -3.9f : -4.6f) * s);
                _flameScale = (mesh ? new Vector3(1.1f, 1.1f, 3.4f) : new Vector3(0.8f, 1.6f, 0.8f)) * s;
                _flame.transform.localScale = _flameScale;
                _flame.SetActive(visible);
            }
        }

        GameObject MeshBubble(string name, Mesh mesh, Material material, Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
            go.SetActive(false);
            return go;
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
            // The shield swells for a moment when hit; the flame flickers.
            if (_shield != null && _shield.activeSelf)
            {
                _shieldPulse = Mathf.MoveTowards(_shieldPulse, 0f, Time.deltaTime * 5f);
                _shield.transform.localScale = Vector3.one * (_shieldSize * (1f + 0.08f * _shieldPulse));
            }
            if (_flame != null && _flame.activeSelf)
            {
                float flicker = Mathf.PerlinNoise(Time.time * 14f, 0.7f);
                _flame.transform.localScale = Vector3.Scale(_flameScale, new Vector3(1f, 1f, Mathf.Lerp(0.8f, 1.2f, flicker)));
            }
        }
    }
}

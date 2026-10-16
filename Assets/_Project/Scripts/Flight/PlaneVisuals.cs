using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Placeholder presentation for the modular plane: class tint and scale, spinning propeller, landing gear folding,
    /// parts breaking off as tumbling, smoking debris, damage smoke and fire from the body state
    /// (and a dead engine), a cheap break-apart when the plane crashes,
    /// and a mid-air explosion with a burning, falling wreck when it's shot down.
    /// </summary>
    public class PlaneVisuals : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [SerializeField] PlaneController plane;
        [Tooltip("Parent of all visible parts; scaled per class.")]
        [SerializeField] Transform modelRoot;
        [Tooltip("Renderers tinted with the class color.")]
        [SerializeField] Renderer[] tintedRenderers;
        [SerializeField] Transform propeller;
        [SerializeField] float propellerMaxRpm = 1800f;
        [SerializeField] float debrisLifetime = 6f;
        [Tooltip("Wreckage of a plane shot down in mid-air; long enough to fall to the ground.")]
        [SerializeField] float wreckLifetime = 12f;
        [Tooltip("Trail left by a broken-off part as it falls.")]
        [SerializeField] Material smokeMaterial;
        [Tooltip("Flames trailing a Critical plane.")]
        [SerializeField] Material fireMaterial;

        [Tooltip("Landing gear legs: each folds by its angles when the gear is up and hides once fully stowed.")]
        [SerializeField] GearLeg[] gearLegs;

        [Header("Damage smoke")]
        [SerializeField] Color lightSmokeColor = new(0.6f, 0.6f, 0.62f);
        [SerializeField] Color heavySmokeColor = new(0.1f, 0.1f, 0.11f);

        enum SmokeLevel { None, Light, Heavy }

        /// <summary>One landing gear leg: a pivot posed fully extended in the prefab, and its fold when stowed.</summary>
        [System.Serializable]
        public struct GearLeg
        {
            public Transform pivot;
            [Tooltip("Local Euler angles added to the extended pose when fully retracted.")]
            public Vector3 foldAngles;
        }

        PlaneHealth _health;
        LandingGear _gear;
        Quaternion[] _legExtended;
        TrailRenderer _smoke, _fire;
        SmokeLevel _smokeLevel;
        bool _fireOn;

        MaterialPropertyBlock _block;
        readonly List<Renderer> _hidden = new();

        void Reset() => plane = GetComponent<PlaneController>();

        void OnEnable()
        {
            if (plane == null)
                return;
            _health = plane.GetComponent<PlaneHealth>();
            if (_health != null)
                _health.PartDestroyed += OnPartDestroyed;
            _gear = plane.GetComponent<LandingGear>();
            plane.StatsChanged += OnStatsChanged;
            plane.Crashed += OnCrashed;
            plane.Respawned += OnRespawned;
            ApplyClassLook();
        }

        void OnDisable()
        {
            if (plane == null)
                return;
            if (_health != null)
                _health.PartDestroyed -= OnPartDestroyed;
            plane.StatsChanged -= OnStatsChanged;
            plane.Crashed -= OnCrashed;
            plane.Respawned -= OnRespawned;
        }

        void Update()
        {
            if (propeller == null || !plane.IsAlive)
                return;
            float throttle = plane.IsBoosting ? 1.3f : Mathf.Lerp(0.3f, 1f, plane.Throttle);
            propeller.Rotate(0f, 0f, propellerMaxRpm * 6f * throttle * plane.EffectiveThrust * Time.deltaTime, Space.Self);
        }

        void LateUpdate()
        {
            UpdateDamageSmoke();
            UpdateGear();
        }

        void UpdateGear()
        {
            if (gearLegs == null || gearLegs.Length == 0)
                return;
            if (_legExtended == null)
            {
                _legExtended = new Quaternion[gearLegs.Length];
                for (int i = 0; i < gearLegs.Length; i++)
                    if (gearLegs[i].pivot != null)
                        _legExtended[i] = gearLegs[i].pivot.localRotation;
            }

            float extension = _gear != null ? Mathf.SmoothStep(0f, 1f, _gear.Extension) : 1f;
            for (int i = 0; i < gearLegs.Length; i++)
            {
                var leg = gearLegs[i];
                if (leg.pivot == null)
                    continue;
                bool visible = extension > 0f;
                if (leg.pivot.gameObject.activeSelf != visible)
                    leg.pivot.gameObject.SetActive(visible);
                leg.pivot.localRotation = _legExtended[i] * Quaternion.Euler(leg.foldAngles * (1f - extension));
            }
        }

        /// <summary>Light smoke when Damaged; heavy black smoke when Critical or the engine is dead; fire when Critical.</summary>
        void UpdateDamageSmoke()
        {
            var level = SmokeLevel.None;
            bool fire = false;
            if (_health != null && plane.IsAlive)
            {
                var state = _health.BodyState;
                var engine = _health.GetPart(PlanePartType.Engine);
                bool engineDead = engine != null && engine.IsDestroyed;
                level = state == BodyDamageState.Critical || engineDead ? SmokeLevel.Heavy
                    : state == BodyDamageState.Damaged ? SmokeLevel.Light
                    : SmokeLevel.None;
                fire = state == BodyDamageState.Critical;
            }

            if (level != _smokeLevel)
            {
                _smokeLevel = level;
                if (level != SmokeLevel.None && _smoke == null)
                    _smoke = CreateTrail("Damage Smoke", smokeMaterial);
                if (_smoke != null)
                {
                    bool heavy = level == SmokeLevel.Heavy;
                    _smoke.emitting = level != SmokeLevel.None;
                    _smoke.time = heavy ? 2.5f : 1.4f;
                    _smoke.widthCurve = heavy
                        ? new AnimationCurve(new Keyframe(0f, 1.3f), new Keyframe(1f, 4.5f))
                        : new AnimationCurve(new Keyframe(0f, 0.6f), new Keyframe(1f, 2.2f));
                    SetColor(_smoke, heavy ? heavySmokeColor : lightSmokeColor);
                    if (level == SmokeLevel.None)
                        _smoke.Clear();
                }
            }

            if (fire != _fireOn)
            {
                _fireOn = fire;
                if (fire && _fire == null)
                {
                    _fire = CreateTrail("Damage Fire", fireMaterial);
                    if (_fire != null)
                        _fire.time = 0.2f;
                }
                if (_fire != null)
                {
                    _fire.emitting = fire;
                    if (!fire)
                        _fire.Clear();
                }
            }

            // Flicker the flames.
            if (_fireOn && _fire != null)
                _fire.widthMultiplier = Mathf.Lerp(0.6f, 1.3f, Mathf.PerlinNoise(Time.time * 12f, 0.3f));
        }

        /// <summary>A trail that streams from the engine (the nose), behind the propeller.</summary>
        TrailRenderer CreateTrail(string trailName, Material material)
        {
            if (material == null)
                return null;
            var go = new GameObject(trailName);
            var anchor = _health != null && _health.GetPart(PlanePartType.Engine) != null
                ? _health.GetPart(PlanePartType.Engine).transform
                : plane.transform;
            go.transform.SetParent(plane.transform, false);
            go.transform.position = anchor.position;
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = material;
            trail.minVertexDistance = 0.5f;
            trail.emitting = false;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            return trail;
        }

        void SetColor(Renderer r, Color color)
        {
            _block ??= new MaterialPropertyBlock();
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(_block);
        }

        void OnStatsChanged(PlaneController _) => ApplyClassLook();

        void ApplyClassLook()
        {
            var stats = plane.Stats;
            if (stats == null)
                return;
            if (modelRoot != null)
                modelRoot.localScale = Vector3.one * stats.visualScale;

            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, stats.bodyColor);
            foreach (var r in tintedRenderers)
                if (r != null)
                    r.SetPropertyBlock(_block);
        }

        void OnPartDestroyed(PlaneHealth _, PartDamageInfo info)
        {
            if (!info.Part.IsDetached)
                return;

            // The part keeps most of the plane's speed and peels away from the fuselage, then drag and gravity take over.
            var debris = SpawnDebris(info.Part.gameObject, info.Part.transform.position, debrisLifetime);
            Vector3 away = Vector3.ProjectOnPlane(debris.position - plane.transform.position, plane.transform.forward);
            debris.linearVelocity = plane.Velocity * 0.9f + away.normalized * Random.Range(4f, 8f) + Vector3.up * Random.Range(1f, 4f);
            debris.angularVelocity = Random.insideUnitSphere * 5f;
            debris.linearDamping = 0.6f;
            AddSmokeTrail(debris.gameObject);
        }

        void OnCrashed(PlaneController _, Vector3 point)
        {
            Vector3 velocity = plane.Velocity;
            bool shotDown = plane.LastDeathCause == DeathCause.ShotDown;
            _hidden.Clear();

            // Throw a copy of every part still attached, then hide the real plane until respawn.
            // Shot down in the air, the wreck keeps flying along its path, burning, until it hits the ground;
            // a crash into terrain bounces the pieces back off the impact.
            foreach (var part in GetComponentsInChildren<PlanePart>())
            {
                var debris = SpawnDebris(part.gameObject, part.transform.position, shotDown ? wreckLifetime : debrisLifetime);
                Vector3 away = (debris.position - point).normalized;
                if (shotDown)
                {
                    debris.linearVelocity = velocity * 0.7f + away * Random.Range(6f, 14f) + Vector3.up * Random.Range(2f, 8f);
                    debris.angularVelocity = Random.insideUnitSphere * 4f;
                    debris.linearDamping = 0.3f;
                    AddSmokeTrail(debris.gameObject);
                    if (part.partType is PlanePartType.Body or PlanePartType.Engine)
                        AddFireTrail(debris.gameObject);
                }
                else
                {
                    debris.linearVelocity = velocity * 0.35f + away * Random.Range(8f, 20f) + Vector3.up * Random.Range(4f, 12f);
                    debris.angularVelocity = Random.insideUnitSphere * 6f;
                }
            }

            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled)
                    continue;
                r.enabled = false;
                _hidden.Add(r);
            }

            if (shotDown)
                SpawnExplosion(point, velocity);
            else
                SpawnFlash(point, fireMaterial, new Color(1f, 0.55f, 0.1f), 2f, 22f, 0.4f, Vector3.zero);
        }

        void OnRespawned(PlaneController _)
        {
            foreach (var r in _hidden)
                if (r != null)
                    r.enabled = true;
            _hidden.Clear();
        }

        /// <summary>A physics copy of <paramref name="part"/> that despawns after a while. Works on a detached (inactive) part.</summary>
        Rigidbody SpawnDebris(GameObject part, Vector3 position, float lifetime)
        {
            var copy = Instantiate(part, position, part.transform.rotation);
            copy.transform.localScale = part.transform.lossyScale;
            copy.name = part.name + " (Debris)";
            foreach (var c in copy.GetComponentsInChildren<PlanePart>(true))
                Destroy(c);
            // It starts inside the plane, so keep it from shoving against the plane's own hitboxes.
            var planeColliders = plane.GetComponentsInChildren<Collider>();
            foreach (var c in copy.GetComponentsInChildren<Collider>(true))
            {
                c.enabled = true;
                foreach (var own in planeColliders)
                    Physics.IgnoreCollision(c, own);
            }
            copy.SetActive(true);

            copy.AddComponent<PlaneDebris>();
            var body = copy.AddComponent<Rigidbody>();
            body.mass = 50f;
            Destroy(copy, lifetime);
            return body;
        }

        void AddSmokeTrail(GameObject debris)
        {
            if (smokeMaterial == null)
                return;
            var trail = debris.AddComponent<TrailRenderer>();
            trail.sharedMaterial = smokeMaterial;
            trail.time = 1.5f;
            trail.minVertexDistance = 0.5f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.9f), new Keyframe(1f, 2.4f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        void AddFireTrail(GameObject debris)
        {
            if (fireMaterial == null)
                return;
            // A child, since the debris already carries the smoke trail.
            var go = new GameObject("Fire");
            go.transform.SetParent(debris.transform, false);
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = fireMaterial;
            trail.time = 0.35f;
            trail.minVertexDistance = 0.3f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 1.6f), new Keyframe(1f, 0f));
            trail.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>
        /// Placeholder mid-air explosion, big enough to read as a kill from a distance: a white-hot core,
        /// an orange fireball drifting with the wreck, and a dark smoke cloud that hangs for a few seconds.
        /// </summary>
        void SpawnExplosion(Vector3 point, Vector3 velocity)
        {
            // The opaque spheres nest, so the smoke starts small and only swallows the fireball as it fades.
            SpawnFlash(point, smokeMaterial, heavySmokeColor, 2f, 26f, 2.5f, velocity * 0.15f);
            SpawnFlash(point, fireMaterial, new Color(1f, 0.5f, 0.08f), 4f, 24f, 0.7f, velocity * 0.4f);
            SpawnFlash(point, fireMaterial, new Color(1f, 0.95f, 0.75f), 3f, 12f, 0.18f, velocity * 0.5f);
        }

        /// <summary>One expanding sphere. <paramref name="material"/> (unlit) makes it glow; null uses the primitive's default lit look.</summary>
        public static void SpawnFlash(Vector3 point, Material material, Color color, float startSize, float endSize, float duration, Vector3 drift)
        {
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Explosion";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.position = point;
            var r = flash.GetComponent<Renderer>();
            if (material != null)
                r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, color);
            r.SetPropertyBlock(block);
            flash.AddComponent<GrowAndDestroy>().Init(startSize, endSize, duration, drift);
        }

        /// <summary>Placeholder explosion puff: a sphere that swells (fast at first), drifts, and disappears.</summary>
        class GrowAndDestroy : MonoBehaviour
        {
            float _t, _startSize, _endSize, _duration;
            Vector3 _drift;

            public void Init(float startSize, float endSize, float duration, Vector3 drift)
            {
                _startSize = startSize;
                _endSize = endSize;
                _duration = duration;
                _drift = drift;
                transform.localScale = Vector3.one * startSize;
            }

            void Update()
            {
                _t += Time.deltaTime;
                float k = 1f - (1f - _t / _duration) * (1f - _t / _duration); // ease out
                transform.localScale = Vector3.one * Mathf.Lerp(_startSize, _endSize, k);
                transform.position += _drift * Time.deltaTime;
                _drift *= Mathf.Exp(-1.5f * Time.deltaTime);
                if (_t > _duration)
                    Destroy(gameObject);
            }
        }
    }
}

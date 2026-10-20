using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// Wing-mounted unguided rockets. Pressing <see cref="FlightInput.FireRockets"/> launches a salvo from the rails
    /// on intact wings (holding keeps firing salvos); ammo is limited and restocked on respawn or repair.
    /// A rocket damages the part it hits, then explodes and splashes body damage onto every plane near the blast
    /// (<see cref="RocketStats.SplashDamageAt"/>). Rockets that hit the ground or time out still explode.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so rockets leave from this step's pose
    [RequireComponent(typeof(PlaneController))]
    public class PlaneRockets : MonoBehaviour
    {
        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static readonly Dictionary<PlaneHealth, float> SplashTargets = new();
        static readonly List<(GroundTarget target, float distance)> GroundTargets = new();
        static RocketStats _fallbackStats;

        [Tooltip("Rocket launch points. Each sits under the wing PlanePart that carries it; a lost wing loses its rail.")]
        [SerializeField] Transform[] rails;
        [Tooltip("Rocket body (unlit, dark) and its exhaust/blast glow.")]
        [SerializeField] Material rocketMaterial;
        [Tooltip("The rocket model (ArtSource/Weapons); unset flies a placeholder box in rocketMaterial.")]
        [SerializeField] ProjectileVisuals.WeaponModel rocketModel;
        [SerializeField] Material flameMaterial;
        [SerializeField] Material smokeMaterial;

        struct Rocket
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float TimeLeft;
            public Transform Visual;
        }

        PlaneController _plane;
        PlaneHealth _health;
        PlanePart[] _railParts;
        readonly List<Rocket> _rockets = new();
        Transform _visualRoot;
        float _salvoCooldown;
        float _salvoTimer;
        int _salvoLeft;
        int _nextRail;

        /// <summary>Raised once per rocket launched.</summary>
        public event Action<PlaneRockets> Fired;
        /// <summary>Raised when one of our rockets damages another plane (direct hit or splash, once per damage dealt).</summary>
        public event Action<PlaneRockets, PlaneHealth, PartDamageInfo> HitPlane;
        /// <summary>Raised when one of our rockets damages a ground target (an AA gun, a tower).</summary>
        public event Action<PlaneRockets, GroundTarget, float> HitGroundTarget;
        /// <summary>Raised where a rocket explodes.</summary>
        public event Action<PlaneRockets, Vector3> Exploded;

        public PlaneController Plane => _plane;
        public RocketStats Stats => _plane.Stats != null && _plane.Stats.rockets != null ? _plane.Stats.rockets : FallbackStats();
        public int Ammo { get; private set; }
        /// <summary>Whether this plane's class carries rockets (its <see cref="FlightStats.rockets"/> is set). Without them ammo is 0.</summary>
        public bool IsEquipped => _plane.Stats == null || _plane.Stats.rockets != null;
        public int MaxAmmo => IsEquipped ? Stats.ammo : 0;
        /// <summary>Rockets in flight.</summary>
        public int InFlight => _rockets.Count;
        /// <summary>0..1 until the next salvo can start (1 = ready).</summary>
        public float Readiness => 1f - Mathf.Clamp01(_salvoCooldown / Stats.salvoCooldown);
        public int RailCount => rails != null ? rails.Length : 0;

        /// <summary>Rails on wings that haven't been shot off.</summary>
        public int ActiveRailCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < RailCount; i++)
                    if (IsRailActive(i))
                        count++;
                return count;
            }
        }

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
            _railParts = new PlanePart[RailCount];
            for (int i = 0; i < RailCount; i++)
            {
                _railParts[i] = rails[i] != null ? rails[i].GetComponentInParent<PlanePart>() : null;
                if (rails[i] == null)
                    Debug.LogError($"{name}: rocket rail {i} is missing", this);
            }
            _visualRoot = new GameObject($"{name} Rockets").transform;
            Restock();
        }

        void OnEnable()
        {
            _plane.Respawned += OnPlaneReset;
            _plane.StatsChanged += OnPlaneReset;
            if (_health != null)
                _health.Restored += OnRestored;
        }

        void OnDisable()
        {
            _plane.Respawned -= OnPlaneReset;
            _plane.StatsChanged -= OnPlaneReset;
            if (_health != null)
                _health.Restored -= OnRestored;
        }

        void OnDestroy()
        {
            if (_visualRoot != null)
                Destroy(_visualRoot.gameObject);
        }

        void OnPlaneReset(PlaneController _) => Restock();
        void OnRestored(PlaneHealth _) => Restock();

        /// <summary>Full ammo (none if the class has no rockets) and ready to fire. Rockets already in flight keep flying.</summary>
        public void Restock()
        {
            Ammo = MaxAmmo;
            _salvoCooldown = 0f;
            _salvoLeft = 0;
        }

        bool IsRailActive(int i) => rails[i] != null && (_railParts[i] == null || !_railParts[i].IsDestroyed);

        /// <summary>Where to point the nose for a rocket to hit <paramref name="target"/>. False if out of range.</summary>
        public bool TryGetLeadPoint(PlaneController target, out Vector3 leadPoint, out float distance)
        {
            var s = Stats;
            Vector3 targetPos = target.transform.position;
            distance = Vector3.Distance(transform.position, targetPos);
            return Ballistics.TryLeadPoint(transform.position, _plane.Velocity, targetPos, target.Velocity,
                s.rocketSpeed, s.rocketLifetime, out leadPoint, out _);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            StepRockets(dt);
            UpdateTrigger(Stats, dt);
        }

        void UpdateTrigger(RocketStats s, float dt)
        {
            if (_salvoCooldown > 0f)
                _salvoCooldown -= dt;
            if (!_plane.IsAlive)
            {
                _salvoLeft = 0;
                return;
            }

            if (_salvoLeft == 0 && _salvoCooldown <= 0f && _plane.CurrentInput.FireRockets &&
                Ammo > 0 && ActiveRailCount > 0)
            {
                _salvoLeft = Mathf.Min(s.rocketsPerSalvo, Ammo);
                _salvoTimer = 0f;
                _salvoCooldown = s.salvoCooldown;
            }

            if (_salvoLeft > 0)
            {
                _salvoTimer -= dt;
                while (_salvoLeft > 0 && _salvoTimer <= 0f)
                {
                    if (!FireOne(s, -_salvoTimer))
                    {
                        _salvoLeft = 0; // lost the last rail mid-salvo
                        break;
                    }
                    _salvoLeft--;
                    _salvoTimer += s.salvoInterval;
                    if (s.salvoInterval <= 0f)
                        _salvoTimer = 0f;
                }
            }
        }

        bool FireOne(RocketStats s, float age)
        {
            int rail = NextActiveRail();
            if (rail < 0 || Ammo <= 0)
                return false;
            Ammo--;

            // The transform is interpolated, so place the rail on the simulated pose instead.
            Vector3 local = transform.InverseTransformPoint(rails[rail].position);
            Vector3 origin = _plane.Position + _plane.Rotation * local;
            Vector3 dir = _plane.Rotation * Vector3.forward; // rails point straight ahead, parallel to the nose
            if (s.spreadAngle > 0f)
            {
                Vector3 axis = Vector3.Cross(dir, UnityEngine.Random.onUnitSphere);
                if (axis.sqrMagnitude > 1e-4f)
                    dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, s.spreadAngle), axis.normalized) * dir;
            }

            var rocket = new Rocket
            {
                Position = origin,
                Velocity = dir * s.rocketSpeed + _plane.Velocity,
                TimeLeft = s.rocketLifetime,
                Visual = CreateVisual(s, origin, dir),
            };
            Fired?.Invoke(this);

            if (age > 0f && !Step(ref rocket, age))
                DestroyVisual(rocket.Visual);
            else
                _rockets.Add(rocket);
            return true;
        }

        int NextActiveRail()
        {
            for (int n = 0; n < RailCount; n++)
            {
                int i = (_nextRail + n) % RailCount;
                if (IsRailActive(i))
                {
                    _nextRail = i + 1;
                    return i;
                }
            }
            return -1;
        }

        void StepRockets(float dt)
        {
            for (int i = _rockets.Count - 1; i >= 0; i--)
            {
                var r = _rockets[i];
                if (Step(ref r, dt))
                {
                    _rockets[i] = r;
                }
                else
                {
                    DestroyVisual(r.Visual);
                    _rockets.RemoveAt(i);
                }
            }
        }

        /// <summary>Moves one rocket. Returns false once it has exploded.</summary>
        bool Step(ref Rocket r, float dt)
        {
            Vector3 move = r.Velocity * dt;
            float distance = move.magnitude;
            if (distance > 1e-4f && TryHit(r.Position, move / distance, distance, out var hit))
            {
                Explode(hit.point, hit.collider);
                return false;
            }
            r.Position += move;
            r.TimeLeft -= dt;
            if (r.TimeLeft > 0f)
                return true;
            Explode(r.Position, null); // self-destruct at max range
            return false;
        }

        bool TryHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;
            float radius = Stats.rocketRadius;
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform) || PlaneDebris.IsDebris(hit.collider))
                    continue; // our own plane, and falling wreckage rockets fly through
                if (hit.distance <= 0f)
                    hit.point = origin; // started inside it (the target moved into the rocket)
                if (!found || hit.distance < best.distance)
                {
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Direct damage to the part hit (if any), then splash damage to the body of every plane in the blast.</summary>
        void Explode(Vector3 point, Collider directHit)
        {
            var s = Stats;
            PlaneHealth directHealth = null;
            if (directHit != null && PlanePart.TryGetFromCollider(directHit, out var part))
            {
                directHealth = directHit.GetComponentInParent<PlaneHealth>();
                if (directHealth != null)
                    Damage(directHealth, part, s.directDamage, point);
            }

            SplashTargets.Clear();
            if (directHealth != null)
                SplashTargets[directHealth] = 0f;
            Explosions.FindPlanesInRadius(point, s.splashRadius, transform, SplashTargets); // never splash ourselves
            foreach (var (health, distance) in SplashTargets)
                Damage(health, health.GetPart(PlanePartType.Body), s.SplashDamageAt(distance), point);
            SplashTargets.Clear();

            GroundTargets.Clear();
            GroundTarget.FindInRadius(point, s.splashRadius, GroundTargets);
            foreach (var (target, distance) in GroundTargets)
            {
                float taken = target.ApplyDamage(s.GroundDamageAt(distance), point, _plane);
                if (taken > 0f)
                    HitGroundTarget?.Invoke(this, target, taken);
            }
            GroundTargets.Clear();

            Explosions.PlayBlast(point, directHit, s.splashRadius);
            Exploded?.Invoke(this, point);
        }

        void Damage(PlaneHealth health, PlanePart part, float amount, Vector3 point)
        {
            float taken = health.ApplyDamage(part, amount, point, _plane);
            if (taken > 0f)
                HitPlane?.Invoke(this, health, new PartDamageInfo(part, taken, point, _plane));
        }

        // ------------------------------------------------------------------ visuals

        void LateUpdate()
        {
            // Extrapolate from the last physics step so rockets move smoothly every frame.
            float ahead = Time.time - Time.fixedTime;
            foreach (var r in _rockets)
                if (r.Visual != null)
                    r.Visual.SetPositionAndRotation(r.Position + r.Velocity * ahead, Quaternion.LookRotation(r.Velocity));
        }

        Transform CreateVisual(RocketStats s, Vector3 position, Vector3 dir) =>
            ProjectileVisuals.Create("Rocket", _visualRoot, position, dir, s.rocketLength, s.rocketWidth,
                rocketMaterial, flameMaterial, smokeMaterial, smokeTime: 0.6f, smokeEndWidth: 1.6f, rocketModel, puffTrail: 0.7f);

        static void DestroyVisual(Transform visual) => ProjectileVisuals.Release(visual);

        static RocketStats FallbackStats()
        {
            if (_fallbackStats == null)
                _fallbackStats = ScriptableObject.CreateInstance<RocketStats>();
            return _fallbackStats;
        }
    }
}

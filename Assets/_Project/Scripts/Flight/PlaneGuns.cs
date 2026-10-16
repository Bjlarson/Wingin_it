using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Wing-mounted machine guns. Holding <see cref="FlightInput.FireGuns"/> fires straight-flying bullets from every
    /// muzzle whose wing is still intact, so a lost wing halves the firepower. Bullets are simulated in FixedUpdate,
    /// swept as small spheres, and damage the part they hit through <see cref="PlaneHealth"/>.
    /// Ammo is unlimited; holding the trigger builds heat until the guns lock up for a moment.
    /// With a Critical body (<see cref="BodyDamageState"/>) the guns can also jam for a moment while firing.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so shots leave from this step's pose
    [RequireComponent(typeof(PlaneController))]
    public class PlaneGuns : MonoBehaviour
    {
        const int MaxShotsPerStep = 8;
        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static GunStats _fallbackStats;

        [Tooltip("Bullet spawn points. Each sits under the wing PlanePart that carries it; a lost wing silences its guns.")]
        [SerializeField] Transform[] muzzles;
        [SerializeField] Material tracerMaterial;

        struct Bullet
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float TimeLeft;
            public Transform Tracer;
        }

        PlaneController _plane;
        PlaneHealth _health;
        PlanePart[] _muzzleParts;
        readonly List<Bullet> _bullets = new();
        readonly Stack<Transform> _tracerPool = new();
        Transform _tracerRoot;
        float _shotCooldown;
        float _coolDelay;
        float _jamTimeLeft;
        int _nextMuzzle;

        /// <summary>Raised once per bullet fired.</summary>
        public event Action<PlaneGuns> Fired;
        /// <summary>Raised when the guns jam.</summary>
        public event Action<PlaneGuns> Jammed;
        /// <summary>Raised when one of our bullets damages another plane.</summary>
        public event Action<PlaneGuns, PlaneHealth, PartDamageInfo> HitPlane;

        public PlaneController Plane => _plane;
        public GunStats Stats => _plane.Stats != null && _plane.Stats.guns != null ? _plane.Stats.guns : FallbackStats();
        /// <summary>0..1; at 1 the guns overheat.</summary>
        public float Heat { get; private set; }
        public bool IsOverheated { get; private set; }
        /// <summary>Jammed by Critical body damage; clears on its own after <see cref="GunStats.jamDuration"/>.</summary>
        public bool IsJammed => _jamTimeLeft > 0f;
        /// <summary>Trigger held and guns actually shooting.</summary>
        public bool IsFiring { get; private set; }
        public int GunCount => muzzles != null ? muzzles.Length : 0;

        /// <summary>Guns on wings that haven't been shot off.</summary>
        public int ActiveGunCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < GunCount; i++)
                    if (IsGunActive(i))
                        count++;
                return count;
            }
        }

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
            _muzzleParts = new PlanePart[GunCount];
            for (int i = 0; i < GunCount; i++)
            {
                _muzzleParts[i] = muzzles[i] != null ? muzzles[i].GetComponentInParent<PlanePart>() : null;
                if (muzzles[i] == null)
                    Debug.LogError($"{name}: gun {i} has no muzzle", this);
            }
            _tracerRoot = new GameObject($"{name} Bullets").transform;
        }

        void OnEnable()
        {
            _plane.Respawned += OnPlaneReset;
            _plane.StatsChanged += OnPlaneReset;
        }

        void OnDisable()
        {
            _plane.Respawned -= OnPlaneReset;
            _plane.StatsChanged -= OnPlaneReset;
        }

        void OnDestroy()
        {
            if (_tracerRoot != null)
                Destroy(_tracerRoot.gameObject);
        }

        void OnPlaneReset(PlaneController _)
        {
            Heat = 0f;
            IsOverheated = false;
            _shotCooldown = 0f;
            _jamTimeLeft = 0f;
        }

        bool IsGunActive(int i) => muzzles[i] != null && (_muzzleParts[i] == null || !_muzzleParts[i].IsDestroyed);

        /// <summary>
        /// Where to point the nose to hit <paramref name="target"/> (the lead pipper). False if it's out of range
        /// or can't be caught. Uses the interpolated transforms, so it's smooth to draw every frame.
        /// </summary>
        public bool TryGetLeadPoint(PlaneController target, out Vector3 leadPoint, out float distance)
        {
            var s = Stats;
            Vector3 targetPos = target.transform.position;
            distance = Vector3.Distance(transform.position, targetPos);
            return Ballistics.TryLeadPoint(transform.position, _plane.Velocity, targetPos, target.Velocity,
                s.bulletSpeed, s.bulletLifetime, out leadPoint, out _);
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var s = Stats;
            StepBullets(dt);
            UpdateTrigger(s, dt);
        }

        void UpdateTrigger(GunStats s, float dt)
        {
            int guns = ActiveGunCount;
            bool wantsFire = _plane.IsAlive && _plane.CurrentInput.FireGuns;
            if (_jamTimeLeft > 0f)
                _jamTimeLeft -= dt;
            IsFiring = wantsFire && !IsOverheated && !IsJammed && guns > 0;

            if (IsFiring && _health != null && _health.BodyState == BodyDamageState.Critical &&
                UnityEngine.Random.value < s.criticalJamChancePerSecond * dt)
            {
                _jamTimeLeft = s.jamDuration;
                IsFiring = false;
                Jammed?.Invoke(this);
            }

            if (IsFiring)
            {
                Heat = Mathf.Min(1f, Heat + s.heatPerSecond * dt);
                _coolDelay = s.coolDelay;

                float interval = 1f / (s.roundsPerSecondPerGun * guns);
                _shotCooldown -= dt;
                int shots = 0;
                while (_shotCooldown <= 0f && shots++ < MaxShotsPerStep)
                {
                    // Shots fired earlier within this step have already travelled a little.
                    FireOne(s, -_shotCooldown);
                    _shotCooldown += interval;
                }
                if (_shotCooldown < 0f)
                    _shotCooldown = 0f;

                if (Heat >= 1f)
                    IsOverheated = true;
            }
            else
            {
                _shotCooldown = Mathf.Max(0f, _shotCooldown - dt);
                if (_coolDelay > 0f)
                    _coolDelay -= dt;
                else
                    Heat = Mathf.Max(0f, Heat - s.coolPerSecond * dt);
                if (IsOverheated && Heat <= s.overheatRecoverAt)
                    IsOverheated = false;
            }
        }

        void FireOne(GunStats s, float age)
        {
            int muzzle = NextActiveMuzzle();
            if (muzzle < 0)
                return;

            // The transform is interpolated, so place the muzzle on the simulated pose instead.
            Vector3 local = transform.InverseTransformPoint(muzzles[muzzle].position);
            Vector3 origin = _plane.Position + _plane.Rotation * local;
            Vector3 forward = _plane.Rotation * Vector3.forward;

            Vector3 convergence = _plane.Position + forward * s.convergenceDistance;
            Vector3 dir = (convergence - origin).normalized;
            if (s.spreadAngle > 0f)
            {
                Vector3 axis = Vector3.Cross(dir, UnityEngine.Random.onUnitSphere);
                if (axis.sqrMagnitude > 1e-4f)
                    dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, s.spreadAngle), axis.normalized) * dir;
            }

            var bullet = new Bullet
            {
                Position = origin,
                Velocity = dir * s.bulletSpeed + _plane.Velocity,
                TimeLeft = s.bulletLifetime,
                Tracer = GetTracer(s),
            };
            Fired?.Invoke(this);

            if (age > 0f && !Step(ref bullet, age))
                ReleaseTracer(bullet.Tracer);
            else
                _bullets.Add(bullet);
        }

        int NextActiveMuzzle()
        {
            for (int n = 0; n < GunCount; n++)
            {
                int i = (_nextMuzzle + n) % GunCount;
                if (IsGunActive(i))
                {
                    _nextMuzzle = i + 1;
                    return i;
                }
            }
            return -1;
        }

        void StepBullets(float dt)
        {
            for (int i = _bullets.Count - 1; i >= 0; i--)
            {
                var b = _bullets[i];
                if (Step(ref b, dt))
                {
                    _bullets[i] = b;
                }
                else
                {
                    ReleaseTracer(b.Tracer);
                    _bullets.RemoveAt(i);
                }
            }
        }

        /// <summary>Moves one bullet. Returns false once it hits something or expires.</summary>
        bool Step(ref Bullet b, float dt)
        {
            Vector3 move = b.Velocity * dt;
            float distance = move.magnitude;
            if (distance > 1e-4f && TryHit(b.Position, move / distance, distance, out var hit))
            {
                OnBulletHit(hit);
                return false;
            }
            b.Position += move;
            b.TimeLeft -= dt;
            return b.TimeLeft > 0f;
        }

        bool TryHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;
            float radius = Stats.bulletRadius;
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform))
                    continue; // our own plane
                if (hit.distance <= 0f)
                    hit.point = origin; // started inside it (the target moved into the bullet)
                if (!found || hit.distance < best.distance)
                {
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        void OnBulletHit(RaycastHit hit)
        {
            var health = hit.collider.GetComponentInParent<PlaneHealth>();
            if (health != null && PlanePart.TryGetFromCollider(hit.collider, out var part))
            {
                float taken = health.ApplyDamage(part, Stats.damage, hit.point, _plane);
                if (taken > 0f)
                    HitPlane?.Invoke(this, health, new PartDamageInfo(part, taken, hit.point, _plane));
                SpawnSpark(hit.point, 1.2f);
            }
            else
            {
                SpawnSpark(hit.point, 0.6f);
            }
        }

        // ------------------------------------------------------------------ tracers (placeholder visuals)

        void LateUpdate()
        {
            // Extrapolate from the last physics step so tracers move smoothly every frame.
            float ahead = Time.time - Time.fixedTime;
            foreach (var b in _bullets)
            {
                if (b.Tracer == null)
                    continue;
                b.Tracer.SetPositionAndRotation(b.Position + b.Velocity * ahead, Quaternion.LookRotation(b.Velocity));
            }
        }

        Transform GetTracer(GunStats s)
        {
            if (tracerMaterial == null)
                return null;
            Transform t = _tracerPool.Count > 0 ? _tracerPool.Pop() : CreateTracer();
            t.localScale = new Vector3(s.tracerWidth, s.tracerWidth, s.tracerLength);
            t.gameObject.SetActive(true);
            return t;
        }

        Transform CreateTracer()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Tracer";
            DestroyImmediate(go.GetComponent<Collider>()); // never let a plane's sweep hit it
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = tracerMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(_tracerRoot, false);
            return go.transform;
        }

        void ReleaseTracer(Transform tracer)
        {
            if (tracer == null)
                return;
            tracer.gameObject.SetActive(false);
            _tracerPool.Push(tracer);
        }

        void SpawnSpark(Vector3 point, float size)
        {
            if (tracerMaterial == null)
                return;
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = "Bullet Spark";
            DestroyImmediate(go.GetComponent<Collider>()); // never let a plane's sweep hit it
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = tracerMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetPositionAndRotation(point, UnityEngine.Random.rotation);
            go.transform.localScale = Vector3.one * size;
            Destroy(go, 0.08f);
        }

        static GunStats FallbackStats()
        {
            if (_fallbackStats == null)
                _fallbackStats = ScriptableObject.CreateInstance<GunStats>();
            return _fallbackStats;
        }
    }
}

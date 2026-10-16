using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// An anti-aircraft turret guarding a home airfield (its parent <see cref="Airfield"/>), so enemies can't camp
    /// the repair zone. It picks the nearest enemy plane in range and line of sight, swings toward its lead point at a
    /// limited <see cref="AAStats.traverseRate"/>, and after a lock-on delay fires straight-flying tracer shells that
    /// damage whichever part they hit. Spread tightens the longer it tracks the same plane. Can't be destroyed, and
    /// sits idle on neutral or captured (non-home) airfields. Shells pass through planes on its own team.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so it aims at this step's pose
    public class AAGun : MonoBehaviour
    {
        const int MaxShotsPerStep = 4;
        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static readonly List<AAGun> s_all = new();
        static AAStats _fallbackStats;

        [SerializeField] AAStats stats;
        [Tooltip("Turns to aim (yaw and pitch together); the barrels point along its +Z.")]
        [SerializeField] Transform head;
        [Tooltip("Where shells leave from. Defaults to the head.")]
        [SerializeField] Transform muzzle;
        [SerializeField] Material tracerMaterial;

        struct Shell
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float TimeLeft;
            public Transform Tracer;
        }

        Airfield _airfield;
        Quaternion _aim, _previousAim;
        float _retargetTimer;
        float _shotCooldown;
        readonly List<Shell> _shells = new();
        readonly Stack<Transform> _tracerPool = new();
        Transform _tracerRoot;

        public static IReadOnlyList<AAGun> All => s_all;

        /// <summary>Dev only: this plane is treated as an enemy by every AA gun, even its own team's (dev key J).</summary>
        public static PlaneController DevHostilePlane { get; set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DevHostilePlane = null;

        /// <summary>Raised when one of this gun's shells damages a plane.</summary>
        public event Action<AAGun, PlaneHealth, PartDamageInfo> HitPlane;

        public AAStats Stats => stats != null ? stats : FallbackStats();
        public Airfield Airfield => _airfield != null ? _airfield : _airfield = GetComponentInParent<Airfield>();
        public int Team => Airfield != null ? Airfield.Team : Airfield.Neutral;
        /// <summary>Only a team's home airfield is defended.</summary>
        public bool IsArmed => isActiveAndEnabled && Airfield != null && Airfield.IsHome && !Airfield.IsNeutral;
        public PlaneController Target { get; private set; }
        /// <summary>Seconds spent tracking the current target.</summary>
        public float TrackTime { get; private set; }
        public bool IsFiring { get; private set; }
        public Vector3 Pivot => head != null ? head.position : transform.position;
        /// <summary>The direction the barrels point (the simulated aim, not the interpolated head).</summary>
        public Vector3 AimDirection => _aim * Vector3.forward;

        public void Configure(AAStats gunStats, Transform turretHead, Transform shellMuzzle, Material tracer)
        {
            stats = gunStats;
            head = turretHead;
            muzzle = shellMuzzle;
            tracerMaterial = tracer;
        }

        void Awake()
        {
            _aim = _previousAim = head != null ? head.rotation : transform.rotation;
            _tracerRoot = new GameObject($"{name} Shells").transform;
        }

        void OnEnable() => s_all.Add(this);

        void OnDisable()
        {
            s_all.Remove(this);
            Target = null;
            TrackTime = 0f;
            IsFiring = false;
        }

        void OnDestroy()
        {
            if (_tracerRoot != null)
                Destroy(_tracerRoot.gameObject);
        }

        /// <summary>Whether this gun would shoot at <paramref name="plane"/> right now (ignores line of sight).</summary>
        public bool IsHostileTo(PlaneController plane) =>
            plane != null && (plane == DevHostilePlane || AARules.IsHostile(Team, plane.Team));

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var s = Stats;
            StepShells(dt);
            _previousAim = _aim;

            if (!IsArmed)
            {
                Target = null;
                TrackTime = 0f;
                IsFiring = false;
                return;
            }

            if (Target != null && !IsValidTarget(Target, s))
                Target = null;
            if (Target == null)
            {
                IsFiring = false;
                _retargetTimer -= dt;
                if (_retargetTimer > 0f)
                    return;
                _retargetTimer = s.retargetInterval;
                Target = FindTarget(s);
                TrackTime = 0f;
                if (Target == null)
                    return;
            }

            Vector3 origin = MuzzlePosition();
            if (!Ballistics.TryLeadPoint(origin, Vector3.zero, Target.Position, Target.Velocity, s.shellSpeed, s.shellLifetime,
                    out Vector3 lead, out _))
                lead = Target.Position;
            Vector3 toLead = lead - Pivot;
            if (toLead.sqrMagnitude > 1e-4f)
            {
                var desired = Quaternion.LookRotation(toLead);
                _aim = Quaternion.RotateTowards(_aim, desired, s.traverseRate * dt);
            }
            TrackTime += dt;
            float error = Vector3.Angle(AimDirection, toLead);
            IsFiring = AARules.CanFire(TrackTime, error, s);

            if (IsFiring)
            {
                float interval = 1f / s.roundsPerSecond;
                _shotCooldown -= dt;
                int shots = 0;
                while (_shotCooldown <= 0f && shots++ < MaxShotsPerStep)
                {
                    FireOne(s, -_shotCooldown);
                    _shotCooldown += interval;
                }
            }
            else
            {
                _shotCooldown = Mathf.Max(0f, _shotCooldown - dt);
            }
        }

        PlaneController FindTarget(AAStats s)
        {
            PlaneController best = null;
            float bestSqr = float.MaxValue;
            foreach (var plane in PlaneController.All)
            {
                if (!IsValidTarget(plane, s))
                    continue;
                float sqr = (plane.Position - Pivot).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    best = plane;
                }
            }
            return best;
        }

        bool IsValidTarget(PlaneController plane, AAStats s)
        {
            if (plane == null || !plane.isActiveAndEnabled || !plane.IsAlive || !IsHostileTo(plane))
                return false;
            if ((plane.Position - Pivot).sqrMagnitude > s.range * s.range)
                return false;
            var health = plane.GetComponent<PlaneHealth>();
            if (health != null && health.IsInvulnerable)
                return false; // nothing to gain
            return HasLineOfSight(plane);
        }

        /// <summary>Terrain, buildings and thick cloud block the view; planes don't.</summary>
        bool HasLineOfSight(PlaneController plane)
        {
            Vector3 from = Pivot;
            Vector3 to = plane.Position - from;
            float distance = to.magnitude;
            if (distance < 1e-3f)
                return true;
            if (CloudCover.Blocks(from, plane.Position))
                return false;
            int count = Physics.RaycastNonAlloc(from, to / distance, HitBuffer, distance, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var c = HitBuffer[i].collider;
                if (c.transform.IsChildOf(transform) || c.GetComponentInParent<PlaneController>() != null)
                    continue;
                return false;
            }
            return true;
        }

        Vector3 MuzzlePosition() => muzzle != null
            ? Pivot + _aim * Quaternion.Inverse(head != null ? head.rotation : transform.rotation) * (muzzle.position - Pivot)
            : Pivot;

        void FireOne(AAStats s, float age)
        {
            Vector3 dir = AimDirection;
            float spread = AARules.Spread(TrackTime, s);
            if (spread > 0f)
            {
                Vector3 axis = Vector3.Cross(dir, UnityEngine.Random.onUnitSphere);
                if (axis.sqrMagnitude > 1e-4f)
                    dir = Quaternion.AngleAxis(UnityEngine.Random.Range(0f, spread), axis.normalized) * dir;
            }

            var shell = new Shell
            {
                Position = MuzzlePosition(),
                Velocity = dir * s.shellSpeed,
                TimeLeft = s.shellLifetime,
                Tracer = GetTracer(s),
            };
            if (age > 0f && !Step(ref shell, age))
                ReleaseTracer(shell.Tracer);
            else
                _shells.Add(shell);
        }

        void StepShells(float dt)
        {
            for (int i = _shells.Count - 1; i >= 0; i--)
            {
                var shell = _shells[i];
                if (Step(ref shell, dt))
                {
                    _shells[i] = shell;
                }
                else
                {
                    ReleaseTracer(shell.Tracer);
                    _shells.RemoveAt(i);
                }
            }
        }

        /// <summary>Moves one shell. Returns false once it hits something or expires.</summary>
        bool Step(ref Shell shell, float dt)
        {
            Vector3 move = shell.Velocity * dt;
            float distance = move.magnitude;
            if (distance > 1e-4f && TryHit(shell.Position, move / distance, distance, out var hit))
            {
                OnShellHit(hit);
                return false;
            }
            shell.Position += move;
            shell.TimeLeft -= dt;
            return shell.TimeLeft > 0f;
        }

        bool TryHit(Vector3 origin, Vector3 dir, float distance, out RaycastHit best)
        {
            best = default;
            float radius = Stats.shellRadius;
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform))
                    continue; // our own turret
                var plane = hit.collider.GetComponentInParent<PlaneController>();
                if (plane != null && !IsHostileTo(plane))
                    continue; // friendly planes fly through our fire
                if (hit.distance <= 0f)
                    hit.point = origin;
                if (!found || hit.distance < best.distance)
                {
                    best = hit;
                    found = true;
                }
            }
            return found;
        }

        void OnShellHit(RaycastHit hit)
        {
            var health = hit.collider.GetComponentInParent<PlaneHealth>();
            if (health != null && PlanePart.TryGetFromCollider(hit.collider, out var part))
            {
                float taken = health.ApplyDamage(part, Stats.damage, hit.point, this);
                if (taken > 0f)
                    HitPlane?.Invoke(this, health, new PartDamageInfo(part, taken, hit.point, this));
                SpawnBurst(hit.point, 2f);
            }
            else
            {
                SpawnBurst(hit.point, 1f);
            }
        }

        // ------------------------------------------------------------------ visuals (placeholder)

        void LateUpdate()
        {
            // Interpolate the head between physics steps so the swing looks smooth.
            if (head != null)
            {
                float t = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
                head.rotation = Quaternion.Slerp(_previousAim, _aim, t);
            }
            float ahead = Time.time - Time.fixedTime;
            foreach (var shell in _shells)
                if (shell.Tracer != null)
                    shell.Tracer.SetPositionAndRotation(shell.Position + shell.Velocity * ahead, Quaternion.LookRotation(shell.Velocity));
        }

        Transform GetTracer(AAStats s)
        {
            if (tracerMaterial == null)
                return null;
            Transform t = _tracerPool.Count > 0 ? _tracerPool.Pop() : CreateCube("Shell Tracer", _tracerRoot);
            t.localScale = new Vector3(s.tracerWidth, s.tracerWidth, s.tracerLength);
            t.gameObject.SetActive(true);
            return t;
        }

        void ReleaseTracer(Transform tracer)
        {
            if (tracer == null)
                return;
            tracer.gameObject.SetActive(false);
            _tracerPool.Push(tracer);
        }

        void SpawnBurst(Vector3 point, float size)
        {
            if (tracerMaterial == null)
                return;
            var t = CreateCube("Shell Burst", null);
            t.SetPositionAndRotation(point, UnityEngine.Random.rotation);
            t.localScale = Vector3.one * size;
            Destroy(t.gameObject, 0.1f);
        }

        Transform CreateCube(string cubeName, Transform parent)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = cubeName;
            DestroyImmediate(go.GetComponent<Collider>()); // never let a sweep hit it
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = tracerMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            go.transform.SetParent(parent, false);
            return go.transform;
        }

        static AAStats FallbackStats()
        {
            if (_fallbackStats == null)
                _fallbackStats = ScriptableObject.CreateInstance<AAStats>();
            return _fallbackStats;
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.5f);
            Gizmos.DrawWireSphere(Pivot, Stats.range);
        }
    }
}

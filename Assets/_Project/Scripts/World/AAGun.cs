using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Effects;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// An anti-aircraft turret guarding a home airfield (its parent <see cref="Airfield"/>), so enemies can't camp
    /// the repair zone. It picks the nearest enemy plane in range and line of sight, swings toward its lead point at a
    /// limited <see cref="AAStats.traverseRate"/>, and after a lock-on delay fires straight-flying tracer shells that
    /// damage whichever part they hit. Spread tightens the longer it tracks the same plane. Shells pass through planes on
    /// its own team. A home field's guns can't be destroyed. A gun with its own <see cref="GroundTarget"/> is a capture
    /// field's light AA (<see cref="FieldDefenses"/>): it fires only while it's intact, its field is owned and the
    /// field's tower stands. Any other gun on a non-home field sits idle.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so it aims at this step's pose
    public class AAGun : MonoBehaviour
    {
        const int MaxShotsPerStep = 4;
        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static readonly List<AAGun> s_all = new();
        static AAStats _fallbackStats;

        [SerializeField] AAStats stats;
        [Tooltip("Turns to aim (yaw and pitch together, unless there's a cradle); the barrels point along its +Z.")]
        [SerializeField] Transform head;
        [Tooltip("Optional: tilts (pitch) inside the head, which then only turns (yaw). The barrels point along its +Z.")]
        [SerializeField] Transform cradle;
        [Tooltip("Optional: the left and right barrels, which kick back along their parent's -Z on alternate shots.")]
        [SerializeField] Transform[] barrels = { };
        [SerializeField, Min(0f)] float recoilDistance = 0.35f;
        [Tooltip("Seconds for a barrel to run back out after a shot.")]
        [SerializeField, Min(0.01f)] float recoilReturn = 0.12f;
        [Tooltip("Where shells leave from. Defaults to the head.")]
        [SerializeField] Transform muzzle;
        [Tooltip("The placeholder tracer box, when there's no shell model.")]
        [SerializeField] Material tracerMaterial;
        [Tooltip("The flak shell model (ArtSource/Weapons), a unit box scaled by AAStats' tracer width and length.")]
        [SerializeField] ProjectileVisuals.WeaponModel shellModel;
        [Tooltip("Shown at the muzzle for a moment per shot, on alternate barrels. Unset: no flash.")]
        [SerializeField] ProjectileVisuals.WeaponModel muzzleFlashModel;
        [SerializeField, Min(0f)] float muzzleFlashSize = 2f;
        [Tooltip("Distance between the two barrels; the flashes alternate either side of the muzzle.")]
        [SerializeField, Min(0f)] float barrelSpacing = 0.9f;

        struct Shell
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float TimeLeft;
            public Transform Tracer;
        }

        Airfield _airfield;
        GroundTarget _body;
        Quaternion _aim, _previousAim;
        float _retargetTimer;
        float _shotCooldown;
        readonly List<Shell> _shells = new();
        readonly Stack<Transform> _tracerPool = new();
        Transform _tracerRoot;
        ProjectileVisuals.MuzzleFlash[] _flashes;
        int _nextBarrel;
        Vector3[] _barrelRest;
        float[] _recoil;

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
        /// <summary>The gun's own destructible body, on a capture field's light AA; null on a home field's guns.</summary>
        public GroundTarget Body => _body;
        /// <summary>
        /// A team's home airfield is defended while it's open. A capture field's light AA also needs the field owned,
        /// its tower standing and the gun itself in one piece (and switched on for the match).
        /// </summary>
        public bool IsArmed => isActiveAndEnabled && Airfield != null && Airfield.IsOpen && !Airfield.IsNeutral && !Airfield.TowerDown
            && (_body == null ? Airfield.IsHome : _body.isActiveAndEnabled && !_body.IsDestroyed);
        public PlaneController Target { get; private set; }
        /// <summary>Seconds spent tracking the current target.</summary>
        public float TrackTime { get; private set; }
        public bool IsFiring { get; private set; }
        public Vector3 Pivot => head != null ? head.position : transform.position;
        /// <summary>What points the barrels: the cradle, or the head without one.</summary>
        Transform Aimed => cradle != null ? cradle : head;
        /// <summary>The direction the barrels point (the simulated aim, not the interpolated head).</summary>
        public Vector3 AimDirection => _aim * Vector3.forward;

        public void Configure(AAStats gunStats, Transform turretHead, Transform shellMuzzle, Material tracer)
        {
            stats = gunStats;
            head = turretHead;
            muzzle = shellMuzzle;
            tracerMaterial = tracer;
        }

        /// <summary>Swaps the gun's stats (Domination's light AA on a capture field).</summary>
        public void SetStats(AAStats gunStats) => stats = gunStats;

        /// <summary>
        /// A modelled turret:<see cref="head"/> only turns, <paramref name="pitchCradle"/> tilts in it, and
        /// <paramref name="recoilBarrels"/> (left, right) kick back on alternate shots.
        /// </summary>
        public void SetMount(Transform pitchCradle, Transform[] recoilBarrels)
        {
            cradle = pitchCradle;
            barrels = recoilBarrels ?? new Transform[0];
        }

        void Awake()
        {
            _body = GetComponent<GroundTarget>();
            _aim = _previousAim = Aimed != null ? Aimed.rotation : transform.rotation;
            _barrelRest = new Vector3[barrels.Length];
            _recoil = new float[barrels.Length];
            for (int i = 0; i < barrels.Length; i++)
                _barrelRest[i] = barrels[i] != null ? barrels[i].localPosition : Vector3.zero;
            _tracerRoot = new GameObject($"{name} Shells").transform;
            var flashParent = muzzle != null ? muzzle : head;
            _flashes = new[]
            {
                ProjectileVisuals.CreateFlash(flashParent, muzzleFlashModel, Vector3.left * (barrelSpacing * 0.5f), muzzleFlashSize),
                ProjectileVisuals.CreateFlash(flashParent, muzzleFlashModel, Vector3.right * (barrelSpacing * 0.5f), muzzleFlashSize),
            };
        }

        void OnEnable() => s_all.Add(this);

        void OnDisable()
        {
            s_all.Remove(this);
            Target = null;
            TrackTime = 0f;
            IsFiring = false;
            foreach (var flash in _flashes ?? System.Array.Empty<ProjectileVisuals.MuzzleFlash>())
                flash?.Hide();
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
            ? Pivot + _aim * Quaternion.Inverse(Aimed != null ? Aimed.rotation : transform.rotation) * (muzzle.position - Pivot)
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
            _flashes[_nextBarrel]?.Show();
            if (_nextBarrel < _recoil.Length)
                _recoil[_nextBarrel] = 1f;
            _nextBarrel = 1 - _nextBarrel;
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
                Vfx.Play(VfxKind.FlakBurst, hit.point);
            }
            else
            {
                Explosions.PlayBulletHit(hit, null, 2f);
            }
        }

        // ------------------------------------------------------------------ visuals

        void LateUpdate()
        {
            foreach (var flash in _flashes)
                flash?.Tick(Time.deltaTime);
            // Interpolate the head between physics steps so the swing looks smooth.
            if (head != null)
            {
                float t = Time.fixedDeltaTime > 0f ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime) : 1f;
                Quaternion shown = Quaternion.Slerp(_previousAim, _aim, t);
                if (cradle == null)
                {
                    head.rotation = shown;
                }
                else
                {
                    // The head turns flat about the mount's up; the cradle takes the elevation.
                    Vector3 forward = shown * Vector3.forward, up = transform.up;
                    Vector3 flat = Vector3.ProjectOnPlane(forward, up);
                    if (flat.sqrMagnitude > 1e-6f)
                        head.rotation = Quaternion.LookRotation(flat, up);
                    cradle.rotation = Quaternion.LookRotation(forward, head.up);
                }
            }
            for (int i = 0; i < barrels.Length; i++)
            {
                if (barrels[i] == null)
                    continue;
                _recoil[i] = Mathf.MoveTowards(_recoil[i], 0f, Time.deltaTime / recoilReturn);
                barrels[i].localPosition = _barrelRest[i] - Vector3.forward * (recoilDistance * _recoil[i] * _recoil[i]);
            }
            float ahead = Time.time - Time.fixedTime;
            foreach (var shell in _shells)
                if (shell.Tracer != null)
                    shell.Tracer.SetPositionAndRotation(shell.Position + shell.Velocity * ahead, Quaternion.LookRotation(shell.Velocity));
        }

        Transform GetTracer(AAStats s)
        {
            if (tracerMaterial == null && !shellModel.IsSet)
                return null;
            Transform t = _tracerPool.Count > 0 ? _tracerPool.Pop()
                : shellModel.IsSet ? ProjectileVisuals.MeshObject("Shell Tracer", _tracerRoot, shellModel).transform
                : CreateCube("Shell Tracer", _tracerRoot);
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

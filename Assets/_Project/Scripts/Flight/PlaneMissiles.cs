using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>What a plane's missile warning shows: who is locking it and the nearest missile chasing it.</summary>
    public readonly struct MissileThreat
    {
        /// <summary>Someone is building a lock on this plane.</summary>
        public readonly bool BeingLocked;
        /// <summary>Someone has a full lock and can fire.</summary>
        public readonly bool Locked;
        /// <summary>Missiles in flight still tracking this plane.</summary>
        public readonly int Incoming;
        public readonly Vector3 NearestPosition;
        public readonly float NearestDistance;
        /// <summary>0..1 how close a hard turn is to shaking off the nearest missile.</summary>
        public readonly float EvadeProgress;

        public MissileThreat(bool beingLocked, bool locked, int incoming, Vector3 nearestPosition, float nearestDistance,
            float evadeProgress)
        {
            BeingLocked = beingLocked;
            Locked = locked;
            Incoming = incoming;
            NearestPosition = nearestPosition;
            NearestDistance = nearestDistance;
            EvadeProgress = evadeProgress;
        }
    }

    /// <summary>
    /// Lock-on homing missiles. Keeping another plane near the nose (<see cref="MissileStats.lockConeAngle"/>) for
    /// <see cref="MissileStats.lockTime"/> locks it; it can then drift out to <see cref="MissileStats.lockHoldAngle"/>
    /// before the lock breaks. Terrain between the planes also breaks it. Pressing
    /// <see cref="FlightInput.FireMissile"/> while locked launches one missile. The missile steers toward its target
    /// at a limited turn rate. Turning hard for <see cref="MissileStats.evadeTime"/> once it is within
    /// <see cref="MissileStats.evadeRange"/>, or getting outside its seeker cone, shakes it off for good. Missiles explode on contact, on passing close to their target, or at max range,
    /// damaging the part hit and splashing the body of every plane near the blast.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so missiles leave from this step's pose
    [RequireComponent(typeof(PlaneController))]
    public class PlaneMissiles : MonoBehaviour
    {
        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static readonly Dictionary<PlaneHealth, float> SplashTargets = new();
        static readonly List<PlaneMissiles> _all = new();
        static MissileStats _fallbackStats;

        [Tooltip("Where missiles leave from: a pylon under the fuselage (the body never detaches).")]
        [SerializeField] Transform launchPoint;
        [Tooltip("Missile body (unlit, dark) and its exhaust/blast glow.")]
        [SerializeField] Material missileMaterial;
        [Tooltip("The missile model (ArtSource/Weapons); unset flies a placeholder box in missileMaterial.")]
        [SerializeField] ProjectileVisuals.WeaponModel missileModel;
        [SerializeField] Material flameMaterial;
        [SerializeField] Material smokeMaterial;

        class Missile
        {
            public Vector3 Position;
            public Vector3 Direction;
            public float Speed;
            public float Age;
            /// <summary>Null once the seeker has lost it (or it was never guided).</summary>
            public PlaneController Target;
            /// <summary>The target's flight direction last step, to measure how hard it's turning.</summary>
            public Vector3 TargetHeading;
            /// <summary>Seconds of hard turning by the target so far (see <see cref="MissileStats.evadeTime"/>).</summary>
            public float EvadeTime;
            public Transform Visual;
        }

        PlaneController _plane;
        PlaneHealth _health;
        readonly List<Missile> _missiles = new();
        Transform _visualRoot;
        float _cooldown;
        bool _triggerLatched;

        /// <summary>Raised once per missile launched, with its target.</summary>
        public event Action<PlaneMissiles, PlaneController> Fired;
        /// <summary>Raised when one of our missiles damages another plane (direct hit or splash, once per damage dealt).</summary>
        public event Action<PlaneMissiles, PlaneHealth, PartDamageInfo> HitPlane;
        /// <summary>Raised where a missile explodes.</summary>
        public event Action<PlaneMissiles, Vector3> Exploded;
        /// <summary>Raised when a missile's target slips out of its seeker (a dodge), with the plane that got away.</summary>
        public event Action<PlaneMissiles, PlaneController> Evaded;

        /// <summary>Every enabled launcher, for missile warnings.</summary>
        public static IReadOnlyList<PlaneMissiles> All => _all;

        public PlaneController Plane => _plane;
        public MissileStats Stats => _plane.Stats != null && _plane.Stats.missiles != null ? _plane.Stats.missiles : FallbackStats();
        public int Ammo { get; private set; }
        /// <summary>Whether this plane's class carries missiles (its <see cref="FlightStats.missiles"/> is set). Without them ammo is 0.</summary>
        public bool IsEquipped => _plane.Stats == null || _plane.Stats.missiles != null;
        public int MaxAmmo => IsEquipped ? Stats.ammo : 0;
        /// <summary>Missiles in flight.</summary>
        public int InFlight => _missiles.Count;
        /// <summary>0..1 until the next missile can launch (1 = ready).</summary>
        public float Readiness => 1f - Mathf.Clamp01(_cooldown / Stats.launchCooldown);
        /// <summary>The plane we're locking or have locked, or null.</summary>
        public PlaneController LockTarget { get; private set; }
        /// <summary>0..1 lock progress on <see cref="LockTarget"/> (1 = locked).</summary>
        public float LockProgress { get; private set; }
        public bool IsLocked => LockTarget != null && LockProgress >= 1f;

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
            if (launchPoint == null)
                Debug.LogError($"{name}: missile launch point is missing", this);
            _visualRoot = new GameObject($"{name} Missiles").transform;
            Restock();
        }

        void OnEnable()
        {
            _all.Add(this);
            _plane.Respawned += OnPlaneReset;
            _plane.StatsChanged += OnPlaneReset;
            if (_health != null)
                _health.Restored += OnRestored;
        }

        void OnDisable()
        {
            _all.Remove(this);
            _plane.Respawned -= OnPlaneReset;
            _plane.StatsChanged -= OnPlaneReset;
            if (_health != null)
                _health.Restored -= OnRestored;
            ClearLock();
        }

        void OnDestroy()
        {
            if (_visualRoot != null)
                Destroy(_visualRoot.gameObject);
        }

        void OnPlaneReset(PlaneController _) => Restock();
        void OnRestored(PlaneHealth _) => Restock();

        /// <summary>Full ammo (none if the class has no missiles) and ready to fire. Missiles already in flight keep flying.</summary>
        public void Restock()
        {
            Ammo = MaxAmmo;
            _cooldown = 0f;
        }

        /// <summary>
        /// Warnings for <paramref name="plane"/>: whether anyone is locking it and the nearest missile tracking it.
        /// </summary>
        public static MissileThreat GetThreat(PlaneController plane)
        {
            bool locking = false, locked = false;
            int incoming = 0;
            Vector3 nearest = Vector3.zero;
            float nearestDistance = float.MaxValue, evade = 0f;
            foreach (var launcher in _all)
            {
                if (launcher._plane == plane)
                    continue;
                if (launcher.LockTarget == plane)
                {
                    if (launcher.IsLocked)
                        locked = true;
                    else
                        locking = true;
                }
                foreach (var m in launcher._missiles)
                {
                    if (m.Target != plane)
                        continue;
                    incoming++;
                    float d = Vector3.Distance(m.Position, plane.Position);
                    if (d < nearestDistance)
                    {
                        nearestDistance = d;
                        nearest = m.Position;
                        evade = m.EvadeTime / launcher.Stats.evadeTime;
                    }
                }
            }
            return new MissileThreat(locking, locked, incoming, nearest, incoming > 0 ? nearestDistance : 0f,
                Mathf.Clamp01(evade));
        }

        /// <summary>
        /// Shakes <paramref name="target"/> free (Barrel Roll): every lock on it starts over from zero and every
        /// missile chasing it loses it for good. Returns how many missiles were shaken off.
        /// </summary>
        public static int BreakLocksOn(PlaneController target)
        {
            int shaken = 0;
            foreach (var launcher in _all)
            {
                if (launcher.LockTarget == target)
                    launcher.LockProgress = 0f;
                foreach (var m in launcher._missiles)
                {
                    if (m.Target != target)
                        continue;
                    m.Target = null;
                    shaken++;
                    launcher.Evaded?.Invoke(launcher, target);
                }
            }
            return shaken;
        }

        /// <summary>
        /// Prototype/test hook: launches a missile straight at <paramref name="target"/>, skipping the lock and the
        /// launch cooldown. Still uses ammo. False if dead or empty.
        /// </summary>
        public bool LaunchAt(PlaneController target)
        {
            if (!_plane.IsAlive || Ammo <= 0 || target == null)
                return false;
            Vector3 origin = LaunchOrigin();
            Vector3 dir = target.Position - origin;
            Launch(target, dir.sqrMagnitude > 1e-4f ? dir.normalized : _plane.Rotation * Vector3.forward);
            return true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            var s = Stats;
            StepMissiles(s, dt);
            UpdateLock(s, dt);
            UpdateTrigger(s, dt);
        }

        // ------------------------------------------------------------------ lock-on

        void UpdateLock(MissileStats s, float dt)
        {
            if (!_plane.IsAlive || Ammo <= 0)
            {
                ClearLock(); // no lock (and no warning for the target) with nothing to fire
                return;
            }

            Vector3 position = _plane.Position;
            Vector3 nose = _plane.Rotation * Vector3.forward;
            if (CanTrack(LockTarget, position, nose, s.lockHoldAngle, s))
            {
                LockProgress = Mathf.Min(1f, LockProgress + dt / s.lockTime);
                return;
            }

            LockTarget = FindLockCandidate(position, nose, s);
            LockProgress = 0f;
        }

        void ClearLock()
        {
            LockTarget = null;
            LockProgress = 0f;
        }

        PlaneController FindLockCandidate(Vector3 position, Vector3 nose, MissileStats s)
        {
            PlaneController best = null;
            float bestAngle = float.MaxValue;
            foreach (var other in PlaneController.All)
            {
                if (other == _plane || !other.IsAlive)
                    continue;
                float angle = Vector3.Angle(nose, other.Position - position);
                if (angle < bestAngle && CanTrack(other, position, nose, s.lockConeAngle, s))
                {
                    best = other;
                    bestAngle = angle;
                }
            }
            return best;
        }

        bool CanTrack(PlaneController target, Vector3 position, Vector3 nose, float coneAngle, MissileStats s)
        {
            if (target == null || !target.isActiveAndEnabled || !target.IsAlive)
                return false;
            Vector3 targetPos = target.Position;
            return (targetPos - position).sqrMagnitude <= s.lockRange * s.lockRange &&
                   MissileGuidance.InCone(position, nose, targetPos, coneAngle) &&
                   HasLineOfSight(position, targetPos);
        }

        /// <summary>False if terrain, a building or thick cloud is between the two points. Planes and debris don't block.</summary>
        static bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            Vector3 delta = to - from;
            float distance = delta.magnitude;
            if (distance < 1e-3f)
                return true;
            if (CloudCover.Blocks(from, to))
                return false;
            int count = Physics.RaycastNonAlloc(from, delta / distance, HitBuffer, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var col = HitBuffer[i].collider;
                if (!PlanePart.TryGetFromCollider(col, out _) && !PlaneDebris.IsDebris(col))
                    return false;
            }
            return true;
        }

        // ------------------------------------------------------------------ launching

        void UpdateTrigger(MissileStats s, float dt)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;

            // One missile per press: the trigger has to be released before the next launch.
            bool held = _plane.IsAlive && _plane.CurrentInput.FireMissile;
            bool pressed = held && !_triggerLatched;
            _triggerLatched = held;
            if (pressed && IsLocked && Ammo > 0 && _cooldown <= 0f)
                Launch(LockTarget, _plane.Rotation * Vector3.forward);
        }

        Vector3 LaunchOrigin()
        {
            if (launchPoint == null)
                return _plane.Position;
            // The transform is interpolated, so place the pylon on the simulated pose instead.
            Vector3 local = transform.InverseTransformPoint(launchPoint.position);
            return _plane.Position + _plane.Rotation * local;
        }

        void Launch(PlaneController target, Vector3 dir)
        {
            var s = Stats;
            Ammo--;
            _cooldown = s.launchCooldown;
            Vector3 origin = LaunchOrigin();
            _missiles.Add(new Missile
            {
                Position = origin,
                Direction = dir,
                Speed = Mathf.Max(_plane.Speed, 1f),
                Target = target,
                Visual = ProjectileVisuals.Create("Missile", _visualRoot, origin, dir, s.missileLength, s.missileWidth,
                    missileMaterial, flameMaterial, smokeMaterial, smokeTime: 1.5f, smokeEndWidth: 2.2f, missileModel, puffTrail: 1f),
            });
            Fired?.Invoke(this, target);
        }

        // ------------------------------------------------------------------ flight

        void StepMissiles(MissileStats s, float dt)
        {
            for (int i = _missiles.Count - 1; i >= 0; i--)
            {
                var m = _missiles[i];
                if (Step(m, s, dt))
                    continue;
                ProjectileVisuals.Release(m.Visual);
                _missiles.RemoveAt(i);
            }
        }

        /// <summary>Moves one missile. Returns false once it has exploded.</summary>
        bool Step(Missile m, MissileStats s, float dt)
        {
            m.Age += dt;
            bool armed = m.Age >= s.armDelay;
            var target = m.Target;
            if (target != null && (!target.isActiveAndEnabled || !target.IsAlive))
                target = m.Target = null; // already dead: fly on
            if (target != null && armed)
            {
                // The seeker also loses a target that hides behind or inside thick cloud.
                if (MissileGuidance.InCone(m.Position, m.Direction, target.Position, s.seekerAngle) &&
                    CloudCover.CanSee(m.Position, target.Position) && !Evading(m, target, s, dt))
                {
                    m.Direction = MissileGuidance.Steer(m.Direction, m.Position, m.Speed, target.Position,
                        target.Velocity, s.turnRate, dt);
                }
                else
                {
                    m.Target = null; // shaken off: lost for good
                    Evaded?.Invoke(this, target);
                    target = null;
                }
            }

            m.Speed = Mathf.MoveTowards(m.Speed, s.missileSpeed, s.acceleration * dt);
            Vector3 move = m.Direction * (m.Speed * dt);
            float distance = move.magnitude;
            if (distance > 1e-4f && TryHit(m.Position, m.Direction, distance, s.missileRadius, out var hit))
            {
                Explode(hit.point, hit.collider);
                return false;
            }

            // Proximity fuse: did we pass close to the target during this step (both of us moving)?
            if (target != null && armed && s.proximityRadius > 0f)
            {
                Vector3 targetEnd = target.Position;
                Vector3 targetStart = targetEnd - target.Velocity * dt;
                float miss = MissileGuidance.DistanceToSegment(Vector3.zero, m.Position - targetStart,
                    m.Position + move - targetEnd, out float t);
                if (miss <= s.proximityRadius)
                {
                    // Blow up where the missile was at the closest moment.
                    Explode(m.Position + move * t, null);
                    return false;
                }
            }

            m.Position += move;
            if (m.Age < s.lifetime)
                return true;
            Explode(m.Position, null); // self-destruct at max range
            return false;
        }

        /// <summary>
        /// Tracks how long the target has been turning hard while the missile is close. True once that's long
        /// enough to break the lock.
        /// </summary>
        static bool Evading(Missile m, PlaneController target, MissileStats s, float dt)
        {
            Vector3 velocity = target.Velocity;
            if (velocity.sqrMagnitude < 1f)
                return false;
            Vector3 heading = velocity.normalized;
            if (m.TargetHeading != Vector3.zero)
            {
                float turnRate = Vector3.Angle(m.TargetHeading, heading) / dt;
                bool close = (target.Position - m.Position).sqrMagnitude <= s.evadeRange * s.evadeRange;
                m.EvadeTime = turnRate >= s.evadeTurnRate && close ? m.EvadeTime + dt : Mathf.Max(0f, m.EvadeTime - dt);
            }
            m.TargetHeading = heading;
            return m.EvadeTime >= s.evadeTime;
        }

        bool TryHit(Vector3 origin, Vector3 dir, float distance, float radius, out RaycastHit best)
        {
            best = default;
            int count = radius > 0f
                ? Physics.SphereCastNonAlloc(origin, radius, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(origin, dir, HitBuffer, distance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);

            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = HitBuffer[i];
                if (hit.collider.transform.IsChildOf(transform) || PlaneDebris.IsDebris(hit.collider))
                    continue; // our own plane, and falling wreckage missiles fly through
                if (hit.distance <= 0f)
                    hit.point = origin; // started inside it (the target moved into the missile)
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
            // Extrapolate from the last physics step so missiles move smoothly every frame.
            float ahead = Time.time - Time.fixedTime;
            foreach (var m in _missiles)
                if (m.Visual != null)
                    m.Visual.SetPositionAndRotation(m.Position + m.Direction * (m.Speed * ahead), Quaternion.LookRotation(m.Direction));
        }

        static MissileStats FallbackStats()
        {
            if (_fallbackStats == null)
                _fallbackStats = ScriptableObject.CreateInstance<MissileStats>();
            return _fallbackStats;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// Free-fall bombs from a bay under the fuselage. Pressing <see cref="FlightInput.DropBomb"/> releases one bomb
    /// (holding keeps dropping one per <see cref="BombStats.dropInterval"/>). A bomb keeps the plane's velocity and
    /// falls under gravity until it hits something, then explodes: heavy damage to <see cref="GroundTarget"/>s in the
    /// blast, a little to planes. <see cref="TryPredictImpact"/> runs the same trajectory for the HUD's impact marker.
    /// </summary>
    [DefaultExecutionOrder(10)] // after PlaneController, so bombs leave from this step's pose
    [RequireComponent(typeof(PlaneController))]
    public class PlaneBombs : MonoBehaviour
    {
        static readonly RaycastHit[] HitBuffer = new RaycastHit[16];
        static readonly Dictionary<PlaneHealth, float> SplashTargets = new();
        static readonly List<(GroundTarget target, float distance)> GroundTargets = new();
        static BombStats _fallbackStats;

        /// <summary>Time step of the impact prediction (s). Short chords of the parabola, each one raycast.</summary>
        const float PredictionStep = 0.1f;

        [Tooltip("Release point under the fuselage. The body never detaches, so bombs survive a lost wing.")]
        [SerializeField] Transform bombBay;
        [SerializeField] Material bombMaterial;
        [Tooltip("The bomb model (ArtSource/Weapons); unset drops a placeholder box in bombMaterial.")]
        [SerializeField] ProjectileVisuals.WeaponModel bombModel;
        [SerializeField] Material smokeMaterial;

        struct Bomb
        {
            public Vector3 Position;
            public Vector3 Velocity;
            public float TimeLeft;
            public Transform Visual;
        }

        PlaneController _plane;
        PlaneHealth _health;
        readonly List<Bomb> _bombs = new();
        Transform _visualRoot;
        float _cooldown;

        int _predictionFrame = -1;
        bool _predictionHit;
        BombImpact _prediction;

        /// <summary>Where a bomb released now would land.</summary>
        public struct BombImpact
        {
            public Vector3 Point;
            public Vector3 Normal;
            public float Time;
            /// <summary>The collider it would hit (ground, building, plane...).</summary>
            public Collider Collider;
        }

        /// <summary>Raised once per bomb released.</summary>
        public event Action<PlaneBombs> Dropped;
        /// <summary>Raised when one of our bombs damages another plane (direct hit or splash).</summary>
        public event Action<PlaneBombs, PlaneHealth, PartDamageInfo> HitPlane;
        /// <summary>Raised when one of our bombs damages a ground target, with the damage dealt.</summary>
        public event Action<PlaneBombs, GroundTarget, float> HitGroundTarget;
        /// <summary>Raised where a bomb explodes.</summary>
        public event Action<PlaneBombs, Vector3> Exploded;

        public PlaneController Plane => _plane;
        public BombStats Stats => _plane.Stats != null && _plane.Stats.bombs != null ? _plane.Stats.bombs : FallbackStats();
        public int Ammo { get; private set; }
        /// <summary>Whether this plane's class carries bombs (its <see cref="FlightStats.bombs"/> is set). Without them ammo is 0.</summary>
        public bool IsEquipped => _plane.Stats == null || _plane.Stats.bombs != null;
        public int MaxAmmo => IsEquipped ? Stats.ammo : 0;
        /// <summary>Bombs still falling.</summary>
        public int InFlight => _bombs.Count;
        /// <summary>0..1 until the next bomb can drop (1 = ready).</summary>
        public float Readiness => 1f - Mathf.Clamp01(_cooldown / Stats.dropInterval);

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _health = GetComponent<PlaneHealth>();
            if (bombBay == null)
                Debug.LogError($"{name}: bomb bay is missing", this);
            _visualRoot = new GameObject($"{name} Bombs").transform;
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

        void OnPlaneReset(PlaneController _)
        {
            _predictionFrame = -1; // the plane jumped (or changed class) mid-frame
            Restock();
        }
        void OnRestored(PlaneHealth _) => Restock();

        /// <summary>Full ammo (none if the class has no bombs) and ready to drop. Bombs already falling keep falling.</summary>
        public void Restock()
        {
            Ammo = MaxAmmo;
            _cooldown = 0f;
        }

        /// <summary>Release point and velocity for a bomb dropped from the given plane pose.</summary>
        void ReleaseState(Vector3 planePosition, Quaternion planeRotation, Vector3 planeVelocity, out Vector3 origin, out Vector3 velocity)
        {
            Vector3 local = bombBay != null ? transform.InverseTransformPoint(bombBay.position) : Vector3.down * 1.2f;
            origin = planePosition + planeRotation * local;
            velocity = planeVelocity + planeRotation * Vector3.down * Stats.ejectSpeed;
        }

        /// <summary>
        /// Where a bomb released right now would land, following the same trajectory and hit rules as a real bomb.
        /// Cached per frame, so the HUD can ask as often as it likes. False if it would fall for its whole lifetime
        /// without hitting anything.
        /// </summary>
        public bool TryPredictImpact(out BombImpact impact)
        {
            if (_predictionFrame != Time.frameCount)
            {
                _predictionFrame = Time.frameCount;
                // Start from where the plane is drawn this frame (extrapolated from the last physics step), so the
                // marker doesn't jitter against the interpolated plane.
                float ahead = Time.inFixedTimeStep ? 0f : Time.time - Time.fixedTime;
                ReleaseState(_plane.Position + _plane.Velocity * ahead, _plane.Rotation, _plane.Velocity, out var origin, out var velocity);
                _predictionHit = PredictImpact(Stats, origin, velocity, out _prediction);
            }
            impact = _prediction;
            return _predictionHit;
        }

        bool PredictImpact(BombStats s, Vector3 origin, Vector3 velocity, out BombImpact impact)
        {
            impact = default;
            Vector3 from = origin;
            for (float t = PredictionStep; t <= s.lifetime + PredictionStep * 0.5f; t += PredictionStep)
            {
                Vector3 to = s.PositionAt(origin, velocity, t);
                Vector3 move = to - from;
                float distance = move.magnitude;
                if (distance > 1e-4f && TryHit(from, move / distance, distance, s.bombRadius, out var hit))
                {
                    impact = new BombImpact
                    {
                        Point = hit.point,
                        Normal = hit.normal,
                        Time = t - PredictionStep + PredictionStep * (hit.distance / distance),
                        Collider = hit.collider,
                    };
                    return true;
                }
                from = to;
            }
            return false;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            StepBombs(dt);
            UpdateTrigger(Stats, dt);
        }

        void UpdateTrigger(BombStats s, float dt)
        {
            if (_cooldown > 0f)
                _cooldown -= dt;
            if (!_plane.IsAlive || !_plane.CurrentInput.DropBomb || _cooldown > 0f || Ammo <= 0)
                return;
            _cooldown = s.dropInterval;
            DropOne(s);
        }

        void DropOne(BombStats s)
        {
            Ammo--;
            ReleaseState(_plane.Position, _plane.Rotation, _plane.Velocity, out var origin, out var velocity);
            _bombs.Add(new Bomb
            {
                Position = origin,
                Velocity = velocity,
                TimeLeft = s.lifetime,
                Visual = CreateVisual(s, origin, velocity),
            });
            Dropped?.Invoke(this);
        }

        void StepBombs(float dt)
        {
            var s = Stats;
            for (int i = _bombs.Count - 1; i >= 0; i--)
            {
                var b = _bombs[i];
                if (Step(s, ref b, dt))
                {
                    _bombs[i] = b;
                }
                else
                {
                    ProjectileVisuals.Release(b.Visual);
                    _bombs.RemoveAt(i);
                }
            }
        }

        /// <summary>Moves one bomb along its parabola. Returns false once it has exploded or timed out.</summary>
        bool Step(BombStats s, ref Bomb b, float dt)
        {
            // Exact free fall over the step, swept as a straight chord (same as the prediction).
            Vector3 to = s.PositionAt(b.Position, b.Velocity, dt);
            Vector3 move = to - b.Position;
            float distance = move.magnitude;
            if (distance > 1e-4f && TryHit(b.Position, move / distance, distance, s.bombRadius, out var hit))
            {
                Explode(hit.point, hit.collider);
                return false;
            }
            b.Position = to;
            b.Velocity = s.VelocityAt(b.Velocity, dt);
            b.TimeLeft -= dt;
            return b.TimeLeft > 0f; // a bomb that never lands is simply removed
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
                    continue; // our own plane, and falling wreckage
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

        /// <summary>Direct damage to a plane part hit (if any), then the blast: ground targets hard, plane bodies lightly.</summary>
        void Explode(Vector3 point, Collider directHit)
        {
            var s = Stats;
            PlaneHealth directHealth = null;
            if (directHit != null && PlanePart.TryGetFromCollider(directHit, out var part))
            {
                directHealth = directHit.GetComponentInParent<PlaneHealth>();
                if (directHealth != null)
                    DamagePlane(directHealth, part, s.directDamage, point);
            }

            SplashTargets.Clear();
            if (directHealth != null)
                SplashTargets[directHealth] = 0f;
            Explosions.FindPlanesInRadius(point, s.splashRadius, transform, SplashTargets); // never splash ourselves
            foreach (var (health, distance) in SplashTargets)
                DamagePlane(health, health.GetPart(PlanePartType.Body), s.PlaneSplashDamageAt(distance), point);
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

        void DamagePlane(PlaneHealth health, PlanePart part, float amount, Vector3 point)
        {
            float taken = health.ApplyDamage(part, amount, point, _plane);
            if (taken > 0f)
                HitPlane?.Invoke(this, health, new PartDamageInfo(part, taken, point, _plane));
        }

        // ------------------------------------------------------------------ visuals

        void LateUpdate()
        {
            // Extrapolate from the last physics step so bombs fall smoothly every frame.
            var s = Stats;
            float ahead = Time.time - Time.fixedTime;
            foreach (var b in _bombs)
                if (b.Visual != null)
                    b.Visual.SetPositionAndRotation(s.PositionAt(b.Position, b.Velocity, ahead),
                        Quaternion.LookRotation(s.VelocityAt(b.Velocity, ahead)));
        }

        Transform CreateVisual(BombStats s, Vector3 position, Vector3 velocity) =>
            // No exhaust: just a faint smoke streak so falling bombs are easy to follow.
            ProjectileVisuals.Create("Bomb", _visualRoot, position, velocity, s.bombLength, s.bombWidth,
                bombMaterial, null, smokeMaterial, smokeTime: 0.4f, smokeEndWidth: 0.6f, bombModel);

        static BombStats FallbackStats()
        {
            if (_fallbackStats == null)
                _fallbackStats = ScriptableObject.CreateInstance<BombStats>();
            return _fallbackStats;
        }
    }
}

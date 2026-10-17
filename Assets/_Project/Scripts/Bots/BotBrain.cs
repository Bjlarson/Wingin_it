using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Bots
{
    /// <summary>What a <see cref="BotBrain"/> is doing right now, highest priority last.</summary>
    public enum BotState
    {
        /// <summary>No enemy in sight: flying the patrol route (or circling home).</summary>
        Patrol,
        /// <summary>Closing on an enemy that's still out of weapons range.</summary>
        Chase,
        /// <summary>Enemy in weapons range: tracking it and firing when lined up.</summary>
        Attack,
        /// <summary>Damaged (or out of ammo): flying an approach lane home to repair, shooting only what crosses its nose.</summary>
        Retreat,
        /// <summary>On final: tracking the glide path down to the runway, then rolling out.</summary>
        Land,
        /// <summary>Parked in the repair zone while the repair timer runs.</summary>
        Repair,
        /// <summary>Rolling down the runway and climbing out, after a repair or a runway spawn.</summary>
        TakeOff,
        /// <summary>A missile is closing: breaking hard (and barrel rolling if the class can).</summary>
        Evade,
        /// <summary>About to fly into another plane: sidestepping it.</summary>
        AvoidCollision,
        /// <summary>Terrain or a building ahead or below: pulling up and away. Overrides everything.</summary>
        AvoidTerrain,
    }

    /// <summary>
    /// A bot's decisions. Each physics step it picks a <see cref="BotState"/> and turns it into orders and buttons for
    /// its <see cref="BotPilot"/>: patrol a route, chase and attack the best visible enemy, break from incoming
    /// missiles, pull up from terrain, and when it's hurt fly an approach lane home, land, repair and take off again.
    /// Like the pilot it never touches the plane directly, and it only knows what a
    /// player could: it sees enemies within its detection range unless cloud hides them (or an allied Support's
    /// radar reveals them), and the missile warning tells it what's chasing it.
    /// How well it does all that is its <see cref="Difficulty"/> (see <see cref="BotSkill"/>).
    /// </summary>
    [DefaultExecutionOrder(-20)] // decide before BotPilot turns the order into input
    [RequireComponent(typeof(BotPilot))]
    public class BotBrain : MonoBehaviour
    {
        [Header("Patrol")]
        [Tooltip("Points to fly between in a loop. Empty: circle the spot where the bot started.")]
        [SerializeField] List<Vector3> patrolRoute = new();

        [Header("Skill")]
        [Tooltip("Sets detection, reactions, aim, evasion, ability use, the retreat threshold and the stick hand.")]
        [SerializeField] BotDifficulty difficulty = BotDifficulty.Normal;

        [Header("Combat")]
        [Tooltip("A target farther than this (m), or hidden for longer than the memory time, is dropped.")]
        [SerializeField] float disengageRange = 2400f;
        [Tooltip("Seconds a target can stay hidden in cloud before the bot gives up on it.")]
        [SerializeField] float targetMemory = 3f;
        [Tooltip("Seconds in gun range without getting the lead point near the nose before the bot looks for an easier target.")]
        [SerializeField] float stalemateTime = 6f;
        [Tooltip("The lead point within this angle (deg) of the nose counts as a shot coming (it isn't a stalemate).")]
        [SerializeField] float shotChanceAngle = 10f;
        [Tooltip("Rockets fire within this fraction of rocket range.")]
        [SerializeField] float rocketRangeFraction = 0.7f;
        [Tooltip("Chasing a target farther than this (m), boost while there's boost to spare.")]
        [SerializeField] float boostChaseDistance = 1000f;

        [Header("Missile evasion and abilities")]
        [Tooltip("A missile this close (m): Barrel Roll classes roll, Afterburner classes light it, Armor Shield classes raise it.")]
        [SerializeField] float barrelRollDistance = 250f;
        [Tooltip("Armor Shield: raise it when hit this recently (s) with an enemy in sight.")]
        [SerializeField] float shieldHitWindow = 0.5f;
        [Tooltip("Repair Aura: switch it on when it, or an ally inside the aura, is below this body fraction (0..1).")]
        [SerializeField, Range(0f, 1f)] float auraHealth = 0.75f;

        [Header("Terrain")]
        [Tooltip("Look this many seconds of flight ahead for obstacles.")]
        [SerializeField] float lookAheadTime = 3.5f;
        [Tooltip("Pull up when lower than this (m) above the ground and not climbing.")]
        [SerializeField] float minClearance = 50f;
        [Tooltip("Width (m) of the swept probe ahead: wingspan plus a margin.")]
        [SerializeField] float probeRadius = 12f;
        [Tooltip("Keep pulling up this long (s) after the obstacle is clear, so the bot doesn't nose straight back in.")]
        [SerializeField] float avoidHoldTime = 1f;

        [Header("Retreat to repair")]
        [Tooltip("With no enemy around and every missile and rocket spent, go home to restock.")]
        [SerializeField] bool restockWhenEmpty = true;
        [Tooltip("Lanes whose trip is within this multiple of the shortest are picked at random, so chasers can't guess.")]
        [SerializeField] float laneChoiceSlack = 1.3f;
        [Tooltip("A lane waypoint counts as passed inside this horizontal distance (m).")]
        [SerializeField] float waypointRadius = 250f;
        [Tooltip("Turn onto final within this horizontal distance (m) of the final approach fix.")]
        [SerializeField] float finalJoinRadius = 350f;
        [Tooltip("Slow to approach speed with this much (m) of the lane left.")]
        [SerializeField] float slowDownDistance = 900f;
        [Tooltip("An enemy this close (m) while retreating: boost.")]
        [SerializeField] float retreatBoostDistance = 800f;
        [Tooltip("Stopped on the ground but not repairing (missed the zone) for this long (s): take off and try again.")]
        [SerializeField] float missedZoneWait = 2f;
        [Tooltip("With no approach lanes into the field, start the straight-in final this far (m) out on the glide path...")]
        [SerializeField] float defaultFinalLength = 1100f;
        [Tooltip("...this high (m) above it, so the bot doesn't come in skimming the ground on the way there...")]
        [SerializeField] float defaultEntryHeight = 40f;
        [Tooltip("...and join the glide path at this distance (m), like a lane's final approach fix.")]
        [SerializeField] float defaultFixDistance = 600f;
        [Tooltip("On final, sinking this far (m) under the glide path brings back the full terrain check.")]
        [SerializeField] float belowPathLimit = 6f;
        [Tooltip("After takeoff, keep climbing straight until this high (m) above the ground.")]
        [SerializeField] float takeoffClimbHeight = 60f;

        [Header("Other planes")]
        [Tooltip("Sidestep any plane that will pass closer than this (m)...")]
        [SerializeField] float separation = 35f;
        [Tooltip("...within this many seconds.")]
        [SerializeField] float collisionHorizon = 2f;

        static readonly RaycastHit[] s_hits = new RaycastHit[16];
        static readonly List<float> s_laneCosts = new();

        enum RetreatPhase { Lane, Hold, Final }

        BotPilot _pilot;
        PlaneController _plane;
        PlaneGuns _guns;
        PlaneMissiles _missiles;
        PlaneRockets _rockets;
        PlaneAbility _ability;
        PlaneHealth _health;
        PlaneRepair _repair;
        LandingGear _gear;
        Vector3 _home;
        int _waypoint;
        bool _patrolOrdered, _gunsFiring, _pulse;
        float _retargetTimer, _lastSeen, _threatSince = -1f, _avoidUntil = -1f, _breakSide;
        Vector3 _avoidNormal = Vector3.up;
        BotSkill _skill;
        float _engageAt, _lockedSince = -1f, _lastHitAt = -1f, _lastShotChance;
        bool _threatNoticed;

        // Retreat: the field, the route into it (an approach lane's points, ending on the final approach fix) and progress.
        bool _retreating;
        Airfield _field;
        ApproachLane _lane;
        readonly List<Vector3> _route = new();
        int _routeIndex;
        RetreatPhase _phase;
        float _offApproachSince = -1f, _stoppedSince = -1f;
        bool _routeOrdered, _routeSlow, _missedZone, _forced;
        ApproachLane _forcedLane;

        /// <summary>Every active brain, for debug views and tests.</summary>
        public static readonly List<BotBrain> All = new();

        public BotPilot Pilot => _pilot;

        /// <summary>How good this bot is. Setting it applies its <see cref="BotSkill"/> preset at once.</summary>
        public BotDifficulty Difficulty
        {
            get => difficulty;
            set
            {
                difficulty = value;
                ApplySkill(BotSkill.For(value));
            }
        }

        /// <summary>The knobs in use: the <see cref="Difficulty"/> preset, unless <see cref="ApplySkill"/> set custom ones.</summary>
        public BotSkill Skill => _skill;

        /// <summary>Use these knobs instead of a preset (tuning, tests). Setting <see cref="Difficulty"/> goes back to a preset.</summary>
        public void ApplySkill(BotSkill skill)
        {
            _skill = skill;
            if (_pilot != null)
                _pilot.SetSkill(skill.steerSensitivity, skill.stickSlewRate, skill.aimError);
        }

        /// <summary>Going after <see cref="Target"/>: it has one and the reaction time since spotting it has passed.</summary>
        public bool IsEngaged => Target != null && Time.fixedTime >= _engageAt;
        public BotState State { get; private set; }
        /// <summary>The enemy being chased or attacked, or null.</summary>
        public PlaneController Target { get; private set; }
        /// <summary>Index into the patrol route of the point being flown to.</summary>
        public int Waypoint => _waypoint;
        public IReadOnlyList<Vector3> PatrolRoute => patrolRoute;
        /// <summary>Where the last terrain probe saw something in the way (for debug drawing).</summary>
        public Vector3? Obstacle { get; private set; }
        /// <summary>Heading home to repair: from the decision until the repair finishes (or the plane dies).</summary>
        public bool IsRetreating => _retreating;
        /// <summary>The airfield it's retreating to, or null.</summary>
        public Airfield RetreatField => _retreating ? _field : null;
        /// <summary>The approach lane it's flying home, or null (none, or the field has no lanes: a straight-in final).</summary>
        public ApproachLane RetreatLane => _retreating ? _lane : null;
        /// <summary>The route home: the lane's points from where it joined, ending on the final approach fix.</summary>
        public IReadOnlyList<Vector3> RetreatRoute => _route;
        /// <summary>Index into <see cref="RetreatRoute"/> of the point being flown to.</summary>
        public int RetreatWaypoint => _routeIndex;
        /// <summary>Retreat attempts that ended in a go-around (for tests and tuning).</summary>
        public int GoArounds { get; private set; }
        /// <summary>Why the last go-around happened, and where.</summary>
        public GoAroundReason LastGoAround { get; private set; }
        public Vector3 LastGoAroundAt { get; private set; }

        /// <summary>
        /// Head home to repair now, whatever the damage, down <paramref name="lane"/> if given (it must lead to a field
        /// friendly to us), else the nearest friendly field's usual pick. No-op without a friendly airfield.
        /// </summary>
        public void Retreat(ApproachLane lane = null)
        {
            _forced = true;
            StartRetreat(lane != null && lane.Airfield != null && lane.Airfield.IsFriendlyTo(_plane.Team) ? lane : null);
        }

        /// <summary>Fly <paramref name="route"/> in a loop when there's nothing to fight (empty: circle the current spot).</summary>
        public void SetPatrol(IEnumerable<Vector3> route)
        {
            patrolRoute = new List<Vector3>(route);
            _waypoint = 0;
            _home = _plane != null ? _plane.Position : transform.position;
            _patrolOrdered = false;
        }

        void Awake()
        {
            _pilot = GetComponent<BotPilot>();
            _plane = GetComponent<PlaneController>();
            _guns = GetComponent<PlaneGuns>();
            _missiles = GetComponent<PlaneMissiles>();
            _rockets = GetComponent<PlaneRockets>();
            _ability = GetComponent<PlaneAbility>();
            _health = GetComponent<PlaneHealth>();
            _repair = GetComponent<PlaneRepair>();
            _gear = GetComponent<LandingGear>();
            ApplySkill(BotSkill.For(difficulty));
        }

        void OnEnable()
        {
            All.Add(this);
            _plane.Respawned += OnRespawned;
            if (_health != null)
            {
                _health.Restored += OnRestored;
                _health.Damaged += OnDamaged;
            }
        }

        void OnDisable()
        {
            All.Remove(this);
            _plane.Respawned -= OnRespawned;
            if (_health != null)
            {
                _health.Restored -= OnRestored;
                _health.Damaged -= OnDamaged;
            }
        }

        void Start() => _home = _plane.Position;

        void OnRespawned(PlaneController _)
        {
            Target = null;
            _patrolOrdered = false;
            _gunsFiring = false;
            _threatSince = _avoidUntil = _lockedSince = _lastHitAt = -1f;
            _retreating = _forced = false;
            State = BotState.Patrol;
        }

        /// <summary>Repaired (or respawned): nothing left to go home for.</summary>
        void OnRestored(PlaneHealth _) => _retreating = _forced = false;

        void OnDamaged(PlaneHealth _, PartDamageInfo __) => _lastHitAt = Time.fixedTime;

        void FixedUpdate()
        {
            if (!_plane.IsAlive)
                return;

            float now = Time.fixedTime;
            _pilot.FireGuns = _pilot.FireRockets = _pilot.Boost = false;
            // Missile and ability triggers act on a press: let go every other step so the next press counts.
            _pulse = !_pulse;
            _pilot.FireMissile = _pilot.UseAbility = false;

            if (_plane.IsGrounded)
            {
                OnGround(now);
                return;
            }
            _stoppedSince = -1f;

            UpdateTarget(now);
            UpdateRetreat();

            if (CheckTerrain(now, ApproachMode()))
            {
                State = BotState.AvoidTerrain;
                Vector3 forward = _plane.Velocity.sqrMagnitude > 1f ? _plane.Velocity : _plane.Rotation * Vector3.forward;
                _pilot.Override(BotSenses.EscapePoint(_plane.Position, forward, _avoidNormal), null, 1.5f);
            }
            else if (!CheckTraffic() && !CheckMissiles(now) && !ClimbingOut())
            {
                if (_retreating)
                    FlyHome(now);
                else if (IsEngaged)
                    Fight();
                else
                    Patrol();
            }

            if (_skill.useAbilities)
                UseAbility(now);
        }

        // ------------------------------------------------------------------ terrain

        /// <summary>
        /// How close to the ground the bot means to be: 0 normally; 1 on the last lane leg, descending to the final
        /// approach fix (~30 m up), or turning onto final; 2 lined up on final, where the runway is the point. Sunk well
        /// under the glide path, it's back to 1, so whatever is short of the runway counts as terrain again.
        /// </summary>
        int ApproachMode()
        {
            if (!_retreating || _phase == RetreatPhase.Hold)
                return 0;
            if (_phase == RetreatPhase.Final)
            {
                var guide = _field.GetApproach(_plane.Position, _plane.Velocity);
                return guide.OnApproach && guide.HeightError > -belowPathLimit ? 2 : 1;
            }
            return _routeIndex == _route.Count - 1 ? 1 : 0;
        }

        /// <summary>
        /// True while an obstacle is ahead or the ground is too close (and for a moment after). On the way down to land
        /// (<paramref name="approach"/> 1) the minimum height drops to 20 m; on final (2) only walls count, not the
        /// ground the plane is descending onto.
        /// </summary>
        bool CheckTerrain(float now, int approach = 0)
        {
            Vector3 pos = _plane.Position;
            Vector3 velocity = _plane.Velocity;
            Obstacle = null;

            float speed = velocity.magnitude;
            if (speed > 1f && ProbeAhead(pos, velocity / speed, Mathf.Max(150f, speed * lookAheadTime), out RaycastHit hit,
                    approach == 2 ? _field : null))
            {
                Obstacle = hit.point;
                _avoidNormal = hit.normal;
                _avoidUntil = now + avoidHoldTime;
            }
            else if (approach < 2 && LandingGear.ProbeGround(pos, out RaycastHit ground) &&
                     BotSenses.TooLow(pos.y - ground.point.y, velocity.y, approach == 1 ? 20f : minClearance, lookAheadTime))
            {
                Obstacle = ground.point;
                _avoidNormal = Vector3.up;
                _avoidUntil = now + avoidHoldTime;
            }
            return now < _avoidUntil;
        }

        /// <summary>
        /// The nearest solid thing (not a plane or debris) the plane would fly into within <paramref name="distance"/>.
        /// On final into <paramref name="landingAt"/> it skips flat surfaces and anything below the glide path (the runway,
        /// its edge and the pad it sits on are meant to be flown down to): what's left sticks up into the approach.
        /// </summary>
        bool ProbeAhead(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest, Airfield landingAt = null)
        {
            int count = Physics.SphereCastNonAlloc(origin, probeRadius, direction, s_hits, distance,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            nearest = default;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                var hit = s_hits[i];
                if ((found && hit.distance >= nearest.distance) || hit.distance <= 0f || PlaneDebris.IsDebris(hit.collider) ||
                    hit.collider.GetComponentInParent<PlaneController>() != null)
                    continue;
                if (landingAt != null && (hit.normal.y > 0.7f ||
                                          landingAt.GetApproach(hit.point, Vector3.zero).HeightError < -1.5f))
                    continue;
                nearest = hit;
                found = true;
            }
            return found;
        }

        // ------------------------------------------------------------------ other planes

        /// <summary>True (and sidestepping) while another plane, friend or foe, is about to hit us.</summary>
        bool CheckTraffic()
        {
            Vector3 pos = _plane.Position;
            Vector3 velocity = _plane.Velocity;
            float reach = (velocity.magnitude * 2f + separation) * collisionHorizon; // ignore anything that can't get here
            float nearest = float.MaxValue;
            Vector3 avoid = default;
            foreach (var other in PlaneController.All)
            {
                if (other == _plane || !other.IsAlive || !other.isActiveAndEnabled || other.IsGrounded)
                    continue;
                Vector3 rel = other.Position - pos;
                if (rel.sqrMagnitude > reach * reach)
                    continue;
                if (BotSenses.OnCollisionCourse(rel, other.Velocity - velocity, collisionHorizon, separation, out Vector3 miss) &&
                    rel.sqrMagnitude < nearest)
                {
                    nearest = rel.sqrMagnitude;
                    avoid = miss;
                }
            }
            if (nearest == float.MaxValue)
                return false;

            State = BotState.AvoidCollision;
            Vector3 forward = velocity.sqrMagnitude > 1f ? velocity : _plane.Rotation * Vector3.forward;
            _pilot.Override(BotSenses.SidestepPoint(pos, forward, avoid), null, 2f);
            return true;
        }

        // ------------------------------------------------------------------ missiles

        /// <summary>True (and breaking) while a missile chasing us is close.</summary>
        bool CheckMissiles(float now)
        {
            var threat = PlaneMissiles.GetThreat(_plane);
            if (threat.Incoming == 0 || threat.NearestDistance > _skill.evadeStartDistance)
            {
                _threatSince = -1f;
                return false;
            }

            if (_threatSince < 0f)
            {
                _threatSince = now;
                _threatNoticed = Random.value < _skill.missileNoticeChance;
                _breakSide = BotSenses.BreakSide(_plane.Rotation * Vector3.right, _plane.Position, threat.NearestPosition);
            }
            if (!_threatNoticed || now - _threatSince < _skill.evadeReactionTime)
                return false;

            State = BotState.Evade;
            _pilot.Boost = true;
            Vector3 forward = _plane.Rotation * Vector3.forward;
            _pilot.Override(BotSenses.BreakTurnPoint(_plane.Position, forward, _breakSide),
                _plane.Modifiers.Get(FlightStat.MaxSpeed), 2f);
            return true;
        }

        // ------------------------------------------------------------------ abilities

        /// <summary>
        /// Fires the class ability when it pays off: Barrel Roll and Afterburner against a missile about to hit (and
        /// Afterburner to close a long chase, or to get away on the way home), Armor Shield when taking hits with an
        /// enemy in sight or a missile about to hit, Repair Aura when it or an ally in reach is hurt.
        /// </summary>
        void UseAbility(float now)
        {
            if (_ability == null || !_ability.IsReady)
                return;
            var threat = PlaneMissiles.GetThreat(_plane);
            bool missileClose = State == BotState.Evade && threat.Incoming > 0 && threat.NearestDistance <= barrelRollDistance;
            bool use = _ability.Stats switch
            {
                BarrelRollAbility => missileClose,
                AfterburnerAbility => missileClose ||
                                      (State == BotState.Chase && Target != null &&
                                       Vector3.Distance(_plane.Position, Target.Position) > boostChaseDistance) ||
                                      (State == BotState.Retreat && EnemyWithin(retreatBoostDistance)),
                ArmorShieldAbility => missileClose ||
                                      (_lastHitAt >= 0f && now - _lastHitAt <= shieldHitWindow && EnemyWithin(_skill.detectionRange)),
                RepairAuraAbility aura => AuraNeeded(aura.radius),
                _ => false,
            };
            if (use)
                _pilot.UseAbility = _pulse;
        }

        /// <summary>This plane, or an ally within <paramref name="radius"/>, has a body hurt enough to be worth healing.</summary>
        bool AuraNeeded(float radius)
        {
            foreach (var other in PlaneController.All)
            {
                if (!other.IsAlive || !other.isActiveAndEnabled || (other != _plane && !other.IsAllyOf(_plane)))
                    continue;
                if ((other.Position - _plane.Position).sqrMagnitude > radius * radius)
                    continue;
                var health = other == _plane ? _health : other.GetComponent<PlaneHealth>();
                if (health != null && health.GetPart(PlanePartType.Body).NormalizedHealth < auraHealth)
                    return true;
            }
            return false;
        }

        // ------------------------------------------------------------------ combat

        void UpdateTarget(float now)
        {
            if (Target != null)
            {
                float d = Vector3.Distance(_plane.Position, Target.Position);
                if (!IsEnemy(Target) || d > disengageRange)
                    Target = null;
                else if (CanSee(Target, d))
                    _lastSeen = now;
                else if (now - _lastSeen > targetMemory)
                    Target = null;
            }

            _retargetTimer -= Time.fixedDeltaTime;
            if (_retargetTimer > 0f && Target != null)
                return;
            _retargetTimer = _skill.retargetInterval;

            Vector3 pos = _plane.Position;
            Vector3 forward = _plane.Rotation * Vector3.forward;
            PlaneController best = null;
            float bestScore = float.MaxValue;
            foreach (var other in PlaneController.All)
            {
                if (!IsEnemy(other))
                    continue;
                float d = Vector3.Distance(pos, other.Position);
                if (d > _skill.detectionRange || !CanSee(other, d))
                    continue;
                float score = TargetScore(other, pos, forward);
                if (score < bestScore)
                {
                    bestScore = score;
                    best = other;
                }
            }

            // Sticky: only switch for a clearly better target. Stuck in a turning circle with no shot (equal planes
            // can't out-turn each other), any better target will do.
            bool stalemate = Target != null && now - _lastShotChance > stalemateTime;
            if (best != null && best != Target &&
                (Target == null || bestScore < (stalemate ? 1.5f : 0.7f) * TargetScore(Target, pos, forward)))
            {
                // A first sighting takes a moment to react to; switching targets mid-fight doesn't.
                if (Target == null)
                    _engageAt = now + _skill.reactionTime;
                Target = best;
                _lastSeen = _lastShotChance = now;
            }
        }

        static float TargetScore(PlaneController target, Vector3 pos, Vector3 forward)
        {
            var health = target.GetComponent<PlaneHealth>();
            float body = health != null ? health.GetPart(PlanePartType.Body).NormalizedHealth : 1f;
            return BotSenses.TargetScore(pos, forward, target.Position, target.Rotation * Vector3.forward, body);
        }

        bool IsEnemy(PlaneController other) =>
            other != null && other != _plane && other.isActiveAndEnabled && other.IsAlive && !_plane.IsAllyOf(other);

        bool CanSee(PlaneController other, float distance) =>
            distance <= disengageRange && (CloudCover.CanSee(_plane.Position, other.Position) ||
                                           PlaneAbility.IsRevealedTo(other, _plane.Team));

        void Fight()
        {
            _patrolOrdered = false;
            if (_pilot.Maneuver != BotManeuver.Pursue || _pilot.Target != Target)
                _pilot.Pursue(Target);

            Vector3 pos = _plane.Position;
            Vector3 forward = _plane.Rotation * Vector3.forward;
            float distance = Vector3.Distance(pos, Target.Position);
            bool inRange = false;

            if (AimGuns())
                inRange = true;

            if (_rockets != null && _rockets.Ammo > 0 && distance <= _rockets.Stats.Range * rocketRangeFraction &&
                _rockets.TryGetLeadPoint(Target, out Vector3 rocketLead, out _))
            {
                inRange = true;
                _pilot.FireRockets = BotSenses.IsAimedAt(pos, forward, rocketLead, _skill.rocketAimTolerance);
            }

            if (_missiles != null && _missiles.Ammo > 0 && distance <= _missiles.Stats.lockRange)
            {
                inRange = true;
                // Keeping the nose on the target builds the lock; fire once it's been ours for the fire delay.
                bool locked = _missiles.IsLocked && _missiles.LockTarget == Target;
                if (!locked)
                    _lockedSince = -1f;
                else if (_lockedSince < 0f)
                    _lockedSince = Time.fixedTime;
                _pilot.FireMissile = locked && Time.fixedTime - _lockedSince >= _skill.missileFireDelay && _pulse;
            }

            State = inRange ? BotState.Attack : BotState.Chase;
            _pilot.Boost = !inRange && distance > boostChaseDistance && _plane.BoostMeter > 0.3f;
        }

        /// <summary>
        /// Fires the guns while the lead point on <see cref="Target"/> is on the nose (with heat discipline). True if the
        /// target is in gun range at all.
        /// </summary>
        bool AimGuns()
        {
            if (Target == null || _guns == null || _guns.ActiveGunCount == 0 ||
                !_guns.TryGetLeadPoint(Target, out Vector3 lead, out _))
            {
                _gunsFiring = false;
                _lastShotChance = Time.fixedTime; // still closing in: not a stalemate
                return false;
            }
            if (BotSenses.IsAimedAt(_plane.Position, _plane.Rotation * Vector3.forward, lead, shotChanceAngle))
                _lastShotChance = Time.fixedTime;
            _gunsFiring = BotSenses.GunsCool(_guns.Heat, _gunsFiring) &&
                          BotSenses.IsGunShot(_plane.Position, _plane.Rotation * Vector3.forward, lead, _skill.gunAimTolerance,
                                              _skill.gunMissDistance, _skill.gunFireRange);
            _pilot.FireGuns = _gunsFiring;
            return true;
        }

        // ------------------------------------------------------------------ retreat to repair

        bool WantsRepair()
        {
            if (_health == null || _repair == null)
                return false;
            float body = _health.GetPart(PlanePartType.Body).NormalizedHealth;
            return BotSenses.NeedsRepair(body, _health.LostPartsMask, _skill.retreatHealth) ||
                   (restockWhenEmpty && Target == null && OutOfAmmo());
        }

        /// <summary>Carries missiles or rockets, and has none of either left.</summary>
        bool OutOfAmmo()
        {
            bool missiles = _missiles != null && _missiles.IsEquipped, rockets = _rockets != null && _rockets.IsEquipped;
            return (missiles || rockets) && (!missiles || _missiles.Ammo == 0) && (!rockets || _rockets.Ammo == 0);
        }

        /// <summary>Starts a retreat when one's needed, and calls it off if the reason went away (healed in the air) before final.</summary>
        void UpdateRetreat()
        {
            bool wants = WantsRepair();
            if (!_retreating && wants)
                StartRetreat();
            else if (_retreating && !wants && _phase != RetreatPhase.Final && !_forced)
                _retreating = false;
        }

        void StartRetreat(ApproachLane lane = null)
        {
            _field = lane != null ? lane.Airfield : Airfield.NearestFriendly(_plane.Position, _plane.Team);
            if (_field == null)
                return;
            _retreating = true;
            _missedZone = false;
            _forcedLane = lane;
            PlanRoute();
        }

        /// <summary>
        /// Picks the way in: one of the field's approach lanes (any whose trip is close to the shortest, at random),
        /// joined at the point that makes the shortest trip. A field without lanes gets a straight-in final instead.
        /// </summary>
        void PlanRoute()
        {
            Vector3 pos = _plane.Position;
            _route.Clear();
            _lane = null;

            var lanes = ApproachLane.Into(_field);
            s_laneCosts.Clear();
            foreach (var lane in lanes)
            {
                BotSenses.LaneJoinIndex(lane.Points, pos, out float cost);
                bool allowed = lane.Points.Count >= 2 && (_forcedLane == null || lane == _forcedLane);
                s_laneCosts.Add(allowed ? cost : float.MaxValue);
            }
            int pick = BotSenses.PickLane(s_laneCosts, Random.value, laneChoiceSlack);
            if (pick >= 0 && s_laneCosts[pick] < float.MaxValue)
            {
                _lane = lanes[pick];
                int join = BotSenses.LaneJoinIndex(_lane.Points, pos, out _);
                for (int i = join; i < _lane.Points.Count; i++)
                    _route.Add(_lane.Points[i]);
            }
            else
            {
                _route.Add(_field.GlidePathPoint(defaultFinalLength) + Vector3.up * defaultEntryHeight);
                _route.Add(_field.GlidePathPoint(defaultFixDistance));
            }

            _routeIndex = 0;
            _phase = RetreatPhase.Lane;
            _routeOrdered = false;
            _offApproachSince = -1f;
        }

        /// <summary>Another plane is on the wheels on our runway: landing now would mean rolling into it.</summary>
        bool RunwayBusy()
        {
            foreach (var other in PlaneController.All)
                if (other != _plane && other.isActiveAndEnabled && other.IsAlive && other.IsGrounded &&
                    _field.IsOverRunway(other.Position))
                    return true;
            return false;
        }

        /// <summary>Flies the route home, holds while the runway is taken, then the final approach (going around if it's blown).</summary>
        void FlyHome(float now)
        {
            if (_field == null || !_field.isActiveAndEnabled || !_field.IsFriendlyTo(_plane.Team))
            {
                StartRetreat(); // the field's gone (captured, say): pick another
                if (_field == null)
                {
                    _retreating = false;
                    return;
                }
            }
            _patrolOrdered = false;
            Vector3 pos = _plane.Position;
            AimGuns(); // shoot whatever crosses the nose, but don't turn to fight

            if (_phase == RetreatPhase.Final)
            {
                var guide = _field.GetApproach(pos, _plane.Velocity);
                if (guide.OnApproach)
                    _offApproachSince = -1f;
                else if (_offApproachSince < 0f)
                    _offApproachSince = now;
                float off = _offApproachSince < 0f ? 0f : now - _offApproachSince;
                var reason = BotSenses.ShouldGoAround(guide.OnApproach, off, guide.Distance, guide.Lateral,
                    _field.RunwaySize.x * 0.5f, RunwayBusy());
                if (reason == GoAroundReason.None)
                {
                    State = BotState.Land;
                    if (_pilot.Maneuver != BotManeuver.Land || _pilot.Field != _field)
                        _pilot.Land(_field);
                    return;
                }
                GoArounds++;
                LastGoAround = reason;
                LastGoAroundAt = pos;
                PlanRoute();
            }

            State = BotState.Retreat;
            bool toFix = _routeIndex == _route.Count - 1;
            Vector3 next = _route[_routeIndex];
            float flat = FlatDistance(pos, next);

            // The runway's taken: circle the point before the fix until it's clear.
            if (toFix && RunwayBusy())
            {
                if (_phase != RetreatPhase.Hold)
                {
                    _phase = RetreatPhase.Hold;
                    _pilot.Orbit(_route.Count >= 2 ? _route[^2] : next);
                }
                return;
            }
            if (_phase == RetreatPhase.Hold)
            {
                _phase = RetreatPhase.Lane;
                _routeOrdered = false;
            }

            if (toFix && flat <= finalJoinRadius)
            {
                _phase = RetreatPhase.Final;
                _offApproachSince = -1f;
                State = BotState.Land;
                _pilot.Land(_field);
                return;
            }
            if (!toFix && (flat <= waypointRadius || (_routeOrdered && _pilot.HasArrived)))
            {
                _routeIndex++;
                _routeOrdered = false;
                next = _route[_routeIndex];
                flat = FlatDistance(pos, next);
            }

            // Slow down for the approach near the end of the lane; before that, boost away from anyone close.
            float remaining = flat;
            for (int i = _routeIndex + 1; i < _route.Count; i++)
                remaining += Vector3.Distance(_route[i - 1], _route[i]);
            bool slow = remaining <= slowDownDistance;
            if (!_routeOrdered || slow != _routeSlow)
            {
                _pilot.FlyTo(next, slow ? _pilot.ApproachSpeed : null);
                _routeOrdered = true;
                _routeSlow = slow;
            }
            _pilot.Boost = !slow && _plane.BoostMeter > 0.3f && EnemyWithin(retreatBoostDistance);
        }

        static float FlatDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));

        bool EnemyWithin(float range)
        {
            foreach (var other in PlaneController.All)
            {
                if (!IsEnemy(other))
                    continue;
                float d = Vector3.Distance(other.Position, _plane.Position);
                if (d <= range && CanSee(other, d))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// On the wheels: roll out and wait in the repair zone while retreating; otherwise (repaired, a runway spawn)
        /// take off. A plane that stopped but isn't repairing has missed the zone: it takes off and flies the approach
        /// again.
        /// </summary>
        void OnGround(float now)
        {
            _patrolOrdered = false;
            _gunsFiring = false;
            if (_repair != null && _repair.IsRepairing)
            {
                State = BotState.Repair;
                _stoppedSince = -1f;
                return;
            }

            if (_retreating && !_missedZone)
            {
                State = BotState.Land;
                var field = _field != null ? _field : Airfield.RunwayUnder(_plane.Position);
                if (_pilot.Maneuver != BotManeuver.Land && field != null)
                    _pilot.Land(field);
                if (_plane.Speed > 0.5f)
                    _stoppedSince = -1f;
                else if (_stoppedSince < 0f)
                    _stoppedSince = now;
                else if (now - _stoppedSince > missedZoneWait)
                    _missedZone = true;
                if (!_missedZone)
                    return;
            }

            State = BotState.TakeOff;
            if (_pilot.Maneuver != BotManeuver.TakeOff)
                _pilot.TakeOff();
            _pilot.Boost = true;
        }

        /// <summary>Just off the runway: keep climbing straight out until there's room to maneuver.</summary>
        bool ClimbingOut()
        {
            if (_pilot.Maneuver != BotManeuver.TakeOff)
                return false;
            float height = _gear != null ? _gear.HeightAboveGround : float.PositiveInfinity;
            if (height < takeoffClimbHeight)
            {
                State = BotState.TakeOff;
                return true;
            }
            if (_retreating)
                PlanRoute(); // missed the zone: around again
            _missedZone = false;
            _pilot.Cruise();
            return false;
        }

        // ------------------------------------------------------------------ patrol

        void Patrol()
        {
            State = BotState.Patrol;
            if (patrolRoute.Count == 0)
            {
                if (!_patrolOrdered)
                    _pilot.Orbit(_home);
                _patrolOrdered = true;
                return;
            }

            if (_patrolOrdered && _pilot.HasArrived)
            {
                _waypoint = (_waypoint + 1) % patrolRoute.Count;
                _patrolOrdered = false;
            }
            if (!_patrolOrdered)
            {
                _pilot.FlyTo(patrolRoute[_waypoint]);
                _patrolOrdered = true;
            }
        }
    }
}

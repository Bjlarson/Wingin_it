using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Flight
{
    public readonly struct PartDamageInfo
    {
        public readonly PlanePart Part;
        /// <summary>Damage actually taken, after clamping to the part's remaining HP.</summary>
        public readonly float Amount;
        public readonly Vector3 Point;
        /// <summary>Whoever dealt it (a plane, AA gun, dev tool). Decides kill credit (see <see cref="PlaneHealth.LastAttacker"/>).</summary>
        public readonly UnityEngine.Object Source;

        public PartDamageInfo(PlanePart part, float amount, Vector3 point, UnityEngine.Object source)
        {
            Part = part;
            Amount = amount;
            Point = point;
            Source = source;
        }
    }

    /// <summary>Who gets credit for a plane's death. Raised for every death, credited or not.</summary>
    public readonly struct KillInfo
    {
        public readonly PlaneHealth Victim;
        /// <summary>The plane credited with the kill, or null (solo crash, no recent attacker, or a non-plane source such as AA).</summary>
        public readonly PlaneController Killer;
        /// <summary>The damage source credited with the kill (the killer plane, an AA gun, ...), or null.</summary>
        public readonly UnityEngine.Object Source;
        public readonly DeathCause Cause;
        /// <summary>
        /// Other planes that helped (see <see cref="PlaneHealth.AssistMinDamage"/>), any team, never the killer. Never null.
        /// </summary>
        public readonly IReadOnlyList<PlaneController> Assisters;

        public KillInfo(PlaneHealth victim, PlaneController killer, UnityEngine.Object source, DeathCause cause,
            IReadOnlyList<PlaneController> assisters = null)
        {
            Victim = victim;
            Killer = killer;
            Source = source;
            Cause = cause;
            Assisters = assisters ?? Array.Empty<PlaneController>();
        }
    }

    /// <summary>
    /// Per-part HP for one plane. All damage goes through here: hits on a destroyed part are ignored,
    /// wings and stabilizers at 0 HP break off (taking their hitbox with them), and the body reaching 0 HP
    /// destroys the plane. Max HP comes from the class's <see cref="PlaneDurability"/>,
    /// and everything is restored on respawn or class change. Lost parts change the handling through
    /// <see cref="PartFlightEffects"/>, and body HP sets the <see cref="BodyDamageState"/> through
    /// <see cref="BodyDamageEffects"/>.
    /// It also remembers the last attacker, so a plane that crashes after being shot still gives someone the kill.
    /// </summary>
    [RequireComponent(typeof(PlaneController))]
    public class PlaneHealth : MonoBehaviour
    {
        static PlaneDurability _fallbackDurability;

        [Tooltip("How each lost part changes the handling.")]
        [SerializeField] PartFlightEffects flightEffects = new();
        [Tooltip("Body HP thresholds for Damaged / Critical and their penalties.")]
        [SerializeField] BodyDamageEffects bodyEffects = new();
        [Tooltip("Seconds a hit keeps kill credit if the plane then crashes. A crippled plane (lost part or Critical body) keeps it until repaired.")]
        [SerializeField] float killCreditWindow = 20f;
        [Tooltip("Assist: another plane that did at least this fraction of the body's max HP in damage (any parts), with a hit inside the kill credit window, or shot off a part this life.")]
        [SerializeField, Range(0f, 1f)] float assistMinDamage = 0.2f;

        PlaneController _plane;
        readonly PlanePart[] _parts = new PlanePart[PlanePart.TypeCount];
        float _lastAttackTime = float.NegativeInfinity;
        readonly Dictionary<PlaneController, AttackRecord> _attackers = new();

        /// <summary>What one enemy plane has done to us this life, for assists.</summary>
        struct AttackRecord
        {
            public float Damage;
            public float LastHitTime;
            public bool BrokePart;
        }

        public event Action<PlaneHealth, PartDamageInfo> Damaged;
        /// <summary>Raised after a detachable part has already broken off (<see cref="PlanePart.IsDetached"/>).</summary>
        public event Action<PlaneHealth, PartDamageInfo> PartDestroyed;
        /// <summary>Raised when in-air healing (Repair Aura) restores HP: the total restored across all parts, and the healer.</summary>
        public event Action<PlaneHealth, float, UnityEngine.Object> Healed;
        /// <summary>Raised when every part is back to full HP (respawn, class change, repair).</summary>
        public event Action<PlaneHealth> Restored;
        /// <summary>Raised when body HP crosses into another <see cref="BodyDamageState"/>. The argument is the previous state.</summary>
        public event Action<PlaneHealth, BodyDamageState> BodyStateChanged;
        /// <summary>Raised on every death of this plane, after <see cref="PlaneController.Crashed"/>.</summary>
        public event Action<KillInfo> Killed;
        /// <summary>Raised on every death of any plane: what scoring and the kill feed listen to.</summary>
        public static event Action<KillInfo> AnyKilled;

        public PlaneController Plane => _plane;

        public BodyDamageState BodyState { get; private set; }

        /// <summary>Scales all incoming damage (Armor Shield sets 0.5 while it's up). Owned by whoever set it.</summary>
        public float DamageTakenMultiplier { get; set; } = 1f;
        /// <summary>Takes no damage at all and credits no attacker (set by <see cref="PlaneRepair"/> while its timer runs).</summary>
        public bool IsInvulnerable { get; set; }

        /// <summary>The last damage source that wasn't this plane itself, this life. Cleared on respawn and repair.</summary>
        public UnityEngine.Object LastAttacker { get; private set; }
        /// <summary>Who got credit for this plane's most recent death.</summary>
        public KillInfo LastKill { get; private set; }

        public float KillCreditWindow
        {
            get => killCreditWindow;
            set => killCreditWindow = Mathf.Max(0f, value);
        }

        /// <summary>Fraction of the body's max HP another plane must deal (inside the kill credit window) for an assist.</summary>
        public float AssistMinDamage
        {
            get => assistMinDamage;
            set => assistMinDamage = Mathf.Clamp01(value);
        }

        /// <summary>Lost a part or the body is Critical: whoever did it keeps kill credit until the plane is repaired.</summary>
        public bool IsCrippled => LostPartsMask != 0 || BodyState == BodyDamageState.Critical;

        public PlanePart GetPart(PlanePartType type) => _parts[(int)type];

        /// <summary>Every part attached and at full HP: a fresh or fully repaired plane.</summary>
        public bool IsFullHealth
        {
            get
            {
                foreach (var part in _parts)
                    if (part != null && part.Health < part.MaxHealth)
                        return false;
                return true;
            }
        }

        /// <summary>Bitmask of destroyed parts (bit = <see cref="PlanePartType"/>); what the network will sync.</summary>
        public int LostPartsMask
        {
            get
            {
                int mask = 0;
                foreach (var part in _parts)
                    if (part != null && part.IsDestroyed)
                        mask |= PartFlightEffects.Bit(part.partType);
                return mask;
            }
        }

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            foreach (var part in GetComponentsInChildren<PlanePart>(true))
            {
                int i = (int)part.partType;
                if (_parts[i] != null)
                    Debug.LogError($"{name} has two {part.partType} parts ({_parts[i].name}, {part.name})", this);
                _parts[i] = part;
            }
            for (int i = 0; i < _parts.Length; i++)
                if (_parts[i] == null)
                    Debug.LogError($"{name} is missing a {(PlanePartType)i} part", this);
            RestoreAll();
        }

        void OnEnable()
        {
            _plane.StatsChanged += OnPlaneReset;
            _plane.Respawned += OnPlaneReset;
            _plane.Crashed += OnPlaneCrashed;
        }

        void OnDisable()
        {
            _plane.StatsChanged -= OnPlaneReset;
            _plane.Respawned -= OnPlaneReset;
            _plane.Crashed -= OnPlaneCrashed;
        }

        void OnPlaneReset(PlaneController _) => RestoreAll();

        /// <summary>Reattaches lost parts and puts every part back to full HP for the current class.</summary>
        public void RestoreAll()
        {
            LastAttacker = null;
            _lastAttackTime = float.NegativeInfinity;
            _attackers.Clear();
            var durability = Durability();
            foreach (var part in _parts)
            {
                if (part == null)
                    continue;
                part.SetDetached(false);
                part.ResetHealth(durability.GetMaxHealth(part.partType));
            }
            ApplyFlightEffects();
            UpdateBodyState(force: true);
            Restored?.Invoke(this);
        }

        /// <summary>Damages whichever of this plane's parts owns <paramref name="hitCollider"/>.</summary>
        public float ApplyDamage(Collider hitCollider, float amount, Vector3 point, UnityEngine.Object source = null) =>
            PlanePart.TryGetFromCollider(hitCollider, out var part) ? ApplyDamage(part, amount, point, source) : 0f;

        public float ApplyDamage(PlanePartType type, float amount, Vector3 point, UnityEngine.Object source = null) =>
            ApplyDamage(GetPart(type), amount, point, source);

        /// <summary>Returns the damage actually taken (0 if the part is already destroyed or not ours).</summary>
        public float ApplyDamage(PlanePart part, float amount, Vector3 point, UnityEngine.Object source = null)
        {
            if (part == null || !_plane.IsAlive || part.IsDestroyed || amount <= 0f || IsInvulnerable)
                return 0f;
            if (_parts[(int)part.partType] != part)
                return 0f; // a part from another plane

            RecordAttacker(source);
            float taken = part.TakeDamage(amount * Mathf.Max(0f, DamageTakenMultiplier));
            RecordDamageBy(source, taken, part.IsDestroyed);
            var info = new PartDamageInfo(part, taken, point, source);
            Damaged?.Invoke(this, info);
            if (part.partType == PlanePartType.Body)
                UpdateBodyState();

            if (part.IsDestroyed)
            {
                if (part.CanDetach)
                    part.SetDetached(true);
                ApplyFlightEffects();
                PartDestroyed?.Invoke(this, info);
                if (part.partType == PlanePartType.Body)
                    _plane.Crash(_plane.Position, DeathCause.ShotDown);
            }
            return taken;
        }

        /// <summary>
        /// Heals every surviving part by <paramref name="fraction"/> of its max HP. Lost parts stay lost and a dead
        /// engine stays dead: only landing restores those. Returns the total HP restored.
        /// </summary>
        public float HealFraction(float fraction, UnityEngine.Object source = null)
        {
            if (!_plane.IsAlive || fraction <= 0f)
                return 0f;
            float total = 0f;
            foreach (var part in _parts)
                if (part != null)
                    total += part.Heal(part.MaxHealth * fraction);
            if (total <= 0f)
                return 0f;
            UpdateBodyState();
            Healed?.Invoke(this, total, source);
            return total;
        }

        void RecordAttacker(UnityEngine.Object source)
        {
            if (source == null || GetPlane(source) == _plane)
                return; // dev tools and self-damage never steal credit
            LastAttacker = source;
            _lastAttackTime = Time.time;
        }

        void RecordDamageBy(UnityEngine.Object source, float taken, bool brokePart)
        {
            var attacker = GetPlane(source);
            if (attacker == null || attacker == _plane)
                return;
            _attackers.TryGetValue(attacker, out var record);
            record.Damage += taken;
            record.LastHitTime = Time.time;
            record.BrokePart |= brokePart;
            _attackers[attacker] = record;
        }

        /// <summary>Every plane but <paramref name="killer"/> that shot off a part this life, or did enough damage recently.</summary>
        List<PlaneController> Assisters(PlaneController killer)
        {
            var body = GetPart(PlanePartType.Body);
            float minDamage = assistMinDamage * (body != null ? body.MaxHealth : 0f);
            var assisters = new List<PlaneController>();
            foreach (var (plane, record) in _attackers)
                if (plane != null && plane != killer
                    && (record.BrokePart || (record.Damage >= minDamage && Time.time - record.LastHitTime <= killCreditWindow)))
                    assisters.Add(plane);
            return assisters;
        }

        /// <summary>
        /// Kill credit: the last attacker gets it if they hit within <see cref="KillCreditWindow"/>, or at any
        /// time if the plane is crippled. A plane shot down always credits whoever fired the fatal hit.
        /// </summary>
        void OnPlaneCrashed(PlaneController _, Vector3 point)
        {
            var source = LastAttacker;
            if (source != null && !IsCrippled && Time.time - _lastAttackTime > killCreditWindow)
                source = null;
            var killer = GetPlane(source);
            LastKill = new KillInfo(this, killer, source, _plane.LastDeathCause, Assisters(killer));
            Killed?.Invoke(LastKill);
            AnyKilled?.Invoke(LastKill);
        }

        /// <summary>The plane behind a damage source (the plane itself, one of its guns, ...), or null.</summary>
        public static PlaneController GetPlane(UnityEngine.Object source) => source switch
        {
            PlaneController plane => plane,
            Component c => c.GetComponentInParent<PlaneController>(),
            GameObject go => go.GetComponentInParent<PlaneController>(),
            _ => null,
        };

        void ApplyFlightEffects()
        {
            float rollRate = _plane.Stats != null ? _plane.Stats.rollRate : 0f;
            flightEffects.Apply(_plane.Modifiers, LostPartsMask, rollRate, this);
        }

        void UpdateBodyState(bool force = false)
        {
            var body = GetPart(PlanePartType.Body);
            var state = body != null ? bodyEffects.GetState(body.NormalizedHealth) : BodyDamageState.Healthy;
            var previous = BodyState;
            if (state == previous && !force)
                return;
            BodyState = state;
            bodyEffects.Apply(_plane.Modifiers, state, bodyEffects);
            if (state != previous)
                BodyStateChanged?.Invoke(this, previous);
        }

        PlaneDurability Durability()
        {
            var stats = _plane.Stats;
            if (stats != null && stats.durability != null)
                return stats.durability;
            if (_fallbackDurability == null)
                _fallbackDurability = ScriptableObject.CreateInstance<PlaneDurability>();
            return _fallbackDurability;
        }
    }
}

using System.Collections.Generic;
using UnityEngine;
using WingingIt.Effects;

namespace WingingIt.Flight
{
    /// <summary>
    /// Presentation for the modular plane: the class's finished model (or the tinted placeholder boxes) and team colors,
    /// class scale, spinning propeller, control surfaces deflecting with the turn rates, landing gear folding,
    /// parts breaking off as tumbling, smoking debris, damage smoke and fire from the body state
    /// (and a dead engine), a cheap break-apart when the plane crashes (a ground blast, or a splash on the sea),
    /// and a mid-air "BOOM!" with a burning, falling wreck when it's shot down. Effects come from the VFX pack (<see cref="Vfx"/>).
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
        [Tooltip("How fast a class model's radar dome turns.")]
        [SerializeField] float radarRpm = 12f;
        [Tooltip("Control surface throw at a full-rate roll, pitch and yaw, degrees.")]
        [SerializeField] float aileronThrow = 20f, elevatorThrow = 20f, rudderThrow = 25f;
        [SerializeField] float debrisLifetime = 6f;
        [Tooltip("Wreckage of a plane shot down in mid-air; long enough to fall to the ground.")]
        [SerializeField] float wreckLifetime = 12f;

        [Tooltip("Landing gear legs: each folds by its angles when the gear is up and hides once fully stowed.")]
        [SerializeField] GearLeg[] gearLegs;

        [Header("Class models")]
        [Tooltip("The placeholder boxes' renderers, hidden while the class flown has its own model. Hitboxes stay.")]
        [SerializeField] Renderer[] placeholderRenderers;
        [Tooltip("Finished models: each shows only while its class is flown.")]
        [SerializeField] ClassModel[] classModels;
        [Tooltip("The gun muzzles (PlaneGuns' order): a class model can move them onto its own barrels.")]
        [SerializeField] Transform[] muzzles;
        [Tooltip("The rocket rails (PlaneRockets' order): a class model can move them onto its own pods.")]
        [SerializeField] Transform[] rails;
        [Tooltip("The wing hitboxes (left, right): a class model whose wing sits elsewhere (a high wing) moves them onto it.")]
        [SerializeField] Transform[] wingHitboxes;
        [Tooltip("Team-zone material (wingtips, tail, roundel): tinted with the plane's team color.")]
        [SerializeField] Material teamMaterial;
        [Tooltip("Team colors without a match (team 0, team 1).")]
        [SerializeField] Color[] fallbackTeamColors = { new(0.35f, 0.6f, 1f), new(1f, 0.38f, 0.32f) };

        /// <summary>The running match's team colors (high-contrast setting included), or null for the fallbacks.</summary>
        public static System.Func<int, Color> TeamColors { get; set; }

        /// <summary>The team ramp's base tone in PlanePalette.png: the team tint scales it up to the team color.</summary>
        const float TeamSwatchBase = 215f / 255f;

        enum SmokeLevel { None, Light, Heavy }

        /// <summary>One class's finished model, spread over the parts, gear legs and broken edges of the shared prefab.</summary>
        [System.Serializable]
        public struct ClassModel
        {
            public FlightStats stats;
            [Tooltip("Roots of the model's pieces; shown only while this class is flown.")]
            public GameObject[] pieces;
            [Tooltip("Local positions for the muzzles (same order) on this model's barrels; empty keeps the prefab's.")]
            public Vector3[] muzzlePositions;
            [Tooltip("More propellers that spin with the engine (e.g. a twin's, on the wings), besides the nose one.")]
            public Transform[] propellers;
            [Tooltip("Local positions for the rocket rails (same order) at this model's pods; empty keeps the prefab's.")]
            public Vector3[] railPositions;
            [Tooltip("Local positions for the wing hitboxes (left, right) on this model's wings; empty keeps the prefab's.")]
            public Vector3[] wingHitboxPositions;
            [Tooltip("Radar domes that turn slowly about their vertical axis while the plane flies.")]
            public Transform[] radars;
            [Tooltip("Ailerons, elevators and rudder: each turns about its hinge with the plane's turn rates.")]
            public ControlSurface[] surfaces;
        }

        public enum ControlSurfaceKind { AileronLeft, AileronRight, Elevator, Rudder }

        /// <summary>
        /// A moving control surface, its pivot on the hinge line. Riding on its wing or tail part, it leaves with that part.
        /// </summary>
        [System.Serializable]
        public struct ControlSurface
        {
            public Transform pivot;
            public ControlSurfaceKind kind;
            [Tooltip("Hinge axis in the pivot's parent space: toward +x for ailerons and elevators, +y for a rudder.")]
            public Vector3 axis;
        }

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
        VfxEffect _smoke, _fire;
        SmokeLevel _smokeLevel;
        bool _fireOn;

        MaterialPropertyBlock _block;
        readonly List<Renderer> _hidden = new();
        readonly List<(Renderer renderer, int index)> _teamSlots = new();
        Vector3[] _muzzleDefaults, _railDefaults, _hitboxDefaults;
        Transform[] _extraPropellers, _radars;
        ControlSurface[] _surfaces;
        List<Renderer> _discs;
        Color _teamColor;
        bool _teamTinted;

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
            plane.TouchedDown += OnTouchedDown;
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
            plane.TouchedDown -= OnTouchedDown;
        }

        void Update()
        {
            if (!plane.IsAlive)
                return;
            bool spinning = plane.Throttle > 0.02f || plane.Speed > 1f || plane.IsBoosting;
            UpdatePropDiscs(spinning);
            float throttle = plane.IsBoosting ? 1.3f : Mathf.Lerp(0.3f, 1f, plane.Throttle);
            float degrees = spinning ? propellerMaxRpm * 6f * throttle * plane.EffectiveThrust * Time.deltaTime : 0f;
            if (propeller != null)
                propeller.Rotate(0f, 0f, degrees, Space.Self);
            if (_extraPropellers != null)
                foreach (var p in _extraPropellers)
                    if (p != null)
                        p.Rotate(0f, 0f, degrees, Space.Self);
            if (_radars != null)
                foreach (var r in _radars)
                    if (r != null)
                        r.Rotate(0f, radarRpm * 6f * Time.deltaTime, 0f, Space.Self);
            UpdateControlSurfaces();
        }

        /// <summary>The colour the team zones are painted (white until first tinted).</summary>
        public Color TeamColor => _teamTinted ? _teamColor : Color.white;

        /// <summary>The flown class's control surfaces (empty for the placeholder boxes).</summary>
        public IReadOnlyList<ControlSurface> ControlSurfaces => _surfaces ?? System.Array.Empty<ControlSurface>();

        /// <summary>
        /// Deflects the surfaces with the plane's turn rates (stick input plus auto-level and damage, already eased by the
        /// flight model), each as a share of the class's full rate.
        /// </summary>
        void UpdateControlSurfaces()
        {
            if (_surfaces == null || _surfaces.Length == 0 || plane.Stats == null)
                return;
            var stats = plane.Stats;
            Vector3 rates = plane.AngularRates;
            var share = new Vector3(Share(rates.x, stats.pitchRate), Share(rates.y, stats.yawRate), Share(rates.z, stats.rollRate));
            foreach (var s in _surfaces)
                if (s.pivot != null)
                    s.pivot.localRotation = Quaternion.AngleAxis(SurfaceAngle(s.kind, share), s.axis);
        }

        static float Share(float rate, float fullRate) => fullRate > 0f ? Mathf.Clamp(rate / fullRate, -1f, 1f) : 0f;

        /// <summary>
        /// A surface's turn about its hinge axis for turn rates given as shares of full rate (x pitch, y yaw, z roll).
        /// About +x a positive angle lifts the trailing edge; about +y it swings it left. So rolling right lifts the
        /// right aileron and drops the left, pulling up lifts the elevators, and yawing right swings the rudder right.
        /// </summary>
        public float SurfaceAngle(ControlSurfaceKind kind, Vector3 share) => kind switch
        {
            ControlSurfaceKind.AileronLeft => -share.z * aileronThrow,
            ControlSurfaceKind.AileronRight => share.z * aileronThrow,
            ControlSurfaceKind.Elevator => share.x * elevatorThrow,
            _ => -share.y * rudderThrow,
        };

        /// <summary>The blur discs only show while the prop turns; a stopped prop is just its blades.</summary>
        void UpdatePropDiscs(bool spinning)
        {
            if (_discs == null)
            {
                _discs = new List<Renderer>();
                foreach (var model in classModels)
                foreach (var piece in model.pieces)
                    if (piece != null && piece.name.EndsWith("PropDisc"))
                        _discs.AddRange(piece.GetComponentsInChildren<Renderer>(true));
            }
            foreach (var r in _discs)
                if (r != null && r.enabled != spinning)
                    r.enabled = spinning;
        }

        void LateUpdate()
        {
            UpdateDamageSmoke();
            UpdateGear();
            UpdateTeamColor();
        }

        /// <summary>Tints the team zones whenever the team (or its color setting) changes.</summary>
        void UpdateTeamColor()
        {
            if (teamMaterial == null)
                return;
            int team = plane.Team;
            Color color = TeamColors != null ? TeamColors(team)
                : fallbackTeamColors.Length > 0 ? fallbackTeamColors[Mathf.Clamp(team, 0, fallbackTeamColors.Length - 1)]
                : Color.white;
            if (_teamTinted && color == _teamColor)
                return;
            _teamColor = color;
            _teamTinted = true;

            if (_teamSlots.Count == 0)
                foreach (var model in classModels)
                foreach (var piece in model.pieces)
                {
                    if (piece == null)
                        continue;
                    foreach (var r in piece.GetComponentsInChildren<Renderer>(true))
                    {
                        var mats = r.sharedMaterials;
                        for (int i = 0; i < mats.Length; i++)
                            if (mats[i] == teamMaterial)
                                _teamSlots.Add((r, i));
                    }
                }

            var tinted = TeamMaterial(teamMaterial, color);
            foreach (var (r, index) in _teamSlots)
            {
                if (r == null)
                    continue;
                var mats = r.sharedMaterials;
                if (mats[index] == tinted)
                    continue;
                mats[index] = tinted;
                r.sharedMaterials = mats;
            }
        }

        static readonly Dictionary<(Material source, Color color), Material> TeamMaterials = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetTeamMaterials() => TeamMaterials.Clear();

        /// <summary>
        /// <paramref name="source"/> tinted to <paramref name="color"/>, one copy per color shared by every plane and
        /// airfield on that team. A shared material (not a property block per renderer) keeps the pieces in the SRP
        /// Batcher, so a sky full of planes doesn't cost a full draw call per piece.
        /// </summary>
        public static Material TeamMaterial(Material source, Color color)
        {
            if (TeamMaterials.TryGetValue((source, color), out var material) && material != null)
                return material;
            material = new Material(source) { name = source.name, hideFlags = HideFlags.DontSave };
            material.SetVector(BaseColorId, TeamTint(color));
            TeamMaterials[(source, color)] = material;
            return material;
        }

        /// <summary>
        /// The team material's raw _BaseColor for <paramref name="team"/>: the texture's ramp is grey, so it's scaled
        /// so the base tone lands exactly on the team color. Raw (set with SetVector), since the scale has to happen in
        /// the shader's color space.
        /// </summary>
        public static Vector4 TeamTint(Color team)
        {
            bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;
            Color c = linear ? team.linear : team;
            float b = linear ? Mathf.GammaToLinearSpace(TeamSwatchBase) : TeamSwatchBase;
            return new Vector4(c.r / b, c.g / b, c.b / b, 1f);
        }

        /// <summary>
        /// Shows the class's own model, if it has one, in place of the placeholder boxes. Waits while the plane is down
        /// (its renderers are hidden until the respawn re-applies it).
        /// </summary>
        void ApplyClassModel()
        {
            if (!plane.IsAlive || classModels == null)
                return;
            var stats = plane.Stats;
            bool hasModel = false;
            Vector3[] muzzlePositions = null, railPositions = null, hitboxPositions = null;
            _extraPropellers = _radars = null;
            _surfaces = null;
            foreach (var model in classModels)
                if (model.stats != null && model.stats == stats)
                {
                    hasModel = true;
                    muzzlePositions = model.muzzlePositions;
                    railPositions = model.railPositions;
                    hitboxPositions = model.wingHitboxPositions;
                    _extraPropellers = model.propellers;
                    _radars = model.radars;
                    _surfaces = model.surfaces;
                }
            foreach (var model in classModels)
            foreach (var piece in model.pieces)
                if (piece != null)
                    piece.SetActive(model.stats == stats);
            foreach (var r in placeholderRenderers)
                if (r != null)
                    r.enabled = !hasModel;
            Place(muzzles, ref _muzzleDefaults, muzzlePositions);
            Place(rails, ref _railDefaults, railPositions);
            Place(wingHitboxes, ref _hitboxDefaults, hitboxPositions);
        }

        /// <summary>
        /// Moves gun muzzles, rocket rails or wing hitboxes to the class model's positions for them, or back where the
        /// prefab has them.
        /// </summary>
        static void Place(Transform[] points, ref Vector3[] defaults, Vector3[] positions)
        {
            if (points == null || points.Length == 0)
                return;
            if (defaults == null)
            {
                defaults = new Vector3[points.Length];
                for (int i = 0; i < points.Length; i++)
                    if (points[i] != null)
                        defaults[i] = points[i].localPosition;
            }
            bool own = positions != null && positions.Length == points.Length;
            for (int i = 0; i < points.Length; i++)
                if (points[i] != null)
                    points[i].localPosition = own ? positions[i] : defaults[i];
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

        /// <summary>Grey wisps when Damaged; thick black smoke when Critical or the engine is dead; flames too when Critical.</summary>
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

            // A stopped emitter lets its puffs drift away behind the plane, so a change of level never pops.
            if (level != _smokeLevel)
            {
                _smokeLevel = level;
                _smoke?.Stop();
                _smoke = level == SmokeLevel.None ? null
                    : AttachAtEngine(level == SmokeLevel.Heavy ? VfxKind.DamageSmokeHeavy : VfxKind.DamageSmokeLight);
            }
            if (fire != _fireOn)
            {
                _fireOn = fire;
                _fire?.Stop();
                _fire = fire ? AttachAtEngine(VfxKind.DamageFire) : null;
            }
        }

        /// <summary>
        /// A looping effect streaming from the engine (the nose), behind the propeller. It rides on the plane itself,
        /// not the engine part, which can break off.
        /// </summary>
        VfxEffect AttachAtEngine(VfxKind kind)
        {
            var engine = _health != null ? _health.GetPart(PlanePartType.Engine) : null;
            Vector3 local = engine != null ? plane.transform.InverseTransformPoint(engine.transform.position) : Vector3.zero;
            return Vfx.Attach(kind, plane.transform, local, ClassScale);
        }

        float ClassScale => plane.Stats != null ? plane.Stats.visualScale : 1f;

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
            ApplyClassModel();
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
            Vfx.Attach(VfxKind.DebrisSmoke, debris.transform, Vector3.zero);
            Vfx.Play(VfxKind.PartDetach, info.Part.transform.position, ClassScale);
        }

        void OnCrashed(PlaneController _, Vector3 point)
        {
            Vector3 velocity = plane.Velocity;
            bool shotDown = plane.LastDeathCause is DeathCause.ShotDown or DeathCause.OutOfBounds or DeathCause.Rammed; // all break up in mid-air
            bool ejected = plane.LastDeathCause == DeathCause.Ejected; // the empty plane flies on, smoking, in one piece
            _hidden.Clear();
            var wreck = ejected ? new List<Collider>() : null;

            // Throw a copy of every part still attached, then hide the real plane until respawn.
            // Shot down in the air, the wreck keeps flying along its path, burning, until it hits the ground;
            // a crash into terrain bounces the pieces back off the impact. Ejected, the pieces all keep the plane's
            // velocity, so the empty plane falls on whole until it hits something.
            foreach (var part in GetComponentsInChildren<PlanePart>())
            {
                var debris = SpawnDebris(part.gameObject, part.transform.position, shotDown || ejected ? wreckLifetime : debrisLifetime);
                Vector3 away = (debris.position - point).normalized;
                if (ejected)
                {
                    debris.linearVelocity = velocity;
                    debris.angularVelocity = Vector3.zero;
                    debris.linearDamping = 0.05f;
                    wreck.AddRange(debris.GetComponentsInChildren<Collider>());
                    if (part.partType is PlanePartType.Body or PlanePartType.Engine)
                        Vfx.Attach(VfxKind.DebrisSmoke, debris.transform, Vector3.zero, 1.3f);
                }
                else if (shotDown)
                {
                    debris.linearVelocity = velocity * 0.7f + away * Random.Range(6f, 14f) + Vector3.up * Random.Range(2f, 8f);
                    debris.angularVelocity = Random.insideUnitSphere * 4f;
                    debris.linearDamping = 0.3f;
                    Vfx.Attach(VfxKind.DebrisSmoke, debris.transform, Vector3.zero, 1.3f);
                    if (part.partType is PlanePartType.Body or PlanePartType.Engine)
                        Vfx.Attach(VfxKind.DamageFire, debris.transform, Vector3.zero, 1.5f);
                }
                else
                {
                    debris.linearVelocity = velocity * 0.35f + away * Random.Range(8f, 20f) + Vector3.up * Random.Range(4f, 12f);
                    debris.angularVelocity = Random.insideUnitSphere * 6f;
                }
            }

            if (wreck != null) // the pieces start touching: keep them from shoving each other apart in the air
                for (int i = 0; i < wreck.Count; i++)
                    for (int j = i + 1; j < wreck.Count; j++)
                        Physics.IgnoreCollision(wreck[i], wreck[j]);

            UpdateDamageSmoke(); // the plane is down: stop the damage smoke now, so it drifts off instead of hiding
            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled || r is ParticleSystemRenderer) // effects finish on their own
                    continue;
                r.enabled = false;
                _hidden.Add(r);
            }

            if (ejected)
                return; // no blast: the plane is still flying
            if (shotDown)
                Vfx.Play(VfxKind.ExplosionPlane, point, ClassScale);
            else if (Physics.CheckSphere(point, 2f, 1 << PlaneController.WaterLayer, QueryTriggerInteraction.Ignore))
                Vfx.Play(VfxKind.WaterSplash, point, ClassScale); // ditched
            else
                Explosions.PlayGround(point, EffectsSurface.Below(point), 1.2f * ClassScale);
        }

        void OnRespawned(PlaneController _)
        {
            foreach (var r in _hidden)
                if (r != null)
                    r.enabled = true;
            _hidden.Clear();
            ApplyClassModel(); // the class may have changed while the plane was down
            if (Time.timeScale == 0f)
                return; // the menu or scoreboard has time stopped: the sparkles would hang frozen in the air
            Vfx.Attach(VfxKind.RespawnShimmer, plane.transform, Vector3.zero, ClassScale);
        }

        /// <summary>Tyre smoke under each main wheel, more for a harder landing; a bounce kicks up more still.</summary>
        void OnTouchedDown(PlaneController _, TouchdownInfo info)
        {
            if (info.Result == TouchdownResult.Crashed || gearLegs == null)
                return;
            float scale = Mathf.Clamp(0.8f + info.SinkRate / 3f, 0.8f, 2f) * (info.Result == TouchdownResult.Bounced ? 1.5f : 1f);
            float groundY = plane.Position.y - (_gear != null ? _gear.WheelClearance : 0f);
            for (int i = 0; i < gearLegs.Length && i < 2; i++) // the mains come first; the nose wheel touches later
            {
                if (gearLegs[i].pivot == null)
                    continue;
                var wheel = gearLegs[i].pivot.position;
                Vfx.Play(VfxKind.Touchdown, new Vector3(wheel.x, groundY, wheel.z), scale * ClassScale);
            }
        }

        /// <summary>A physics copy of <paramref name="part"/> that despawns after a while. Works on a detached (inactive) part.</summary>
        Rigidbody SpawnDebris(GameObject part, Vector3 position, float lifetime)
        {
            var copy = Instantiate(part, position, part.transform.rotation);
            copy.transform.localScale = part.transform.lossyScale;
            copy.name = part.name + " (Debris)";
            CopyTints(part, copy);
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

            copy.AddComponent<PlaneDebris>().Expire(lifetime);
            var body = copy.AddComponent<Rigidbody>();
            body.mass = 50f;
            return body;
        }

        /// <summary>
        /// Property blocks (the class tint) aren't cloned with the object, so carry them over to debris. The team tint
        /// is a shared material, which the clone already has.
        /// </summary>
        static void CopyTints(GameObject source, GameObject copy)
        {
            var from = source.GetComponentsInChildren<Renderer>(true);
            var to = copy.GetComponentsInChildren<Renderer>(true);
            var block = new MaterialPropertyBlock();
            for (int i = 0; i < from.Length && i < to.Length; i++)
                if (from[i].HasPropertyBlock())
                {
                    from[i].GetPropertyBlock(block);
                    to[i].SetPropertyBlock(block);
                }
        }
    }
}

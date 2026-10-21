using System.Collections.Generic;
using UnityEngine;
using WingingIt.Effects;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// Ground effect (living-world.md, section 1): a plane flying low and fast kicks up the surface under it, in trails
    /// under each wing and a cluster under the nose, left hanging in the air behind it. Sand on the beaches, brown dirt
    /// over grass, grey dust over rock and paving, white mist and droplets over the sea; roofs and bridges give nothing.
    /// It thickens as the plane drops, so a banked plane kicks up its low side more. The colour blends as the surface
    /// changes, so crossing a beach shifts smoothly from sand to spray. Only the <see cref="MaxEmitters"/> nearest planes
    /// to the camera (within <see cref="range"/>) emit.
    /// </summary>
    public class GroundKickup : MonoBehaviour
    {
        /// <summary>Most planes kicking up the ground at once: the nearest to the camera.</summary>
        public const int MaxEmitters = 4;
        /// <summary>The surface below is looked up this often (s); the emitters follow the ground every frame.</summary>
        public const float SurfaceInterval = 0.1f;
        /// <summary>A change of surface blends the colour over about this long (s).</summary>
        public const float BlendTime = 0.5f;
        /// <summary>Puffs are full size from this height (m) up, and smaller below it, so they stay under the plane.</summary>
        public const float FullSizeHeight = 8f;

        [SerializeField] PlaneController plane;
        [Tooltip("Height above the surface (m) where the kick-up starts: a few wisps just under it, a thick plume near the ground.")]
        [SerializeField] float maxHeight = 40f;
        [Tooltip("Slowest it starts, as a fraction of the stall speed (slow taxiing gives tyre smoke instead).")]
        [SerializeField, Range(0f, 1f)] float minSpeed = 0.6f;
        [Tooltip("Gap (m) from the middle to each wing trail, for a class of visual scale 1.")]
        [SerializeField] float wingOffset = 6f;
        [Tooltip("How far (degrees) the wing trails spray outward from straight up, out past the wingtips where they show.")]
        [SerializeField] float wingSplay = 35f;
        [Tooltip("How far ahead (m) of the middle the nose cluster kicks up, for a class of visual scale 1.")]
        [SerializeField] float noseOffset = 2.5f;
        [Tooltip("Planes further than this from the camera (m) don't kick anything up.")]
        [SerializeField] float range = 300f;

        static readonly List<GroundKickup> All = new();
        static readonly RaycastHit[] Hits = new RaycastHit[8];
        static readonly List<float> Distances = new();

        VfxEffect _effect;
        int _left = -1, _right = -1, _nose = -1, _droplets = -1;
        GroundSurface _target = GroundSurface.None;
        Color _color = Color.white;
        float _amount, _nextLookup;
        float _cameraDistance = float.PositiveInfinity;
        bool _wants;

        /// <summary>The surface under the plane, as last looked up.</summary>
        public GroundSurface Surface => _target;
        /// <summary>How strongly it's kicking up right now, 0..1 (height, speed and the surface's amount together).</summary>
        public float Strength { get; private set; }
        /// <summary>The light tone of the puffs it emits now.</summary>
        public Color CurrentColor => _color;
        /// <summary>True while it has an emitter out (it's low, fast and among the nearest to the camera).</summary>
        public bool IsEmitting => _effect != null;

        /// <summary>
        /// How strongly a plane <paramref name="height"/> m above the surface at <paramref name="speed"/> kicks it up, 0..1:
        /// nothing at or above <paramref name="maxHeight"/> or below <paramref name="minSpeed"/> of the stall speed, then
        /// thicker the lower it gets (linearly) and the faster it goes (from 35% at the slowest to all of it at top speed).
        /// </summary>
        public static float StrengthAt(float height, float speed, float stallSpeed, float maxSpeed, float maxHeight = 40f, float minSpeed = 0.6f)
        {
            float slowest = minSpeed * stallSpeed;
            if (speed < slowest || height >= maxHeight)
                return 0f;
            float low = 1f - Mathf.Clamp01(height / Mathf.Max(1f, maxHeight));
            float fast = Mathf.Lerp(0.35f, 1f, Mathf.InverseLerp(slowest, Mathf.Max(slowest + 1f, maxSpeed), speed));
            return low * fast;
        }

        /// <summary>
        /// Puff size for a strength at <paramref name="height"/> m: half size for the faintest wisps, full size for the
        /// thickest plume, but shrunk (to 30% on the deck) below <see cref="FullSizeHeight"/>, so the dust stays under
        /// the plane and never fills the chase camera's view.
        /// </summary>
        public static float SizeAt(float strength, float height) =>
            strength <= 0f ? 0f : Mathf.Lerp(0.5f, 1f, strength) * Mathf.Clamp(height / FullSizeHeight, 0.3f, 1f);

        /// <summary>Whether a plane <paramref name="distance"/> m from the camera is among the <see cref="MaxEmitters"/> nearest that want to emit.</summary>
        public static bool IsAmongNearest(float distance, IReadOnlyList<float> othersWanting, int maxEmitters = MaxEmitters)
        {
            int closer = 0;
            for (int i = 0; i < othersWanting.Count; i++)
                if (othersWanting[i] < distance && ++closer >= maxEmitters)
                    return false;
            return true;
        }

        void Awake()
        {
            if (plane == null)
                plane = GetComponent<PlaneController>();
        }

        void OnEnable() => All.Add(this);

        void OnDisable()
        {
            All.Remove(this);
            _wants = false;
            Release();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            _cameraDistance = cam != null ? Vector3.Distance(cam.transform.position, transform.position) : 0f;
            _wants = false;
            Strength = 0f;
            if (plane == null || !plane.IsAlive || plane.Stats == null || _cameraDistance > range || !TryGround(out var ground))
            {
                Release();
                return;
            }

            float dt = Time.deltaTime;
            if (Time.time >= _nextLookup)
            {
                _nextLookup = Time.time + SurfaceInterval;
                _target = EffectsSurface.Classify(ground.collider, ground.point);
            }
            // The amount and colour ease toward the surface's (a roof fades it out, a beach shades sand into spray).
            float blend = 1f - Mathf.Exp(-dt * 3f / BlendTime);
            _amount = Mathf.Lerp(_amount, EffectsSurface.Amount(_target), blend);
            if (_target != GroundSurface.None)
                _color = Color.Lerp(_color, EffectsSurface.Color(_target), blend);

            var stats = plane.Stats;
            float maxSpeed = plane.Modifiers.Get(FlightStat.MaxSpeed);
            float StrengthFor(float height) => StrengthAt(height, plane.Speed, stats.stallSpeed, maxSpeed, maxHeight, minSpeed) * _amount;

            var t = transform;
            float classScale = stats.visualScale;
            Vector3 right = Vector3.ProjectOnPlane(t.right, Vector3.up).normalized * (wingOffset * classScale);
            bool water = _target == GroundSurface.Water;
            float groundY = water ? OceanWaves.Height(t.position.x, t.position.z, Time.timeSinceLevelLoad) : ground.point.y;
            float middleHeight = t.position.y - groundY;
            // Each wing trail thickens with that wingtip's own height, so a bank kicks up the low side more.
            float leftHeight = (t.position - t.right * (wingOffset * classScale)).y - groundY;
            float rightHeight = (t.position + t.right * (wingOffset * classScale)).y - groundY;
            float middle = StrengthFor(middleHeight), left = StrengthFor(leftHeight), rightSide = StrengthFor(rightHeight);
            Strength = Mathf.Max(middle, Mathf.Max(left, rightSide));
            _wants = Strength > 0.01f;
            if (!_wants || !IsAmongNearest(_cameraDistance, OthersWanting()))
            {
                Release();
                return;
            }

            if (_effect == null && !Begin())
                return;
            Vector3 flatForward = Vector3.ProjectOnPlane(t.forward, Vector3.up);
            if (flatForward.sqrMagnitude < 0.01f)
                flatForward = Vector3.ProjectOnPlane(t.up, Vector3.up);
            var root = _effect.transform;
            root.SetPositionAndRotation(new Vector3(t.position.x, groundY, t.position.z), Quaternion.LookRotation(flatForward.normalized));
            Place(_left, root.position - right, left, leftHeight, wingSplay);
            Place(_right, root.position + right, rightSide, rightHeight, -wingSplay);
            Place(_nose, root.position + flatForward.normalized * (noseOffset * classScale), middle, middleHeight, 0f);
            Place(_droplets, root.position, water ? middle : 0f, FullSizeHeight, 0f);
        }

        /// <summary>
        /// Moves one wing or nose emitter, tilts it <paramref name="splay"/> degrees about the heading (out to the side),
        /// and sets how thick it kicks up from <paramref name="height"/> m below, in the blended surface colour.
        /// </summary>
        void Place(int system, Vector3 position, float strength, float height, float splay)
        {
            if (system < 0)
                return;
            var emitter = _effect.transform.GetChild(system); // the systems are the effect's direct children, in order
            emitter.SetPositionAndRotation(position, _effect.transform.rotation * Quaternion.Euler(0f, 0f, splay));
            bool water = _target == GroundSurface.Water;
            _effect.Tune(system, strength, SizeAt(strength, height) * (water ? 1.1f : 1f));
            if (system != _droplets)
                _effect.SetColor(system, _color, VfxEffect.Shade(_color, 0.85f));
        }

        bool Begin()
        {
            _effect = Vfx.Attach(VfxKind.GroundKickup, transform, Vector3.zero);
            if (_effect == null)
                return false;
            _left = _effect.IndexOf("Left");
            _right = _effect.IndexOf("Right");
            _nose = _effect.IndexOf("Nose");
            _droplets = _effect.IndexOf("Droplets");
            if (_target != GroundSurface.None)
                _color = EffectsSurface.Color(_target); // no blend from wherever it last kicked up
            return true;
        }

        void Release()
        {
            if (_effect == null)
                return;
            _effect.Stop();
            _effect = null;
        }

        List<float> OthersWanting()
        {
            Distances.Clear();
            foreach (var other in All)
                if (other != this && other._wants)
                    Distances.Add(other._cameraDistance);
            return Distances;
        }

        /// <summary>The nearest solid surface straight below, skipping planes and debris (anything with a rigidbody).</summary>
        bool TryGround(out RaycastHit ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(transform.position, Vector3.down, Hits, maxHeight + wingOffset * 2f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].rigidbody != null || Hits[i].distance >= best)
                    continue;
                best = Hits[i].distance;
                ground = Hits[i];
            }
            return best < float.MaxValue;
        }
    }
}

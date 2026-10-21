using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using WingingIt.World;

namespace WingingIt.Flight
{
    /// <summary>
    /// The altitude shadow: a soft ellipse cast straight down onto whatever is under the plane (terrain, roofs, the sea),
    /// lying flat on its slope, turned with the heading and squashed by bank and pitch. It shrinks and fades with height,
    /// so it reads as an altitude cue. Up close the plane's real (shadow-map) silhouette is the shadow: the blob only fades
    /// in where that can't reach (past the shadow distance from the camera, or with Shadows off), so there's never two.
    /// </summary>
    public class PlaneShadow : MonoBehaviour
    {
        static readonly int StrengthId = Shader.PropertyToID("_Strength");

        [SerializeField] PlaneController plane;
        [Tooltip("The Blob Shadow material (Winging It/Blob Shadow).")]
        [SerializeField] Material material;
        [Tooltip("Span (x) and length (y) of the shadow in meters, for a class of visual scale 1, flying level.")]
        [SerializeField] Vector2 size = new(10f, 8.5f);
        [Tooltip("Height above the surface at which the shadow has faded out completely.")]
        [SerializeField] float fadeHeight = 300f;
        [Tooltip("Size at the fade height, as a fraction of the size on the ground.")]
        [SerializeField, Range(0.1f, 1f)] float highScale = 0.55f;
        [Tooltip("Darkness straight under a plane on the ground (the material's _Strength is overridden).")]
        [SerializeField, Range(0f, 1f)] float strength = 0.6f;
        [Tooltip("The blob fades in over this last fraction of the shadow distance, as the real shadow runs out.")]
        [SerializeField, Range(0.05f, 0.5f)] float handoverBand = 0.2f;

        const float Lift = 0.1f; // above the surface, on top of the shader's depth offset

        static Mesh _quad;
        static readonly RaycastHit[] Hits = new RaycastHit[8];

        Transform _blob;
        MeshRenderer _renderer;
        MaterialPropertyBlock _block;

        /// <summary>Size multiplier and darkness (0..1) of the shadow at <paramref name="height"/> above the surface.</summary>
        public static (float scale, float fade) Shape(float height, float fadeHeight, float highScale)
        {
            float t = Mathf.Clamp01(height / Mathf.Max(1f, fadeHeight));
            return (Mathf.Lerp(1f, highScale, t), 1f - t * t); // stays readable through the low and middle heights
        }

        /// <summary>
        /// How much of the blob shows (0..1) for a shadow <paramref name="distance"/> m from the camera: none while the
        /// shadow map covers it, rising over the last <paramref name="band"/> of <paramref name="shadowDistance"/>, all of
        /// it beyond (and everywhere with no shadow map, a distance of 0).
        /// </summary>
        public static float Handover(float distance, float shadowDistance, float band)
        {
            if (shadowDistance <= 0f)
                return 1f;
            return Mathf.InverseLerp(shadowDistance * (1f - band), shadowDistance, distance);
        }

        /// <summary>
        /// The shadow's footprint on a surface with normal <paramref name="normal"/>: its forward axis (the heading, flat
        /// on the surface) and how much of the span and length survive the plane's bank and pitch (0.15..1).
        /// </summary>
        public static (Vector3 forward, float span, float length) Footprint(Vector3 planeForward, Vector3 planeRight, Vector3 planeUp,
            Vector3 normal)
        {
            Vector3 forward = Vector3.ProjectOnPlane(planeForward, normal);
            float length = forward.magnitude;
            if (length < 0.05f) // nose straight down or up: the top or belly of the plane faces the ground
                forward = Vector3.ProjectOnPlane(planeUp, normal);
            float span = Vector3.ProjectOnPlane(planeRight, normal).magnitude;
            return (forward.normalized, Mathf.Max(0.15f, span), Mathf.Max(0.15f, length));
        }

        void Awake()
        {
            if (plane == null)
                plane = GetComponent<PlaneController>();
            if (material == null)
                return;
            _blob = new GameObject($"{name} Shadow").transform;
            _blob.gameObject.AddComponent<MeshFilter>().sharedMesh = Quad();
            _renderer = _blob.gameObject.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = material;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _block = new MaterialPropertyBlock();
            _blob.gameObject.SetActive(false);
        }

        void OnDisable()
        {
            if (_blob != null)
                _blob.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (_blob != null)
                Destroy(_blob.gameObject);
        }

        void LateUpdate()
        {
            if (_blob == null)
                return;
            if (plane == null || !plane.IsAlive || !TryGround(out var ground))
            {
                _blob.gameObject.SetActive(false);
                return;
            }

            var (scale, fade) = Shape(ground.distance, fadeHeight, highScale);
            var cam = Camera.main;
            if (cam != null)
                fade *= Handover(Vector3.Distance(cam.transform.position, ground.point), MapShadowDistance(), handoverBand);
            if (fade <= 0f)
            {
                _blob.gameObject.SetActive(false);
                return;
            }

            var t = transform;
            var (forward, span, length) = Footprint(t.forward, t.right, t.up, ground.normal);
            float classScale = plane.Stats != null ? plane.Stats.visualScale : 1f;
            // On the sea, ride above the tallest wave: the collider is flat but the surface you see isn't.
            float lift = PlaneController.IsWater(ground.collider) ? Lift + OceanWaves.MaxHeight : Lift;
            _blob.SetPositionAndRotation(ground.point + ground.normal * lift, Quaternion.LookRotation(forward, ground.normal));
            _blob.localScale = new Vector3(size.x * span, 1f, size.y * length) * (scale * classScale);
            _block.SetFloat(StrengthId, strength * fade);
            _renderer.SetPropertyBlock(_block);
            _blob.gameObject.SetActive(true);
        }

        /// <summary>How far the sun's shadow map reaches from the camera (0: no sun shadows at all).</summary>
        static float MapShadowDistance()
        {
            var sun = RenderSettings.sun;
            if (sun == null || !sun.isActiveAndEnabled || sun.shadows == LightShadows.None)
                return 0f;
            return GraphicsSettings.currentRenderPipeline is UniversalRenderPipelineAsset urp ? urp.shadowDistance : QualitySettings.shadowDistance;
        }

        /// <summary>The nearest solid surface straight below, skipping planes and debris (anything with a rigidbody).</summary>
        bool TryGround(out RaycastHit ground)
        {
            ground = default;
            int count = Physics.RaycastNonAlloc(transform.position, Vector3.down, Hits, fadeHeight, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
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

        /// <summary>A 1 m quad flat in XZ (normal +Y), UVs 0..1 across it.</summary>
        static Mesh Quad()
        {
            if (_quad != null)
                return _quad;
            _quad = new Mesh { name = "Blob Shadow Quad" };
            _quad.SetVertices(new[]
            {
                new Vector3(-0.5f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, -0.5f),
            });
            _quad.SetUVs(0, new[] { new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(1f, 0f) });
            _quad.SetTriangles(new[] { 0, 1, 2, 0, 2, 3 }, 0);
            _quad.RecalculateBounds();
            return _quad;
        }
    }
}

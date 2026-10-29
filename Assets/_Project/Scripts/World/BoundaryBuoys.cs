using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// The ring of markers at the <see cref="FlightBoundary"/> edge, so the edge can be seen before the turn-back takes
    /// over (island-map.md): a buoy floating wherever the edge is over water, a boundary post standing on the ground
    /// wherever it crosses land (<see cref="Spot"/>). Their lights blink together so the ring reads as one line.
    /// When a mode's arena swaps in its own boundary (<see cref="FlightBoundary.UseArea"/>), the level's ring hides and
    /// a ring of the same markers is laid round the arena's edge instead, until the level's boundary comes back.
    /// </summary>
    public class BoundaryBuoys : MonoBehaviour
    {
        /// <summary>One mesh of a marker, as built: what the arena's ring copies.</summary>
        [Serializable]
        struct Piece
        {
            public string name;
            public Mesh mesh;
            public Material[] materials;
            public Vector3 position;
            public Quaternion rotation;
            public Vector3 scale;
            public bool castShadows;
            public bool light;
        }

        /// <summary>Probes start this high: above every map's ceiling.</summary>
        const float ProbeHeight = 5000f;
        /// <summary>A post stands on the lowest ground this far round its foot, so on a slope its footing sinks in rather than floats.</summary>
        const float FootingRadius = 2.5f;
        static readonly Vector3[] FootingProbes = { Vector3.forward, Vector3.right, Vector3.back, Vector3.left };

        [SerializeField] Renderer[] lights;
        [Tooltip("A buoy's meshes, recorded when the ring is built (the scene's are static-batched at runtime, so their meshes can't be copied then).")]
        [SerializeField] Piece[] model;
        [Tooltip("A boundary post's meshes, recorded the same way, for the arena ring's stretches over land.")]
        [SerializeField] Piece[] postModel;
        [Tooltip("Light blinks per second.")]
        [SerializeField] float blinkRate = 0.6f;
        [Tooltip("Gap between markers (m) on an arena's ring.")]
        [SerializeField] float arenaSpacing = 150f;

        FlightBoundary _boundary;
        Transform _arenaRing;
        readonly List<Renderer> _arenaLights = new();

        public int Count => lights != null ? lights.Length : 0;
        /// <summary>Markers on the arena's ring (0 while the level's own boundary is in use).</summary>
        public int ArenaCount => _arenaLights.Count;
        /// <summary>The arena's ring while one is up, else null.</summary>
        public Transform ArenaRing => _arenaRing;

        /// <summary>
        /// Takes the markers' lights, and records <paramref name="buoy"/> and <paramref name="post"/> (each with its lamp,
        /// <paramref name="buoyLamp"/> / <paramref name="postLamp"/>) as the models for an arena's ring.
        /// </summary>
        public void Configure(Renderer[] markerLights, Transform buoy, Renderer buoyLamp, Transform post, Renderer postLamp)
        {
            lights = markerLights;
            model = Record(buoy, buoyLamp);
            postModel = Record(post, postLamp);
        }

        static Piece[] Record(Transform marker, Renderer lamp)
        {
            var pieces = new List<Piece>();
            if (marker != null)
                foreach (var filter in marker.GetComponentsInChildren<MeshFilter>())
                {
                    var t = filter.transform;
                    var r = filter.GetComponent<Renderer>();
                    pieces.Add(new Piece
                    {
                        name = t.name,
                        mesh = filter.sharedMesh,
                        materials = r.sharedMaterials,
                        position = marker.InverseTransformPoint(t.position),
                        rotation = Quaternion.Inverse(marker.rotation) * t.rotation,
                        scale = t.lossyScale,
                        castShadows = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off,
                        light = r == lamp,
                    });
                }
            return pieces.ToArray();
        }

        /// <summary>
        /// Where a marker stands at <paramref name="at"/>'s x, z: on the water's surface (<paramref name="onLand"/> false:
        /// a buoy), or on the lowest ground round a post's footing (true: a post). The sea's surface when nothing's there.
        /// </summary>
        public static Vector3 Spot(Vector3 at, out bool onLand)
        {
            onLand = false;
            if (!Probe(at, out var hit))
                return new Vector3(at.x, 0f, at.z);
            if (PlaneController.IsWater(hit.collider))
                return new Vector3(at.x, hit.point.y, at.z);
            onLand = true;
            float low = hit.point.y;
            foreach (var d in FootingProbes)
                if (Probe(at + d * FootingRadius, out var foot) && !PlaneController.IsWater(foot.collider))
                    low = Mathf.Min(low, foot.point.y);
            return new Vector3(at.x, low, at.z);
        }

        /// <summary>The first solid thing straight down at x, z; planes and other moving bodies don't count.</summary>
        static bool Probe(Vector3 at, out RaycastHit ground)
        {
            var hits = Physics.RaycastAll(new Vector3(at.x, ProbeHeight, at.z), Vector3.down, ProbeHeight * 2f, ~0,
                QueryTriggerInteraction.Ignore);
            ground = default;
            float best = float.MaxValue;
            foreach (var h in hits)
                if (h.rigidbody == null && h.distance < best)
                {
                    best = h.distance;
                    ground = h;
                }
            return best < float.MaxValue;
        }

        void OnEnable()
        {
            _boundary = FlightBoundary.Active != null ? FlightBoundary.Active : FindAnyObjectByType<FlightBoundary>();
            if (_boundary == null)
                return;
            _boundary.Changed += Follow;
            Follow();
        }

        void OnDisable()
        {
            if (_boundary != null)
                _boundary.Changed -= Follow;
        }

        void Update()
        {
            bool on = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;
            if (lights != null)
                foreach (var r in lights)
                    if (r != null)
                        r.enabled = on;
            foreach (var r in _arenaLights)
                r.enabled = on;
        }

        /// <summary>Shows the level's ring, or an arena ring at the boundary's current edge.</summary>
        void Follow()
        {
            if (_arenaRing != null)
            {
                Destroy(_arenaRing.gameObject);
                _arenaRing = null;
                _arenaLights.Clear();
            }
            bool arena = _boundary.IsOverridden;
            foreach (Transform marker in transform)
                marker.gameObject.SetActive(!arena);
            if (arena)
                BuildArenaRing();
        }

        /// <summary>
        /// The arena's ring: copies of the recorded buoy over water and post over land, each on its own <see cref="Spot"/>
        /// (the level's are static-batched, so they can't be moved).
        /// </summary>
        void BuildArenaRing()
        {
            if (model == null || model.Length == 0)
                return;
            _arenaRing = new GameObject("Arena Buoys").transform;
            _arenaRing.SetParent(transform, false);
            Vector3 c = _boundary.Center;
            float r = _boundary.radius;
            int count = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / arenaSpacing));
            for (int i = 0; i < count; i++)
            {
                Vector3 spot = Spot(c + Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward * r, out bool onLand);
                bool post = onLand && postModel != null && postModel.Length > 0;
                var marker = new GameObject(post ? $"Arena Post {i:000}" : $"Arena Buoy {i:000}").transform;
                marker.SetParent(_arenaRing, false);
                marker.position = spot;
                foreach (var piece in post ? postModel : model)
                {
                    var go = new GameObject(piece.name); // no collider: they mark the edge, they aren't in the way
                    go.transform.SetParent(marker, false);
                    go.transform.SetLocalPositionAndRotation(piece.position, piece.rotation);
                    go.transform.localScale = piece.scale;
                    go.AddComponent<MeshFilter>().sharedMesh = piece.mesh;
                    var renderer = go.AddComponent<MeshRenderer>();
                    renderer.sharedMaterials = piece.materials;
                    renderer.shadowCastingMode = piece.castShadows ? UnityEngine.Rendering.ShadowCastingMode.On
                        : UnityEngine.Rendering.ShadowCastingMode.Off;
                    if (piece.light)
                        _arenaLights.Add(renderer);
                }
            }
        }
    }
}

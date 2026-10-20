using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// The ring of marker buoys floating on the sea at the <see cref="FlightBoundary"/> edge, so the edge can be seen
    /// before the turn-back takes over (island-map.md). Their lights blink together so the ring reads as one line.
    /// When a mode's arena swaps in its own boundary (<see cref="FlightBoundary.UseArea"/>), the level's ring hides and
    /// a ring of the same buoys is laid round the arena's edge instead, until the level's boundary comes back.
    /// </summary>
    public class BoundaryBuoys : MonoBehaviour
    {
        /// <summary>One mesh of a buoy, as built: what the arena's ring copies.</summary>
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

        [SerializeField] Renderer[] lights;
        [Tooltip("The first buoy's meshes, recorded when it's built (the scene's are static-batched at runtime, so their meshes can't be copied then).")]
        [SerializeField] Piece[] model;
        [Tooltip("Light blinks per second.")]
        [SerializeField] float blinkRate = 0.6f;
        [Tooltip("Gap between buoys (m) on an arena's ring.")]
        [SerializeField] float arenaSpacing = 150f;

        FlightBoundary _boundary;
        Transform _arenaRing;
        readonly List<Renderer> _arenaLights = new();

        public int Count => lights != null ? lights.Length : 0;
        /// <summary>Buoys on the arena's ring (0 while the level's own boundary is in use).</summary>
        public int ArenaCount => _arenaLights.Count;

        /// <summary>Takes the buoys' lights, and records the first light's buoy as the model for an arena's ring.</summary>
        public void Configure(Renderer[] buoyLights)
        {
            lights = buoyLights;
            var pieces = new List<Piece>();
            Transform buoy = buoyLights.Length > 0 && buoyLights[0] != null ? buoyLights[0].transform.parent : null;
            if (buoy != null)
                foreach (var filter in buoy.GetComponentsInChildren<MeshFilter>())
                {
                    var t = filter.transform;
                    var r = filter.GetComponent<Renderer>();
                    pieces.Add(new Piece
                    {
                        name = t.name,
                        mesh = filter.sharedMesh,
                        materials = r.sharedMaterials,
                        position = buoy.InverseTransformPoint(t.position),
                        rotation = Quaternion.Inverse(buoy.rotation) * t.rotation,
                        scale = t.lossyScale,
                        castShadows = r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off,
                        light = r == buoyLights[0],
                    });
                }
            model = pieces.ToArray();
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
            foreach (Transform buoy in transform)
                buoy.gameObject.SetActive(!arena);
            if (arena)
                BuildArenaRing();
        }

        /// <summary>The arena's ring: copies of the recorded buoy (the level's are static-batched, so they can't be moved).</summary>
        void BuildArenaRing()
        {
            if (model == null || model.Length == 0)
                return;
            _arenaRing = new GameObject("Arena Buoys").transform;
            _arenaRing.SetParent(transform, false);
            Vector3 c = _boundary.Center;
            float r = _boundary.radius;
            float y = transform.childCount > 0 ? transform.GetChild(0).position.y : 0f;
            int count = Mathf.Max(8, Mathf.CeilToInt(2f * Mathf.PI * r / arenaSpacing));
            for (int i = 0; i < count; i++)
            {
                var buoy = new GameObject($"Arena Buoy {i:000}").transform;
                buoy.SetParent(_arenaRing, false);
                buoy.position = new Vector3(c.x, y, c.z) + Quaternion.Euler(0f, i * 360f / count, 0f) * Vector3.forward * r;
                foreach (var piece in model)
                {
                    var go = new GameObject(piece.name); // no collider: they mark the edge, they aren't in the way
                    go.transform.SetParent(buoy, false);
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

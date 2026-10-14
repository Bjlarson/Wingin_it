using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Placeholder presentation for the modular plane: class tint and scale, spinning propeller,
    /// and a cheap break-apart when the plane crashes.
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
        [SerializeField] float debrisLifetime = 6f;

        MaterialPropertyBlock _block;
        readonly List<Renderer> _hidden = new();

        void Reset() => plane = GetComponent<PlaneController>();

        void OnEnable()
        {
            if (plane == null)
                return;
            plane.StatsChanged += OnStatsChanged;
            plane.Crashed += OnCrashed;
            plane.Respawned += OnRespawned;
            ApplyClassLook();
        }

        void OnDisable()
        {
            if (plane == null)
                return;
            plane.StatsChanged -= OnStatsChanged;
            plane.Crashed -= OnCrashed;
            plane.Respawned -= OnRespawned;
        }

        void Update()
        {
            if (propeller == null || !plane.IsAlive)
                return;
            float throttle = plane.IsBoosting ? 1.3f : Mathf.Lerp(0.3f, 1f, plane.Throttle);
            float thrust = plane.Modifiers.Get(FlightStat.Thrust);
            propeller.Rotate(0f, 0f, propellerMaxRpm * 6f * throttle * thrust * Time.deltaTime, Space.Self);
        }

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
        }

        void OnCrashed(PlaneController _, Vector3 point)
        {
            Vector3 velocity = plane.Velocity;
            _hidden.Clear();

            // Throw a copy of every part, then hide the real plane until respawn.
            foreach (var part in GetComponentsInChildren<PlanePart>())
                SpawnDebris(part.gameObject, velocity, point);

            foreach (var r in GetComponentsInChildren<Renderer>())
            {
                if (!r.enabled)
                    continue;
                r.enabled = false;
                _hidden.Add(r);
            }

            SpawnFlash(point);
        }

        void OnRespawned(PlaneController _)
        {
            foreach (var r in _hidden)
                if (r != null)
                    r.enabled = true;
            _hidden.Clear();
        }

        void SpawnDebris(GameObject part, Vector3 velocity, Vector3 point)
        {
            var copy = Instantiate(part, part.transform.position, part.transform.rotation);
            copy.transform.localScale = part.transform.lossyScale;
            copy.name = part.name + " (Debris)";
            foreach (var c in copy.GetComponentsInChildren<PlanePart>())
                Destroy(c);
            foreach (var c in copy.GetComponentsInChildren<Collider>())
                c.enabled = true;

            var body = copy.AddComponent<Rigidbody>();
            body.mass = 50f;
            Vector3 away = (copy.transform.position - point).normalized;
            body.linearVelocity = velocity * 0.35f + away * Random.Range(8f, 20f) + Vector3.up * Random.Range(4f, 12f);
            body.angularVelocity = Random.insideUnitSphere * 6f;
            Destroy(copy, debrisLifetime);
        }

        static void SpawnFlash(Vector3 point)
        {
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Crash Flash";
            Destroy(flash.GetComponent<Collider>());
            flash.transform.position = point;
            var r = flash.GetComponent<Renderer>();
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColorId, new Color(1f, 0.55f, 0.1f));
            r.SetPropertyBlock(block);
            flash.AddComponent<GrowAndDestroy>();
        }

        /// <summary>Placeholder explosion: a sphere that swells and disappears.</summary>
        class GrowAndDestroy : MonoBehaviour
        {
            float _t;

            void Update()
            {
                _t += Time.deltaTime;
                transform.localScale = Vector3.one * Mathf.Lerp(2f, 22f, _t / 0.4f);
                if (_t > 0.4f)
                    Destroy(gameObject);
            }
        }
    }
}

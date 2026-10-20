using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Effects;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// A destructible thing on the ground (truck, radar dish, ...) for bombs to hit. Prototype stand-in for the Phase 8
    /// objectives: it has HP, turns into a smoking wreck at 0, and rebuilds itself after a delay.
    /// </summary>
    public class GroundTarget : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly List<GroundTarget> s_all = new();

        [SerializeField] string displayName = "Target";
        [SerializeField, Min(1f)] float maxHealth = 100f;
        [Tooltip("Seconds a wreck stays before the target rebuilds (prototype only).")]
        [SerializeField, Min(0f)] float rebuildDelay = 20f;
        [SerializeField] Renderer[] renderers;
        [SerializeField] Color wreckColor = new Color(0.12f, 0.11f, 0.1f);

        Collider[] _colliders;
        MaterialPropertyBlock _block;
        Vector3 _intactScale;
        float _rebuildTimer;
        VfxEffect _smoke, _fire;

        public static IReadOnlyList<GroundTarget> All => s_all;
        /// <summary>Raised when any ground target is destroyed, with whatever destroyed it (a plane, or null).</summary>
        public static event Action<GroundTarget, UnityEngine.Object> AnyDestroyed;

        public event Action<GroundTarget, float> Damaged;

        public string DisplayName => displayName;
        public float Health { get; private set; }
        public float MaxHealth => maxHealth;
        public float NormalizedHealth => Health / maxHealth;
        public bool IsDestroyed => Health <= 0f;
        public float RebuildDelay => rebuildDelay;
        /// <summary>Seconds until a wreck rebuilds (0 while intact).</summary>
        public float RebuildTimeLeft => IsDestroyed ? Mathf.Max(0f, _rebuildTimer) : 0f;

        public void Configure(string name, float health, Renderer[] targetRenderers)
        {
            displayName = name;
            maxHealth = health;
            renderers = targetRenderers;
        }

        public void Configure(string name, float health, Renderer[] targetRenderers, float rebuildSeconds)
        {
            Configure(name, health, targetRenderers);
            rebuildDelay = rebuildSeconds;
        }

        void Awake()
        {
            _colliders = GetComponentsInChildren<Collider>();
            _block = new MaterialPropertyBlock();
            _intactScale = transform.localScale;
            Health = maxHealth;
        }

        void OnEnable() => s_all.Add(this);
        void OnDisable() => s_all.Remove(this);

        /// <summary>Removes HP. Returns the damage actually taken (0 once it's a wreck).</summary>
        public float ApplyDamage(float amount, Vector3 point, UnityEngine.Object source = null)
        {
            if (IsDestroyed || amount <= 0f)
                return 0f;
            float taken = Mathf.Min(amount, Health);
            Health -= taken;
            Damaged?.Invoke(this, taken);
            if (IsDestroyed)
                Wreck(source);
            return taken;
        }

        /// <summary>Back to full HP and intact.</summary>
        public void Rebuild()
        {
            Health = maxHealth;
            transform.localScale = _intactScale;
            SetTint(null);
            _smoke?.Stop();
            _fire?.Stop();
            _smoke = _fire = null;
        }

        void Wreck(UnityEngine.Object source)
        {
            _rebuildTimer = rebuildDelay;
            // Squashed and charred; the collider stays, so it's still something to crash into.
            transform.localScale = Vector3.Scale(_intactScale, new Vector3(1f, 0.45f, 1f));
            SetTint(wreckColor);
            Vector3 center = Center;
            Vfx.Play(VfxKind.ExplosionGround, center, 1.3f);
            // A rising smoke column over the burning wreck marks it from the air.
            Vector3 local = transform.InverseTransformPoint(center);
            _smoke = Vfx.Attach(VfxKind.WreckSmoke, transform, local);
            _fire = Vfx.Attach(VfxKind.DamageFire, transform, local, 2f);
            AnyDestroyed?.Invoke(this, source);
        }

        void Update()
        {
            if (!IsDestroyed)
                return;
            if (rebuildDelay > 0f && (_rebuildTimer -= Time.deltaTime) <= 0f)
                Rebuild();
        }

        Vector3 Center
        {
            get
            {
                if (_colliders == null || _colliders.Length == 0)
                    return transform.position;
                var bounds = _colliders[0].bounds;
                for (int i = 1; i < _colliders.Length; i++)
                    bounds.Encapsulate(_colliders[i].bounds);
                return bounds.center;
            }
        }

        void SetTint(Color? color)
        {
            if (renderers == null)
                return;
            foreach (var r in renderers)
            {
                if (r == null)
                    continue;
                if (color.HasValue)
                {
                    r.GetPropertyBlock(_block);
                    _block.SetColor(BaseColorId, color.Value);
                    r.SetPropertyBlock(_block);
                }
                else
                {
                    r.SetPropertyBlock(null);
                }
            }
        }

        /// <summary>Distance from <paramref name="point"/> to this target's nearest collider (0 inside).</summary>
        public float DistanceTo(Vector3 point)
        {
            float best = float.MaxValue;
            foreach (var col in _colliders)
                if (col != null && col.enabled)
                    best = Mathf.Min(best, Vector3.Distance(point, col.ClosestPoint(point)));
            return best == float.MaxValue ? Vector3.Distance(point, transform.position) : best;
        }

        /// <summary>Every intact ground target within <paramref name="radius"/> of <paramref name="point"/>, with its distance.</summary>
        public static void FindInRadius(Vector3 point, float radius, List<(GroundTarget target, float distance)> results)
        {
            foreach (var target in s_all)
            {
                if (target.IsDestroyed)
                    continue;
                float d = target.DistanceTo(point);
                if (d <= radius)
                    results.Add((target, d));
            }
        }
    }
}

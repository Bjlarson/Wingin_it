using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// A fly-through ring for flight practice. The trigger collider covers the hole; the ring
    /// segments are solid, so clipping the rim is a crash.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class RingGate : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        static readonly List<RingGate> s_gates = new();

        public static int PassedCount { get; private set; }
        public static int TotalCount => s_gates.Count;

        [SerializeField] Renderer[] ringRenderers;
        [SerializeField] Color idleColor = new Color(1f, 0.75f, 0.1f);
        [SerializeField] Color passedColor = new Color(0.2f, 0.9f, 0.3f);

        bool _passed;
        MaterialPropertyBlock _block;

        void OnEnable()
        {
            s_gates.Add(this);
            ApplyColor(idleColor);
        }

        void OnDisable()
        {
            s_gates.Remove(this);
            if (_passed)
                PassedCount--;
            _passed = false;
        }

        void Reset() => GetComponent<Collider>().isTrigger = true;

        public void SetRenderers(Renderer[] renderers) => ringRenderers = renderers;

        public void OnPlanePassed(PlaneController plane)
        {
            if (_passed)
                return;
            _passed = true;
            PassedCount++;
            ApplyColor(passedColor);

            if (PassedCount >= TotalCount)
                ResetAll();
        }

        /// <summary>When every ring is done, start the course over.</summary>
        static void ResetAll()
        {
            foreach (var gate in s_gates)
            {
                gate._passed = false;
                gate.ApplyColor(gate.idleColor);
            }
            PassedCount = 0;
        }

        void ApplyColor(Color color)
        {
            if (ringRenderers == null)
                return;
            _block ??= new MaterialPropertyBlock();
            _block.SetColor(BaseColorId, color);
            foreach (var r in ringRenderers)
                if (r != null)
                    r.SetPropertyBlock(_block);
        }
    }
}

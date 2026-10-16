using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// The ring of marker buoys floating on the sea at the <see cref="FlightBoundary"/> edge, so the edge can be seen
    /// before the turn-back takes over (island-map.md). Their lights blink together so the ring reads as one line.
    /// </summary>
    public class BoundaryBuoys : MonoBehaviour
    {
        [SerializeField] Renderer[] lights;
        [Tooltip("Light blinks per second.")]
        [SerializeField] float blinkRate = 0.6f;

        public int Count => lights != null ? lights.Length : 0;

        public void Configure(Renderer[] buoyLights) => lights = buoyLights;

        void Update()
        {
            if (lights == null)
                return;
            bool on = Mathf.Repeat(Time.time * blinkRate, 1f) < 0.5f;
            foreach (var r in lights)
                if (r != null)
                    r.enabled = on;
        }
    }
}

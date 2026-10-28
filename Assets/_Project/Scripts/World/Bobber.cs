using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// Bobs and rocks its transform gently about where it was placed, and optionally drifts it round a slow circle: a
    /// moored boat on the swell, a hot-air balloon on the breeze. Each starts at its own phase, so a harbour doesn't
    /// rise and fall in step.
    /// </summary>
    public class Bobber : MonoBehaviour
    {
        [Tooltip("Metres up and down.")]
        [SerializeField] float heave = 0.4f;
        [Tooltip("Degrees of roll and pitch.")]
        [SerializeField] float rock = 3f;
        [Tooltip("Seconds per bob.")]
        [SerializeField] float period = 5f;
        [Tooltip("Radius (m) of the slow circle it drifts round; 0 stays put.")]
        [SerializeField] float driftRadius;
        [Tooltip("Seconds per drift circle.")]
        [SerializeField] float driftPeriod = 240f;

        Vector3 _home;
        Quaternion _rest;
        float _phase;

        public void Configure(float heave, float rock, float period, float driftRadius = 0f, float driftPeriod = 240f)
        {
            this.heave = heave;
            this.rock = rock;
            this.period = period;
            this.driftRadius = driftRadius;
            this.driftPeriod = driftPeriod;
        }

        void OnEnable()
        {
            _home = transform.position;
            _rest = transform.rotation;
            // From the position, so it is the same every run (and differs from the next boat along).
            _phase = Mathf.Repeat(_home.x * 0.137f + _home.z * 0.071f, 1f) * Mathf.PI * 2f;
        }

        void OnDisable()
        {
            transform.SetPositionAndRotation(_home, _rest);
        }

        void Update()
        {
            float t = Time.time * Mathf.PI * 2f / Mathf.Max(0.1f, period) + _phase;
            Vector3 p = _home + Vector3.up * (Mathf.Sin(t) * heave);
            if (driftRadius > 0f)
            {
                float d = Time.time * Mathf.PI * 2f / Mathf.Max(1f, driftPeriod) + _phase;
                p += new Vector3(Mathf.Cos(d) - Mathf.Cos(_phase), 0f, Mathf.Sin(d) - Mathf.Sin(_phase)) * driftRadius;
            }
            var tilt = Quaternion.Euler(Mathf.Sin(t * 0.8f + 1.3f) * rock, 0f, Mathf.Sin(t) * rock);
            transform.SetPositionAndRotation(p, _rest * tilt);
        }
    }
}

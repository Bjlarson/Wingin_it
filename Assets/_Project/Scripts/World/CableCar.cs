using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// Runs gondolas round a cable car's loop: up one track and back down the other, at a steady speed, each hanging
    /// straight down from its grip and facing along the cable. The loop is a closed polyline in world space (the
    /// builder lays it through the pylon heads); the gondolas are spaced evenly along it.
    /// </summary>
    public class CableCar : MonoBehaviour
    {
        [SerializeField] Vector3[] loop = new Vector3[0];
        [SerializeField] Transform[] gondolas = new Transform[0];
        [Tooltip("Metres per second along the cable.")]
        [SerializeField] float speed = 5f;

        float[] _cumulative;
        float _length;

        public void Configure(Vector3[] loop, Transform[] gondolas, float speed)
        {
            this.loop = loop;
            this.gondolas = gondolas;
            this.speed = speed;
            Measure();
            Place(0f);
        }

        public float Length
        {
            get
            {
                if (_cumulative == null)
                    Measure();
                return _length;
            }
        }

        void Measure()
        {
            int n = loop.Length;
            _cumulative = new float[n + 1];
            for (int i = 0; i < n; i++)
                _cumulative[i + 1] = _cumulative[i] + Vector3.Distance(loop[i], loop[(i + 1) % n]);
            _length = _cumulative[n];
        }

        /// <summary>The point <paramref name="distance"/> m round the loop, and the cable's direction there.</summary>
        public (Vector3 point, Vector3 direction) At(float distance)
        {
            if (_cumulative == null)
                Measure();
            int n = loop.Length;
            if (n < 2 || _length <= 0f)
                return (n > 0 ? loop[0] : transform.position, Vector3.forward);
            distance = Mathf.Repeat(distance, _length);
            int i = 0;
            while (i < n - 1 && _cumulative[i + 1] < distance)
                i++;
            Vector3 a = loop[i], b = loop[(i + 1) % n];
            float seg = _cumulative[i + 1] - _cumulative[i];
            float t = seg > 0f ? (distance - _cumulative[i]) / seg : 0f;
            return (Vector3.Lerp(a, b, t), (b - a).normalized);
        }

        void Update() => Place(Time.time * speed);

        void Place(float offset)
        {
            if (gondolas.Length == 0 || loop.Length < 2)
                return;
            float spacing = Length / gondolas.Length;
            for (int k = 0; k < gondolas.Length; k++)
            {
                if (gondolas[k] == null)
                    continue;
                var (p, dir) = At(offset + k * spacing);
                var flat = new Vector3(dir.x, 0f, dir.z);
                gondolas[k].SetPositionAndRotation(p, flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat) : Quaternion.identity);
            }
        }
    }
}

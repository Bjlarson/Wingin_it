using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// Cylindrical play area with an altitude ceiling. Planes that leave it are steered back
    /// (soft turn-back, not an invisible wall).
    /// </summary>
    public class FlightBoundary : MonoBehaviour
    {
        public static FlightBoundary Active { get; private set; }

        [Tooltip("Horizontal radius of the play area, measured from this transform.")]
        public float radius = 2800f;
        [Tooltip("Distance inside the radius where the warning starts.")]
        public float warningMargin = 300f;
        [Tooltip("World-space altitude where the plane is pushed back down.")]
        public float ceiling = 1500f;
        [Tooltip("Altitude band below the ceiling where the warning starts.")]
        public float ceilingWarningMargin = 150f;

        void OnEnable() => Active = this;

        void OnDisable()
        {
            if (Active == this)
                Active = null;
        }

        public float HorizontalDistance(Vector3 position)
        {
            Vector3 d = position - transform.position;
            d.y = 0f;
            return d.magnitude;
        }

        public bool IsOutside(Vector3 position) => HorizontalDistance(position) > radius;
        public bool IsNearEdge(Vector3 position) => HorizontalDistance(position) > radius - warningMargin;
        public bool IsAboveCeiling(Vector3 position) => position.y > ceiling;
        public bool IsNearCeiling(Vector3 position) => position.y > ceiling - ceilingWarningMargin;

        /// <summary>A point back inside the play area to steer toward, at a safe altitude.</summary>
        public Vector3 ReturnPoint(Vector3 position)
        {
            Vector3 c = transform.position;
            float y = Mathf.Clamp(position.y, c.y + 150f, ceiling - ceilingWarningMargin * 2f);
            return new Vector3(c.x, y, c.z);
        }

        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            DrawCircle(transform.position, radius);
            DrawCircle(new Vector3(transform.position.x, ceiling, transform.position.z), radius);
            Gizmos.color = new Color(1f, 0.5f, 0f);
            DrawCircle(transform.position, radius - warningMargin);
        }

        static void DrawCircle(Vector3 center, float r)
        {
            const int segments = 64;
            Vector3 prev = center + new Vector3(r, 0f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}

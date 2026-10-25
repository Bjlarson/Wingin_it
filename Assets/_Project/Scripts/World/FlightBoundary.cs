using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// Cylindrical play area with an altitude ceiling. Planes that leave it are steered back (soft turn-back, not an
    /// invisible wall), unless it's a <see cref="hardEdge"/>: then a plane that stays outside for
    /// <see cref="outOfBoundsGrace"/> seconds is out. A match mode can swap in its own area for the match
    /// (<see cref="UseArea"/>, from a <see cref="ModeArena"/>) and put the level's back after (<see cref="RestoreArea"/>).
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
        [Tooltip("No turn-back: a plane outside the radius for longer than the grace is destroyed (Ram Royale).")]
        public bool hardEdge;
        [Tooltip("Seconds a plane may spend past a hard edge before it's out.")]
        public float outOfBoundsGrace = 3f;

        (Vector3 position, float radius, float warningMargin, float ceiling, bool hardEdge)? _levelArea;

        /// <summary>Raised when the area moves or changes size (the minimap and buoys follow it).</summary>
        public event System.Action Changed;

        /// <summary>The middle of the play area at sea level.</summary>
        public Vector3 Center => transform.position;

        /// <summary>A mode's own area is in use (<see cref="UseArea"/>).</summary>
        public bool IsOverridden => _levelArea != null;

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

        /// <summary>
        /// Swaps in a mode's play area for the match: centered on <paramref name="center"/> (XZ), out to
        /// <paramref name="newRadius"/>, under <paramref name="newCeiling"/>, with or without a hard edge. The level's
        /// own area is kept for <see cref="RestoreArea"/>.
        /// </summary>
        public void UseArea(Vector3 center, float newRadius, float newWarningMargin, float newCeiling, bool newHardEdge)
        {
            _levelArea ??= (transform.position, radius, warningMargin, ceiling, hardEdge);
            transform.position = new Vector3(center.x, transform.position.y, center.z);
            radius = newRadius;
            warningMargin = newWarningMargin;
            ceiling = newCeiling;
            hardEdge = newHardEdge;
            Changed?.Invoke();
        }

        /// <summary>
        /// Where the ceiling takeover steers a plane over the ceiling: on along its heading, nose gently down. Near a
        /// hard edge (<paramref name="turnIn"/>), where holding the heading could fly it out of the arena, it also turns
        /// toward <paramref name="center"/>, at most <paramref name="maxTurn"/> degrees off the nose so it's a level turn.
        /// </summary>
        public static Vector3 CeilingReturnPoint(Vector3 position, Vector3 forward, Vector3 up, Vector3 center, bool turnIn,
            float maxTurn = 80f)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            if (flat.sqrMagnitude < 0.01f)
                flat = Vector3.ProjectOnPlane(up, Vector3.up); // nose straight up or down: the way the cockpit roof faces
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
            Vector3 toCenter = center - position;
            toCenter.y = 0f;
            if (turnIn && toCenter.sqrMagnitude > 1f)
            {
                float angle = Mathf.Clamp(Vector3.SignedAngle(flat, toCenter, Vector3.up), -maxTurn, maxTurn);
                flat = Quaternion.AngleAxis(angle, Vector3.up) * flat;
            }
            return position + flat * 300f + Vector3.down * 80f;
        }

        /// <summary>Puts the level's own play area back after <see cref="UseArea"/>.</summary>
        public void RestoreArea()
        {
            if (_levelArea is not { } level)
                return;
            _levelArea = null;
            (transform.position, radius, warningMargin, ceiling, hardEdge) = level;
            Changed?.Invoke();
        }

        /// <summary>A hard edge has put out a plane that has been outside for <paramref name="secondsOutside"/>.</summary>
        public bool IsOut(float secondsOutside) => hardEdge && secondsOutside > outOfBoundsGrace;

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

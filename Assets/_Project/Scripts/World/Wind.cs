using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// The one wind of the match (living-world.md, "Same wind everywhere"): it rolls the sea's waves and drifts the dust
    /// and spray planes kick up. Fixed until the match sets one. Purely visual: the flight model has no wind.
    /// </summary>
    public static class Wind
    {
        /// <summary>Where the wind blows toward, in degrees from north (+Z), clockwise.</summary>
        public const float Heading = 60f;
        /// <summary>How fast it carries loose things along (m/s).</summary>
        public const float Speed = 4f;

        /// <summary>The flat unit vector the wind blows along.</summary>
        public static Vector3 Direction
        {
            get
            {
                float a = Heading * Mathf.Deg2Rad;
                return new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
            }
        }

        public static Vector3 Velocity => Direction * Speed;
    }
}

using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>Max HP of each plane part for one aircraft class.</summary>
    [CreateAssetMenu(menuName = "Winging It/Plane Durability", fileName = "Durability")]
    public class PlaneDurability : ScriptableObject
    {
        [Tooltip("At 0 the plane explodes.")]
        [Min(1f)] public float body = 100f;
        [Tooltip("Per wing. At 0 the wing breaks off.")]
        [Min(1f)] public float wing = 45f;
        [Min(1f)] public float horizontalStabilizer = 30f;
        [Min(1f)] public float verticalStabilizer = 30f;
        [Tooltip("At 0 the engine dies (glide only) but stays attached and keeps soaking hits.")]
        [Min(1f)] public float engine = 50f;

        public float GetMaxHealth(PlanePartType type) => type switch
        {
            PlanePartType.Body => body,
            PlanePartType.WingLeft or PlanePartType.WingRight => wing,
            PlanePartType.HorizontalStabilizer => horizontalStabilizer,
            PlanePartType.VerticalStabilizer => verticalStabilizer,
            PlanePartType.Engine => engine,
            _ => body,
        };
    }
}

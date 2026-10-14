using UnityEngine;

namespace WingingIt.Flight
{
    public enum PlanePartType
    {
        Body,
        WingLeft,
        WingRight,
        Tail,
        Engine,
    }

    /// <summary>
    /// Marks one detachable section of the modular plane. Phase 2 adds per-part HP and hitboxes here.
    /// </summary>
    public class PlanePart : MonoBehaviour
    {
        public PlanePartType partType;
    }
}

using UnityEngine;

namespace WingingIt.Flight
{
    /// <summary>
    /// Loose wreckage (a broken-off part or a crashed plane's pieces). Cosmetic only: planes fly through it
    /// instead of crashing, and it despawns on its own.
    /// </summary>
    public class PlaneDebris : MonoBehaviour
    {
        public static bool IsDebris(Collider collider) =>
            collider.attachedRigidbody != null && collider.attachedRigidbody.TryGetComponent<PlaneDebris>(out _);
    }
}

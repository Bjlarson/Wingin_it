using UnityEngine;

namespace WingingIt.World
{
    /// <summary>Turns its transform steadily about its local Y axis: an airfield beacon, a radar dish.</summary>
    public class Spinner : MonoBehaviour
    {
        [Tooltip("Degrees per second; negative turns the other way.")]
        [SerializeField] float degreesPerSecond = 90f;

        public void Configure(float speed) => degreesPerSecond = speed;

        void Update() => transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
    }
}

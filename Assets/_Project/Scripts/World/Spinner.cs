using UnityEngine;

namespace WingingIt.World
{
    /// <summary>Turns its transform steadily about its local Y axis: an airfield beacon, a radar dish.</summary>
    public class Spinner : MonoBehaviour
    {
        [Tooltip("Degrees per second; negative turns the other way.")]
        [SerializeField] float degreesPerSecond = 90f;
        [Tooltip("Turns on unscaled time, so it keeps going while the game is frozen (the main menu's backdrop).")]
        [SerializeField] bool unscaledTime;

        public void Configure(float speed) => degreesPerSecond = speed;

        void Update() => transform.Rotate(0f, degreesPerSecond * (unscaledTime ? Time.unscaledDeltaTime : Time.deltaTime), 0f, Space.Self);
    }
}

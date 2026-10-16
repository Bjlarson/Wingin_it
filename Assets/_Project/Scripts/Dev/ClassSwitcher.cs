using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Flight;
using WingingIt.Input;

namespace WingingIt.Dev
{
    /// <summary>
    /// Prototype-only: swap the plane's class mid-flight to compare handling.
    /// Keys 1-4, Tab / gamepad Select to cycle, or the touch "Class" button.
    /// </summary>
    [RequireComponent(typeof(PlaneController))]
    public class ClassSwitcher : MonoBehaviour
    {
        [SerializeField] FlightStats[] classes;

        PlaneController _plane;
        PlayerFlightInput _input;
        int _index;

        /// <summary>The classes this switcher cycles through (all four in the test scene).</summary>
        public System.Collections.Generic.IReadOnlyList<FlightStats> Classes => classes;

        void Awake()
        {
            _plane = GetComponent<PlaneController>();
            _input = GetComponent<PlayerFlightInput>();
            _index = Mathf.Max(0, System.Array.IndexOf(classes, _plane.Stats));
        }

        void OnEnable()
        {
            if (_input != null)
                _input.NextClassPressed += CycleClass;
        }

        void OnDisable()
        {
            if (_input != null)
                _input.NextClassPressed -= CycleClass;
        }

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.digit1Key.wasPressedThisFrame) SelectClass(0);
            if (keyboard.digit2Key.wasPressedThisFrame) SelectClass(1);
            if (keyboard.digit3Key.wasPressedThisFrame) SelectClass(2);
            if (keyboard.digit4Key.wasPressedThisFrame) SelectClass(3);
        }

        public void CycleClass()
        {
            if (classes.Length > 0)
                SelectClass((_index + 1) % classes.Length);
        }

        public void SelectClass(int index)
        {
            if (index < 0 || index >= classes.Length || classes[index] == null)
                return;
            _index = index;
            _plane.SetStats(classes[index]);
        }
    }
}

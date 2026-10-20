using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// A windsock on its hoop (pivot), streaming along its local +Z: it swings slowly round a prevailing direction as the
    /// wind wanders, lifts and drops with the gusts, and flutters. Purely visual; the flight model has no wind.
    /// </summary>
    public class Windsock : MonoBehaviour
    {
        [Tooltip("How far (degrees) the wind wanders either side of the direction it was placed facing.")]
        [SerializeField] float swing = 35f;
        [Tooltip("How far (degrees) the sock droops in a lull and lifts in a gust.")]
        [SerializeField] float droop = 12f;
        [SerializeField] float flutter = 3f;

        Quaternion _rest;
        float _seed;

        void Awake()
        {
            _rest = transform.localRotation;
            _seed = Random.value * 100f;
        }

        void Update()
        {
            float t = Time.time + _seed;
            float yaw = (Mathf.PerlinNoise(t * 0.05f, 0.3f) - 0.5f) * 2f * swing;
            float gust = Mathf.PerlinNoise(t * 0.2f, 1.7f);
            float pitch = (1f - gust) * droop + Mathf.Sin(t * 9f) * flutter * gust;
            float roll = Mathf.Sin(t * 7.3f) * flutter * gust;
            transform.localRotation = _rest * Quaternion.Euler(pitch, yaw, roll);
        }
    }
}

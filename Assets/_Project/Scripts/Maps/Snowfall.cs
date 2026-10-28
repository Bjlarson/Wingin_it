using UnityEngine;
using WingingIt.World;

namespace WingingIt.Maps
{
    /// <summary>
    /// Snow falling round the camera on a winter map (<see cref="MapSky.snowfall"/>): flakes are born in a box that
    /// follows the main camera and stay where they are born, drifting down and with the <see cref="Wind"/>, so a fast
    /// plane streams through them. Purely visual. Built in code on <see cref="Awake"/> from the flake material the map
    /// scene builder hands it.
    /// </summary>
    public class Snowfall : MonoBehaviour
    {
        [SerializeField] Material flakeMaterial;
        [Tooltip("Width, height and depth of the box round the camera where flakes are born (m).")]
        [SerializeField] Vector3 boxSize = new(160f, 80f, 160f);
        [SerializeField] float flakesPerSecond = 1200f;
        [SerializeField] float fallSpeed = 2.5f;

        ParticleSystem _particles;

        public void Configure(Material material) => flakeMaterial = material;

        void Awake()
        {
            _particles = gameObject.AddComponent<ParticleSystem>();
            _particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = _particles.main;
            main.loop = true;
            main.playOnAwake = true;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 6f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.28f);
            main.startColor = new Color(0.97f, 0.98f, 1f);
            main.maxParticles = Mathf.CeilToInt(flakesPerSecond * 6f);
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // the box moves with the camera, never off screen

            var emission = _particles.emission;
            emission.rateOverTime = flakesPerSecond;

            var shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = boxSize;

            Vector3 drift = Wind.Velocity * 0.6f + Vector3.down * fallSpeed;
            var velocity = _particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = drift.x;
            velocity.y = drift.y;
            velocity.z = drift.z;

            var noise = _particles.noise; // the flutter
            noise.enabled = true;
            noise.strength = 0.8f;
            noise.frequency = 0.4f;

            var fade = _particles.colorOverLifetime; // no pop at birth or death
            fade.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(1f, 0.85f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            var renderer = GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = flakeMaterial;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _particles.Play();
        }

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam != null)
                transform.position = cam.transform.position;
        }
    }
}

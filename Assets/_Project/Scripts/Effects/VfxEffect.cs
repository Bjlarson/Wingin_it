using UnityEngine;

namespace WingingIt.Effects
{
    /// <summary>
    /// The root of one VFX prefab: its particle systems (children included) play together, scaled as a whole, and the
    /// effect goes back to <see cref="Vfx"/>'s pool once its last particle is gone. Looping effects run until
    /// <see cref="Stop"/>, then let their particles finish.
    /// </summary>
    [DisallowMultipleComponent]
    public class VfxEffect : MonoBehaviour
    {
        [Tooltip("Children moved this far (× scale) toward the camera when played, so they show in front of the rest " +
                 "(the BOOM! in front of its own fireball).")]
        [SerializeField] Transform[] pullTowardCamera = System.Array.Empty<Transform>();
        [SerializeField] float pullDistance;

        ParticleSystem[] _systems;
        Base[] _base;
        float _lifetime;
        bool _looping;
        float _endTime = float.PositiveInfinity;
        float _scale = 1f;

        /// <summary>What a system's scaled values are at scale 1.</summary>
        struct Base
        {
            public float size, sizeX, sizeY, sizeZ, speed, radius, gravity, velX, velY, velZ, rate;
            public ParticleSystem.MinMaxGradient color;
        }

        public VfxKind Kind { get; internal set; }
        /// <summary>True from Play until it goes back to the pool.</summary>
        public bool IsPlaying { get; private set; }
        /// <summary>True while a looping effect is still emitting (it hasn't been stopped).</summary>
        public bool IsEmitting { get; private set; }
        /// <summary>Longest a particle can live, plus the longest emission: how long a one-shot runs.</summary>
        public float Lifetime
        {
            get
            {
                Init();
                return _lifetime;
            }
        }

        void Init()
        {
            if (_systems != null)
                return;
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            _base = new Base[_systems.Length];
            for (int i = 0; i < _systems.Length; i++)
            {
                var ps = _systems[i];
                var main = ps.main;
                var shape = ps.shape;
                var vel = ps.velocityOverLifetime;
                _base[i] = new Base
                {
                    size = main.startSizeMultiplier, sizeX = main.startSizeXMultiplier, sizeY = main.startSizeYMultiplier,
                    sizeZ = main.startSizeZMultiplier, speed = main.startSpeedMultiplier, radius = shape.radius,
                    gravity = main.gravityModifierMultiplier, velX = vel.xMultiplier, velY = vel.yMultiplier, velZ = vel.zMultiplier,
                    rate = ps.emission.rateOverTimeMultiplier, color = main.startColor,
                };
                _looping |= main.loop;
                float emit = main.loop ? 0f : main.duration;
                _lifetime = Mathf.Max(_lifetime, MaxOf(main.startDelay) + emit + MaxOf(main.startLifetime));
            }
        }

        static float MaxOf(ParticleSystem.MinMaxCurve curve) => curve.mode switch
        {
            ParticleSystemCurveMode.TwoConstants => Mathf.Max(curve.constantMin, curve.constantMax),
            ParticleSystemCurveMode.Curve or ParticleSystemCurveMode.TwoCurves => curve.curveMultiplier,
            _ => curve.constant,
        };

        /// <summary>Scales sizes, speeds, emitter shapes and gravity together, so a bigger blast looks the same, only bigger.</summary>
        void ApplyScale(float scale)
        {
            _scale = scale;
            for (int i = 0; i < _systems.Length; i++)
            {
                var ps = _systems[i];
                var b = _base[i];
                var main = ps.main;
                if (main.startSize3D)
                {
                    main.startSizeXMultiplier = b.sizeX * scale;
                    main.startSizeYMultiplier = b.sizeY * scale;
                    main.startSizeZMultiplier = b.sizeZ * scale;
                }
                else
                {
                    main.startSizeMultiplier = b.size * scale;
                }
                main.startSpeedMultiplier = b.speed * scale;
                main.gravityModifierMultiplier = b.gravity * scale;
                main.startColor = b.color; // undo a tint from its last use
                var emission = ps.emission;
                emission.rateOverTimeMultiplier = b.rate;
                var shape = ps.shape;
                shape.radius = b.radius * scale;
                var vel = ps.velocityOverLifetime;
                if (vel.enabled)
                {
                    vel.xMultiplier = b.velX * scale;
                    vel.yMultiplier = b.velY * scale;
                    vel.zMultiplier = b.velZ * scale;
                }
            }
        }

        internal void Play(float scale)
        {
            Init();
            ApplyScale(scale);
            var cam = Camera.main;
            if (cam != null && pullDistance > 0f)
                foreach (var t in pullTowardCamera)
                    if (t != null)
                        t.position = transform.position + (cam.transform.position - transform.position).normalized * (pullDistance * scale);
            IsPlaying = true;
            IsEmitting = true;
            SetEmitting(true);
            foreach (var ps in _systems)
            {
                ps.Clear(false);
                ps.Play(false);
            }
            _endTime = _looping ? float.PositiveInfinity : Time.time + _lifetime;
        }

        /// <summary>The index of the particle system called <paramref name="systemName"/> (for <see cref="Tune"/>), or -1.</summary>
        public int IndexOf(string systemName)
        {
            Init();
            for (int i = 0; i < _systems.Length; i++)
                if (_systems[i].name == systemName)
                    return i;
            return -1;
        }

        /// <summary>
        /// Scales one system's emission rate and puff size against what <see cref="Vfx"/> played it at, so a looping
        /// emitter can swell and fade (a rate of 0 stops new puffs; those already out keep going).
        /// </summary>
        public void Tune(int system, float rate, float size)
        {
            Init();
            if (system < 0 || system >= _systems.Length)
                return;
            var ps = _systems[system];
            var b = _base[system];
            var emission = ps.emission;
            emission.rateOverTimeMultiplier = b.rate * Mathf.Max(0f, rate);
            var main = ps.main;
            if (!main.startSize3D)
                main.startSizeMultiplier = b.size * _scale * Mathf.Max(0f, size);
        }

        /// <summary>Tints one system's new puffs: a random colour between <paramref name="a"/> and <paramref name="b"/>.</summary>
        public void SetColor(int system, Color a, Color b)
        {
            Init();
            if (system < 0 || system >= _systems.Length)
                return;
            var main = _systems[system].main;
            main.startColor = new ParticleSystem.MinMaxGradient(a, b);
        }

        /// <summary>Tints the systems called <paramref name="systemName"/>, light tone <paramref name="color"/> (a one-shot's dust).</summary>
        public void SetColor(string systemName, Color color, float shade = 0.82f)
        {
            Init();
            for (int i = 0; i < _systems.Length; i++)
                if (_systems[i].name == systemName)
                    SetColor(i, color, Shade(color, shade));
        }

        /// <summary><paramref name="color"/> darkened by <paramref name="shade"/> (alpha kept): the second tone of a tinted puff.</summary>
        public static Color Shade(Color color, float shade) => new(color.r * shade, color.g * shade, color.b * shade, color.a);

        /// <summary>Pauses or resumes emission of a looping effect (its puffs already out keep going).</summary>
        public void SetEmitting(bool emitting)
        {
            Init();
            foreach (var ps in _systems)
            {
                var emission = ps.emission;
                emission.enabled = emitting;
            }
        }

        /// <summary>Stops emitting and detaches from whatever it follows; the effect returns to the pool once its puffs are gone.</summary>
        public void Stop()
        {
            if (!IsPlaying || !IsEmitting)
                return;
            IsEmitting = false;
            if (!gameObject.activeInHierarchy)
            {
                // What it rides on is being switched off (Unity forbids reparenting mid-deactivation): just end it.
                // It goes back to the pool when that comes back on, or dies with it.
                foreach (var ps in _systems)
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                _endTime = 0f;
                return;
            }
            transform.SetParent(Vfx.PoolRoot, true);
            foreach (var ps in _systems)
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            _endTime = Time.time + _lifetime;
        }

        /// <summary>Back to the pool now, particles and all.</summary>
        internal void Recycle()
        {
            IsPlaying = false;
            IsEmitting = false;
            _endTime = float.PositiveInfinity;
            if (_systems != null)
                foreach (var ps in _systems)
                    ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            gameObject.SetActive(false);
        }

        void Update()
        {
            if (IsPlaying && Time.time >= _endTime)
                Vfx.Release(this);
        }
    }
}

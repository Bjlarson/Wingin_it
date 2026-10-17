using Unity.Cinemachine;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.Settings;

namespace WingingIt.CameraSystem
{
    /// <summary>
    /// Third-person chase pose for the plane. A Cinemachine camera hard-locks to this transform;
    /// this rig decides where it sits and owns the mouse-aim direction.
    /// <para>Mouse-aim: the mouse turns the camera and the plane chases where the camera looks.
    /// Direct: the camera trails the plane with a little lag and partial roll.</para>
    /// Sensitivities are scaled, and the base FOV set, by <see cref="GameSettings"/>; Speed FOV off holds the base FOV.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class FlightCameraRig : MonoBehaviour
    {
        [SerializeField] PlaneController target;
        [SerializeField] CinemachineCamera cinemachineCamera;

        [Header("Framing")]
        [SerializeField] Vector3 offset = new Vector3(0f, 3.5f, -16f);
        [Tooltip("The camera looks at a point this far ahead of the plane.")]
        [SerializeField] float lookAheadDistance = 40f;
        [Tooltip("Distance of the aim point used for mouse-aim and the reticles.")]
        [SerializeField] float aimDistance = 600f;

        [Header("Direct mode")]
        [SerializeField] float chaseRotationSmoothing = 7f;
        [Range(0f, 1f)]
        [SerializeField] float rollFollow = 0.35f;

        [Header("Mouse aim")]
        [Tooltip("Degrees of aim per mouse count.")]
        [SerializeField] float mouseAimSensitivity = 0.1f;
        [SerializeField] float aimPitchLimit = 80f;

        [Header("Free look")]
        [SerializeField] float freeLookMouseSensitivity = 0.15f;
        [SerializeField] float stickLookSpeed = 160f;
        [SerializeField] float freeLookReturnSpeed = 5f;

        [Header("Death cam")]
        [Tooltip("While dead, the camera eases back to this multiple of the chase offset to frame the explosion.")]
        [SerializeField] float deathPullback = 3f;
        [SerializeField] float deathPullbackSpeed = 1.2f;

        [Header("Field of view")]
        [SerializeField] float baseFov = 60f;
        [SerializeField] float topSpeedFov = 68f;
        [SerializeField] float boostFov = 78f;
        [SerializeField] float fovSmoothing = 4f;

        float _aimYaw, _aimPitch;          // world-space aim, pitch + = up
        float _lookYaw, _lookPitch;        // free-look offset from the base view
        Quaternion _chaseRotation = Quaternion.identity;
        Vector2 _stickLook;
        float _fov;
        float _deathBlend; // 0 = chase framing, 1 = fully pulled back
        float _aimScale = 1f, _lookScale = 1f, _fovOffset;
        bool _speedFov = true;

        public PlaneController Target => target;
        public bool MouseAimMode { get; set; }
        public bool FreeLookHeld { get; set; }

        public Quaternion AimRotation => Quaternion.Euler(-_aimPitch, _aimYaw, 0f);
        public Vector3 AimPoint => target.transform.position + AimRotation * Vector3.forward * aimDistance;
        public Vector3 BoresightPoint => target.transform.position + target.transform.forward * aimDistance;

        void OnEnable()
        {
            GameSettings.Current.Changed += ReadSettings;
            ReadSettings();
            _fov = baseFov + _fovOffset;
            if (target != null)
            {
                target.Respawned += OnRespawned;
                SnapBehindTarget();
            }
        }

        void OnDisable()
        {
            GameSettings.Current.Changed -= ReadSettings;
            if (target != null)
                target.Respawned -= OnRespawned;
        }

        void ReadSettings()
        {
            var s = GameSettings.Current.Data;
            _aimScale = s.mouseAimSensitivity;
            _lookScale = s.freeLookSensitivity;
            _fovOffset = s.fieldOfView - baseFov; // the speed and boost widening keep their offsets
            _speedFov = s.speedFov;
        }

        public void SetTarget(PlaneController plane)
        {
            if (target != null)
                target.Respawned -= OnRespawned;
            target = plane;
            if (isActiveAndEnabled && target != null)
            {
                target.Respawned += OnRespawned;
                SnapBehindTarget();
            }
        }

        public void SetCinemachineCamera(CinemachineCamera cam) => cinemachineCamera = cam;

        /// <summary>Raw mouse movement this frame. Aims in mouse-aim mode, orbits while free look is held.</summary>
        public void AddMouseDelta(Vector2 delta)
        {
            if (FreeLookHeld)
            {
                _lookYaw += delta.x * freeLookMouseSensitivity * _lookScale;
                _lookPitch = Mathf.Clamp(_lookPitch + delta.y * freeLookMouseSensitivity * _lookScale, -70f, 70f);
            }
            else if (MouseAimMode)
            {
                _aimYaw += delta.x * mouseAimSensitivity * _aimScale;
                _aimPitch = Mathf.Clamp(_aimPitch + delta.y * mouseAimSensitivity * _aimScale, -aimPitchLimit, aimPitchLimit);
            }
        }

        /// <summary>Right-stick look (-1..1). Springs back when released.</summary>
        public void SetStickLook(Vector2 stick) => _stickLook = stick;

        /// <summary>Pulls the mouse-aim direction toward the plane's nose (used while flying with keys in mouse-aim mode).</summary>
        public void RecenterAim(float degreesPerSecond)
        {
            GetYawPitch(target.transform.forward, out float yaw, out float pitch);
            float step = degreesPerSecond * Time.deltaTime;
            _aimYaw = Mathf.MoveTowardsAngle(_aimYaw, yaw, step);
            _aimPitch = Mathf.MoveTowards(_aimPitch, pitch, step);
        }

        void OnRespawned(PlaneController _) => SnapBehindTarget();

        void SnapBehindTarget()
        {
            GetYawPitch(target.transform.forward, out _aimYaw, out _aimPitch);
            _chaseRotation = Quaternion.LookRotation(target.transform.forward, Vector3.up);
            _lookYaw = _lookPitch = 0f;
            _deathBlend = 0f;
        }

        static void GetYawPitch(Vector3 forward, out float yaw, out float pitch)
        {
            yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
            pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        }

        void LateUpdate()
        {
            if (target == null)
                return;

            float dt = Time.deltaTime;
            Transform plane = target.transform;

            // Free look: stick or held button; otherwise spring back to center.
            if (_stickLook.sqrMagnitude > 0.01f)
            {
                _lookYaw = Mathf.Clamp(_lookYaw + _stickLook.x * stickLookSpeed * _lookScale * dt, -160f, 160f);
                _lookPitch = Mathf.Clamp(_lookPitch + _stickLook.y * stickLookSpeed * _lookScale * dt, -70f, 70f);
            }
            else if (!FreeLookHeld)
            {
                float k = 1f - Mathf.Exp(-freeLookReturnSpeed * dt);
                _lookYaw = Mathf.Lerp(_lookYaw, 0f, k);
                _lookPitch = Mathf.Lerp(_lookPitch, 0f, k);
            }

            Vector3 desiredUp = Vector3.Slerp(Vector3.up, plane.up, rollFollow);
            Quaternion chaseTarget = Quaternion.LookRotation(plane.forward, desiredUp);
            _chaseRotation = Quaternion.Slerp(_chaseRotation, chaseTarget, 1f - Mathf.Exp(-chaseRotationSmoothing * dt));

            if (!MouseAimMode && !FreeLookHeld)
                GetYawPitch(plane.forward, out _aimYaw, out _aimPitch); // keep in sync for switching modes

            Quaternion baseView = MouseAimMode ? AimRotation : _chaseRotation;
            Quaternion view = baseView * Quaternion.Euler(-_lookPitch, _lookYaw, 0f);

            // The dead plane's transform stays where it died, so the camera holds on the explosion and backs away.
            float deathTarget = target.IsAlive ? 0f : 1f;
            _deathBlend = Mathf.Lerp(_deathBlend, deathTarget, 1f - Mathf.Exp(-deathPullbackSpeed * dt));
            float scale = (target.Stats != null ? target.Stats.visualScale : 1f) * Mathf.Lerp(1f, deathPullback, _deathBlend);
            Vector3 pivot = plane.position;
            Vector3 camPos = pivot + view * (offset * scale);
            Vector3 lookPoint = pivot + view * Vector3.forward * lookAheadDistance;
            transform.SetPositionAndRotation(camPos, Quaternion.LookRotation(lookPoint - camPos, view * Vector3.up));

            UpdateFov(dt);
        }

        void UpdateFov(float dt)
        {
            if (cinemachineCamera == null)
                return;
            float targetFov = !_speedFov ? baseFov
                : target.IsBoosting ? boostFov
                : Mathf.Lerp(baseFov, topSpeedFov, Mathf.Clamp01(target.NormalizedSpeed));
            targetFov += _fovOffset;
            _fov = Mathf.Lerp(_fov, targetFov, 1f - Mathf.Exp(-fovSmoothing * dt));
            cinemachineCamera.Lens.FieldOfView = _fov;
        }
    }
}

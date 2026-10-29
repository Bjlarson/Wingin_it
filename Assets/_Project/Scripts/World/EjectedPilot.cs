using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// A pilot who bailed out (<see cref="PlaneEjection"/>): fired clear of the wreck, a moment of free fall (spread out,
    /// <see cref="PilotMove.Freefall"/>), then the canopy blooms open with a jolt (<see cref="PilotMove.Deploy"/>) and the
    /// pilot floats down under it, hands on the risers, legs swinging (<see cref="PilotMove.Hang"/>), the whole hang
    /// swaying a little. On touching ground or sea the pilot lands in a knee bend (<see cref="PilotMove.Land"/>), the
    /// canopy sags and everything is gone a few seconds later. Pure show: no collider, nothing hits it,
    /// and it runs on its own simple motion (<see cref="Step"/>), not physics.
    /// </summary>
    public class EjectedPilot : MonoBehaviour
    {
        /// <summary>Seconds of free fall before the canopy opens.</summary>
        public const float ChuteDelay = 0.8f;
        /// <summary>Seconds the canopy takes to bloom to full size.</summary>
        public const float ChuteOpenTime = 0.6f;
        /// <summary>The steady fall under a full canopy (m/s).</summary>
        public const float DescentSpeed = 5f;
        const float Gravity = 9.81f;
        const float FreeFallDrag = 0.15f; // per second, on the whole velocity
        const float ChuteDrag = 1.6f;     // per second: how fast the canopy bleeds off speed toward the descent
        const float LandedTime = 3f;      // seconds on the ground before it's gone
        const float Lifetime = 90f;       // a pilot that never lands (fell off the map) is gone after this

        static readonly List<EjectedPilot> _all = new();
        /// <summary>Every pilot in the air or just landed.</summary>
        public static IReadOnlyList<EjectedPilot> All => _all;

        PilotFigure _figure;
        PilotParachute _chute;
        Vector3 _velocity;
        float _age;
        float _landedAge = -1f;
        bool _deployed;

        public PilotFigure Figure => _figure;
        public PilotParachute Parachute => _chute;
        public Vector3 Velocity => _velocity;
        public bool ChuteOpen => _age >= ChuteDelay;
        public bool Landed => _landedAge >= 0f;

        /// <summary>
        /// Puts a pilot at <paramref name="position"/> (its feet) facing <paramref name="forward"/> (flattened), moving at
        /// <paramref name="velocity"/>, wearing <paramref name="look"/> (its TEAM swatches in <paramref name="team"/>).
        /// </summary>
        public static EjectedPilot Launch(PilotFigure pilotPrefab, PilotParachute parachutePrefab, Vector3 position,
            Vector3 forward, Vector3 velocity, Color team, PilotLook look)
        {
            Vector3 flat = Vector3.ProjectOnPlane(forward, Vector3.up);
            var root = new GameObject("Ejected Pilot");
            root.transform.SetPositionAndRotation(position,
                flat.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(flat, Vector3.up) : Quaternion.identity);
            var pilot = root.AddComponent<EjectedPilot>();

            pilot._figure = Instantiate(pilotPrefab, root.transform, false);
            if (parachutePrefab != null)
            {
                pilot._chute = Instantiate(parachutePrefab, root.transform, false);
                var attach = pilot._figure.RiserAttach;
                pilot._chute.transform.localPosition = attach != null ? root.transform.InverseTransformPoint(attach.position) : Vector3.up * 1.5f;
                pilot._chute.gameObject.SetActive(false);
            }
            look.ApplyTo(pilot._figure, pilot._chute, team);
            pilot._figure.Play(PilotMove.Freefall, 0f);
            pilot._velocity = velocity;
            return pilot;
        }

        /// <summary>
        /// One step of the fall: gravity with a little air drag until the canopy is open, then the canopy eases the
        /// speed toward a straight-down <see cref="DescentSpeed"/>, so a pilot fired out at speed slows and floats.
        /// </summary>
        public static Vector3 Step(Vector3 velocity, bool chuteOpen, float dt)
        {
            if (!chuteOpen)
                return (velocity + Vector3.down * (Gravity * dt)) * Mathf.Exp(-FreeFallDrag * dt);
            return Vector3.Lerp(velocity, Vector3.down * DescentSpeed, 1f - Mathf.Exp(-ChuteDrag * dt));
        }

        /// <summary>How open the canopy is (0..1) at <paramref name="age"/> seconds after the ejection, eased.</summary>
        public static float Bloom(float age)
        {
            float t = Mathf.Clamp01((age - ChuteDelay) / ChuteOpenTime);
            return t * t * (3f - 2f * t);
        }

        void OnEnable() => _all.Add(this);
        void OnDisable() => _all.Remove(this);

        void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f)
                return;
            _age += dt;
            if (Landed)
            {
                _landedAge += dt;
                if (_chute != null) // the canopy sags down over the pilot
                {
                    float sag = Mathf.Clamp01(_landedAge / 1.2f);
                    _chute.transform.localScale = new Vector3(1f + 0.3f * sag, Mathf.Lerp(1f, 0.08f, sag), 1f + 0.3f * sag);
                    _chute.transform.localRotation = Quaternion.Euler(Mathf.Lerp(0f, 25f, sag), 0f, 0f); // and drifts off downwind
                }
                if (_landedAge >= LandedTime)
                    Destroy(gameObject);
                return;
            }
            if (_age >= Lifetime)
            {
                Destroy(gameObject);
                return;
            }

            _velocity = Step(_velocity, ChuteOpen, dt);
            Vector3 move = _velocity * dt;
            if (TryFindGround(transform.position, move, out var ground))
            {
                transform.position = ground;
                _landedAge = 0f;
                transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
                _figure.Play(PilotMove.Land, 0.1f);
                return;
            }
            transform.position += move;

            float bloom = Bloom(_age);
            bool open = _age >= ChuteDelay;
            if (open && !_deployed)
            {
                _deployed = true;
                _figure.Play(PilotMove.Deploy, 0.08f); // the Animator hands on to Hang by itself
            }
            if (_chute != null)
            {
                if (_chute.gameObject.activeSelf != open)
                    _chute.gameObject.SetActive(open);
                _chute.SetOpen(bloom, _age);
            }
            // A gentle pendulum under the canopy, about the harness point.
            float sway = bloom * 6f * Mathf.Sin(_age * 1.7f);
            transform.rotation = Quaternion.Euler(0f, transform.eulerAngles.y, sway);
        }

        /// <summary>Solid ground or sea under this step (skipping planes, debris and triggers): where the feet land.</summary>
        static bool TryFindGround(Vector3 feet, Vector3 move, out Vector3 point)
        {
            point = default;
            if (move.y >= 0f)
                return false;
            float reach = -move.y + 0.5f;
            var hits = Physics.RaycastAll(feet + Vector3.up * 0.5f, Vector3.down, reach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            foreach (var hit in hits)
            {
                if (PlaneDebris.IsDebris(hit.collider) || hit.collider.GetComponentInParent<PlaneController>() != null)
                    continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    point = hit.point;
                }
            }
            return best < float.MaxValue;
        }
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Dev
{
    /// <summary>
    /// Prototype-only: damage your own plane's parts until guns and target dummies exist.
    /// Keys 5-0 hit body, left wing, right wing, horizontal stabilizer, vertical stabilizer, engine.
    /// Backspace restores every part. K makes the nearest other plane launch a missile at you.
    /// T sends the next target drone on a straight tow run ahead of you; Y toggles drones breaking when hit.
    /// L puts you on final approach to your home runway (gear down, slow), to practice landings.
    /// N flips the level between air and runway spawns and respawns you the new way.
    /// J makes every AA gun, your own home's included, treat you as an enemy, to feel what attacking an airfield is like.
    /// </summary>
    [RequireComponent(typeof(PlaneHealth))]
    public class DamageTester : MonoBehaviour
    {
        [Tooltip("Fraction of the part's max HP removed per key press.")]
        [SerializeField, Range(0.05f, 1f)] float damageFraction = 0.2f;

        [Tooltip("How far ahead of you a tow-run drone appears.")]
        [SerializeField] float towDistance = 250f;
        [SerializeField] float towDuration = 20f;

        [Tooltip("Final approach (L): distance before the runway's glide-path aim point (m).")]
        [SerializeField] float approachDistance = 560f;

        /// <summary>Whether target drones break hard when another plane hits them (Y). Off at start: easy targets first.</summary>
        public static bool DronesEvade { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => DronesEvade = false;

        PlaneHealth _health;
        int _nextTowDrone;

        void Awake() => _health = GetComponent<PlaneHealth>();

        void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null)
                return;
            if (keyboard.digit5Key.wasPressedThisFrame) Hit(PlanePartType.Body);
            if (keyboard.digit6Key.wasPressedThisFrame) Hit(PlanePartType.WingLeft);
            if (keyboard.digit7Key.wasPressedThisFrame) Hit(PlanePartType.WingRight);
            if (keyboard.digit8Key.wasPressedThisFrame) Hit(PlanePartType.HorizontalStabilizer);
            if (keyboard.digit9Key.wasPressedThisFrame) Hit(PlanePartType.VerticalStabilizer);
            if (keyboard.digit0Key.wasPressedThisFrame) Hit(PlanePartType.Engine);
            if (keyboard.backspaceKey.wasPressedThisFrame) _health.RestoreAll();
            if (keyboard.kKey.wasPressedThisFrame) LaunchMissileAtMe();
            if (keyboard.tKey.wasPressedThisFrame) SendTowDrone();
            if (keyboard.yKey.wasPressedThisFrame) ToggleDroneEvasion();
            if (keyboard.lKey.wasPressedThisFrame) StartFinalApproach();
            if (keyboard.jKey.wasPressedThisFrame) ToggleHostileAA();
            if (keyboard.nKey.wasPressedThisFrame) ToggleSpawnMode();
        }

        /// <summary>
        /// Lined up with the home runway, <see cref="approachDistance"/> out on its glide
        /// slope at idle (class min speed) with the gear locked. Repairs the plane like a respawn; damage it after
        /// (5-0) to practice crippled landings.
        /// </summary>
        void StartFinalApproach()
        {
            var me = _health.Plane;
            var home = Airfield.NearestFriendly(me.Position, me.Team);
            if (home == null)
                return;
            Vector3 start = home.GlidePathPoint(approachDistance);
            float slope = home.GlideSlope.angle;
            if (TryGetComponent(out PlaneRespawner respawner))
                respawner.CancelPendingRespawn();
            me.Respawn(start, Quaternion.LookRotation(home.RunwayDirection) * Quaternion.Euler(slope, 0f, 0f), throttle: 0f);
            if (TryGetComponent(out LandingGear gear))
                gear.Snap(true);
        }

        /// <summary>Lets you practice dodging: the nearest plane whose class carries missiles fires one at you (no lock needed).</summary>
        void LaunchMissileAtMe()
        {
            var me = _health.Plane;
            PlaneMissiles nearest = null;
            float best = float.MaxValue;
            foreach (var launcher in PlaneMissiles.All)
            {
                if (launcher.Plane == me || !launcher.Plane.IsAlive || !launcher.IsEquipped)
                    continue;
                float d = Vector3.Distance(launcher.Plane.Position, me.Position);
                if (d < best)
                {
                    best = d;
                    nearest = launcher;
                }
            }
            if (nearest == null)
                return;
            if (nearest.Ammo == 0)
                nearest.Restock();
            nearest.LaunchAt(me);
        }

        /// <summary>Puts the next drone (taking turns) ahead of you, flying your heading, so you can line up a part shot from behind.</summary>
        void SendTowDrone()
        {
            var drones = DummyPilot.All;
            if (drones.Count == 0)
                return;
            var me = _health.Plane;
            var drone = drones[_nextTowDrone++ % drones.Count];
            Vector3 heading = me.Rotation * Vector3.forward;
            heading.y = 0f;
            if (heading.sqrMagnitude < 0.001f)
                heading = Vector3.forward;
            drone.StartTowRun(me.Position + heading.normalized * towDistance, heading, towDuration);
        }

        /// <summary>Switches the level's spawn mode (air / runway) and respawns you with it, fully repaired.</summary>
        void ToggleSpawnMode()
        {
            var level = LevelSettings.Current;
            if (level == null || !TryGetComponent(out PlaneRespawner respawner))
                return;
            level.SpawnMode = level.SpawnMode == SpawnMode.Air ? SpawnMode.Runway : SpawnMode.Air;
            respawner.SpawnNow();
        }

        void ToggleHostileAA()
        {
            var me = _health.Plane;
            AAGun.DevHostilePlane = AAGun.DevHostilePlane == me ? null : me;
        }

        static void ToggleDroneEvasion()
        {
            DronesEvade = !DronesEvade;
            foreach (var drone in DummyPilot.All)
                drone.EvadeWhenHit = DronesEvade;
        }

        void Hit(PlanePartType type)
        {
            var part = _health.GetPart(type);
            if (part != null)
                _health.ApplyDamage(part, part.MaxHealth * damageFraction, part.transform.position, this);
        }
    }
}

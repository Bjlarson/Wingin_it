using UnityEngine;

namespace WingingIt.World
{
    /// <summary>Where planes on a team appear: after every death, and at the start of the level.</summary>
    public enum SpawnMode
    {
        /// <summary>In the air on the airfield's approach, at cruise speed: straight back into the fight.</summary>
        Air,
        /// <summary>Parked at the start of the runway: throttle up and take off.</summary>
        Runway,
    }

    /// <summary>
    /// Per-level rules, set on one object in each scene so different game modes can play differently. With none in
    /// the scene, everything uses its default (<see cref="SpawnMode.Air"/>).
    /// </summary>
    public class LevelSettings : MonoBehaviour
    {
        [Tooltip("Air: respawn over the home airfield at cruise speed. Runway: start parked on the runway and take off. " +
                 "Applies to respawns over an airfield and to the player's first spawn.")]
        [SerializeField] SpawnMode spawnMode = SpawnMode.Air;

        static LevelSettings s_current;

        /// <summary>The loaded level's settings, or null if the scene has none.</summary>
        public static LevelSettings Current => s_current;

        /// <summary>The loaded level's spawn mode (<see cref="SpawnMode.Air"/> without a <see cref="LevelSettings"/>).</summary>
        public static SpawnMode CurrentSpawnMode => s_current != null ? s_current.spawnMode : SpawnMode.Air;

        public SpawnMode SpawnMode
        {
            get => spawnMode;
            set => spawnMode = value;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => s_current = null;

        void OnEnable()
        {
            if (s_current != null && s_current != this)
                Debug.LogWarning($"Two LevelSettings are loaded ({s_current.name}, {name}); using {name}.", this);
            s_current = this;
        }

        void OnDisable()
        {
            if (s_current == this)
                s_current = null;
        }
    }
}

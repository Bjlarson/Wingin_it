using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>
    /// Marks a map scene (<c>Scenes/Maps/Map_&lt;name&gt;</c>) and names the <see cref="MapDefinition"/> it was built from,
    /// so whatever loads the map can read its layout, regions, minimap and sky. A map scene holds only what every mode
    /// needs (terrain, sea, scenery, landmarks, clouds, light); airfields, stands, AA and arenas come from the layout.
    /// </summary>
    public class MapScene : MonoBehaviour
    {
        [SerializeField] MapDefinition definition;

        public MapDefinition Definition => definition;

        /// <summary>The map scene loaded right now, or null.</summary>
        public static MapScene Current => FindAnyObjectByType<MapScene>();

        /// <summary>
        /// The map being played: the one the level's mode pieces spawn from (<see cref="ModePieceBuilder.Map"/>), else the
        /// loaded map scene's; null in a test level with neither.
        /// </summary>
        public static MapDefinition CurrentMap
        {
            get
            {
                var pieces = FindAnyObjectByType<ModePieceBuilder>();
                if (pieces != null && pieces.Map != null)
                    return pieces.Map;
                var scene = Current;
                return scene != null ? scene.Definition : null;
            }
        }

        public void Configure(MapDefinition map) => definition = map;
    }
}

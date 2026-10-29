using UnityEditor;
using UnityEngine;
using WingingIt.Maps;

namespace WingingIt.Editor
{
    /// <summary>
    /// Helpers for writing a heightmap map's definition by hand: airfields set at the height of the ground they stand
    /// on (the terrain builder then levels a pad there, <see cref="MapTerrainBuilder.PadHalfSize"/>), and approach
    /// lanes drawn in the field's own frame so they turn with it.
    /// </summary>
    internal static class MapLayoutWriter
    {
        /// <summary>
        /// An airfield whose runway sits at the mean height of the map's raw ground over its pad (rounded to 0.5 m).
        /// Yaw 0 = landing heading north.
        /// </summary>
        public static AirfieldAnchor Airfield(MapDefinition map, Heightmap ground, string name, int team, bool home,
            bool capturable, bool twoWay, Vector2 position, float yaw)
        {
            float sum = 0f;
            int count = 0;
            var rotation = Quaternion.Euler(0f, yaw, 0f);
            for (float v = -MapTerrainBuilder.PadHalfSize.y; v <= MapTerrainBuilder.PadHalfSize.y; v += 25f)
            for (float u = -MapTerrainBuilder.PadHalfSize.x; u <= MapTerrainBuilder.PadHalfSize.x; u += 25f)
            {
                Vector3 p = rotation * new Vector3(u, 0f, v);
                sum += map.GroundHeight(ground, position.x + p.x, position.y + p.z);
                count++;
            }
            return new AirfieldAnchor
            {
                name = name, team = team, home = home, capturable = capturable, twoWay = twoWay,
                position = position, yaw = yaw, elevation = Mathf.Round(sum / count * 2f) * 0.5f,
            };
        }

        /// <summary>
        /// A lane into <paramref name="field"/> from points in its approach frame: x right of the landing heading, y
        /// along it (negative before the runway's middle; the final fix is ~900 m short of it). The far end's frame
        /// is turned round when <paramref name="reverseEnd"/>.
        /// </summary>
        public static LaneAnchor Lane(AirfieldAnchor field, string name, Color color, bool reverseEnd, params (float x, float y, float agl)[] local)
        {
            var rotation = Quaternion.Euler(0f, field.yaw + (reverseEnd ? 180f : 0f), 0f);
            var points = new LayoutPoint[local.Length];
            for (int i = 0; i < local.Length; i++)
            {
                Vector3 p = rotation * new Vector3(local[i].x, 0f, local[i].y);
                points[i] = new LayoutPoint(field.position + new Vector2(p.x, p.z), local[i].agl);
            }
            return new LaneAnchor { airfield = field.name, name = name, color = color, reverseEnd = reverseEnd, points = points };
        }

        /// <summary>A Ram Royale arena: a center patrol point and four round it, all at <paramref name="patrolAltitude"/> above the sea.</summary>
        public static ArenaAnchor RamArena(Vector2 center, float radius, float warning, float ceiling, float startRadius,
            float startAltitude, float patrolRadius, float patrolAltitude)
        {
            var areas = new LayoutPoint[5];
            areas[0] = new LayoutPoint(center, patrolAltitude);
            for (int i = 0; i < 4; i++)
            {
                Vector3 offset = Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * patrolRadius;
                areas[i + 1] = new LayoutPoint(center + new Vector2(offset.x, offset.z), patrolAltitude);
            }
            return new ArenaAnchor
            {
                modeId = "RamRoyale", center = center, radius = radius, warning = warning, ceiling = ceiling,
                hardEdge = true, closeAirfields = true, airStart = true, startRadius = startRadius,
                startAltitude = startAltitude, startSlots = 16, fightAreas = areas,
            };
        }

        /// <summary>Saves the definition and logs what it holds and any layout problems.</summary>
        public static void Save(MapDefinition map)
        {
            EditorUtility.SetDirty(map);
            AssetDatabase.SaveAssets();
            var problems = map.layout.Validate();
            Debug.Log($"[Winging It] {map.displayName} definition written: {map.layout.airfields.Count} airfields, "
                + $"{map.layout.lanes.Count} lanes, {map.regions.areas.Count} regions, {map.landmarks.Count} landmarks, "
                + $"{map.sky.clouds.Count} cloud banks, {problems.Count} problems {string.Join("; ", problems)}");
        }
    }
}

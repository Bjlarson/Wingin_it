using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Effects
{
    /// <summary>What a surface kicks up when something blasts or skims it (living-world.md, section 1).</summary>
    public enum GroundSurface
    {
        /// <summary>Roofs, bridges, buildings: nothing loose to kick up.</summary>
        None,
        /// <summary>The sea and the canal: white mist and spray.</summary>
        Water,
        Sand,
        /// <summary>Grass: the soil under it puffs, as brown dirt.</summary>
        Grass,
        Rock,
        Snow,
        /// <summary>The city's streets and the runways: light grey dust, and not much of it.</summary>
        Paved,
    }

    /// <summary>
    /// Answers "what's underfoot here" for effects: the sea by its layer, a runway by its airfield, and the terrain by
    /// whichever splat layer is strongest at the point. Each terrain's splat map is read once, on first use, into a
    /// small lookup, so a query never touches the terrain again.
    /// </summary>
    public static class EffectsSurface
    {
        /// <summary>
        /// A splat map boiled down to the strongest layer per texel. <see cref="At"/> takes the point's place across the
        /// terrain, 0..1 each way.
        /// </summary>
        public sealed class Lookup
        {
            readonly GroundSurface[] _cells;
            readonly int _resolution;

            /// <summary><paramref name="weights"/> is [z, x, layer] as <see cref="TerrainData.GetAlphamaps"/> gives it.</summary>
            public Lookup(float[,,] weights, IReadOnlyList<GroundSurface> layers)
            {
                _resolution = weights.GetLength(0);
                int width = weights.GetLength(1), count = Mathf.Min(weights.GetLength(2), layers.Count);
                _cells = new GroundSurface[_resolution * width];
                for (int z = 0; z < _resolution; z++)
                    for (int x = 0; x < width; x++)
                    {
                        int best = 0;
                        for (int l = 1; l < count; l++)
                            if (weights[z, x, l] > weights[z, x, best])
                                best = l;
                        _cells[z * width + x] = count > 0 ? layers[best] : GroundSurface.Grass;
                    }
            }

            public GroundSurface At(float nx, float nz)
            {
                int width = _cells.Length / Mathf.Max(1, _resolution);
                int x = Mathf.Clamp((int)(nx * width), 0, width - 1);
                int z = Mathf.Clamp((int)(nz * _resolution), 0, _resolution - 1);
                return _cells[z * width + x];
            }
        }

        static readonly Dictionary<TerrainData, Lookup> Lookups = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Lookups.Clear();

        /// <summary>The light tone of what <paramref name="surface"/> kicks up (the puff meshes shade it down on their sides).</summary>
        public static Color Color(GroundSurface surface) => surface switch
        {
            GroundSurface.Water => new Color32(0xF4, 0xFB, 0xFF, 0xFF),
            GroundSurface.Sand => new Color32(0xE6, 0xC8, 0x8A, 0xFF),
            GroundSurface.Grass => new Color32(0x8B, 0x6B, 0x4A, 0xFF),
            GroundSurface.Rock => new Color32(0xA3, 0x9E, 0x96, 0xFF),
            GroundSurface.Snow => new Color32(0xF2, 0xF6, 0xFA, 0xFF),
            GroundSurface.Paved => new Color32(0xB9, 0xB9, 0xC2, 0xFF),
            _ => UnityEngine.Color.white,
        };

        /// <summary>How much <paramref name="surface"/> kicks up, 0..1: paving gives much less dust, a roof none.</summary>
        public static float Amount(GroundSurface surface) => surface switch
        {
            GroundSurface.None => 0f,
            GroundSurface.Paved => 0.4f,
            GroundSurface.Rock => 0.8f,
            _ => 1f,
        };

        /// <summary>What a terrain layer counts as, from its name (the builder's Grass, Sand, Rock, Snow and Paved).</summary>
        public static GroundSurface FromLayerName(string layerName)
        {
            if (string.IsNullOrEmpty(layerName))
                return GroundSurface.Grass;
            foreach (var surface in new[] { GroundSurface.Sand, GroundSurface.Rock, GroundSurface.Snow, GroundSurface.Paved })
                if (layerName.IndexOf(surface.ToString(), System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return surface;
            return GroundSurface.Grass;
        }

        /// <summary>What <paramref name="surface"/> is at <paramref name="point"/> (a hit on it).</summary>
        public static GroundSurface Classify(Collider surface, Vector3 point)
        {
            if (surface == null)
                return GroundSurface.None;
            if (PlaneController.IsWater(surface))
                return GroundSurface.Water;
            if (surface is TerrainCollider && surface.TryGetComponent(out Terrain terrain) && terrain.terrainData != null)
            {
                var data = terrain.terrainData;
                Vector3 local = point - terrain.GetPosition();
                return LookupFor(data).At(local.x / data.size.x, local.z / data.size.z);
            }
            var field = surface.GetComponentInParent<Airfield>();
            if (field != null)
            {
                Vector3 local = field.transform.InverseTransformPoint(point);
                Vector2 half = field.RunwaySize * 0.5f;
                if (Mathf.Abs(local.x) <= half.x && Mathf.Abs(local.z) <= half.y)
                    return GroundSurface.Paved;
            }
            return GroundSurface.None;
        }

        /// <summary>What lies straight below <paramref name="point"/> (from a little above it), or None if nothing does nearby.</summary>
        public static GroundSurface Below(Vector3 point, float reach = 10f)
        {
            return Physics.Raycast(point + Vector3.up * 3f, Vector3.down, out var hit, reach + 3f, Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore)
                ? Classify(hit.collider, hit.point)
                : GroundSurface.None;
        }

        static Lookup LookupFor(TerrainData data)
        {
            if (Lookups.TryGetValue(data, out var lookup))
                return lookup;
            var layers = new List<GroundSurface>();
            foreach (var layer in data.terrainLayers)
                layers.Add(FromLayerName(layer != null ? layer.name : null));
            int res = data.alphamapResolution;
            lookup = new Lookup(data.alphamapLayers > 0 ? data.GetAlphamaps(0, 0, res, res) : new float[1, 1, 0], layers);
            Lookups[data] = lookup;
            return lookup;
        }
    }
}

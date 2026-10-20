using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// The city pack (ArtSource/City/build_city.py → Art/Models/City/City.fbx): twelve buildings, each with a plain
    /// <c>&lt;Name&gt;Col</c> mesh for its collider, the street kit (block sidewalks, roads, crossings, plazas, canal quays),
    /// the canal bridge and vehicles. <see cref="Load"/> returns null when the FBX hasn't been generated, and the Island
    /// keeps its greybox boxes.
    /// </summary>
    internal class CityModels : PackModels
    {
        /// <summary>Every building, tallest first. Their sizes come from their collider meshes.</summary>
        static readonly string[] Buildings =
        {
            "TowerDeco", "TowerGlass", "TowerRound", "TowerBlue", "TowerSlab", "Hotel", "OfficeBands", "OfficeGlass",
            "Apartments", "BrickBlock", "Garage", "Shops",
        };

        public static readonly string[] Cars = { "CarSun", "CarWhite", "CarTeal", "Bus" };

        /// <summary>A building is stretched at most this much up or down to make a lot's height.</summary>
        const float MaxStretch = 1.3f;

        /// <summary>Screen height share below which a car or boat is culled (a car at ~600 m).</summary>
        const float VehicleCullHeight = 0.0035f;

        readonly Dictionary<string, Bounds> _sizes = new();

        CityModels(GameObject source) : base(source)
        {
            foreach (var name in Buildings)
                _sizes[name] = Mesh(name + "Col").bounds;
        }

        // Readable: the buildings' mesh colliders are scaled unevenly, which Unity cooks from the mesh data.
        public static CityModels Load()
        {
            var source = LoadSource("City", readable: true);
            return source == null ? null : new CityModels(source);
        }

        /// <summary>
        /// A building for a lot <paramref name="width"/> (x) by <paramref name="depth"/> (z) and <paramref name="height"/>
        /// tall, standing on <paramref name="ground"/>: one whose own height is within <see cref="MaxStretch"/> of it
        /// (chosen at random among those, or the nearest), turned a random quarter, scaled the same across x and z to fit
        /// inside the lot and up to the height. Its collider is its plain masses.
        /// </summary>
        public GameObject PlaceBuilding(Transform parent, Vector3 ground, float width, float depth, float height, System.Random rng)
        {
            var fits = new List<string>();
            string nearest = null;
            float best = float.MaxValue;
            foreach (var name in Buildings)
            {
                float stretch = height / _sizes[name].max.y;
                if (stretch <= MaxStretch && stretch >= 1f / MaxStretch)
                    fits.Add(name);
                float off = Mathf.Abs(Mathf.Log(stretch));
                if (off < best)
                {
                    best = off;
                    nearest = name;
                }
            }
            string kind = fits.Count > 0 ? fits[rng.Next(fits.Count)] : nearest;

            Bounds size = _sizes[kind];
            int quarter = rng.Next(4);
            float ex = quarter % 2 == 0 ? size.size.x : size.size.z, ez = quarter % 2 == 0 ? size.size.z : size.size.x;
            float s = Mathf.Min(width / ex, depth / ez);
            var go = Place(kind, parent, ground, Quaternion.Euler(0f, quarter * 90f, 0f), new Vector3(s, height / size.max.y, s), kind + "Col");
            return go;
        }

        /// <summary>A car or boat: no collider, culled when it's a speck.</summary>
        public GameObject PlaceVehicle(string name, Transform parent, Vector3 position, float yaw)
        {
            var go = Place(name, parent, position, Quaternion.Euler(0f, yaw, 0f), Vector3.one, null);
            go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(VehicleCullHeight, new[] { go.GetComponent<Renderer>() }) });
            return go;
        }
    }
}

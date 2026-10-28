using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// One map's own pack (ArtSource/MapPacks/build_&lt;pack&gt;.py → Art/Models/&lt;Pack&gt;/&lt;Pack&gt;.fbx): Tropics for the
    /// Island, Prairie for Broad Reach, Isles for the Archipelago, Alpine for the Mountain Range. Its buildings and set
    /// pieces are placed as static meshes (<see cref="PackModels.Place(string, Transform, Vector3, Quaternion, Vector3, string, bool)"/>);
    /// its trees and small props become prefabs in Prefabs/MapPacks/&lt;Pack&gt; for the terrain to draw as trees.
    /// <see cref="Load"/> returns null when the FBX hasn't been generated, and the map goes undressed.
    /// </summary>
    internal sealed class MapPackModels : PackModels
    {
        /// <summary>The share of the screen below which a pack prop drawn as a terrain tree is culled (as the nature pack's).</summary>
        const float CullHeight = 0.0035f;

        public readonly string Pack;
        readonly Dictionary<string, GameObject> _prefabs = new();

        MapPackModels(string pack, GameObject source) : base(source) => Pack = pack;

        public static MapPackModels Load(string pack)
        {
            var source = LoadSource(pack);
            return source == null ? null : new MapPackModels(pack, source);
        }

        /// <summary>The horizontal radius (m) an object covers about its origin, from its mesh's bounds.</summary>
        public float Radius(string name)
        {
            var b = Mesh(name).bounds;
            return Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x), Mathf.Abs(b.min.z), Mathf.Abs(b.max.z));
        }

        /// <summary>
        /// A prefab of one prop for the terrain to scatter as a tree (mesh at its root, a one-level LOD group so it
        /// culls when small), made or refreshed in Prefabs/MapPacks/&lt;Pack&gt;.
        /// </summary>
        public GameObject TreePrefab(string name)
        {
            if (_prefabs.TryGetValue(name, out var cached))
                return cached;
            string folder = $"{FlightTestSceneBuilder.Root}/Prefabs/MapPacks/{Pack}";
            FlightTestSceneBuilder.EnsureFolder($"{FlightTestSceneBuilder.Root}/Prefabs/MapPacks");
            FlightTestSceneBuilder.EnsureFolder(folder);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials(name);
            go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(CullHeight, new Renderer[] { r }) });
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{folder}/{name}.prefab");
            Object.DestroyImmediate(go);
            _prefabs[name] = prefab;
            return prefab;
        }
    }
}

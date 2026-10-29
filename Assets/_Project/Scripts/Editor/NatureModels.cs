using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// The nature pack (ArtSource/Nature/build_nature.py → Art/Models/Nature/Nature.fbx): trees, bushes, rocks, desert
    /// and beach props, grass details, the three mesas, Lighthouse Rock's islet and the cloud puffs, one object each,
    /// on the shared plane materials. Props become prefabs in Prefabs/Nature for the terrain to scatter as trees and
    /// details; the hero pieces and puffs are used as meshes. <see cref="Load"/> returns null when the FBX hasn't been
    /// generated, and the Island keeps its greybox.
    /// </summary>
    internal class NatureModels : PackModels
    {
        const string PrefabFolder = FlightTestSceneBuilder.Root + "/Prefabs/Nature";

        /// <summary>Screen height share below which a scattered prop is culled: a 10 m tree at ~2.5 km, a bush at ~600 m.</summary>
        const float TreeCullHeight = 0.0035f;

        readonly Dictionary<string, GameObject> _prefabs = new();

        NatureModels(GameObject source) : base(source) { }

        public static NatureModels Load()
        {
            var source = LoadSource("Nature");
            return source == null ? null : new NatureModels(source);
        }

        Material _cloud;

        /// <summary>
        /// The clouds' material: the palette unlit, so a puff's top-light / side / belly-shadow tones are its whole
        /// shading and it stays bright white from below or against the sun.
        /// </summary>
        public Material CloudMaterial
        {
            get
            {
                if (_cloud != null)
                    return _cloud;
                string path = $"{FlightTestSceneBuilder.MaterialsFolder}/Env_Cloud.mat";
                _cloud = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (_cloud == null)
                {
                    _cloud = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                    AssetDatabase.CreateAsset(_cloud, path);
                }
                _cloud.SetTexture("_BaseMap", ClassModels.PaletteTexture());
                _cloud.SetColor("_BaseColor", Color.white);
                _cloud.enableInstancing = true;
                EditorUtility.SetDirty(_cloud);
                return _cloud;
            }
        }

        /// <summary>
        /// A prefab of one prop (mesh and materials at its root, as terrain trees and details need), made or refreshed
        /// in Prefabs/Nature. Details (grass, flowers) cast no shadows and take no LOD group. Trees get a LOD group
        /// (<see cref="TreeBillboards"/>): the mesh near, a camera-facing picture further off, culled once smaller than
        /// <see cref="TreeCullHeight"/> of the screen (so a bush goes long before a tree). The group also tells the
        /// terrain they're mesh trees, not legacy ones it would billboard itself.
        /// </summary>
        public GameObject Prefab(string name, bool castShadows = true, bool lod = true)
        {
            if (_prefabs.TryGetValue(name, out var cached))
                return cached;
            FlightTestSceneBuilder.EnsureFolder(PrefabFolder);
            var go = new GameObject(name);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials(name);
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (lod)
                TreeBillboards.Add(go, "Nature", TreeCullHeight);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, $"{PrefabFolder}/{name}.prefab");
            Object.DestroyImmediate(go);
            _prefabs[name] = prefab;
            return prefab;
        }
    }
}

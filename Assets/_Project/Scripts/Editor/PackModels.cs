using UnityEditor;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// An environment pack generated in Blender (ArtSource/&lt;Pack&gt;/build_*.py → Art/Models/&lt;Pack&gt;/&lt;Pack&gt;.fbx): one
    /// object per asset on the shared plane materials. Gives the scene builders its meshes and places them as static
    /// scene objects.
    /// </summary>
    internal abstract class PackModels
    {
        protected readonly GameObject Source;

        protected PackModels(GameObject source) => Source = source;

        /// <summary>The pack's FBX imported with the plane materials remapped, or null when it hasn't been generated.</summary>
        protected static GameObject LoadSource(string pack, bool readable = false) =>
            ClassModels.LoadModel($"{ClassModels.ModelsFolder}/{pack}/{pack}.fbx", new ClassModels.PlaneMaterials(), readable: readable);

        public Mesh Mesh(string name) => Child(name).GetComponent<MeshFilter>().sharedMesh;

        public Material[] Materials(string name) => Child(name).GetComponent<MeshRenderer>().sharedMaterials;

        public bool Has(string name) => Source.transform.Find(name) != null;

        protected Transform Child(string name) =>
            Source.transform.Find(name) ?? throw new System.ArgumentException($"{Source.name} has no object '{name}'");

        /// <summary>An object's origin in the pack (Unity space): its pivot.</summary>
        public Vector3 Pivot(string name) => Child(name).localPosition;

        /// <summary>
        /// One of the pack's meshes as a child of <paramref name="parent"/> at <paramref name="localPosition"/>, turned
        /// <paramref name="yaw"/>, with a mesh collider from <paramref name="colliderMesh"/> (another of the pack's objects)
        /// unless that's null. Static; the caller clears that on moving pieces.
        /// </summary>
        public GameObject Child(string name, Transform parent, Vector3 localPosition, float yaw = 0f, string colliderMesh = null,
            bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localRotation = Quaternion.Euler(0f, yaw, 0f);
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials(name);
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (colliderMesh != null)
                go.AddComponent<MeshCollider>().sharedMesh = Mesh(colliderMesh);
            go.isStatic = true;
            return go;
        }

        /// <summary>A piece that moves on <paramref name="parent"/> (made from <paramref name="parentName"/>), at its pivot.</summary>
        public GameObject Mounted(string name, Transform parent, string parentName, bool castShadows = true) =>
            Child(name, parent, Pivot(name) - Pivot(parentName), castShadows: castShadows);

        /// <summary>A static scene object showing one of the pack's meshes, optionally with a matching mesh collider.</summary>
        public GameObject Place(string name, Transform parent, Vector3 position, bool collider, bool castShadows = true) =>
            Place(name, parent, position, Quaternion.identity, Vector3.one, collider ? name : null, castShadows);

        /// <summary>
        /// A static scene object showing one of the pack's meshes, turned and scaled, with a mesh collider made from
        /// <paramref name="colliderMesh"/> (another object of the pack, e.g. a building's plain masses) unless that's null.
        /// </summary>
        public GameObject Place(string name, Transform parent, Vector3 position, Quaternion rotation, Vector3 scale,
            string colliderMesh, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(position, rotation);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = Mesh(name);
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = Materials(name);
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            if (colliderMesh != null)
                go.AddComponent<MeshCollider>().sharedMesh = Mesh(colliderMesh);
            go.isStatic = true;
            return go;
        }
    }
}

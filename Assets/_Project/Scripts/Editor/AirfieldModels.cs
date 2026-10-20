using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// The airfield pack (ArtSource/Airfield/build_airfield.py → Art/Models/Airfield/Airfield.fbx): the runway kit, the
    /// repair pad's paint, the apron, the hangar (with its interior), the control tower and its beacon, the AA gun in
    /// four moving pieces, the radar, the windsock and the props, one object each on the shared plane materials. Each
    /// object's origin is its own pivot, so a piece that moves (beacon, dish, sock, AA head, cradle, barrels) sits at
    /// its pivot minus its parent's. <see cref="Load"/> returns null when the FBX hasn't been generated, and the
    /// airfield keeps its greybox.
    /// </summary>
    internal class AirfieldModels : PackModels
    {
        /// <summary>Screen height share below which a small prop (crates, barrels, a mechanic) is culled.</summary>
        const float PropCullHeight = 0.004f;

        AirfieldModels(GameObject source) : base(source) { }

        public static AirfieldModels Load()
        {
            var source = LoadSource("Airfield");
            return source == null ? null : new AirfieldModels(source);
        }

        /// <summary>A small prop, culled once it's a speck; <paramref name="solid"/> gives it a box collider round its mesh.</summary>
        public GameObject Prop(string name, Transform parent, Vector3 localPosition, float yaw, bool solid)
        {
            var go = Child(name, parent, localPosition, yaw);
            var r = go.GetComponent<Renderer>();
            if (solid)
            {
                var bounds = go.GetComponent<MeshFilter>().sharedMesh.bounds;
                var box = go.AddComponent<BoxCollider>();
                box.center = bounds.center;
                box.size = bounds.size;
            }
            else
            {
                go.AddComponent<LODGroup>().SetLODs(new[] { new LOD(PropCullHeight, new[] { r }) });
            }
            return go;
        }
    }
}

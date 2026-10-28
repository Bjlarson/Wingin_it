using UnityEditor;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Editor
{
    /// <summary>
    /// The course markers and range targets (ArtSource/Markers/build_markers.py → Art/Models/Markers/Markers.fbx), the
    /// Pack 7 pieces the scene builders place (boundary buoys, the flight-test rings, the bombing range's truck and radar
    /// post) and the approach-lane gate pylon's meshes for the mode piece kit, one object each on the shared plane
    /// materials. <see cref="Load"/> returns null when the FBX hasn't been generated, and the builders keep their greybox.
    /// </summary>
    internal class MarkerModels : PackModels
    {
        MarkerModels(GameObject source) : base(source) { }

        public static MarkerModels Load()
        {
            var source = LoadSource("Markers");
            return source == null ? null : new MarkerModels(source);
        }

        /// <summary>
        /// The glow material in a lane's colour (Marker_&lt;name&gt;): unlit palette, tinted so the team swatch the pylon's
        /// marker is painted in comes out as <paramref name="color"/>.
        /// </summary>
        public static Material LaneMaterial(string name, Color color)
        {
            string path = $"{FlightTestSceneBuilder.MaterialsFolder}/Marker_{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", ClassModels.PaletteTexture());
            mat.SetColor("_BaseColor", PlaneVisuals.TeamTint(color));
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>A boundary buoy on <paramref name="buoy"/> (on the waterline). Returns its lamp, for BoundaryBuoys to blink.</summary>
        public Renderer Buoy(Transform buoy)
        {
            Child("Buoy", buoy, Vector3.zero);
            var lamp = Mounted("BuoyLamp", buoy, "Buoy", castShadows: false);
            return lamp.GetComponent<Renderer>();
        }

        /// <summary>A boundary post on <paramref name="post"/> (on the ground), with the buoy's lamp. Returns its lamp.</summary>
        public Renderer BoundaryPost(Transform post)
        {
            Child("BoundaryPost", post, Vector3.zero);
            var lamp = Mounted("BuoyLamp", post, "BoundaryPost", castShadows: false);
            return lamp.GetComponent<Renderer>();
        }

        /// <summary>
        /// A flight-test ring on <paramref name="ring"/> (round its local z), solid rim from RingCol. Returns its renderer,
        /// whose team-material slot <see cref="RingGate"/> tints.
        /// </summary>
        public Renderer Ring(Transform ring) => Child("Ring", ring, Vector3.zero, colliderMesh: "RingCol").GetComponent<Renderer>();

        /// <summary>The range truck on <paramref name="truck"/>, boxed in by one collider. Not static: a wreck squashes it.</summary>
        public Renderer TargetTruck(Transform truck)
        {
            var go = Child("TargetTruck", truck, Vector3.zero);
            go.isStatic = false;
            var bounds = go.GetComponent<MeshFilter>().sharedMesh.bounds;
            var box = go.AddComponent<BoxCollider>();
            box.center = bounds.center;
            box.size = bounds.size;
            return go.GetComponent<Renderer>();
        }

        /// <summary>
        /// The range's radar post on <paramref name="station"/>: blockhouse and mast boxed in, the dish turning on top
        /// (its own box). Not static: a wreck squashes it.
        /// </summary>
        public Renderer[] TargetRadar(Transform station)
        {
            var go = Child("TargetRadar", station, Vector3.zero);
            go.isStatic = false;
            AddBox(go, new Vector3(0f, 3f, 0f), new Vector3(14f, 6f, 10f));
            AddBox(go, new Vector3(0f, 10f, 0f), new Vector3(3.2f, 8f, 3.2f));
            var dish = Mounted("TargetRadarDish", go.transform, "TargetRadar");
            dish.isStatic = false;
            AddBox(dish, new Vector3(0f, 1f, 0.6f), new Vector3(9f, 4f, 1.2f));
            dish.AddComponent<Spinner>().Configure(-40f);
            return new[] { go.GetComponent<Renderer>(), dish.GetComponent<Renderer>() };
        }

        static void AddBox(GameObject go, Vector3 center, Vector3 size)
        {
            var box = go.AddComponent<BoxCollider>();
            box.center = center;
            box.size = size;
        }
    }
}

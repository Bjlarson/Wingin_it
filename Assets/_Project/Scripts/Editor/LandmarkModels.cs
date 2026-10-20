using UnityEngine;
using WingingIt.World;

namespace WingingIt.Editor
{
    /// <summary>
    /// The landmarks pack (ArtSource/Landmarks/build_landmarks.py → Art/Models/Landmarks/Landmarks.fbx): the Spire,
    /// Summit Radar, the Pyramid and Lighthouse Rock's lighthouse, each standing on its origin at the greybox's size
    /// with a plain <c>&lt;Name&gt;Col</c> for its mesh collider, plus the shared beacon gem and the pieces that move.
    /// <see cref="Load"/> returns null when the FBX hasn't been generated, and the Island keeps its greybox landmarks.
    /// </summary>
    internal class LandmarkModels : PackModels
    {
        /// <summary>The radar's antenna turns this fast (degrees per second), a sweep every 6 s.</summary>
        const float AntennaSpeed = 60f;
        /// <summary>The lighthouse's lens turns this fast, so its bullseyes sweep past every ~4 s.</summary>
        const float LensSpeed = -45f;

        LandmarkModels(GameObject source) : base(source) { }

        public static LandmarkModels Load()
        {
            var source = LoadSource("Landmarks");
            return source == null ? null : new LandmarkModels(source);
        }

        /// <summary>
        /// Landmark <paramref name="name"/> (Spire, SummitRadar, Pyramid, Lighthouse) on <paramref name="root"/>, solid
        /// from its <c>&lt;Name&gt;Col</c>.
        /// </summary>
        public GameObject Body(string name, Transform root) => Child(name, root, Vector3.zero, colliderMesh: name + "Col");

        /// <summary>Where a landmark's beacon sits on it (its <c>&lt;Name&gt;Top</c> marker), relative to its origin.</summary>
        public Vector3 Top(string name) => Pivot(name + "Top") - Pivot(name);

        /// <summary>The shared beacon gem, <paramref name="size"/> across, at <paramref name="localPosition"/>: no shadow, no collider.</summary>
        public Renderer Beacon(Transform root, Vector3 localPosition, float size)
        {
            var go = Child("Beacon", root, localPosition, castShadows: false);
            go.transform.localScale = Vector3.one * size;
            return go.GetComponent<Renderer>();
        }

        /// <summary>A piece that turns about its own y on <paramref name="root"/> (made from <paramref name="parentName"/>).</summary>
        public GameObject Turning(string name, Transform root, string parentName, float speed)
        {
            var go = Mounted(name, root, parentName);
            go.isStatic = false;
            go.AddComponent<Spinner>().Configure(speed);
            return go;
        }

        public GameObject RadarAntenna(Transform root) => Turning("SummitRadarAntenna", root, "SummitRadar", AntennaSpeed);

        public GameObject LighthouseLens(Transform root) => Turning("LighthouseLens", root, "Lighthouse", LensSpeed);

        /// <summary>The lighthouse lamp (the beacon Landmark blinks), at its pivot on <paramref name="root"/>.</summary>
        public Renderer LighthouseLamp(Transform root) =>
            Mounted("LighthouseLamp", root, "Lighthouse", castShadows: false).GetComponent<Renderer>();
    }
}

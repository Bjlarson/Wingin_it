using UnityEngine;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// Every map's flight boundary (radius and ceiling round the map's middle) and the markers that show it: buoys on the
    /// sea, boundary posts where the edge crosses land.
    /// </summary>
    internal static class MapBoundaryBuilder
    {
        const float MarkerSpacing = 150f;

        /// <summary>
        /// The boundary under <paramref name="parent"/>, ringed with markers when <paramref name="buoys"/>. Build it after
        /// the terrain and sea: each marker stands on what's under it.
        /// </summary>
        public static FlightBoundary Build(Transform parent, float radius, float ceiling, bool buoys, MarkerModels markers)
        {
            var boundary = new GameObject("Flight Boundary").AddComponent<FlightBoundary>();
            boundary.transform.SetParent(parent, false);
            boundary.radius = radius;
            boundary.ceiling = ceiling;
            if (buoys)
                BuildMarkers(parent, boundary, markers);
            return boundary;
        }

        /// <summary>
        /// A ring of markers at the boundary radius, ~150 m apart, each on its <see cref="BoundaryBuoys.Spot"/>: the
        /// markers pack's buoy (orange float, cage tower, X topmark) on water and its boundary post (concrete footing,
        /// banded mast, the buoy's lantern and topmark) on land, or greybox cylinders. Each has a red light that blinks
        /// in unison (<see cref="BoundaryBuoys"/>). No colliders: they mark the edge, they aren't in the way.
        /// </summary>
        static void BuildMarkers(Transform parent, FlightBoundary boundary, MarkerModels markers)
        {
            var root = new GameObject("Boundary Buoys").transform;
            root.SetParent(parent, false);
            var greybox = markers == null ? new Greybox() : null;
            Renderer Dress(Transform marker, bool onLand) =>
                greybox != null ? greybox.Dress(marker, onLand) : onLand ? markers.BoundaryPost(marker) : markers.Buoy(marker);

            Physics.SyncTransforms(); // the terrain and sea just built, for the probes
            int count = Mathf.CeilToInt(2f * Mathf.PI * boundary.radius / MarkerSpacing);
            var lights = new Renderer[count];
            int posts = 0;
            Vector3 c = boundary.transform.position;
            for (int i = 0; i < count; i++)
            {
                float a = i * 360f / count;
                Vector3 spot = BoundaryBuoys.Spot(c + Quaternion.Euler(0f, a, 0f) * Vector3.forward * boundary.radius, out bool onLand);
                var marker = new GameObject(onLand ? $"Post {i:000}" : $"Buoy {i:000}").transform;
                marker.SetParent(root, false);
                marker.position = spot;
                marker.rotation = Quaternion.Euler(0f, a, 0f); // facing out, so the ring's pieces line up
                lights[i] = Dress(marker, onLand);
                if (onLand)
                    posts++;
            }

            // One of each, recorded for the arena rings and then dropped.
            var buoyModel = new GameObject("Buoy Model").transform;
            var postModel = new GameObject("Post Model").transform;
            root.gameObject.AddComponent<BoundaryBuoys>().Configure(lights, buoyModel, Dress(buoyModel, false),
                postModel, Dress(postModel, true));
            Object.DestroyImmediate(buoyModel.gameObject);
            Object.DestroyImmediate(postModel.gameObject);

            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
            Debug.Log($"[Winging It] Boundary markers: {count - posts} buoys, {posts} posts");
        }

        /// <summary>Greybox markers: an orange float or a concrete footing, a white mast and a red light 28 m up.</summary>
        class Greybox
        {
            readonly Material _float = B.Materials.Mat("Greybox_BuoyFloat", new Color(1f, 0.45f, 0.08f));
            readonly Material _mast = B.Materials.Mat("Greybox_BuoyMast", new Color(0.92f, 0.92f, 0.9f));
            readonly Material _light = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));

            public Renderer Dress(Transform marker, bool onLand)
            {
                if (onLand)
                {
                    B.Cylinder("Footing", marker, new Vector3(0f, -1f, 0f), 5f, 4f, _mast);
                    B.Cylinder("Mast", marker, new Vector3(0f, 13f, 0f), 2f, 24f, _float);
                }
                else
                {
                    B.Cylinder("Float", marker, new Vector3(0f, 3f, 0f), 7f, 8f, _float);
                    B.Cylinder("Mast", marker, new Vector3(0f, 16f, 0f), 1.4f, 20f, _mast);
                }
                return B.Sphere("Light", marker, new Vector3(0f, 28f, 0f), 5f, _light, false);
            }
        }
    }
}

using UnityEngine;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>Every map's flight boundary (radius and ceiling round the map's middle) and the buoys that mark it on the sea.</summary>
    internal static class MapBoundaryBuilder
    {
        const float BuoySpacing = 150f;

        /// <summary>The boundary under <paramref name="parent"/>, ringed with buoys when <paramref name="buoys"/>.</summary>
        public static FlightBoundary Build(Transform parent, float radius, float ceiling, bool buoys, MarkerModels markers)
        {
            var boundary = new GameObject("Flight Boundary").AddComponent<FlightBoundary>();
            boundary.transform.SetParent(parent, false);
            boundary.radius = radius;
            boundary.ceiling = ceiling;
            if (buoys)
                BuildBuoys(parent, boundary, markers);
            return boundary;
        }

        /// <summary>
        /// A ring of marker buoys on the sea at the boundary radius, ~150 m apart: the markers pack's buoy (orange float,
        /// cage tower, X topmark) or greybox cylinders, each with a red light that blinks in unison
        /// (<see cref="BoundaryBuoys"/>). No colliders: they mark the edge, they aren't in the way.
        /// </summary>
        static void BuildBuoys(Transform parent, FlightBoundary boundary, MarkerModels markers)
        {
            var root = new GameObject("Boundary Buoys").transform;
            root.SetParent(parent, false);
            Material floatMat = null, mastMat = null, lightMat = null;
            if (markers == null)
            {
                floatMat = B.Materials.Mat("Greybox_BuoyFloat", new Color(1f, 0.45f, 0.08f));
                mastMat = B.Materials.Mat("Greybox_BuoyMast", new Color(0.92f, 0.92f, 0.9f));
                lightMat = B.Materials.UnlitMat("Greybox_Beacon", new Color(1f, 0.15f, 0.1f));
            }

            int count = Mathf.CeilToInt(2f * Mathf.PI * boundary.radius / BuoySpacing);
            var lights = new Renderer[count];
            Vector3 c = boundary.transform.position;
            for (int i = 0; i < count; i++)
            {
                float a = i * 360f / count;
                var buoy = new GameObject($"Buoy {i:000}").transform;
                buoy.SetParent(root, false);
                buoy.position = new Vector3(c.x, 0f, c.z) + Quaternion.Euler(0f, a, 0f) * Vector3.forward * boundary.radius;
                if (markers != null)
                {
                    lights[i] = markers.Buoy(buoy);
                    continue;
                }
                B.Cylinder("Float", buoy, new Vector3(0f, 3f, 0f), 7f, 8f, floatMat);
                B.Cylinder("Mast", buoy, new Vector3(0f, 16f, 0f), 1.4f, 20f, mastMat);
                lights[i] = B.Sphere("Light", buoy, new Vector3(0f, 28f, 0f), 5f, lightMat, false);
            }
            root.gameObject.AddComponent<BoundaryBuoys>().Configure(lights);
            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
        }
    }
}

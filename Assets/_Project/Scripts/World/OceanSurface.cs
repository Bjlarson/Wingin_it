using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WingingIt.World
{
    /// <summary>
    /// The sea you see (living-world.md, section 2): a grid that is fine round the camera and coarse far out, kept under the
    /// camera in whole fine steps so the waves (<see cref="OceanWaves"/>, world-space in the water shader) never swim.
    /// Render only: the sea you hit is the flat, static Ocean collider beside it.
    /// </summary>
    [ExecuteAlways]
    public class OceanSurface : MonoBehaviour
    {
        [Tooltip("The grid's fine spacing (m): the surface moves in steps of this, so the near vertices stay on one world grid.")]
        [SerializeField] float step = DefaultStep;

        public const float DefaultStep = 4f;
        public const float DefaultFineHalf = 200f;
        public const float DefaultOuterHalf = 15000f;
        public const float DefaultGrowth = 1.08f;

        void OnEnable() => OceanWaves.Apply();

        void LateUpdate()
        {
            var cam = Camera.main;
            if (cam == null || !Application.isPlaying)
                return;
            transform.position = Snapped(cam.transform.position, step);
        }

        /// <summary>The surface's position under <paramref name="camera"/>: on the sea, in whole steps.</summary>
        public static Vector3 Snapped(Vector3 camera, float step) =>
            new(Mathf.Round(camera.x / step) * step, 0f, Mathf.Round(camera.z / step) * step);

        /// <summary>
        /// Grid lines along one axis from the middle: every <paramref name="step"/> out to <paramref name="fineHalf"/>,
        /// then each gap <paramref name="growth"/> times the last, out to at least <paramref name="outerHalf"/>. Symmetric.
        /// </summary>
        public static float[] AxisLines(float step, float fineHalf, float outerHalf, float growth)
        {
            var half = new List<float> { 0f };
            float x = 0f, gap = step;
            while (x < outerHalf)
            {
                if (x >= fineHalf)
                    gap *= growth;
                x += gap;
                half.Add(x);
            }
            var lines = new float[half.Count * 2 - 1];
            for (int i = 0; i < half.Count; i++)
            {
                lines[half.Count - 1 - i] = -half[i];
                lines[half.Count - 1 + i] = half[i];
            }
            return lines;
        }

        /// <summary>
        /// The surface mesh: a full grid on <see cref="AxisLines"/> both ways (no T-junctions, so no cracks), flat at y = 0
        /// with normals up. Each vertex's uv.x is the grid spacing there, so the shader drops the waves too short for it.
        /// </summary>
        public static Mesh BuildMesh(float step = DefaultStep, float fineHalf = DefaultFineHalf, float outerHalf = DefaultOuterHalf,
            float growth = DefaultGrowth)
        {
            float[] lines = AxisLines(step, fineHalf, outerHalf, growth);
            int n = lines.Length;
            var gaps = new float[n];
            for (int i = 0; i < n; i++)
                gaps[i] = Mathf.Max(i > 0 ? lines[i] - lines[i - 1] : 0f, i < n - 1 ? lines[i + 1] - lines[i] : 0f);

            var vertices = new Vector3[n * n];
            var uvs = new Vector2[n * n];
            var normals = new Vector3[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                int v = z * n + x;
                vertices[v] = new Vector3(lines[x], 0f, lines[z]);
                uvs[v] = new Vector2(Mathf.Max(gaps[x], gaps[z]), 0f);
                normals[v] = Vector3.up;
            }

            var triangles = new int[(n - 1) * (n - 1) * 6];
            int t = 0;
            for (int z = 0; z < n - 1; z++)
            for (int x = 0; x < n - 1; x++)
            {
                int a = z * n + x, b = a + 1, c = a + n, d = c + 1;
                // Alternate the diagonal in a checkerboard, so the facets don't all lean one way.
                if (((x + z) & 1) == 0)
                {
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = d;
                    triangles[t++] = a; triangles[t++] = d; triangles[t++] = b;
                }
                else
                {
                    triangles[t++] = a; triangles[t++] = c; triangles[t++] = b;
                    triangles[t++] = b; triangles[t++] = c; triangles[t++] = d;
                }
            }

            var mesh = new Mesh
            {
                name = "Ocean Surface",
                indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16,
            };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            var bounds = mesh.bounds;
            bounds.Expand(new Vector3(0f, OceanWaves.MaxHeight * 2f, 0f)); // room for the waves
            mesh.bounds = bounds;
            return mesh;
        }
    }
}

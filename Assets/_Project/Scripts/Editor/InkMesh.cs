using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace WingingIt.Editor
{
    /// <summary>
    /// Builds a mesh in the Blender kit's style (ArtSource/plane_kit.py) from Unity, for geometry that has to follow the
    /// scene's own layout (the bridge ramps): every face on one cell of the shared palette (PlanePalette.png), its tone
    /// picked from its normal, closed pieces with an inverted ink hull on a second submesh, plus a plain collider mesh.
    /// </summary>
    internal class InkMesh
    {
        /// <summary>Palette indices: the order of <c>COLORS</c> in plane_kit.py.</summary>
        public const int White = 5, Stone = 23, Concrete = 28, Slate = 29, Asphalt = 32;

        const int Cells = 16;

        readonly List<Vector3> _verts = new();
        readonly List<Vector2> _uvs = new();
        readonly List<int> _paint = new(), _ink = new();
        readonly List<Vector3> _colVerts = new();
        readonly List<int> _colTris = new();

        /// <summary>UV of a colour's tone (0 light, 1 base, 2 shadow): <c>swatch_uv</c> in plane_kit.py.</summary>
        static Vector2 Swatch(int color, int tone) =>
            new(((color % 4) * 4 + tone + 0.5f) / Cells, (Cells - 1 - color / 4 + 0.5f) / Cells);

        /// <summary>One flat face (a convex polygon, wound so its front faces <paramref name="outward"/>).</summary>
        void Face(IReadOnlyList<Vector3> poly, int color, Vector3 outward)
        {
            Vector3 n = Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]);
            bool flip = Vector3.Dot(n, outward) < 0f;
            if (flip)
                n = -n;
            float up = n.normalized.y;
            Vector2 uv = Swatch(color, up > 0.7f ? 0 : up < -0.5f ? 2 : 1);
            int b = _verts.Count;
            for (int i = 0; i < poly.Count; i++)
            {
                _verts.Add(poly[flip ? poly.Count - 1 - i : i]);
                _uvs.Add(uv);
            }
            for (int i = 1; i < poly.Count - 1; i++)
                _paint.AddRange(new[] { b, b + i, b + i + 1 });
        }

        /// <summary>A one-sided decal facing up (markings): no ink, no collider.</summary>
        public void Decal(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int color) => Face(new[] { a, b, c, d }, color, Vector3.up);

        /// <summary>
        /// Skins rings of points (same count, each a convex loop) into a solid: <paramref name="color"/>(band, side) for
        /// each quad, side k running from point k to k + 1. Caps close the ends; <paramref name="ink"/> &gt; 0 gives it an
        /// ink hull that thick, <paramref name="collide"/> puts it in the collider.
        /// </summary>
        public void Solid(IReadOnlyList<Vector3[]> rings, System.Func<int, int, int> color, int? capStart, int? capEnd,
            float ink, bool collide)
        {
            int r = rings.Count, n = rings[0].Length;
            var centres = new Vector3[r];
            for (int i = 0; i < r; i++)
            {
                foreach (var p in rings[i])
                    centres[i] += p;
                centres[i] /= n;
            }
            var normals = new Vector3[r * n];
            var tris = new List<(int a, int b, int c)>(); // shared-vertex triangles, wound outward

            void Shared(int[] idx, Vector3 outward)
            {
                Vector3 p0 = rings[idx[0] / n][idx[0] % n], p1 = rings[idx[1] / n][idx[1] % n], p2 = rings[idx[2] / n][idx[2] % n];
                Vector3 fn = Vector3.Cross(p1 - p0, p2 - p0);
                if (Vector3.Dot(fn, outward) < 0f)
                {
                    System.Array.Reverse(idx);
                    fn = -fn;
                }
                foreach (int v in idx)
                    normals[v] += fn;
                for (int i = 1; i < idx.Length - 1; i++)
                    tris.Add((idx[0], idx[i], idx[i + 1]));
            }

            for (int i = 0; i < r - 1; i++)
            for (int k = 0; k < n; k++)
            {
                int k1 = (k + 1) % n;
                var quad = new[] { rings[i][k], rings[i][k1], rings[i + 1][k1], rings[i + 1][k] };
                Vector3 outward = (quad[0] + quad[1] + quad[2] + quad[3]) * 0.25f - (centres[i] + centres[i + 1]) * 0.5f;
                Face(quad, color(i, k), outward);
                Shared(new[] { i * n + k, i * n + k1, (i + 1) * n + k1, (i + 1) * n + k }, outward);
            }
            if (capStart is int cs)
            {
                Vector3 outward = centres[0] - centres[1];
                Face(rings[0], cs, outward);
                Shared(Range(0, n), outward);
            }
            if (capEnd is int ce)
            {
                Vector3 outward = centres[r - 1] - centres[r - 2];
                Face(rings[r - 1], ce, outward);
                Shared(Range((r - 1) * n, n), outward);
            }

            if (ink > 0f)
            {
                // Ink hull: the shared vertices pushed out along their averaged normals, wound inside out.
                int b = _verts.Count;
                Vector2 uv = Swatch(White, 1); // the ink material is a flat colour
                for (int i = 0; i < r * n; i++)
                {
                    _verts.Add(rings[i / n][i % n] + normals[i].normalized * ink);
                    _uvs.Add(uv);
                }
                foreach (var (a, bb, c) in tris)
                    _ink.AddRange(new[] { b + a, b + c, b + bb });
            }
            if (collide)
            {
                int b = _colVerts.Count;
                for (int i = 0; i < r * n; i++)
                    _colVerts.Add(rings[i / n][i % n]);
                foreach (var (a, bb, c) in tris)
                    _colTris.AddRange(new[] { b + a, b + bb, b + c });
            }
        }

        /// <summary>A box from <paramref name="bottom"/> (middle of its base) up to <paramref name="top"/>.</summary>
        public void Box(Vector3 bottom, float top, Vector3 forward, float length, float width, int color, float ink, bool collide)
        {
            forward = new Vector3(forward.x, 0f, forward.z).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, forward);
            Vector3[] Ring(float y)
            {
                Vector3 c = new Vector3(bottom.x, y, bottom.z);
                Vector3 f = forward * (length * 0.5f), s = right * (width * 0.5f);
                return new[] { c + f + s, c - f + s, c - f - s, c + f - s };
            }
            Solid(new[] { Ring(bottom.y), Ring(top) }, (_, _) => color, color, color, ink, collide);
        }

        static int[] Range(int start, int count)
        {
            var a = new int[count];
            for (int i = 0; i < count; i++)
                a[i] = start + i;
            return a;
        }

        /// <summary>The painted mesh: submesh 0 on Plane_Paint, submesh 1 (the hulls) on Plane_Ink.</summary>
        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = _verts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_verts);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = 2;
            mesh.SetTriangles(_paint, 0);
            mesh.SetTriangles(_ink, 1);
            mesh.RecalculateNormals(); // every painted face has its own vertices: flat shading
            mesh.RecalculateBounds();
            return mesh;
        }

        public Mesh ToCollider(string name)
        {
            var mesh = new Mesh { name = name, indexFormat = _colVerts.Count > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16 };
            mesh.SetVertices(_colVerts);
            mesh.SetTriangles(_colTris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}

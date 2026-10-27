using UnityEditor;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;
using B = WingingIt.Editor.FlightTestSceneBuilder;

namespace WingingIt.Editor
{
    /// <summary>
    /// The sea for any map: the solid ocean box, the wave surface and the shore map baked from the ground height
    /// function over the terrain square. Sized from the map, not from a constant.
    /// </summary>
    internal static class MapOceanBuilder
    {
        const string OceanSurfacePath = MapTerrainBuilder.TerrainFolder + "/OceanSurface.asset";
        const string OceanShorePath = MapTerrainBuilder.TerrainFolder + "/OceanShore.asset";
        const string WaterMaterialPath = B.MaterialsFolder + "/Water.mat";
        const int ShoreResolution = 1024;
        /// <summary>Water this deep or deeper is the open-sea blue; shallower shades to teal.</summary>
        const float ShallowDepth = 20f;
        /// <summary>How far the shore foam band's map reaches out from the land (the shader draws the inner half of it).</summary>
        const float ShoreFoamReach = 16f;

        /// <summary>
        /// The sea. What you hit is "Ocean": a flat 30 km box at y = 0 on the Water layer, solid (flying into it is a crash)
        /// but never landable (<see cref="PlaneController.IsWater"/>). What you see is "Ocean Surface" beside it: the wave
        /// shader on a grid that follows the camera (<see cref="OceanSurface"/>), with the shallows and shore foam baked
        /// from the heightmap (<see cref="BakeShore"/>). The canal is the same sea, so it gets both for free.
        /// </summary>
        public static void Build(Transform parent, float terrainSize, System.Func<float, float, float> height)
        {
            var ocean = new GameObject("Ocean");
            ocean.layer = PlaneController.WaterLayer;
            ocean.transform.SetParent(parent, false);
            ocean.transform.localScale = new Vector3(3000f, 1f, 3000f); // 30 km: the edge is always lost in the fog
            var box = ocean.AddComponent<BoxCollider>(); // 10 m thick, top at the surface
            box.center = new Vector3(0f, -5f, 0f);
            box.size = new Vector3(10f, 10f, 10f);
            ocean.isStatic = true;

            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(OceanSurfacePath);
            if (mesh == null)
            {
                mesh = OceanSurface.BuildMesh();
                AssetDatabase.CreateAsset(mesh, OceanSurfacePath);
            }
            var surface = new GameObject("Ocean Surface", typeof(MeshFilter), typeof(MeshRenderer));
            surface.layer = PlaneController.WaterLayer;
            surface.transform.SetParent(parent, false);
            surface.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = surface.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = WaterMaterial(BakeShore(terrainSize, height), terrainSize);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            surface.AddComponent<OceanSurface>();
        }

        /// <summary>The water material (kept between builds for hand tuning), pointed at this build's shore map.</summary>
        static Material WaterMaterial(Texture2D shore, float terrainSize)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath);
            if (mat == null)
            {
                // The palette's water swatches (ArtSource/plane_kit.py: water_deep, foam).
                mat = new Material(Shader.Find("Winging It/Water"));
                mat.SetColor("_Deep", Hex("1E6E9C"));
                mat.SetColor("_Foam", Hex("F4FBFF"));
                AssetDatabase.CreateAsset(mat, WaterMaterialPath);
            }
            mat.SetTexture("_ShoreTex", shore);
            mat.SetTexture("_FoamTex", FoamTexture());
            mat.SetVector("_ShoreRect", new Vector4(-terrainSize * 0.5f, -terrainSize * 0.5f, 1f / terrainSize, 0f));
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;

        const string WaterFoamPath = B.Root + "/Art/Textures/WaterFoam.asset";

        /// <summary>
        /// The white caps' froth (the water shader reveals it where a crest is high enough, lowest values first): a
        /// tileable Worley pattern, distance to the nearest cell border, big cells plus a finer layer. So a cap starts as a
        /// web of foam lines and fills in toward the peak. Made once; delete the asset to regenerate it.
        /// </summary>
        static Texture2D FoamTexture()
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterFoamPath);
            if (tex != null)
                return tex;

            const int size = 256;
            float[] coarse = CellBorders(size, 6, 5151), fine = CellBorders(size, 15, 5252);
            var pixels = new byte[size * size];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = (byte)Mathf.RoundToInt((0.03f + 0.97f * Mathf.Clamp01(0.7f * coarse[i] + 0.3f * fine[i])) * 255f);

            tex = new Texture2D(size, size, TextureFormat.R8, true, true)
            {
                name = "Water Foam",
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Trilinear,
            };
            tex.SetPixelData(pixels, 0);
            tex.Apply(true);
            AssetDatabase.CreateAsset(tex, WaterFoamPath);
            return tex;
        }

        /// <summary>
        /// Tileable Worley noise over a <paramref name="size"/>² image with <paramref name="cells"/>² jittered points:
        /// per pixel, how far it is from the border between its two nearest points (F2 - F1), 0 on the border .. 1.
        /// </summary>
        static float[] CellBorders(int size, int cells, int seed)
        {
            var rng = new System.Random(seed);
            var points = new Vector2[cells * cells];
            for (int i = 0; i < points.Length; i++)
                points[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());

            var result = new float[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                var p = new Vector2((x + 0.5f) * cells / size, (y + 0.5f) * cells / size);
                int cx = Mathf.FloorToInt(p.x), cy = Mathf.FloorToInt(p.y);
                float f1 = float.MaxValue, f2 = float.MaxValue;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = cx + dx, ny = cy + dy;
                    var point = points[((ny % cells + cells) % cells) * cells + (nx % cells + cells) % cells];
                    float d = Vector2.Distance(p, new Vector2(nx, ny) + point);
                    if (d < f1)
                    {
                        f2 = f1;
                        f1 = d;
                    }
                    else if (d < f2)
                        f2 = d;
                }
                result[y * size + x] = Mathf.Clamp01((f2 - f1) / 0.6f);
            }
            return result;
        }

        /// <summary>
        /// The water shader's shore map over the terrain square, from the ground height function: R is how shallow the water is
        /// (0 at <see cref="ShallowDepth"/> and deeper, 1 at the waterline and on land), G the shore foam (1 on land,
        /// falling to 0 <see cref="ShoreFoamReach"/> m out). Beyond the square it's open sea (the sampler clamps).
        /// </summary>
        static Texture2D BakeShore(float terrainSize, System.Func<float, float, float> height)
        {
            const int res = ShoreResolution;
            float cell = terrainSize / res, half = terrainSize * 0.5f;
            var land = new bool[res * res];
            var shallow = new byte[res * res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float h = height(-half + (x + 0.5f) * cell, -half + (z + 0.5f) * cell);
                land[z * res + x] = h > 0f;
                shallow[z * res + x] = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f + h / ShallowDepth) * 255f);
            }

            int reach = Mathf.CeilToInt(ShoreFoamReach / cell);
            var pixels = new byte[res * res * 2];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                int i = z * res + x;
                float nearest = land[i] ? 0f : float.MaxValue;
                for (int dz = -reach; dz <= reach && nearest > 0f; dz++)
                for (int dx = -reach; dx <= reach; dx++)
                {
                    int nx = x + dx, nz = z + dz;
                    if (nx >= 0 && nz >= 0 && nx < res && nz < res && land[nz * res + nx])
                        nearest = Mathf.Min(nearest, Mathf.Sqrt(dx * dx + dz * dz) * cell);
                }
                pixels[i * 2] = shallow[i];
                pixels[i * 2 + 1] = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - nearest / ShoreFoamReach) * 255f);
            }

            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(OceanShorePath);
            if (tex == null)
            {
                tex = new Texture2D(res, res, TextureFormat.RG16, true, true) { name = "Ocean Shore" };
                AssetDatabase.CreateAsset(tex, OceanShorePath);
            }
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            tex.SetPixelData(pixels, 0);
            tex.Apply(true);
            EditorUtility.SetDirty(tex);
            return tex;
        }
    }
}

using UnityEditor;
using UnityEngine;

namespace WingingIt.Editor
{
    /// <summary>
    /// The far stand-in for each prop the terrain draws as a tree: a picture of it on one quad that turns to face the
    /// camera (Art/Shaders/TreeBillboard.shader). <see cref="Add"/> gives a tree prefab a two-level LOD group, its mesh
    /// near and the billboard once it's smaller than <see cref="BillboardHeight"/> of the screen, culled as before. The
    /// picture is the prop drawn unlit from the side in a preview scene (palette colours, ink hull included), keyed out
    /// and saved with its quad and material in Art/Billboards/&lt;Pack&gt; (made once per build, in place, so GUIDs hold).
    /// The Render Distance setting moves both switches through the LOD bias.
    /// </summary>
    internal static class TreeBillboards
    {
        const string Folder = FlightTestSceneBuilder.Root + "/Art/Billboards";
        const string ShaderName = "Winging It/Tree Billboard";
        const int PictureHeight = 256;
        static readonly Color Key = new(1f, 0f, 1f, 0f);

        /// <summary>
        /// Screen height share below which a tree turns into its billboard: a 10 m tree at ~290 m with LOD bias 1,
        /// ~580 m on the PC quality level (bias 2), well past the shadow distance.
        /// </summary>
        public const float BillboardHeight = 0.03f;

        /// <summary>
        /// Gives <paramref name="go"/> (a prop's mesh and materials at its root) a billboard child and the LOD group that
        /// swaps to it, culling it below <paramref name="cullHeight"/>.
        /// </summary>
        public static void Add(GameObject go, string pack, float cullHeight)
        {
            var renderer = go.GetComponent<MeshRenderer>();
            var mesh = go.GetComponent<MeshFilter>().sharedMesh;
            var (quad, material) = Make(pack, go.name, mesh, renderer.sharedMaterials);

            var billboard = new GameObject("Billboard");
            billboard.transform.SetParent(go.transform, false);
            billboard.AddComponent<MeshFilter>().sharedMesh = quad;
            var far = billboard.AddComponent<MeshRenderer>();
            far.sharedMaterial = material;
            far.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            far.receiveShadows = false;

            var group = go.AddComponent<LODGroup>();
            group.SetLODs(new[]
            {
                new LOD(BillboardHeight, new Renderer[] { renderer }),
                new LOD(cullHeight, new Renderer[] { far }),
            });
            group.RecalculateBounds();
        }

        /// <summary>
        /// Remakes every terrain-tree prefab (nature and map packs) and its billboard in place, without rebuilding the
        /// maps: their terrains keep pointing at the same prefabs.
        /// </summary>
        internal static void RebuildPrefabs()
        {
            var nature = NatureModels.Load();
            if (nature != null)
                foreach (var kind in MapScatter.TreeKinds)
                    nature.Prefab(kind);
            foreach (var pack in new[] { "Tropics", "Prairie", "Isles", "Alpine" })
            {
                var models = MapPackModels.Load(pack);
                if (models == null)
                    continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Prefab", new[] { $"{FlightTestSceneBuilder.Root}/Prefabs/MapPacks/{pack}" }))
                    models.TreePrefab(System.IO.Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid)));
            }
            AssetDatabase.SaveAssets();
        }

        /// <summary>The prop's billboard quad and material, made or refreshed in place.</summary>
        static (Mesh quad, Material material) Make(string pack, string name, Mesh mesh, Material[] materials)
        {
            string folder = $"{Folder}/{pack}";
            FlightTestSceneBuilder.EnsureFolder(Folder);
            FlightTestSceneBuilder.EnsureFolder(folder);

            var b = mesh.bounds;
            float radius = Mathf.Max(Mathf.Abs(b.min.x), Mathf.Abs(b.max.x), Mathf.Abs(b.min.z), Mathf.Abs(b.max.z));
            float height = b.size.y;
            int width = Mathf.Clamp(Mathf.CeilToInt(PictureHeight * 2f * radius / height / 4f) * 4, 16, 512);
            float halfWidth = height * 0.5f * width / PictureHeight; // what the picture covers, a little over the radius

            string picturePath = $"{folder}/{name}.png";
            System.IO.File.WriteAllBytes(picturePath, Capture(mesh, materials, b, halfWidth, width).EncodeToPNG());
            AssetDatabase.ImportAsset(picturePath);
            var importer = (TextureImporter)AssetImporter.GetAtPath(picturePath);
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.alphaSource = TextureImporterAlphaSource.FromInput;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true; // a cut-out keeps its fill in the small mips
            importer.alphaTestReferenceValue = 0.5f;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.maxTextureSize = 512;
            importer.SaveAndReimport();

            string quadPath = $"{folder}/{name}.asset";
            var quad = AssetDatabase.LoadAssetAtPath<Mesh>(quadPath);
            if (quad == null)
            {
                quad = new Mesh { name = name + "Billboard" };
                AssetDatabase.CreateAsset(quad, quadPath);
            }
            quad.Clear();
            quad.vertices = new[]
            {
                new Vector3(-halfWidth, b.min.y, 0f), new Vector3(halfWidth, b.min.y, 0f),
                new Vector3(halfWidth, b.max.y, 0f), new Vector3(-halfWidth, b.max.y, 0f),
            };
            quad.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
            quad.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
            quad.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // It turns to face the camera, so it can cover the whole round footprint.
            quad.bounds = new Bounds(new Vector3(0f, b.center.y, 0f), new Vector3(halfWidth * 2f, height, halfWidth * 2f));
            EditorUtility.SetDirty(quad);

            string materialPath = $"{folder}/{name}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find(ShaderName));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            material.shader = Shader.Find(ShaderName);
            material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(picturePath));
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return (quad, material);
        }

        /// <summary>
        /// The prop seen side on (from -z, or -x when it's longer along z), unlit, framed to <paramref name="halfWidth"/> either side of its pivot and its
        /// full height: opaque where it is, transparent elsewhere, the edge colours bled outward so filtering adds no fringe.
        /// </summary>
        static Texture2D Capture(Mesh mesh, Material[] materials, Bounds b, float halfWidth, int width)
        {
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var unlit = new Material[materials.Length];
            for (int i = 0; i < materials.Length; i++)
                unlit[i] = Unlit(materials[i]);
            var rt = new RenderTexture(width, PictureHeight, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var picture = new Texture2D(width, PictureHeight, TextureFormat.RGBA32, false);
            try
            {
                var prop = new GameObject("Prop");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(prop, scene);
                prop.AddComponent<MeshFilter>().sharedMesh = mesh;
                prop.AddComponent<MeshRenderer>().sharedMaterials = unlit;

                var camGo = new GameObject("Billboard Camera");
                UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
                var cam = camGo.AddComponent<Camera>();
                cam.scene = scene;
                cam.orthographic = true;
                cam.orthographicSize = b.size.y * 0.5f;
                cam.aspect = (float)width / PictureHeight;
                float depth = Mathf.Max(b.extents.x, b.extents.z) + 10f;
                // Its broad side: a cow from the flank, not the rump.
                bool fromSide = b.extents.z > b.extents.x;
                cam.transform.SetPositionAndRotation(fromSide ? new Vector3(-depth, b.center.y, 0f) : new Vector3(0f, b.center.y, -depth),
                    Quaternion.Euler(0f, fromSide ? 90f : 0f, 0f));
                cam.nearClipPlane = 0.1f;
                cam.farClipPlane = depth * 2f + 10f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Key;
                cam.targetTexture = rt;
                cam.Render();

                RenderTexture.active = rt;
                picture.ReadPixels(new Rect(0, 0, width, PictureHeight), 0, 0);
                RenderTexture.active = null;
                cam.targetTexture = null;
            }
            finally
            {
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
                Object.DestroyImmediate(rt);
                foreach (var m in unlit)
                    if (m != null && !AssetDatabase.Contains(m))
                        Object.DestroyImmediate(m);
            }

            var pixels = picture.GetPixels32();
            for (int i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                bool background = p.r > 245 && p.g < 10 && p.b > 245;
                pixels[i] = background ? new Color32(0, 0, 0, 0) : new Color32(p.r, p.g, p.b, 255);
            }
            Bleed(pixels, width, PictureHeight, 6);
            picture.SetPixels32(pixels);
            picture.Apply();
            return picture;
        }

        /// <summary>An unlit copy of a lit palette material (the ink is unlit already and is used as it is).</summary>
        static Material Unlit(Material source)
        {
            if (source == null || source.shader.name == "Universal Render Pipeline/Unlit")
                return source;
            var m = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (source.HasProperty("_BaseMap"))
                m.SetTexture("_BaseMap", source.GetTexture("_BaseMap"));
            if (source.HasProperty("_BaseColor"))
                m.SetColor("_BaseColor", source.GetColor("_BaseColor"));
            if (source.HasProperty("_Cull"))
                m.SetFloat("_Cull", source.GetFloat("_Cull"));
            return m;
        }

        /// <summary>Gives each clear pixel next to the prop its neighbour's colour, <paramref name="passes"/> pixels out.</summary>
        static void Bleed(Color32[] pixels, int width, int height, int passes)
        {
            var next = (Color32[])pixels.Clone();
            for (int pass = 0; pass < passes; pass++)
            {
                for (int y = 0; y < height; y++)
                    for (int x = 0; x < width; x++)
                    {
                        int i = y * width + x;
                        if (pixels[i].a > 0 || pixels[i].r + pixels[i].g + pixels[i].b > 0)
                            continue;
                        for (int k = 0; k < 4; k++)
                        {
                            int nx = x + (k == 0 ? 1 : k == 1 ? -1 : 0), ny = y + (k == 2 ? 1 : k == 3 ? -1 : 0);
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                                continue;
                            var n = pixels[ny * width + nx];
                            if (n.a > 0 || n.r + n.g + n.b > 0)
                            {
                                next[i] = new Color32(n.r, n.g, n.b, 0);
                                break;
                            }
                        }
                    }
                System.Array.Copy(next, pixels, pixels.Length);
            }
        }
    }
}

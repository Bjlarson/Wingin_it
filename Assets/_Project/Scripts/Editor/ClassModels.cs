using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Editor
{
    /// <summary>
    /// Finished class models dressed onto the shared plane prefab. Each model is generated in Blender
    /// (ArtSource/&lt;Class&gt;/build_*.py) as one FBX object per modular part, its origin on that part's pivot, so each
    /// object just sits at the local origin of its part, gear leg or broken edge. The placeholder boxes stay as hitboxes.
    /// </summary>
    internal static class ClassModels
    {
        internal const string ModelsFolder = FlightTestSceneBuilder.Root + "/Art/Models";
        internal const string PalettePath = FlightTestSceneBuilder.TexturesFolder + "/PlanePalette.png";

        /// <summary>The classes with a finished model: FlightStats asset name, and the model's folder and file name.</summary>
        internal static readonly (string stats, string model)[] Models =
        {
            ("Fighter", "Fighter"), ("Interceptor", "Interceptor"), ("HeavyFighter", "HeavyFighter"), ("Support", "Support"),
        };

        static string ModelPath(string model) => $"{ModelsFolder}/{model}/{model}.fbx";

        /// <summary>The materials every plane and weapon model shares (names match the Blender materials).</summary>
        internal class PlaneMaterials
        {
            public readonly Material Paint, Team, Trim, Ink, PropBlur, Glow;

            public PlaneMaterials()
            {
                var palette = PaletteTexture();
                Paint = FlightTestSceneBuilder.Materials.Mat("Plane_Paint", Color.white, palette, Vector2.one, smoothness: 0.1f);
                Team = FlightTestSceneBuilder.Materials.Mat("Plane_Team", Color.white, palette, Vector2.one, smoothness: 0.1f);
                // PlaneVisuals tints it per team at runtime; untinted (prefab view, menus) it shows team 0's blue.
                Team.SetVector("_BaseColor", PlaneVisuals.TeamTint(new Color(0.35f, 0.6f, 1f)));
                EditorUtility.SetDirty(Team);
                // The second tintable slot (the parachute's trim gores): the same ramp, white until tinted.
                Trim = FlightTestSceneBuilder.Materials.Mat("Plane_Trim", Color.white, palette, Vector2.one, smoothness: 0.1f);
                Trim.SetVector("_BaseColor", PlaneVisuals.TeamTint(Color.white));
                EditorUtility.SetDirty(Trim);
                Ink = FlightTestSceneBuilder.Materials.UnlitMat("Plane_Ink", new Color32(0x1E, 0x24, 0x33, 0xFF));
                PropBlur = PropBlurMat("Plane_PropBlur", new Color32(0x2C, 0x32, 0x42, 0x38));
                Glow = GlowMat("Weapon_Glow", palette);
            }

            public IEnumerable<Material> All => new[] { Paint, Team, Trim, Ink, PropBlur, Glow };
        }

        /// <summary>Where the model's objects mount on the prefab.</summary>
        internal struct Mounts
        {
            public Transform Body, Engine, Propeller, WingLeft, WingRight, HStab, VStab, GearLeft, GearRight, GearNose;
            /// <summary>The gun muzzles, children of the wings, in PlaneGuns' order.</summary>
            public Transform[] Muzzles;
            /// <summary>The rocket rails, children of the wings, in PlaneRockets' order.</summary>
            public Transform[] Rails;
            /// <summary>The wing hitboxes (left, right), children of the wings.</summary>
            public Transform[] WingHitboxes;
        }

        /// <summary>
        /// Builds <paramref name="modelName"/>'s model onto <paramref name="mounts"/>. Returns false (and leaves the
        /// placeholder alone) if the FBX hasn't been generated yet. Markers move things the placeholder has elsewhere,
        /// for PlaneVisuals to apply per class: Muzzle{Left,Right}{Inner,Outer} the gun muzzles, Rail{Left,Right} the
        /// rocket rails, WingHitbox{Left,Right} the wing hitboxes (a high wing). Optional pieces: a nose Propeller and its
        /// PropDisc (a jet or a twin has none), and extra objects named after the body, a wing or a tail surface (Body*,
        /// WingLeft*, WingRight*, HorizontalStabilizer*, VerticalStabilizer*, origin on their own pivot) that ride on it:
        /// a twin's WingLeftProp spins with the engine, its WingLeftPropDisc is a blur disc, a BodyRadar turns slowly,
        /// and *Aileron, *Elevator*, *Rudder are control surfaces hinged between their origin and a *Hinge marker.
        /// </summary>
        internal static bool Dress(string modelName, FlightStats stats, Mounts mounts, PlaneMaterials materials,
            out PlaneVisuals.ClassModel model)
        {
            model = default;
            var source = LoadModel(ModelPath(modelName), materials);
            if (source == null)
                return false;

            static Transform Edge(Transform part) => part.GetComponent<PlanePart>().brokenEdge.transform;
            var pieces = new List<GameObject>
            {
                Piece(source, "Body", mounts.Body),
                Piece(source, "Engine", mounts.Engine),
                Piece(source, "WingLeft", mounts.WingLeft),
                Piece(source, "WingRight", mounts.WingRight),
                Piece(source, "HorizontalStabilizer", mounts.HStab),
                Piece(source, "VerticalStabilizer", mounts.VStab),
                Piece(source, "GearMainLeft", mounts.GearLeft),
                Piece(source, "GearMainRight", mounts.GearRight),
                Piece(source, "GearNose", mounts.GearNose),
                Piece(source, "EdgeWingLeft", Edge(mounts.WingLeft)),
                Piece(source, "EdgeWingRight", Edge(mounts.WingRight)),
                Piece(source, "EdgeHorizontalStabilizer", Edge(mounts.HStab)),
                Piece(source, "EdgeVerticalStabilizer", Edge(mounts.VStab)),
            };
            if (source.transform.Find("Propeller") != null)
                pieces.Add(Piece(source, "Propeller", mounts.Propeller));
            if (source.transform.Find("PropDisc") != null)
                pieces.Add(Piece(source, "PropDisc", mounts.Engine, mounts.Propeller.localPosition, castShadows: false));

            var propellers = new List<Transform>();
            var radars = new List<Transform>();
            var surfaces = new List<PlaneVisuals.ControlSurface>();
            foreach (Transform child in source.transform)
            foreach (var mount in new[] { mounts.Body, mounts.WingLeft, mounts.WingRight, mounts.HStab, mounts.VStab })
            {
                if (child.name == mount.name || !child.name.StartsWith(mount.name) || child.GetComponent<MeshFilter>() == null)
                    continue;
                bool disc = child.name.EndsWith("PropDisc");
                var piece = Piece(source, child.name, mount, child.localPosition - mount.localPosition, castShadows: !disc);
                pieces.Add(piece);
                if (child.name.EndsWith("Prop"))
                    propellers.Add(piece.transform);
                if (child.name.EndsWith("Radar"))
                    radars.Add(piece.transform);
                if (SurfaceKind(child.name) is { } kind)
                    surfaces.Add(Surface(source, child, piece.transform, kind));
            }

            model = new PlaneVisuals.ClassModel
            {
                stats = stats, pieces = pieces.ToArray(), propellers = propellers.ToArray(), radars = radars.ToArray(),
                surfaces = surfaces.ToArray(),
                muzzlePositions = MarkerPositions(source, mounts.Muzzles, MuzzleMarker),
                railPositions = MarkerPositions(source, mounts.Rails, (_, side) => "Rail" + side),
                wingHitboxPositions = MarkerPositions(source, mounts.WingHitboxes, (_, side) => "WingHitbox" + side),
            };
            return true;
        }

        static PlaneVisuals.ControlSurfaceKind? SurfaceKind(string name) =>
            name.EndsWith("Aileron") ? name.StartsWith("WingLeft") ? PlaneVisuals.ControlSurfaceKind.AileronLeft
                : PlaneVisuals.ControlSurfaceKind.AileronRight
            : name.Contains("Elevator") ? PlaneVisuals.ControlSurfaceKind.Elevator
            : name.EndsWith("Rudder") ? PlaneVisuals.ControlSurfaceKind.Rudder
            : null;

        /// <summary>
        /// A control surface's hinge: the object's origin is one end, its <c>&lt;name&gt;Hinge</c> marker the other. The
        /// axis is turned to point +x (ailerons, elevators) or +y (rudder) so one angle convention fits both sides.
        /// </summary>
        static PlaneVisuals.ControlSurface Surface(GameObject source, Transform child, Transform pivot,
            PlaneVisuals.ControlSurfaceKind kind)
        {
            var end = source.transform.Find(child.name + "Hinge")
                ?? throw new System.ArgumentException($"{source.name} has no marker '{child.name}Hinge'");
            Vector3 axis = (end.localPosition - child.localPosition).normalized;
            Vector3 toward = kind == PlaneVisuals.ControlSurfaceKind.Rudder ? Vector3.up : Vector3.right;
            if (Vector3.Dot(axis, toward) < 0f)
                axis = -axis;
            return new PlaneVisuals.ControlSurface { pivot = pivot, kind = kind, axis = axis };
        }

        /// <summary>A gun muzzle's marker: inner/outer goes by the placeholder muzzle's span.</summary>
        static string MuzzleMarker(Transform muzzle, string side)
        {
            var wing = muzzle.parent;
            float span = Mathf.Abs(wing.localPosition.x + muzzle.localPosition.x);
            return "Muzzle" + side + (span < Mathf.Abs(wing.localPosition.x) ? "Inner" : "Outer");
        }

        /// <summary>
        /// Each point's local position (under its wing) on the model's marker for it, in model space, named by
        /// <paramref name="markerName"/>(point, "Left" or "Right"); null if the model has none of those markers, so the
        /// prefab's positions stay.
        /// </summary>
        static Vector3[] MarkerPositions(GameObject source, Transform[] points, System.Func<Transform, string, string> markerName)
        {
            if (points == null || points.Length == 0)
                return null;
            var names = new string[points.Length];
            for (int i = 0; i < points.Length; i++)
                names[i] = markerName(points[i], points[i].parent.localPosition.x + points[i].localPosition.x < 0f ? "Left" : "Right");
            if (System.Array.TrueForAll(names, n => source.transform.Find(n) == null))
                return null;
            var positions = new Vector3[points.Length];
            for (int i = 0; i < points.Length; i++)
            {
                var marker = source.transform.Find(names[i])
                    ?? throw new System.ArgumentException($"{source.name} has no marker '{names[i]}'");
                positions[i] = marker.localPosition - points[i].parent.localPosition;
            }
            return positions;
        }

        static GameObject Piece(GameObject source, string name, Transform parent, Vector3 localPos = default, bool castShadows = true)
        {
            var child = source.transform.Find(name) ?? throw new System.ArgumentException($"{source.name} has no object '{name}'");
            var go = new GameObject(source.name + " Model");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<MeshFilter>().sharedMesh = child.GetComponent<MeshFilter>().sharedMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterials = child.GetComponent<MeshRenderer>().sharedMaterials;
            r.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        /// <summary>
        /// Imports the FBX in Unity axes with its materials remapped onto the shared plane materials (and
        /// <paramref name="extra"/> ones, by name). <paramref name="readable"/> keeps the meshes on the CPU, which particle
        /// systems emitting them need.
        /// </summary>
        internal static GameObject LoadModel(string path, PlaneMaterials materials, IEnumerable<Material> extra = null, bool readable = false)
        {
            if (!System.IO.File.Exists(path))
            {
                Debug.LogWarning($"[Winging It] {path} is missing; run its ArtSource Blender script. Keeping the placeholder.");
                return null;
            }
            if (AssetImporter.GetAtPath(path) is not ModelImporter importer)
            {
                AssetDatabase.ImportAsset(path);
                importer = (ModelImporter)AssetImporter.GetAtPath(path);
            }

            importer.globalScale = 1f;
            importer.useFileScale = true;
            importer.bakeAxisConversion = true;
            importer.importAnimation = false;
            importer.animationType = ModelImporterAnimationType.None;
            importer.importCameras = false;
            importer.importLights = false;
            importer.importBlendShapes = false;
            importer.importVisibility = false;
            importer.importNormals = ModelImporterNormals.Import;
            importer.importTangents = ModelImporterTangents.None;
            importer.meshCompression = ModelImporterMeshCompression.Off;
            importer.isReadable = readable;
            importer.addCollider = false;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportStandard;
            foreach (var mat in extra != null ? System.Linq.Enumerable.Concat(materials.All, extra) : materials.All)
                importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), mat.name), mat);
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<GameObject>(path);
        }

        /// <summary>The palette atlas: flat swatches, so no filtering, mips or compression to bleed them together.</summary>
        internal static Texture2D PaletteTexture()
        {
            if (AssetImporter.GetAtPath(PalettePath) is not TextureImporter importer)
                return null;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = false;
            importer.filterMode = FilterMode.Point;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.alphaSource = TextureImporterAlphaSource.None;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(PalettePath);
        }

        /// <summary>Unlit palette, two-sided: flames, tracers and muzzle flashes (ArtSource/Weapons).</summary>
        static Material GlowMat(string name, Texture2D palette)
        {
            string path = $"{FlightTestSceneBuilder.MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", palette);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Alpha-blended, unlit and two-sided: the propeller's blur disc.</summary>
        static Material PropBlurMat(string name, Color color)
        {
            string path = $"{FlightTestSceneBuilder.MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null)
                return mat;

            mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_Blend", 0f);   // alpha
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Off);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            AssetDatabase.CreateAsset(mat, path);
            return mat;
        }
    }
}

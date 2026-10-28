using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using WingingIt.CameraSystem;
using WingingIt.Dev;
using WingingIt.Flight;
using WingingIt.Input;
using WingingIt.UI;
using WingingIt.World;

namespace WingingIt.Editor
{
    /// <summary>
    /// Generates the greybox: class stat, durability and gun assets, materials, the modular placeholder plane
    /// prefab, and the FlightTest scene. Those assets and materials are only created if missing,
    /// so tuning survives a rebuild; the prefab and scene are regenerated every time.
    /// </summary>
    public static class FlightTestSceneBuilder
    {
        internal const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/FlightTest.unity";
        internal const string PrefabPath = Root + "/Prefabs/Plane.prefab";
        internal const string AirfieldPrefabPath = Root + "/Prefabs/Airfield.prefab";
        const string StatsFolder = Root + "/ScriptableObjects/FlightStats";
        const string DurabilityFolder = Root + "/ScriptableObjects/Durability";
        internal const string WeaponsFolder =Root + "/ScriptableObjects/Weapons";
        const string AbilitiesFolder = Root + "/ScriptableObjects/Abilities";
        internal const string MaterialsFolder = Root + "/Art/Materials";
        internal const string TexturesFolder = Root + "/Art/Textures";
        internal const string InputActionsPath = Root + "/Settings/FlightControls.inputactions";

        // The home airfield sits south of the action; its air spawn is 300 m up on the approach.
        static readonly Vector3 AirfieldPosition = new Vector3(0f, 0f, -1350f);
        static readonly Vector2 RunwaySize = new Vector2(40f, 600f);
        static readonly Vector3 AirSpawnLocal = new Vector3(0f, 300f, -250f);
        const float BoundaryRadius = 2800f;
        const float Ceiling = 1500f;

        [MenuItem("Winging It/Build Flight Test Scene")]
        static void BuildFromMenu()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild Flight Test",
                    $"This regenerates {ScenePath}, {PrefabPath} and {AirfieldPrefabPath}. Flight stat assets and materials are kept.",
                    "Rebuild", "Cancel"))
                return;
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            BuildAll();
        }

        /// <summary>Batch-mode entry point: -executeMethod WingingIt.Editor.FlightTestSceneBuilder.BuildFromCommandLine</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                BuildAll();
                EditorApplication.Exit(0);
            }
            catch (System.Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void BuildAll()
        {
            EnsureFolder(StatsFolder);
            EnsureFolder(DurabilityFolder);
            EnsureFolder(WeaponsFolder);
            EnsureFolder(AbilitiesFolder);
            EnsureFolder(MaterialsFolder);
            EnsureFolder(TexturesFolder);
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");

            VfxBuilder.BuildAll(); // effects load from Resources at runtime: nothing in the scenes refers to them
            var classes = CreateClassStats();
            var mats = new Materials();
            var planePrefab = BuildPlanePrefab(classes[0], mats);
            var airfieldPrefab = BuildAirfieldPrefab(mats, WeaponAsset<AAStats>("AAGuns"));
            BuildScene(planePrefab, airfieldPrefab, classes, mats);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {ScenePath}");
        }

        // ------------------------------------------------------------------ class stats

        internal static FlightStats[] CreateClassStats()
        {
            // One shared tuning per weapon for now; each class links only the weapons it carries (see ApplyLoadout).
            var guns = WeaponAsset<GunStats>("MachineGuns");
            var rockets = WeaponAsset<RocketStats>("Rockets");
            var missiles = WeaponAsset<MissileStats>("Missiles");
            var bombs = WeaponAsset<BombStats>("Bombs");
            var classes = new[]
            {
                StatsAsset("Fighter", s =>
                {
                    s.bodyColor = new Color(0.72f, 0.80f, 0.90f);
                    s.visualScale = 1f;
                    s.minSpeed = 40f; s.maxSpeed = 110f; s.acceleration = 22f; s.deceleration = 14f;
                    s.pitchRate = 85f; s.rollRate = 190f; s.yawRate = 35f; s.bankTurnRate = 50f; s.responsiveness = 6f;
                    s.grip = 4f; s.stallSpeed = 32f;
                    s.boostMultiplier = 1.5f; s.boostAcceleration = 45f; s.boostDuration = 3f; s.boostRechargeTime = 6f;
                }, (body: 100f, wing: 45f, hStab: 30f, vStab: 30f, engine: 50f)),
                StatsAsset("Interceptor", s =>
                {
                    s.bodyColor = new Color(0.90f, 0.32f, 0.25f);
                    s.visualScale = 0.9f;
                    s.minSpeed = 50f; s.maxSpeed = 140f; s.acceleration = 30f; s.deceleration = 12f;
                    s.pitchRate = 70f; s.rollRate = 230f; s.yawRate = 30f; s.bankTurnRate = 42f; s.responsiveness = 7f;
                    s.grip = 3.2f; s.stallSpeed = 40f;
                    s.boostMultiplier = 1.6f; s.boostAcceleration = 60f; s.boostDuration = 3.5f; s.boostRechargeTime = 7f;
                }, (body: 75f, wing: 34f, hStab: 22f, vStab: 22f, engine: 38f)),
                StatsAsset("Heavy Fighter", s =>
                {
                    s.bodyColor = new Color(0.47f, 0.56f, 0.32f);
                    s.visualScale = 1.25f;
                    s.minSpeed = 35f; s.maxSpeed = 85f; s.acceleration = 14f; s.deceleration = 12f;
                    s.pitchRate = 55f; s.rollRate = 110f; s.yawRate = 25f; s.bankTurnRate = 36f; s.responsiveness = 3.5f;
                    s.autoLevelRoll = 1.8f; s.gravityInfluence = 18f; s.grip = 3f; s.stallSpeed = 28f;
                    s.boostMultiplier = 1.35f; s.boostAcceleration = 30f; s.boostDuration = 2.5f; s.boostRechargeTime = 7f;
                }, (body: 160f, wing: 70f, hStab: 45f, vStab: 45f, engine: 80f)),
                StatsAsset("Support", s =>
                {
                    s.bodyColor = new Color(0.95f, 0.80f, 0.30f);
                    s.visualScale = 1.05f;
                    s.minSpeed = 38f; s.maxSpeed = 100f; s.acceleration = 20f; s.deceleration = 14f;
                    s.pitchRate = 78f; s.rollRate = 170f; s.yawRate = 35f; s.bankTurnRate = 48f; s.responsiveness = 5.5f;
                    s.grip = 4f; s.stallSpeed = 30f;
                    s.boostMultiplier = 1.4f; s.boostAcceleration = 40f; s.boostDuration = 3f; s.boostRechargeTime = 6f;
                }, (body: 110f, wing: 45f, hStab: 30f, vStab: 30f, engine: 55f)),
            };

            // Per-class loadouts (aircraft-classes.md), enforced on every build.
            ApplyLoadout(classes[0], guns, rockets, missiles, bombs, hasRockets: false, hasMissiles: true, hasBombs: false);  // Fighter
            ApplyLoadout(classes[1], guns, rockets, missiles, bombs, hasRockets: false, hasMissiles: true, hasBombs: false);  // Interceptor
            ApplyLoadout(classes[2], guns, rockets, missiles, bombs, hasRockets: true, hasMissiles: false, hasBombs: true);   // Heavy Fighter
            ApplyLoadout(classes[3], guns, rockets, missiles, bombs, hasRockets: true, hasMissiles: false, hasBombs: false);  // Support

            // One ability per class (aircraft-classes.md).
            ApplyAbility<BarrelRollAbility>(classes[0], "BarrelRoll");
            ApplyAbility<AfterburnerAbility>(classes[1], "Afterburner");
            ApplyAbility<ArmorShieldAbility>(classes[2], "ArmorShield");
            ApplyAbility<RepairAuraAbility>(classes[3], "RepairAura");
            return classes;
        }

        /// <summary>
        /// Empties the weapon slots a class doesn't carry and fills the ones it does with the shared asset,
        /// keeping a class-specific asset if one is already linked.
        /// </summary>
        static void ApplyLoadout(FlightStats stats, GunStats guns, RocketStats rockets, MissileStats missiles, BombStats bombs,
            bool hasRockets, bool hasMissiles, bool hasBombs)
        {
            stats.guns = stats.guns != null ? stats.guns : guns; // every class
            stats.rockets = !hasRockets ? null : stats.rockets != null ? stats.rockets : rockets;
            stats.missiles = !hasMissiles ? null : stats.missiles != null ? stats.missiles : missiles;
            stats.bombs = !hasBombs ? null : stats.bombs != null ? stats.bombs : bombs;
            EditorUtility.SetDirty(stats);
        }

        /// <summary>Links the class's ability, keeping a hand-picked asset of the right kind.</summary>
        static void ApplyAbility<T>(FlightStats stats, string fileName) where T : AbilityStats
        {
            if (stats.ability is not T)
                stats.ability = TuningAsset<T>(AbilitiesFolder, fileName);
            EditorUtility.SetDirty(stats);
        }

        internal static T WeaponAsset<T>(string fileName) where T : ScriptableObject => TuningAsset<T>(WeaponsFolder, fileName);

        static T TuningAsset<T>(string folder, string fileName) where T : ScriptableObject
        {
            string path = $"{folder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<T>(path);
            if (existing != null)
                return existing; // keep hand tuning
            var asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static FlightStats StatsAsset(string displayName, System.Action<FlightStats> setup,
            (float body, float wing, float hStab, float vStab, float engine) hp)
        {
            string fileName = displayName.Replace(" ", "");
            string path = $"{StatsFolder}/{fileName}.asset";
            var stats = AssetDatabase.LoadAssetAtPath<FlightStats>(path); // keep hand tuning
            if (stats == null)
            {
                stats = ScriptableObject.CreateInstance<FlightStats>();
                stats.displayName = displayName;
                setup(stats);
                AssetDatabase.CreateAsset(stats, path);
            }

            if (stats.durability == null)
            {
                stats.durability = DurabilityAsset(fileName, hp);
                EditorUtility.SetDirty(stats);
            }
            return stats;
        }

        static PlaneDurability DurabilityAsset(string fileName,
            (float body, float wing, float hStab, float vStab, float engine) hp)
        {
            string path = $"{DurabilityFolder}/{fileName}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<PlaneDurability>(path);
            if (existing != null)
                return existing; // keep hand tuning

            var d = ScriptableObject.CreateInstance<PlaneDurability>();
            d.body = hp.body;
            d.wing = hp.wing;
            d.horizontalStabilizer = hp.hStab;
            d.verticalStabilizer = hp.vStab;
            d.engine = hp.engine;
            AssetDatabase.CreateAsset(d, path);
            return d;
        }

        // ------------------------------------------------------------------ materials

        internal class Materials
        {
            public readonly Material Ground, Runway, Marking, RepairMarking, Concrete, ConcreteDark, Mountain, Ring, Target, PlaneBody, PlaneDark, Glass, Tracer, FlakTracer, Smoke, Ember, Shield, BlobShadow;

            public Materials()
            {
                var checker = CheckerTexture();
                Ground = Mat("Greybox_Ground", new Color(0.55f, 0.72f, 0.45f), checker, new Vector2(35f, 35f));
                Runway = Mat("Greybox_Runway", new Color(0.3f, 0.3f, 0.32f));
                Marking = UnlitMat("Greybox_RunwayMarking", new Color(0.92f, 0.92f, 0.9f));
                RepairMarking = UnlitMat("Greybox_RepairMarking", new Color(1f, 0.78f, 0.1f));
                Concrete = Mat("Greybox_Concrete", new Color(0.78f, 0.78f, 0.80f), checker, new Vector2(1f, 3f));
                ConcreteDark = Mat("Greybox_ConcreteDark", new Color(0.55f, 0.57f, 0.62f), checker, new Vector2(1f, 3f));
                Mountain = Mat("Greybox_Mountain", new Color(0.62f, 0.55f, 0.45f), checker, new Vector2(6f, 6f));
                Ring = Mat("Greybox_Ring", Color.white);
                Target = Mat("Greybox_Target", new Color(0.45f, 0.5f, 0.33f));
                PlaneBody = Mat("Placeholder_PlaneBody", Color.white);
                PlaneDark = Mat("Placeholder_PlaneDark", new Color(0.2f, 0.2f, 0.22f));
                Glass = Mat("Placeholder_PlaneGlass", new Color(0.35f, 0.65f, 0.9f), smoothness: 0.9f);
                Tracer = UnlitMat("FX_Tracer", new Color(1f, 0.82f, 0.35f));
                FlakTracer = UnlitMat("FX_FlakTracer", new Color(1f, 0.32f, 0.18f));
                Smoke = UnlitMat("FX_Smoke", new Color(0.32f, 0.32f, 0.34f));
                Ember = UnlitMat("FX_Ember", new Color(1f, 0.45f, 0.1f));
                Shield = TransparentUnlitMat("FX_Shield", new Color(0.45f, 0.75f, 1f, 0.3f));
                BlobShadow = BlobShadowMat("FX_BlobShadow");
            }

            WeaponModels _weapons;

            /// <summary>The weapons pack (rocket, missile, bomb, tracers, muzzle flash), imported once on first use.</summary>
            public WeaponModels Weapons => _weapons ??= WeaponModels.Load(new ClassModels.PlaneMaterials());

            /// <summary>Alpha-blended unlit, for the shield bubble and the repair aura (tinted per use).</summary>
            static Material TransparentUnlitMat(string name, Color color)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
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
                mat.SetOverrideTag("RenderType", "Transparent");
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            /// <summary>The planes' projected altitude shadow (Winging It/Blob Shadow): a multiply, so a cool hue-shifted tint.</summary>
            static Material BlobShadowMat(string name)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    return mat;

                mat = new Material(Shader.Find("Winging It/Blob Shadow"));
                mat.SetColor("_Color", new Color(0.32f, 0.36f, 0.52f));
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            internal static Material UnlitMat(string name, Color color)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    return mat;

                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                mat.SetColor("_BaseColor", color);
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            internal static Material Mat(string name, Color color, Texture2D texture = null, Vector2? tiling = null, float smoothness = 0.15f)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    return mat;

                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                if (texture != null)
                {
                    mat.SetTexture("_BaseMap", texture);
                    mat.SetTextureScale("_BaseMap", tiling ?? Vector2.one);
                }
                mat.enableInstancing = true;
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            /// <summary>The URP terrain material (terrain layers carry the colors).</summary>
            internal static Material TerrainMat(string name)
            {
                string path = $"{MaterialsFolder}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat != null)
                    return mat;

                mat = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
                AssetDatabase.CreateAsset(mat, path);
                return mat;
            }

            internal static Texture2D CheckerTexture() => TintedChecker("Greybox_Checker", Color.white);

            /// <summary>The greybox checker (8 × 8 cells with grid lines) multiplied by <paramref name="tint"/>.</summary>
            internal static Texture2D TintedChecker(string name, Color tint)
            {
                string path = $"{TexturesFolder}/{name}.png";
                var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
                if (tex != null)
                    return tex;

                const int size = 256, cells = 8;
                var t = new Texture2D(size, size, TextureFormat.RGB24, false);
                for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    bool odd = ((x / (size / cells)) + (y / (size / cells))) % 2 == 1;
                    bool line = x % (size / cells) == 0 || y % (size / cells) == 0;
                    float v = line ? 0.7f : odd ? 0.88f : 1f;
                    t.SetPixel(x, y, new Color(v * tint.r, v * tint.g, v * tint.b));
                }
                File.WriteAllBytes(path, t.EncodeToPNG());
                Object.DestroyImmediate(t);
                AssetDatabase.ImportAsset(path);

                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.filterMode = FilterMode.Trilinear;
                importer.anisoLevel = 8;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
        }

        // ------------------------------------------------------------------ plane prefab

        static GameObject BuildPlanePrefab(FlightStats defaultClass, Materials m)
        {
            var root = new GameObject("Plane");
            var rb = root.AddComponent<Rigidbody>();
            rb.useGravity = false;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            var controller = root.AddComponent<PlaneController>();
            SetField(controller, "stats", defaultClass);

            var model = new GameObject("Model").transform;
            model.SetParent(root.transform, false);

            var tinted = new List<Renderer>();

            // Body
            var body = Part("Body", PlanePartType.Body, model, Vector3.zero);
            tinted.Add(Box("Fuselage", body, Vector3.zero, new Vector3(1.3f, 1.3f, 7f), m.PlaneBody, true));
            Box("Cockpit", body, new Vector3(0f, 0.85f, 0.6f), new Vector3(0.9f, 0.6f, 1.6f), m.Glass, false);

            // Wings
            var wingL = Part("WingLeft", PlanePartType.WingLeft, model, new Vector3(-2.65f, 0f, 0.4f));
            var wingBoxL = Box("Wing", wingL, Vector3.zero, new Vector3(4f, 0.22f, 1.9f), m.PlaneBody, true);
            tinted.Add(wingBoxL);
            var wingR = Part("WingRight", PlanePartType.WingRight, model, new Vector3(2.65f, 0f, 0.4f));
            var wingBoxR = Box("Wing", wingR, Vector3.zero, new Vector3(4f, 0.22f, 1.9f), m.PlaneBody, true);
            tinted.Add(wingBoxR);

            // Two guns per wing, so losing a wing halves the firepower.
            var muzzles = new List<Transform>();
            foreach (var wing in new[] { wingL, wingR })
            foreach (float x in new[] { -0.55f, 0.55f })
                muzzles.Add(Gun(wing, new Vector3(x, 0f, 0.95f), m));

            // One rocket rail under each wing, outboard of the guns; a lost wing loses its rockets too.
            var rails = new List<Transform>();
            rails.Add(RocketRail(wingL, new Vector3(-1.3f, -0.3f, 0.1f), m));
            rails.Add(RocketRail(wingR, new Vector3(1.3f, -0.3f, 0.1f), m));

            // Missile pylon under the fuselage: the body never detaches, so missiles survive a lost wing.
            var missilePylon = MissilePylon(body, m);

            // Bomb bay behind the missile pylon, also on the body.
            var bombBay = BombBay(body, m);

            // Tail: horizontal and vertical stabilizers break off separately
            var hStab = Part("HorizontalStabilizer", PlanePartType.HorizontalStabilizer, model, new Vector3(0f, 0.2f, -3.3f));
            tinted.Add(Box("Stabilizer", hStab, Vector3.zero, new Vector3(3.8f, 0.18f, 1.1f), m.PlaneBody, true));
            var vStab = Part("VerticalStabilizer", PlanePartType.VerticalStabilizer, model, new Vector3(0f, 1.0f, -3.35f));
            tinted.Add(Box("Fin", vStab, Vector3.zero, new Vector3(0.18f, 1.7f, 1.2f), m.PlaneBody, true));

            // Stumps left on the fuselage when a part breaks off (mirrored for the right side by x sign).
            BrokenEdge(wingL, body, m, (new Vector3(-0.85f, 0f, 0.4f), new Vector3(0.4f, 0.22f, 1.9f)));
            BrokenEdge(wingR, body, m, (new Vector3(0.85f, 0f, 0.4f), new Vector3(0.4f, 0.22f, 1.9f)));
            BrokenEdge(hStab, body, m,
                (new Vector3(-0.8f, 0.2f, -3.3f), new Vector3(0.3f, 0.18f, 1.1f)),
                (new Vector3(0.8f, 0.2f, -3.3f), new Vector3(0.3f, 0.18f, 1.1f)));
            BrokenEdge(vStab, body, m, (new Vector3(0f, 0.8f, -3.35f), new Vector3(0.18f, 0.3f, 1.2f)));

            // Engine + propeller
            var engine = Part("Engine", PlanePartType.Engine, model, new Vector3(0f, 0f, 3.75f));
            Box("Cowling", engine, Vector3.zero, new Vector3(1.15f, 1.15f, 0.6f), m.PlaneDark, true);
            var prop = new GameObject("Propeller").transform;
            prop.SetParent(engine, false);
            prop.localPosition = new Vector3(0f, 0f, 0.4f);
            Box("Blade", prop, Vector3.zero, new Vector3(3.4f, 0.28f, 0.06f), m.PlaneDark, false);
            Box("Spinner", prop, new Vector3(0f, 0f, 0.1f), new Vector3(0.4f, 0.4f, 0.35f), m.PlaneBody, false);

            // Tricycle landing gear on the body (never detaches), so a one-winged plane can still land.
            // Mains fold inward under the fuselage, the nose wheel folds back.
            var gearLegs = new[]
            {
                GearLeg("Main Gear L", body, new Vector3(-1.1f, -0.55f, 0f), 1.2f, 0.35f, new Vector3(0f, 0f, 90f), m),
                GearLeg("Main Gear R", body, new Vector3(1.1f, -0.55f, 0f), 1.2f, 0.35f, new Vector3(0f, 0f, -90f), m),
                GearLeg("Nose Gear", body, new Vector3(0f, -0.55f, 2.7f), 1.25f, 0.28f, new Vector3(90f, 0f, 0f), m),
            };

            var visuals = root.AddComponent<PlaneVisuals>();
            SetField(visuals, "plane", controller);
            SetGearLegs(visuals, gearLegs);
            SetField(visuals, "modelRoot", model);
            SetField(visuals, "propeller", prop);
            SetArray(visuals, "tintedRenderers", tinted.ToArray());
            DressClassModels(root, visuals, defaultClass, new ClassModels.Mounts
            {
                Body = body, Engine = engine, Propeller = prop, WingLeft = wingL, WingRight = wingR, HStab = hStab, VStab = vStab,
                GearLeft = gearLegs[0].pivot, GearRight = gearLegs[1].pivot, GearNose = gearLegs[2].pivot,
                Muzzles = muzzles.ToArray(), Rails = rails.ToArray(),
                WingHitboxes = new[] { wingBoxL.transform, wingBoxR.transform },
            });

            root.AddComponent<PlaneHealth>();
            root.AddComponent<PlaneRespawner>();
            var repair = root.AddComponent<PlaneRepair>();
            SetField(repair, "bubbleMaterial", m.Shield);
            var shadow = root.AddComponent<PlaneShadow>();
            SetField(shadow, "plane", controller);
            SetField(shadow, "material", m.BlobShadow);
            SetField(root.AddComponent<GroundKickup>(), "plane", controller);
            var landingGear = root.AddComponent<LandingGear>();
            // The plane sits on the lowest wheel: pivot height + leg length + wheel radius below the origin.
            SetFloat(landingGear, "wheelDrop", Mathf.Max(0.55f + 1.2f + 0.35f, 0.55f + 1.25f + 0.28f));
            var guns = root.AddComponent<PlaneGuns>();
            SetArray(guns, "muzzles", muzzles.ToArray());
            SetField(guns, "tracerMaterial", m.Tracer);
            WeaponModels.Set(guns, "tracerModel", m.Weapons.Tracer);
            WeaponModels.Set(guns, "muzzleFlashModel", m.Weapons.MuzzleFlash);
            var rocketLauncher = root.AddComponent<PlaneRockets>();
            SetArray(rocketLauncher, "rails", rails.ToArray());
            SetField(rocketLauncher, "rocketMaterial", m.PlaneDark);
            WeaponModels.Set(rocketLauncher, "rocketModel", m.Weapons.Rocket);
            SetField(rocketLauncher, "flameMaterial", m.Ember);
            SetField(rocketLauncher, "smokeMaterial", m.Smoke);
            var missileLauncher = root.AddComponent<PlaneMissiles>();
            SetField(missileLauncher, "launchPoint", missilePylon);
            SetField(missileLauncher, "missileMaterial", m.PlaneDark);
            WeaponModels.Set(missileLauncher, "missileModel", m.Weapons.Missile);
            SetField(missileLauncher, "flameMaterial", m.Ember);
            SetField(missileLauncher, "smokeMaterial", m.Smoke);
            var bombLauncher = root.AddComponent<PlaneBombs>();
            SetField(bombLauncher, "bombBay", bombBay);
            SetField(bombLauncher, "bombMaterial", m.PlaneDark);
            WeaponModels.Set(bombLauncher, "bombModel", m.Weapons.Bomb);
            SetField(bombLauncher, "smokeMaterial", m.Smoke);
            var ability = root.AddComponent<PlaneAbility>();
            SetField(ability, "shieldMaterial", m.Shield);
            SetField(ability, "flameMaterial", m.Ember);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>
        /// Adds the finished class models over the placeholder (<see cref="ClassModels.Models"/>). Every box built up to
        /// here becomes a placeholder renderer, hidden while a class with a model is flown; the prefab is saved showing
        /// <paramref name="defaultClass"/>.
        /// </summary>
        static void DressClassModels(GameObject root, PlaneVisuals visuals, FlightStats defaultClass, ClassModels.Mounts mounts)
        {
            var placeholders = root.GetComponentsInChildren<Renderer>(true);
            var materials = new ClassModels.PlaneMaterials();
            var models = new List<PlaneVisuals.ClassModel>();
            foreach (var (statsName, modelName) in ClassModels.Models)
            {
                var stats = AssetDatabase.LoadAssetAtPath<FlightStats>($"{StatsFolder}/{statsName}.asset");
                if (stats != null && ClassModels.Dress(modelName, stats, mounts, materials, out var model))
                    models.Add(model);
            }

            SetArray(visuals, "placeholderRenderers", placeholders);
            SetArray(visuals, "muzzles", mounts.Muzzles);
            SetArray(visuals, "rails", mounts.Rails);
            SetArray(visuals, "wingHitboxes", mounts.WingHitboxes);
            SetField(visuals, "teamMaterial", materials.Team);
            var so = new SerializedObject(visuals);
            var prop = so.FindProperty("classModels");
            prop.arraySize = models.Count;
            for (int i = 0; i < models.Count; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("stats").objectReferenceValue = models[i].stats;
                SetObjects(element.FindPropertyRelative("pieces"), models[i].pieces);
                SetObjects(element.FindPropertyRelative("propellers"), models[i].propellers);
                SetObjects(element.FindPropertyRelative("radars"), models[i].radars);
                SetVectors(element.FindPropertyRelative("muzzlePositions"), models[i].muzzlePositions);
                SetVectors(element.FindPropertyRelative("railPositions"), models[i].railPositions);
                SetVectors(element.FindPropertyRelative("wingHitboxPositions"), models[i].wingHitboxPositions);
            }
            so.ApplyModifiedPropertiesWithoutUndo();

            static void SetObjects(SerializedProperty array, Object[] values)
            {
                values ??= System.Array.Empty<Object>();
                array.arraySize = values.Length;
                for (int p = 0; p < values.Length; p++)
                    array.GetArrayElementAtIndex(p).objectReferenceValue = values[p];
            }

            static void SetVectors(SerializedProperty array, Vector3[] values)
            {
                values ??= System.Array.Empty<Vector3>();
                array.arraySize = values.Length;
                for (int p = 0; p < values.Length; p++)
                    array.GetArrayElementAtIndex(p).vector3Value = values[p];
            }

            bool defaultHasModel = models.Exists(x => x.stats == defaultClass);
            foreach (var x in models)
            foreach (var piece in x.pieces)
                piece.SetActive(x.stats == defaultClass);
            foreach (var r in placeholders)
                r.enabled = !defaultHasModel;
        }

        static Transform Part(string name, PlanePartType type, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<PlanePart>().partType = type;
            return go.transform;
        }

        /// <summary>
        /// Placeholder broken-edge cap: a dark, jagged stump with a glowing edge per stub, parented to the body
        /// and hidden until <paramref name="part"/> breaks off. Stub positions are in body space.
        /// </summary>
        static void BrokenEdge(Transform part, Transform body, Materials m, params (Vector3 pos, Vector3 size)[] stubs)
        {
            var edge = new GameObject(part.name + " Broken Edge").transform;
            edge.SetParent(body, false);
            foreach (var (pos, size) in stubs)
            {
                // Which way the stub points out of the fuselage: the broken face is at its outer end.
                Vector3 outward = Mathf.Abs(pos.x) > 0.01f ? new Vector3(Mathf.Sign(pos.x), 0f, 0f) : Vector3.up;
                Vector3 tip = pos + Vector3.Scale(outward, size) * 0.5f;
                Box("Stump", edge, pos, size, m.PlaneDark, false);
                var jag = Box("Jag", edge, tip - Vector3.Scale(outward, size) * 0.1f + new Vector3(0f, 0f, size.z * 0.2f),
                    Vector3.Scale(size, new Vector3(0.7f, 0.9f, 0.45f)), m.PlaneDark, false).transform;
                jag.localRotation = Quaternion.AngleAxis(25f, outward == Vector3.up ? Vector3.right : Vector3.up);
                Vector3 emberSize = outward == Vector3.up
                    ? new Vector3(size.x * 1.2f, 0.06f, size.z * 0.6f)
                    : new Vector3(0.06f, size.y * 1.2f, size.z * 0.6f);
                Box("Ember", edge, tip - new Vector3(0f, 0f, size.z * 0.15f), emberSize, m.Ember, false);
            }
            edge.gameObject.SetActive(false);
            part.GetComponent<PlanePart>().brokenEdge = edge.gameObject;
        }

        /// <summary>
        /// A landing gear leg (strut + wheel, no colliders) hanging straight down from a pivot on the body, posed
        /// extended. <paramref name="fold"/> is the local rotation that stows it.
        /// </summary>
        static PlaneVisuals.GearLeg GearLeg(string name, Transform body, Vector3 pivotPos, float length, float wheelRadius,
            Vector3 fold, Materials m)
        {
            var pivot = new GameObject(name).transform;
            pivot.SetParent(body, false);
            pivot.localPosition = pivotPos;
            Box("Strut", pivot, new Vector3(0f, -length * 0.5f, 0f), new Vector3(0.12f, length, 0.12f), m.PlaneDark, false);
            Box("Wheel", pivot, new Vector3(0f, -length, 0f), new Vector3(0.22f, wheelRadius * 2f, wheelRadius * 2f), m.PlaneDark, false);
            pivot.gameObject.SetActive(false); // starts stowed
            return new PlaneVisuals.GearLeg { pivot = pivot, foldAngles = fold };
        }

        static void SetGearLegs(PlaneVisuals visuals, PlaneVisuals.GearLeg[] legs)
        {
            var so = new SerializedObject(visuals);
            var prop = so.FindProperty("gearLegs");
            prop.arraySize = legs.Length;
            for (int i = 0; i < legs.Length; i++)
            {
                var element = prop.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("pivot").objectReferenceValue = legs[i].pivot;
                element.FindPropertyRelative("foldAngles").vector3Value = legs[i].foldAngles;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A gun barrel poking out of the wing's leading edge; returns its muzzle.</summary>
        static Transform Gun(Transform wing, Vector3 localPos, Materials m)
        {
            Box("Gun Barrel", wing, localPos, new Vector3(0.12f, 0.12f, 0.7f), m.PlaneDark, false);
            var muzzle = new GameObject("Muzzle").transform;
            muzzle.SetParent(wing, false);
            muzzle.localPosition = localPos + new Vector3(0f, 0f, 0.4f);
            return muzzle;
        }

        /// <summary>A rocket pod hanging under the wing; returns the launch point at its nose.</summary>
        static Transform RocketRail(Transform wing, Vector3 localPos, Materials m)
        {
            Box("Rocket Pod", wing, localPos, new Vector3(0.3f, 0.3f, 1.4f), m.PlaneDark, false);
            var rail = new GameObject("Rocket Rail").transform;
            rail.SetParent(wing, false);
            rail.localPosition = localPos + new Vector3(0f, 0f, 0.9f);
            return rail;
        }

        /// <summary>A missile slung under the fuselage; returns the launch point just below its nose.</summary>
        static Transform MissilePylon(Transform body, Materials m)
        {
            Box("Missile Pylon", body, new Vector3(0f, -0.75f, 0.3f), new Vector3(0.2f, 0.2f, 1.2f), m.PlaneDark, false);
            Box("Slung Missile", body, new Vector3(0f, -0.95f, 0.4f), new Vector3(0.3f, 0.3f, 2.2f), m.PlaneDark, false);
            var pylon = new GameObject("Missile Launch Point").transform;
            pylon.SetParent(body, false);
            pylon.localPosition = new Vector3(0f, -1.3f, 1.8f);
            return pylon;
        }

        /// <summary>A bomb slung under the rear fuselage; returns the release point just below it.</summary>
        static Transform BombBay(Transform body, Materials m)
        {
            Box("Bomb Rack", body, new Vector3(0f, -0.72f, -1.5f), new Vector3(0.25f, 0.15f, 1f), m.PlaneDark, false);
            Box("Slung Bomb", body, new Vector3(0f, -0.95f, -1.5f), new Vector3(0.45f, 0.45f, 1.3f), m.PlaneDark, false);
            var bay = new GameObject("Bomb Bay").transform;
            bay.SetParent(body, false);
            bay.localPosition = new Vector3(0f, -1.5f, -1.5f);
            return bay;
        }

        /// <summary>Thin parts get a thicker hitbox than their mesh so they're hittable edge-on.</summary>
        const float MinHitboxThickness = 0.6f;

        internal static Renderer Box(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (!collider)
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
            }
            else
            {
                var box = go.GetComponent<BoxCollider>();
                box.size = new Vector3(
                    Mathf.Max(1f, MinHitboxThickness / localScale.x),
                    Mathf.Max(1f, MinHitboxThickness / localScale.y),
                    Mathf.Max(1f, MinHitboxThickness / localScale.z));
            }
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        internal static Renderer Sphere(string name, Transform parent, Vector3 localPos, float diameter, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * diameter;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        /// <summary>A collider-less cylinder <paramref name="diameter"/> across and <paramref name="height"/> tall.</summary>
        internal static Renderer Cylinder(string name, Transform parent, Vector3 localPos, float diameter, float height, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = new Vector3(diameter, height * 0.5f, diameter); // the primitive is 2 units tall
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ------------------------------------------------------------------ airfield prefab

        /// <summary>
        /// An airfield: runway along +Z, a repair zone on its far half, a hangar, a control tower and a windsock off to
        /// the right, air + runway spawn points on the approach end, and four AA guns around the runway (armed only while
        /// it's a home airfield). Dressed with the airfield pack (<see cref="AirfieldModels"/>) once it's been generated,
        /// else greybox boxes.
        /// </summary>
        static GameObject BuildAirfieldPrefab(Materials m, AAStats aaStats)
        {
            var models = AirfieldModels.Load();
            var root = new GameObject("Airfield");
            float halfLength = RunwaySize.y * 0.5f, halfWidth = RunwaySize.x * 0.5f;
            const float surface = 0.1f;
            var moving = new List<Transform>(); // turret heads, the beacon, the radar dish, the sock: not static

            // Runway slab: a plain box collider (no hitbox thickening) so its top is exactly the surface.
            if (models != null)
            {
                var runway = models.Child("Runway", root.transform, Vector3.zero, castShadows: false);
                var slab = runway.AddComponent<BoxCollider>();
                slab.center = new Vector3(0f, surface * 0.5f, 0f);
                slab.size = new Vector3(RunwaySize.x, surface, RunwaySize.y);
                models.Child("Apron", root.transform, Vector3.zero, castShadows: false);
            }
            else
            {
                var runway = GameObject.CreatePrimitive(PrimitiveType.Cube);
                runway.name = "Runway";
                runway.transform.SetParent(root.transform, false);
                runway.transform.localPosition = new Vector3(0f, surface * 0.5f, 0f);
                runway.transform.localScale = new Vector3(RunwaySize.x, surface, RunwaySize.y);
                runway.GetComponent<Renderer>().sharedMaterial = m.Runway;
                GreyboxMarkings(root.transform, m, surface);
            }

            // Repair zone on the far half: land, roll out, stop inside the yellow box.
            var zoneSize = new Vector2(RunwaySize.x - 10f, 200f);
            var zone = new GameObject("Repair Zone").AddComponent<RepairZone>();
            zone.transform.SetParent(root.transform, false);
            zone.transform.localPosition = new Vector3(0f, surface, 140f);
            zone.Configure(zoneSize, 8f);
            if (models != null)
                models.Child("RepairPad", zone.transform, Vector3.zero, castShadows: false);
            else
                GreyboxZonePaint(zone.transform, zoneSize, m);

            // Buildings off the right side of the runway. Solid: flying into them is a crash.
            var buildings = new GameObject("Buildings").transform;
            buildings.SetParent(root.transform, false);
            Transform hangarSpot;
            if (models != null)
            {
                hangarSpot = BuildHangar(buildings, new Vector3(halfWidth + 55f, 0f, -40f), models);
                DressAirfield(buildings, models, halfWidth, halfLength, moving);
            }
            else
            {
                hangarSpot = BuildHangar(buildings, new Vector3(halfWidth + 55f, 0f, -40f), m);
                Box("Control Tower", buildings, new Vector3(halfWidth + 45f, 11f, 120f), new Vector3(8f, 22f, 8f), m.ConcreteDark, true);
                Box("Tower Cab", buildings, new Vector3(halfWidth + 45f, 24f, 120f), new Vector3(11f, 4f, 11f), m.Glass, true);
                Box("Windsock Pole", buildings, new Vector3(halfWidth + 15f, 5f, -halfLength + 60f), new Vector3(0.4f, 10f, 0.4f), m.PlaneDark, false);
                Box("Windsock", buildings, new Vector3(halfWidth + 15f, 9.6f, -halfLength + 62f), new Vector3(1f, 1f, 4f), m.Ember, false);
            }

            var air = new GameObject("Air Spawn").transform;
            air.SetParent(root.transform, false);
            air.localPosition = AirSpawnLocal;
            var ground = new GameObject("Runway Spawn").transform;
            ground.SetParent(root.transform, false);
            ground.localPosition = new Vector3(0f, surface + 3f, -halfLength + 30f);

            // AA guns at the corners, well clear of the runway and the buildings.
            var aa = new GameObject("AA Guns").transform;
            aa.SetParent(root.transform, false);
            int n = 1;
            foreach (float z in new[] { -halfLength + 80f, halfLength - 80f })
                foreach (float x in new[] { -halfWidth - 50f, halfWidth + 90f })
                    moving.Add(AAGunTurret(aa, $"AA Gun {n++}", new Vector3(x, 0f, z), aaStats, m, models));

            var airfield = root.AddComponent<Airfield>();
            airfield.Configure("Home Airfield", 0, true, RunwaySize, air, ground, zone);
            airfield.SetHangarSpot(hangarSpot);
            if (models != null)
                root.AddComponent<AirfieldColors>().Configure(new ClassModels.PlaneMaterials().Team);

            foreach (var t in root.GetComponentsInChildren<Transform>())
                t.gameObject.isStatic = true;
            foreach (var piece in moving) // turret heads turn, the beacon and dish spin, the sock swings
                foreach (var t in piece.GetComponentsInChildren<Transform>())
                    t.gameObject.isStatic = false;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, AirfieldPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        /// <summary>Greybox runway paint: centreline dashes, edge lines and threshold "piano keys" at both ends.</summary>
        static void GreyboxMarkings(Transform root, Materials m, float surface)
        {
            float halfLength = RunwaySize.y * 0.5f, halfWidth = RunwaySize.x * 0.5f, paint = surface + 0.01f;
            const float paintThickness = 0.02f;
            var markings = new GameObject("Markings").transform;
            markings.SetParent(root, false);
            for (float z = -halfLength + 60f; z <= halfLength - 60f; z += 30f)
                Box("Centerline", markings, new Vector3(0f, paint, z), new Vector3(0.9f, paintThickness, 15f), m.Marking, false);
            foreach (float side in new[] { -1f, 1f })
            {
                Box("Edge Line", markings, new Vector3(side * (halfWidth - 1f), paint, 0f), new Vector3(0.6f, paintThickness, RunwaySize.y - 4f), m.Marking, false);
                for (int i = 0; i < 4; i++)
                {
                    float x = side * (3.5f + i * 4f);
                    Box("Threshold", markings, new Vector3(x, paint, -halfLength + 22f), new Vector3(2f, paintThickness, 30f), m.Marking, false);
                    Box("Threshold", markings, new Vector3(x, paint, halfLength - 22f), new Vector3(2f, paintThickness, 30f), m.Marking, false);
                }
            }
        }

        /// <summary>Greybox repair zone paint: a yellow border and a big cross in the middle, readable from the air.</summary>
        static void GreyboxZonePaint(Transform zone, Vector2 zoneSize, Materials m)
        {
            const float line = 1.5f, zonePaint = 0.02f, paintThickness = 0.02f;
            foreach (float side in new[] { -0.5f, 0.5f })
            {
                Box("Zone Border", zone, new Vector3(side * zoneSize.x, zonePaint, 0f), new Vector3(line, paintThickness, zoneSize.y), m.RepairMarking, false);
                Box("Zone Border", zone, new Vector3(0f, zonePaint, side * zoneSize.y), new Vector3(zoneSize.x, paintThickness, line), m.RepairMarking, false);
            }
            Box("Zone Cross", zone, new Vector3(0f, zonePaint, 0f), new Vector3(4f, paintThickness, 24f), m.RepairMarking, false);
            Box("Zone Cross", zone, new Vector3(0f, zonePaint, 0f), new Vector3(16f, paintThickness, 4f), m.RepairMarking, false);
        }

        /// <summary>
        /// The airfield pack's buildings and props besides the hangar: the control tower (solid, with a spinning beacon),
        /// the radar (its dish turns), the windsock, a fuel tank and refueller, crates and barrels by the hangar, and a
        /// gantry crane, tool cart and mechanic beside the repair zone. Big things are solid; small props aren't, and cull
        /// when they're specks. Adds what moves to <paramref name="moving"/>.
        /// </summary>
        static void DressAirfield(Transform buildings, AirfieldModels models, float halfWidth, float halfLength, List<Transform> moving)
        {
            var tower = models.Child("ControlTower", buildings, new Vector3(halfWidth + 45f, 0f, 120f), colliderMesh: "ControlTowerCol");
            tower.name = "Control Tower"; // what the Island's field defenses look for
            var beacon = models.Mounted("ControlTowerBeacon", tower.transform, "ControlTower");
            beacon.AddComponent<Spinner>().Configure(120f);
            moving.Add(beacon.transform);

            var mast = models.Child("RadarMast", buildings, new Vector3(halfWidth + 78f, 0f, 160f));
            var mastBox = mast.AddComponent<BoxCollider>();
            mastBox.center = new Vector3(0f, 5.5f, 0f);
            mastBox.size = new Vector3(3.4f, 11f, 3f);
            var dish = models.Mounted("RadarDish", mast.transform, "RadarMast");
            dish.AddComponent<Spinner>().Configure(-45f);
            moving.Add(dish.transform);

            // The sock streams along its +Z; it swings about the way it's placed, across the approach.
            var pole = models.Child("WindsockPole", buildings, new Vector3(halfWidth + 15f, 0f, -halfLength + 60f), yaw: 35f);
            var sock = models.Mounted("Windsock", pole.transform, "WindsockPole");
            sock.AddComponent<Windsock>();
            moving.Add(sock.transform);

            models.Prop("FuelTank", buildings, new Vector3(halfWidth + 90f, 0f, -6f), 0f, solid: true);
            models.Prop("FuelTruck", buildings, new Vector3(halfWidth + 74f, 0f, 6f), 90f, solid: true);
            models.Prop("CrateStack", buildings, new Vector3(halfWidth + 40f, 0f, -76f), 15f, solid: false);
            models.Prop("Barrels", buildings, new Vector3(halfWidth + 38f, 0f, -8f), 0f, solid: false);
            models.Prop("RepairCrane", buildings, new Vector3(halfWidth + 12f, 0f, 110f), 0f, solid: false);
            models.Prop("ToolCart", buildings, new Vector3(halfWidth + 8f, 0f, 170f), -100f, solid: false);
            models.Prop("MechanicClipboard", buildings, new Vector3(halfWidth + 10f, 0f, 125f), -90f, solid: false);
        }

        /// <summary>
        /// The pack's hangar, centred on <paramref name="center"/> (on the ground), doors open toward -X (the runway): a
        /// mesh collider from its walls, roof and doors, the workshop modelled inside, warm lamps under the trusses and a
        /// mechanic by the plane. Returns the parking spot (nose to the doors), the main menu's backdrop.
        /// </summary>
        internal static Transform BuildHangar(Transform parent, Vector3 center, AirfieldModels models)
        {
            var hangar = models.Child("Hangar", parent, center, colliderMesh: "HangarCol").transform;
            models.Prop("Mechanic", hangar, new Vector3(8f, 0.1f, -11f), 0f, solid: false);
            foreach (float x in new[] { -12f, 0f, 12f })
            {
                var lamp = new GameObject("Lamp Light").AddComponent<Light>();
                lamp.transform.SetParent(hangar, false);
                lamp.transform.localPosition = new Vector3(x, 9.6f, 0f);
                lamp.type = LightType.Point;
                lamp.range = 24f;
                lamp.intensity = 6f;
                lamp.color = new Color(1f, 0.9f, 0.75f);
                lamp.shadows = LightShadows.None;
            }
            var spot = new GameObject("Hangar Spot").transform;
            spot.SetParent(hangar, false);
            spot.localPosition = new Vector3(0f, 0.1f, 0f);
            spot.localRotation = Quaternion.LookRotation(Vector3.left); // nose to the doors
            return spot;
        }

        /// <summary>
        /// The hangar, centred on <paramref name="center"/> (on the ground), 40 m deep along X with its open doors facing
        /// -X (the runway), 32 m wide, 14 m tall. Inside: a painted floor, lights, a workbench, crates, barrels and a
        /// parking T. It's the main menu's backdrop, so it has an interior. Walls and roof are solid (flying into them is
        /// a crash); the floor is paint on the airfield pad. Returns the parking spot (nose to the doors).
        /// </summary>
        internal static Transform BuildHangar(Transform parent, Vector3 center, Materials m)
        {
            const float depth = 40f, width = 32f, height = 14f, wall = 1f, doorHeight = 11f;
            var root = new GameObject("Hangar").transform;
            root.SetParent(parent, false);
            root.localPosition = center;
            float front = -depth / 2f, back = depth / 2f, side = width / 2f;

            Box("Floor", root, new Vector3(0f, 0.03f, 0f), new Vector3(depth, 0.02f, width), m.ConcreteDark, false);
            Box("Back Wall", root, new Vector3(back - wall / 2f, height / 2f, 0f), new Vector3(wall, height, width), m.Concrete, true);
            foreach (float z in new[] { -1f, 1f })
            {
                Box("Side Wall", root, new Vector3(0f, height / 2f, z * (side - wall / 2f)), new Vector3(depth, height, wall), m.Concrete, true);
                // Door pillar, and the door slid open past it along its track.
                Box("Door Pillar", root, new Vector3(front + 0.75f, doorHeight / 2f, z * (side - 1.5f)), new Vector3(1.5f, doorHeight, 3f), m.Concrete, true);
                Box("Door", root, new Vector3(front - 0.6f, doorHeight / 2f, z * (side + 3.5f)), new Vector3(0.4f, doorHeight, 8f), m.ConcreteDark, true);
                Box("Door Track", root, new Vector3(front - 0.6f, doorHeight + 0.2f, z * (side + 3f)), new Vector3(0.5f, 0.4f, 11f), m.PlaneDark, false);
            }
            Box("Roof", root, new Vector3(0f, height - 0.5f, 0f), new Vector3(depth + 1f, 1f, width + 1f), m.Concrete, true);
            Box("Door Header", root, new Vector3(front + wall / 2f, (doorHeight + height) / 2f, 0f), new Vector3(wall, height - doorHeight, width), m.Concrete, true);
            // Roof trusses, for some depth looking up.
            for (float x = front + 6f; x < back; x += 8f)
                Box("Truss", root, new Vector3(x, height - 1.6f, 0f), new Vector3(0.5f, 1.2f, width - 2f), m.PlaneDark, false);

            // Parking T (yellow), under the plane.
            Box("Parking Line", root, new Vector3(-2f, 0.05f, 0f), new Vector3(22f, 0.02f, 0.5f), m.RepairMarking, false);
            Box("Parking Bar", root, new Vector3(-12f, 0.05f, 0f), new Vector3(0.5f, 0.02f, 8f), m.RepairMarking, false);

            // Workshop along the back wall: bench, tool chest, crates, oil drums, a tyre stack.
            Box("Workbench", root, new Vector3(back - 2.2f, 0.9f, -8f), new Vector3(1.6f, 0.15f, 7f), m.Target, false);
            foreach (float z in new[] { -11f, -5f })
                Box("Bench Leg", root, new Vector3(back - 2.2f, 0.45f, z), new Vector3(1.2f, 0.9f, 0.2f), m.PlaneDark, false);
            Box("Tool Chest", root, new Vector3(back - 2f, 0.7f, -3f), new Vector3(1.2f, 1.4f, 2f), m.Ember, false);
            Box("Crate", root, new Vector3(back - 3f, 0.75f, 9f), new Vector3(1.5f, 1.5f, 1.5f), m.Target, false);
            Box("Crate", root, new Vector3(back - 3f, 2.1f, 9.3f), new Vector3(1.2f, 1.2f, 1.2f), m.Target, false);
            Box("Crate", root, new Vector3(back - 5f, 0.6f, 11f), new Vector3(1.2f, 1.2f, 1.2f), m.Target, false);
            foreach (var (x, z) in new[] { (front + 6f, side - 2.5f), (front + 7.3f, side - 2.2f), (front + 6.5f, side - 3.8f) })
                Cylinder("Oil Drum", root, new Vector3(x, 0.45f, z), new Vector3(0.6f, 0.45f, 0.6f), m.RepairMarking);
            for (int i = 0; i < 3; i++)
                Cylinder("Tyre", root, new Vector3(back - 4f, 0.2f + i * 0.4f, -side + 3f), new Vector3(1.1f, 0.2f, 1.1f), m.PlaneDark);

            // Lights: warm work lamps under the roof (the roof shades out the sun), each with a little fixture.
            foreach (float x in new[] { front + 8f, 0f, back - 8f })
            {
                Box("Lamp", root, new Vector3(x, height - 2.4f, 0f), new Vector3(1.4f, 0.25f, 1.4f), m.Marking, false);
                var lamp = new GameObject("Lamp Light").AddComponent<Light>();
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(x, height - 3f, 0f);
                lamp.type = LightType.Point;
                lamp.range = 24f;
                lamp.intensity = 6f;
                lamp.color = new Color(1f, 0.9f, 0.75f);
                lamp.shadows = LightShadows.None;
            }

            var spot = new GameObject("Hangar Spot").transform;
            spot.SetParent(root, false);
            spot.localPosition = new Vector3(0f, 0.1f, 0f);
            spot.localRotation = Quaternion.LookRotation(Vector3.left); // nose to the doors
            return spot;
        }

        static void Cylinder(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        /// <summary>
        /// A sandbagged twin-barrel AA turret. With the airfield pack its head only turns, the cradle in it tilts and the
        /// barrels recoil; the greybox head does both turns. Returns its head (the part that turns).
        /// </summary>
        static Transform AAGunTurret(Transform parent, string name, Vector3 localPos, AAStats stats, Materials m, AirfieldModels models)
        {
            var root = new GameObject(name).transform;
            root.SetParent(parent, false);
            root.localPosition = localPos;
            Transform head, muzzle;
            AAGun gun;
            if (models != null)
            {
                models.Child("AAGunBase", root, Vector3.zero);
                foreach (var (center, size) in new[] { (new Vector3(0f, 0.8f, 0f), new Vector3(8.4f, 1.6f, 8.4f)), (new Vector3(0f, 1.8f, 0f), new Vector3(1.6f, 1.6f, 1.6f)) })
                {
                    var box = root.gameObject.AddComponent<BoxCollider>();
                    box.center = center;
                    box.size = size;
                }
                head = models.Mounted("AAGunHead", root, "AAGunBase").transform;
                head.name = "Head";
                var cradle = models.Mounted("AAGunCradle", head, "AAGunHead").transform;
                var barrels = new[]
                {
                    models.Mounted("AAGunBarrelLeft", cradle, "AAGunCradle").transform,
                    models.Mounted("AAGunBarrelRight", cradle, "AAGunCradle").transform,
                };
                muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(cradle, false);
                muzzle.localPosition = models.Pivot("AAGunMuzzle") - models.Pivot("AAGunCradle");
                gun = root.gameObject.AddComponent<AAGun>();
                gun.Configure(stats, head, muzzle, m.FlakTracer);
                gun.SetMount(cradle, barrels);
            }
            else
            {
                Box("Sandbags", root, new Vector3(0f, 0.6f, 0f), new Vector3(7f, 1.2f, 7f), m.Target, true);
                Box("Base", root, new Vector3(0f, 1.8f, 0f), new Vector3(2.4f, 1.2f, 2.4f), m.PlaneDark, true);
                head = new GameObject("Head").transform;
                head.SetParent(root, false);
                head.localPosition = new Vector3(0f, 3f, 0f);
                Box("Mount", head, Vector3.zero, new Vector3(2.2f, 1.4f, 2.2f), m.PlaneDark, false);
                foreach (float side in new[] { -0.45f, 0.45f })
                    Box("Barrel", head, new Vector3(side, 0.2f, 2.4f), new Vector3(0.25f, 0.25f, 3.6f), m.PlaneDark, false);
                muzzle = new GameObject("Muzzle").transform;
                muzzle.SetParent(head, false);
                muzzle.localPosition = new Vector3(0f, 0.2f, 4.3f);
                gun = root.gameObject.AddComponent<AAGun>();
                gun.Configure(stats, head, muzzle, m.FlakTracer);
            }
            WeaponModels.Set(gun, "shellModel", m.Weapons.FlakShell);
            WeaponModels.Set(gun, "muzzleFlashModel", m.Weapons.MuzzleFlash);
            return head;
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene(GameObject planePrefab, GameObject airfieldPrefab, FlightStats[] classes, Materials m)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            SetUpAtmosphere(900f, 6000f);

            // Per-level rules. The test range air-spawns; dev key N flips it to runway takeoffs at runtime.
            new GameObject("Level Settings").AddComponent<LevelSettings>().SpawnMode = SpawnMode.Air;

            var world = new GameObject("World").transform;
            BuildGround(world, m);
            var markers = MarkerModels.Load();
            var ringPoints = BuildRings(world, m, markers);
            var targetSite = BuildGroundTargets(world, m, markers);
            BuildTowers(world, m, ringPoints, targetSite);
            BuildMountains(world, m);

            var boundary = new GameObject("Flight Boundary").AddComponent<FlightBoundary>();
            boundary.transform.SetParent(world, false);
            boundary.radius = BoundaryRadius;
            boundary.ceiling = Ceiling;

            // Home airfield for the player's team (0). The player has no spawn point of its own, so it respawns over it.
            var airfieldGo = (GameObject)PrefabUtility.InstantiatePrefab(airfieldPrefab, scene);
            airfieldGo.name = "Home Airfield";
            airfieldGo.transform.SetParent(world, false);
            airfieldGo.transform.SetPositionAndRotation(AirfieldPosition, Quaternion.identity);
            var spawn = airfieldGo.GetComponent<Airfield>().AirSpawn;

            BuildPlayer(scene, planePrefab, classes, spawn);
            BuildDummies(planePrefab, classes, scene);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath, first: false); // the game boots Island; this stays in for tests
        }

        /// <summary>The sun, soft shadows and linear distance fog (start/end in meters).</summary>
        internal static void SetUpAtmosphere(float fogStart, float fogEnd)
        {
            var sun = Object.FindAnyObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            // Toy-diorama shadows: the dim sky ambient alone leaves full-strength shadows near black on the grass.
            sun.shadowStrength = 0.65f;
            RenderSettings.sun = sun; // the Shadows setting and the planes' blob shadows find it here
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.92f);
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
        }

        /// <summary>
        /// The player plane at <paramref name="spawn"/> with its chase camera, input, dev tools (class switcher,
        /// damage tester), debug HUD and touch controls. With no spawn point of its own, it respawns over the nearest
        /// friendly airfield.
        /// </summary>
        internal static void BuildPlayer(Scene scene, GameObject planePrefab, FlightStats[] classes, Transform spawn)
        {
            // Player plane
            var planeGo = (GameObject)PrefabUtility.InstantiatePrefab(planePrefab, scene);
            planeGo.name = "Player Plane";
            planeGo.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var plane = planeGo.GetComponent<PlaneController>();
            var respawner = planeGo.GetComponent<PlaneRespawner>();

            // Camera: Cinemachine camera hard-locked to the rig
            var mainCam = Camera.main;
            mainCam.farClipPlane = 8000f;
            mainCam.nearClipPlane = 0.3f;
            mainCam.gameObject.AddComponent<CinemachineBrain>();

            var rig = new GameObject("Camera Rig").AddComponent<FlightCameraRig>();
            var cmGo = new GameObject("CM Chase Camera");
            var cm = cmGo.AddComponent<CinemachineCamera>();
            cm.Follow = rig.transform;
            cm.Lens.FieldOfView = 60f;
            cm.Lens.NearClipPlane = 0.3f;
            cm.Lens.FarClipPlane = 8000f;
            cmGo.AddComponent<CinemachineHardLockToTarget>();
            cmGo.AddComponent<CinemachineRotateWithFollowTarget>();
            SetField(rig, "target", plane);
            SetField(rig, "cinemachineCamera", cm);
            rig.transform.SetPositionAndRotation(spawn.position + new Vector3(0f, 3.5f, -16f), spawn.rotation);
            mainCam.transform.SetPositionAndRotation(rig.transform.position, rig.transform.rotation);

            // Player input + dev class switching (scene overrides on the prefab instance)
            var input = planeGo.AddComponent<PlayerFlightInput>();
            SetField(input, "actions", AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath));
            SetField(input, "cameraRig", rig);
            var switcher = planeGo.AddComponent<ClassSwitcher>();
            SetArray(switcher, "classes", classes);
            planeGo.AddComponent<DamageTester>();

            // HUD: the real one, plus the dev overlay (dev readouts, F1 help)
            var hud = new GameObject("Flight HUD").AddComponent<FlightHUD>();
            SetField(hud, "plane", plane);
            SetField(hud, "player", input);
            SetField(hud, "cameraRig", rig);
            SetField(hud, "health", planeGo.GetComponent<PlaneHealth>());
            SetField(hud, "guns", planeGo.GetComponent<PlaneGuns>());
            SetField(hud, "rockets", planeGo.GetComponent<PlaneRockets>());
            SetField(hud, "missiles", planeGo.GetComponent<PlaneMissiles>());
            SetField(hud, "bombs", planeGo.GetComponent<PlaneBombs>());
            SetField(hud, "ability", planeGo.GetComponent<PlaneAbility>());
            SetField(hud, "gear", planeGo.GetComponent<LandingGear>());
            SetField(hud, "repair", planeGo.GetComponent<PlaneRepair>());
            SetField(hud, "respawner", respawner);
            SetField(hud, "viewCamera", mainCam);
            var feedback = hud.gameObject.AddComponent<DamageFeedback>();
            SetField(feedback, "plane", plane);
            SetField(feedback, "health", planeGo.GetComponent<PlaneHealth>());
            SetField(feedback, "player", input);

            var debugHud = new GameObject("Flight Debug HUD").AddComponent<FlightDebugHUD>();
            SetField(debugHud, "plane", plane);
            SetField(debugHud, "player", input);

            BuildTouchControls(input, switcher, planeGo);
        }

        /// <summary>
        /// Target drones ahead of the spawn, above the rings and towers: one per class, each flying its own pattern.
        /// They're on the enemy team, so they fly out of the home AA's range (tow one over the field with T to see it fire).
        /// </summary>
        static void BuildDummies(GameObject planePrefab, FlightStats[] classes, Scene scene)
        {
            var group = new GameObject("Dummy Planes").transform;
            // radius = orbit radius, or half a racetrack leg. heading = racetrack leg direction (deg).
            var drones = new[]
            {
                (cls: 2, pattern: DummyPattern.Orbit, center: new Vector3(0f, 450f, 0f), radius: 300f, clockwise: true, heading: 0f),
                (cls: 3, pattern: DummyPattern.Orbit, center: new Vector3(0f, 520f, 0f), radius: 450f, clockwise: false, heading: 0f),
                (cls: 1, pattern: DummyPattern.Weave, center: new Vector3(0f, 420f, 600f), radius: 400f, clockwise: false, heading: 0f),
                (cls: 0, pattern: DummyPattern.Racetrack, center: new Vector3(0f, 620f, 200f), radius: 800f, clockwise: true, heading: 90f),
            };

            for (int i = 0; i < drones.Length; i++)
            {
                var (cls, pattern, center, radius, clockwise, heading) = drones[i];
                Vector3 pos;
                Quaternion rot;
                if (pattern == DummyPattern.Racetrack)
                {
                    // Start at one end, heading down the leg.
                    Vector3 axis = Quaternion.Euler(0f, heading, 0f) * Vector3.forward;
                    pos = center - axis * radius;
                    rot = Quaternion.LookRotation(axis);
                }
                else
                {
                    // Start on the circle, already heading along it.
                    pos = center + Vector3.right * radius;
                    rot = Quaternion.LookRotation(clockwise ? Vector3.back : Vector3.forward);
                }

                var spawn = new GameObject($"Dummy Spawn {i + 1}").transform;
                spawn.SetParent(group, false);
                spawn.SetPositionAndRotation(pos, rot);

                var go = (GameObject)PrefabUtility.InstantiatePrefab(planePrefab, scene);
                go.name = $"Dummy {i + 1} ({classes[cls].displayName}, {pattern})";
                go.transform.SetParent(group, false);
                go.transform.SetPositionAndRotation(pos, rot);
                SetField(go.GetComponent<PlaneController>(), "stats", classes[cls]);
                SetInt(go.GetComponent<PlaneController>(), "team", 1); // the player is team 0
                SetField(go.GetComponent<PlaneRespawner>(), "spawnPoint", spawn);
                go.AddComponent<DummyPilot>().Configure(pattern, center, radius, clockwise, heading, evade: false);
            }
        }

        static void BuildGround(Transform parent, Materials m)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(700f, 1f, 700f); // 7 km square
            ground.GetComponent<Renderer>().sharedMaterial = m.Ground;
            ground.isStatic = true;
        }

        static List<Vector3> BuildRings(Transform parent, Materials m, MarkerModels markers)
        {
            var points = new List<Vector3>();
            var group = new GameObject("Rings").transform;
            group.SetParent(parent, false);

            const int count = 10;
            const float rx = 1000f, rz = 700f;
            for (int i = 0; i < count; i++)
            {
                float a = i * Mathf.PI * 2f / count;
                float y = 250f + 120f * Mathf.Sin(a * 2f);
                var pos = new Vector3(Mathf.Sin(a) * rx, y, -Mathf.Cos(a) * rz);
                float an = a + 0.01f;
                var next = new Vector3(Mathf.Sin(an) * rx, 250f + 120f * Mathf.Sin(an * 2f), -Mathf.Cos(an) * rz);
                BuildRing(group, $"Ring {i + 1:00}", pos, Quaternion.LookRotation(next - pos), m, markers);
                points.Add(pos);
            }
            return points;
        }

        /// <summary>
        /// A fly-through ring: the markers pack's ring (its team-swatch tube tinted by <see cref="RingGate"/>, a solid
        /// rim from RingCol) or greybox box segments, with a trigger over the hole.
        /// </summary>
        static void BuildRing(Transform parent, string name, Vector3 position, Quaternion rotation, Materials m, MarkerModels markers)
        {
            const float radius = 22f, thickness = 2.5f;
            const int segments = 20;

            var ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.transform.SetPositionAndRotation(position, rotation);

            var trigger = ring.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(radius * 1.3f, radius * 1.3f, 6f);
            var gate = ring.AddComponent<RingGate>();
            if (markers != null)
            {
                gate.SetRenderers(new[] { markers.Ring(ring.transform) }, AssetDatabase.LoadAssetAtPath<Material>($"{MaterialsFolder}/Plane_Team.mat"));
                return;
            }

            var renderers = new List<Renderer>();
            float segLength = 2f * Mathf.PI * radius / segments * 1.12f;
            for (int i = 0; i < segments; i++)
            {
                float a = i * 360f / segments;
                float rad = a * Mathf.Deg2Rad;
                var seg = GameObject.CreatePrimitive(PrimitiveType.Cube);
                seg.name = $"Segment {i:00}";
                seg.transform.SetParent(ring.transform, false);
                seg.transform.localPosition = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
                seg.transform.localRotation = Quaternion.Euler(0f, 0f, a + 90f);
                seg.transform.localScale = new Vector3(segLength, thickness, thickness);
                var r = seg.GetComponent<Renderer>();
                r.sharedMaterial = m.Ring;
                renderers.Add(r);
            }
            gate.SetRenderers(renderers.ToArray());
        }

        /// <summary>
        /// A bombing range ahead of the spawn: a four-truck convoy on a road and a radar station (the markers pack's
        /// army trucks and radar post, or greybox boxes). Returns points the towers keep clear of.
        /// </summary>
        static List<Vector3> BuildGroundTargets(Transform parent, Materials m, MarkerModels markers)
        {
            var group = new GameObject("Ground Targets").transform;
            group.SetParent(parent, false);

            var road = GameObject.CreatePrimitive(PrimitiveType.Cube);
            road.name = "Road";
            road.transform.SetParent(group, false);
            road.transform.localPosition = new Vector3(-200f, 0.05f, -340f);
            road.transform.localScale = new Vector3(9f, 0.1f, 220f);
            road.GetComponent<Renderer>().sharedMaterial = m.Runway;
            road.isStatic = true;

            for (int i = 0; i < 4; i++)
            {
                var truck = new GameObject($"Truck {i + 1}").transform;
                truck.SetParent(group, false);
                truck.localPosition = new Vector3(-200f, 0f, -385f + i * 16f);
                var renderers = markers != null ? new[] { markers.TargetTruck(truck) } : new[]
                {
                    Box("Cab", truck, new Vector3(0f, 1.4f, 2.6f), new Vector3(2.6f, 2.4f, 2f), m.Target, true),
                    Box("Cargo", truck, new Vector3(0f, 1.8f, -1f), new Vector3(2.8f, 3.2f, 5f), m.Target, true),
                };
                truck.gameObject.AddComponent<GroundTarget>().Configure("Truck", 100f, renderers);
            }

            var radar = new GameObject("Radar Station").transform;
            radar.SetParent(group, false);
            radar.localPosition = new Vector3(-130f, 0f, -255f);
            var radarRenderers = markers != null ? markers.TargetRadar(radar) : new[]
            {
                Box("Building", radar, new Vector3(0f, 3f, 0f), new Vector3(14f, 6f, 10f), m.ConcreteDark, true),
                Box("Mast", radar, new Vector3(0f, 10f, 0f), new Vector3(1.2f, 8f, 1.2f), m.PlaneDark, true),
                Box("Dish", radar, new Vector3(0f, 15f, 0f), new Vector3(9f, 4f, 0.6f), m.Target, true),
            };
            radar.gameObject.AddComponent<GroundTarget>().Configure("Radar Station", 300f, radarRenderers);

            return new List<Vector3> { new(-200f, 0f, -380f), new(-200f, 0f, -300f), new(-130f, 0f, -255f) };
        }

        static void BuildTowers(Transform parent, Materials m, List<Vector3> avoid, List<Vector3> keepClear)
        {
            var group = new GameObject("Towers").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(1234);
            float Range(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            // A dense downtown plus scattered towers.
            var clusters = new[] { (center: new Vector2(450f, 350f), radius: 350f, count: 30), (center: new Vector2(-1500f, 900f), radius: 300f, count: 14), (center: Vector2.zero, radius: 1900f, count: 25) };
            int n = 0;
            foreach (var (center, radius, count) in clusters)
            {
                int placed = 0, attempts = 0;
                while (placed < count && attempts++ < count * 20)
                {
                    var p = center + Random2(rng) * radius;
                    if (!ClearOf(p, avoid, 90f) || !ClearOf(p, keepClear, 110f))
                        continue;
                    if (Mathf.Abs(p.x) < 120f && p.y < -900f) // spawn approach
                        continue;

                    float w = Range(18f, 45f), d = Range(18f, 45f);
                    float h = radius < 400f ? Range(80f, 260f) : Range(50f, 160f);
                    var tower = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    tower.name = $"Tower {n++:000}";
                    tower.transform.SetParent(group, false);
                    tower.transform.localPosition = new Vector3(p.x, h * 0.5f, p.y);
                    tower.transform.localRotation = Quaternion.Euler(0f, Range(0f, 90f), 0f);
                    tower.transform.localScale = new Vector3(w, h, d);
                    tower.GetComponent<Renderer>().sharedMaterial = rng.NextDouble() < 0.5 ? m.Concrete : m.ConcreteDark;
                    tower.isStatic = true;
                    placed++;
                }
            }
        }

        internal static Vector2 Random2(System.Random rng)
        {
            float a = (float)rng.NextDouble() * Mathf.PI * 2f;
            float r = Mathf.Sqrt((float)rng.NextDouble());
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
        }

        static bool ClearOf(Vector2 p, List<Vector3> points, float distance)
        {
            foreach (var q in points)
                if (Vector2.Distance(p, new Vector2(q.x, q.z)) < distance)
                    return false;
            return true;
        }

        static void BuildMountains(Transform parent, Materials m)
        {
            var group = new GameObject("Mountains").transform;
            group.SetParent(parent, false);
            var rng = new System.Random(77);
            const int count = 9;
            for (int i = 0; i < count; i++)
            {
                float a = (i + 0.5f) * Mathf.PI * 2f / count;
                if (Mathf.Abs(Mathf.DeltaAngle(a * Mathf.Rad2Deg, 180f)) < 20f)
                    continue; // keep the spawn side open
                float dist = 1900f + (float)rng.NextDouble() * 600f;
                float width = 700f + (float)rng.NextDouble() * 400f;
                float height = 450f + (float)rng.NextDouble() * 450f;

                var mountain = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mountain.name = $"Mountain {i:00}";
                mountain.transform.SetParent(group, false);
                mountain.transform.localPosition = new Vector3(Mathf.Sin(a) * dist, 0f, Mathf.Cos(a) * dist);
                mountain.transform.localScale = new Vector3(width, height * 2f, width * 0.8f);
                mountain.transform.localRotation = Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f);
                Object.DestroyImmediate(mountain.GetComponent<SphereCollider>());
                mountain.AddComponent<MeshCollider>(); // matches the squashed shape
                mountain.GetComponent<Renderer>().sharedMaterial = m.Mountain;
                mountain.isStatic = true;
            }
        }

        // ------------------------------------------------------------------ touch controls

        static void BuildTouchControls(PlayerFlightInput input, ClassSwitcher switcher, GameObject planeGo)
        {
            var eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<UnityEngine.EventSystems.EventSystem>();
            eventSystem.AddComponent<InputSystemUIInputModule>();

            var canvasGo = new GameObject("Touch Controls", typeof(RectTransform));
            var canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasGo.AddComponent<GraphicRaycaster>();

            var controls = new GameObject("Controls", typeof(RectTransform));
            controls.transform.SetParent(canvasGo.transform, false);
            Stretch((RectTransform)controls.transform);

            var knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

            // Left: virtual stick (pitch / roll)
            var stickArea = UiImage("Stick Area", controls.transform, knob, new Color(1f, 1f, 1f, 0.15f),
                new Vector2(0f, 0f), new Vector2(260f, 260f), new Vector2(300f, 300f));
            var stick = UiImage("Stick", stickArea.transform, knob, new Color(1f, 1f, 1f, 0.55f),
                new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(130f, 130f));
            var onScreenStick = stick.gameObject.AddComponent<OnScreenStick>();
            onScreenStick.controlPath = "<Gamepad>/leftStick";
            onScreenStick.movementRange = 100f;
            // The stick centers wherever the thumb lands in its area, so a press never has to find the knob and never
            // starts with a jump.
            onScreenStick.behaviour = OnScreenStick.Behaviour.ExactPositionWithDynamicOrigin;
            onScreenStick.dynamicOriginRange = 150f;

            // Right: action buttons
            var fireButton = TouchButton("Fire", controls.transform, knob, new Vector2(-230f, 230f), 190f, new Color(1f, 0.35f, 0.25f, 0.5f), "<Gamepad>/rightTrigger");
            var missileButton = TouchButton("Missile", controls.transform, knob, new Vector2(-430f, 170f), 130f, new Color(1f, 0.75f, 0.2f, 0.5f), "<Gamepad>/leftTrigger");
            TouchButton("Boost", controls.transform, knob, new Vector2(-190f, 450f), 130f, new Color(0.3f, 0.75f, 1f, 0.5f), "<Gamepad>/buttonEast");
            var rocketsButton = TouchButton("Rockets", controls.transform, knob, new Vector2(-560f, 300f), 110f, new Color(1f, 0.55f, 0.15f, 0.5f), "<Gamepad>/dpad/left");
            var bombButton = TouchButton("Bomb", controls.transform, knob, new Vector2(-610f, 130f), 110f, new Color(0.75f, 0.75f, 0.8f, 0.5f), "<Gamepad>/dpad/right");
            TouchButton("Ability", controls.transform, knob, new Vector2(-390f, 370f), 110f, new Color(0.5f, 1f, 0.5f, 0.5f), "<Gamepad>/buttonSouth");

            // Left, above the stick: throttle up / down (down also backs the plane up on the ground, to reach the repair zone)
            TouchButton("Thr +", controls.transform, knob, new Vector2(110f, 660f), 120f, new Color(1f, 1f, 1f, 0.35f), "<Gamepad>/buttonNorth", new Vector2(0f, 0f));
            TouchButton("Thr -", controls.transform, knob, new Vector2(110f, 520f), 120f, new Color(1f, 1f, 1f, 0.35f), "<Gamepad>/buttonWest", new Vector2(0f, 0f));

            // Top-right: prototype class switch
            var classBtn = UiImage("Class", controls.transform, null, new Color(0f, 0f, 0f, 0.4f),
                new Vector2(1f, 1f), new Vector2(-110f, -230f), new Vector2(180f, 70f));
            Label(classBtn.transform, "CLASS");
            var button = classBtn.gameObject.AddComponent<Button>();
            UnityEventTools.AddPersistentListener(button.onClick, switcher.CycleClass);

            var touchUi = canvasGo.AddComponent<TouchControlsUI>();
            SetField(touchUi, "controlsRoot", controls);
            SetField(touchUi, "player", input);
            // Weapon buttons only show for the weapons the current class carries (and not with weapons off).
            SetField(touchUi, "guns", planeGo.GetComponent<PlaneGuns>());
            SetField(touchUi, "fireButton", fireButton);
            SetField(touchUi, "rockets", planeGo.GetComponent<PlaneRockets>());
            SetField(touchUi, "rocketsButton", rocketsButton);
            SetField(touchUi, "missiles", planeGo.GetComponent<PlaneMissiles>());
            SetField(touchUi, "missileButton", missileButton);
            SetField(touchUi, "bombs", planeGo.GetComponent<PlaneBombs>());
            SetField(touchUi, "bombButton", bombButton);
            controls.SetActive(false);
        }

        static GameObject TouchButton(string name, Transform parent, Sprite sprite, Vector2 pos, float size, Color color, string controlPath,
            Vector2? anchor = null)
        {
            var img = UiImage(name, parent, sprite, color, anchor ?? new Vector2(1f, 0f), pos, new Vector2(size, size));
            img.gameObject.AddComponent<OnScreenButton>().controlPath = controlPath;
            Label(img.transform, name.ToUpperInvariant());
            return img.gameObject;
        }

        static Image UiImage(string name, Transform parent, Sprite sprite, Color color, Vector2 anchor, Vector2 pos, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = pos;
            rt.sizeDelta = size;
            var img = go.AddComponent<Image>();
            img.sprite = sprite;
            img.color = color;
            return img;
        }

        static void Label(Transform parent, string text)
        {
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(parent, false);
            Stretch((RectTransform)go.transform);
            var t = go.AddComponent<Text>();
            t.text = text;
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.fontSize = 26;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.MiddleCenter;
            t.color = Color.white;
            t.raycastTarget = false;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Adds <paramref name="path"/> to the build scenes: first (the scene a build opens) or last.</summary>
        internal static void AddSceneToBuildSettings(string path, bool first = true)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(first ? 0 : scenes.Count, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        /// <summary>Takes <paramref name="path"/> out of the build scenes (a test level the release leaves out).</summary>
        internal static void RemoveSceneFromBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.RemoveAll(s => s.path == path) > 0)
                EditorBuildSettings.scenes = scenes.ToArray();
        }

        internal static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        internal static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetInt(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetArray<T>(Object target, string field, T[] values) where T : Object
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

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
    /// Generates the Phase 1 greybox: class stat assets, materials, the modular placeholder plane
    /// prefab, and the FlightTest scene. Stat assets and materials are only created if missing,
    /// so tuning survives a rebuild; the prefab and scene are regenerated every time.
    /// </summary>
    public static class FlightTestSceneBuilder
    {
        const string Root = "Assets/_Project";
        const string ScenePath = Root + "/Scenes/FlightTest.unity";
        const string PrefabPath = Root + "/Prefabs/Plane.prefab";
        const string StatsFolder = Root + "/ScriptableObjects/FlightStats";
        const string MaterialsFolder = Root + "/Art/Materials";
        const string TexturesFolder = Root + "/Art/Textures";
        const string InputActionsPath = Root + "/Settings/FlightControls.inputactions";

        static readonly Vector3 SpawnPosition = new Vector3(0f, 300f, -1600f);
        const float BoundaryRadius = 2800f;
        const float Ceiling = 1500f;

        [MenuItem("Winging It/Build Flight Test Scene")]
        static void BuildFromMenu()
        {
            if (File.Exists(ScenePath) && !EditorUtility.DisplayDialog("Rebuild Flight Test",
                    $"This regenerates {ScenePath} and {PrefabPath}. Flight stat assets and materials are kept.",
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
            EnsureFolder(MaterialsFolder);
            EnsureFolder(TexturesFolder);
            EnsureFolder(Root + "/Prefabs");
            EnsureFolder(Root + "/Scenes");

            var classes = CreateClassStats();
            var mats = new Materials();
            var planePrefab = BuildPlanePrefab(classes[0], mats);
            BuildScene(planePrefab, classes, mats);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Winging It] Built {ScenePath}");
        }

        // ------------------------------------------------------------------ class stats

        static FlightStats[] CreateClassStats()
        {
            return new[]
            {
                StatsAsset("Fighter", s =>
                {
                    s.bodyColor = new Color(0.72f, 0.80f, 0.90f);
                    s.visualScale = 1f;
                    s.minSpeed = 40f; s.maxSpeed = 110f; s.acceleration = 22f; s.deceleration = 14f;
                    s.pitchRate = 85f; s.rollRate = 190f; s.yawRate = 35f; s.bankTurnRate = 50f; s.responsiveness = 6f;
                    s.grip = 4f; s.stallSpeed = 32f;
                    s.boostMultiplier = 1.5f; s.boostAcceleration = 45f; s.boostDuration = 3f; s.boostRechargeTime = 6f;
                }),
                StatsAsset("Interceptor", s =>
                {
                    s.bodyColor = new Color(0.90f, 0.32f, 0.25f);
                    s.visualScale = 0.9f;
                    s.minSpeed = 50f; s.maxSpeed = 140f; s.acceleration = 30f; s.deceleration = 12f;
                    s.pitchRate = 70f; s.rollRate = 230f; s.yawRate = 30f; s.bankTurnRate = 42f; s.responsiveness = 7f;
                    s.grip = 3.2f; s.stallSpeed = 40f;
                    s.boostMultiplier = 1.6f; s.boostAcceleration = 60f; s.boostDuration = 3.5f; s.boostRechargeTime = 7f;
                }),
                StatsAsset("Heavy Fighter", s =>
                {
                    s.bodyColor = new Color(0.47f, 0.56f, 0.32f);
                    s.visualScale = 1.25f;
                    s.minSpeed = 35f; s.maxSpeed = 85f; s.acceleration = 14f; s.deceleration = 12f;
                    s.pitchRate = 55f; s.rollRate = 110f; s.yawRate = 25f; s.bankTurnRate = 36f; s.responsiveness = 3.5f;
                    s.autoLevelRoll = 1.8f; s.gravityInfluence = 18f; s.grip = 3f; s.stallSpeed = 28f;
                    s.boostMultiplier = 1.35f; s.boostAcceleration = 30f; s.boostDuration = 2.5f; s.boostRechargeTime = 7f;
                }),
                StatsAsset("Support", s =>
                {
                    s.bodyColor = new Color(0.95f, 0.80f, 0.30f);
                    s.visualScale = 1.05f;
                    s.minSpeed = 38f; s.maxSpeed = 100f; s.acceleration = 20f; s.deceleration = 14f;
                    s.pitchRate = 78f; s.rollRate = 170f; s.yawRate = 35f; s.bankTurnRate = 48f; s.responsiveness = 5.5f;
                    s.grip = 4f; s.stallSpeed = 30f;
                    s.boostMultiplier = 1.4f; s.boostAcceleration = 40f; s.boostDuration = 3f; s.boostRechargeTime = 6f;
                }),
            };
        }

        static FlightStats StatsAsset(string displayName, System.Action<FlightStats> setup)
        {
            string path = $"{StatsFolder}/{displayName.Replace(" ", "")}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<FlightStats>(path);
            if (existing != null)
                return existing; // keep hand tuning

            var stats = ScriptableObject.CreateInstance<FlightStats>();
            stats.displayName = displayName;
            setup(stats);
            AssetDatabase.CreateAsset(stats, path);
            return stats;
        }

        // ------------------------------------------------------------------ materials

        class Materials
        {
            public readonly Material Ground, Runway, Concrete, ConcreteDark, Mountain, Ring, PlaneBody, PlaneDark, Glass;

            public Materials()
            {
                var checker = CheckerTexture();
                Ground = Mat("Greybox_Ground", new Color(0.55f, 0.72f, 0.45f), checker, new Vector2(35f, 35f));
                Runway = Mat("Greybox_Runway", new Color(0.3f, 0.3f, 0.32f));
                Concrete = Mat("Greybox_Concrete", new Color(0.78f, 0.78f, 0.80f), checker, new Vector2(1f, 3f));
                ConcreteDark = Mat("Greybox_ConcreteDark", new Color(0.55f, 0.57f, 0.62f), checker, new Vector2(1f, 3f));
                Mountain = Mat("Greybox_Mountain", new Color(0.62f, 0.55f, 0.45f), checker, new Vector2(6f, 6f));
                Ring = Mat("Greybox_Ring", Color.white);
                PlaneBody = Mat("Placeholder_PlaneBody", Color.white);
                PlaneDark = Mat("Placeholder_PlaneDark", new Color(0.2f, 0.2f, 0.22f));
                Glass = Mat("Placeholder_PlaneGlass", new Color(0.35f, 0.65f, 0.9f), smoothness: 0.9f);
            }

            static Material Mat(string name, Color color, Texture2D texture = null, Vector2? tiling = null, float smoothness = 0.15f)
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

            static Texture2D CheckerTexture()
            {
                string path = $"{TexturesFolder}/Greybox_Checker.png";
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
                    t.SetPixel(x, y, new Color(v, v, v));
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
            tinted.Add(Box("Wing", wingL, Vector3.zero, new Vector3(4f, 0.22f, 1.9f), m.PlaneBody, true));
            var wingR = Part("WingRight", PlanePartType.WingRight, model, new Vector3(2.65f, 0f, 0.4f));
            tinted.Add(Box("Wing", wingR, Vector3.zero, new Vector3(4f, 0.22f, 1.9f), m.PlaneBody, true));

            // Tail
            var tail = Part("Tail", PlanePartType.Tail, model, new Vector3(0f, 0f, -3.3f));
            tinted.Add(Box("HorizontalStabilizer", tail, new Vector3(0f, 0.2f, 0f), new Vector3(3.8f, 0.18f, 1.1f), m.PlaneBody, true));
            tinted.Add(Box("VerticalFin", tail, new Vector3(0f, 1.0f, -0.05f), new Vector3(0.18f, 1.7f, 1.2f), m.PlaneBody, true));

            // Engine + propeller
            var engine = Part("Engine", PlanePartType.Engine, model, new Vector3(0f, 0f, 3.75f));
            Box("Cowling", engine, Vector3.zero, new Vector3(1.15f, 1.15f, 0.6f), m.PlaneDark, true);
            var prop = new GameObject("Propeller").transform;
            prop.SetParent(engine, false);
            prop.localPosition = new Vector3(0f, 0f, 0.4f);
            Box("Blade", prop, Vector3.zero, new Vector3(3.4f, 0.28f, 0.06f), m.PlaneDark, false);
            Box("Spinner", prop, new Vector3(0f, 0f, 0.1f), new Vector3(0.4f, 0.4f, 0.35f), m.PlaneBody, false);

            var visuals = root.AddComponent<PlaneVisuals>();
            SetField(visuals, "plane", controller);
            SetField(visuals, "modelRoot", model);
            SetField(visuals, "propeller", prop);
            SetArray(visuals, "tintedRenderers", tinted.ToArray());

            root.AddComponent<PlaneRespawner>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        static Transform Part(string name, PlanePartType type, Transform parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.AddComponent<PlanePart>().partType = type;
            return go.transform;
        }

        static Renderer Box(string name, Transform parent, Vector3 localPos, Vector3 localScale, Material mat, bool collider)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = localScale;
            if (!collider)
                Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>();
            r.sharedMaterial = mat;
            return r;
        }

        // ------------------------------------------------------------------ scene

        static void BuildScene(GameObject planePrefab, FlightStats[] classes, Materials m)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);

            // Lighting & atmosphere
            var sun = Object.FindAnyObjectByType<Light>();
            sun.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            sun.intensity = 1.4f;
            sun.shadows = LightShadows.Soft;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.72f, 0.82f, 0.92f);
            RenderSettings.fogStartDistance = 900f;
            RenderSettings.fogEndDistance = 6000f;

            var world = new GameObject("World").transform;
            BuildGround(world, m);
            var ringPoints = BuildRings(world, m);
            BuildTowers(world, m, ringPoints);
            BuildMountains(world, m);

            var boundary = new GameObject("Flight Boundary").AddComponent<FlightBoundary>();
            boundary.transform.SetParent(world, false);
            boundary.radius = BoundaryRadius;
            boundary.ceiling = Ceiling;

            var spawn = new GameObject("Spawn Point").transform;
            spawn.SetPositionAndRotation(SpawnPosition, Quaternion.identity);

            // Player plane
            var planeGo = (GameObject)PrefabUtility.InstantiatePrefab(planePrefab, scene);
            planeGo.name = "Player Plane";
            planeGo.transform.SetPositionAndRotation(spawn.position, spawn.rotation);
            var plane = planeGo.GetComponent<PlaneController>();
            var respawner = planeGo.GetComponent<PlaneRespawner>();
            SetField(respawner, "spawnPoint", spawn);

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

            // HUD
            var hud = new GameObject("Flight Debug HUD").AddComponent<FlightDebugHUD>();
            SetField(hud, "plane", plane);
            SetField(hud, "player", input);
            SetField(hud, "cameraRig", rig);
            SetField(hud, "respawner", respawner);
            SetField(hud, "viewCamera", mainCam);

            BuildTouchControls(input, switcher);

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddSceneToBuildSettings(ScenePath);
        }

        static void BuildGround(Transform parent, Materials m)
        {
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(parent, false);
            ground.transform.localScale = new Vector3(700f, 1f, 700f); // 7 km square
            ground.GetComponent<Renderer>().sharedMaterial = m.Ground;
            ground.isStatic = true;

            // A runway-shaped strip under the spawn, for orientation (real airfields come in Phase 3).
            var runway = GameObject.CreatePrimitive(PrimitiveType.Cube);
            runway.name = "Runway (reference)";
            runway.transform.SetParent(parent, false);
            runway.transform.localPosition = new Vector3(0f, 0.05f, -1350f);
            runway.transform.localScale = new Vector3(40f, 0.1f, 600f);
            runway.GetComponent<Renderer>().sharedMaterial = m.Runway;
            runway.isStatic = true;
        }

        static List<Vector3> BuildRings(Transform parent, Materials m)
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
                BuildRing(group, $"Ring {i + 1:00}", pos, Quaternion.LookRotation(next - pos), m);
                points.Add(pos);
            }
            return points;
        }

        static void BuildRing(Transform parent, string name, Vector3 position, Quaternion rotation, Materials m)
        {
            const float radius = 22f, thickness = 2.5f;
            const int segments = 20;

            var ring = new GameObject(name);
            ring.transform.SetParent(parent, false);
            ring.transform.SetPositionAndRotation(position, rotation);

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

            var trigger = ring.AddComponent<BoxCollider>();
            trigger.isTrigger = true;
            trigger.size = new Vector3(radius * 1.3f, radius * 1.3f, 6f);
            var gate = ring.AddComponent<RingGate>();
            gate.SetRenderers(renderers.ToArray());
        }

        static void BuildTowers(Transform parent, Materials m, List<Vector3> avoid)
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
                    if (!ClearOf(p, avoid, 90f))
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

        static Vector2 Random2(System.Random rng)
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

        static void BuildTouchControls(PlayerFlightInput input, ClassSwitcher switcher)
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

            // Right: action buttons
            TouchButton("Fire", controls.transform, knob, new Vector2(-230f, 230f), 190f, new Color(1f, 0.35f, 0.25f, 0.5f), "<Gamepad>/rightTrigger");
            TouchButton("Missile", controls.transform, knob, new Vector2(-430f, 170f), 130f, new Color(1f, 0.75f, 0.2f, 0.5f), "<Gamepad>/leftTrigger");
            TouchButton("Boost", controls.transform, knob, new Vector2(-190f, 450f), 130f, new Color(0.3f, 0.75f, 1f, 0.5f), "<Gamepad>/rightShoulder");
            TouchButton("Ability", controls.transform, knob, new Vector2(-390f, 370f), 110f, new Color(0.5f, 1f, 0.5f, 0.5f), "<Gamepad>/buttonSouth");

            // Top-right: prototype class switch
            var classBtn = UiImage("Class", controls.transform, null, new Color(0f, 0f, 0f, 0.4f),
                new Vector2(1f, 1f), new Vector2(-110f, -230f), new Vector2(180f, 70f));
            Label(classBtn.transform, "CLASS");
            var button = classBtn.gameObject.AddComponent<Button>();
            UnityEventTools.AddPersistentListener(button.onClick, switcher.CycleClass);

            var touchUi = canvasGo.AddComponent<TouchControlsUI>();
            SetField(touchUi, "controlsRoot", controls);
            SetField(touchUi, "player", input);
            controls.SetActive(false);
        }

        static void TouchButton(string name, Transform parent, Sprite sprite, Vector2 pos, float size, Color color, string controlPath)
        {
            var img = UiImage(name, parent, sprite, color, new Vector2(1f, 0f), pos, new Vector2(size, size));
            img.gameObject.AddComponent<OnScreenButton>().controlPath = controlPath;
            Label(img.transform, name.ToUpperInvariant());
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

        static void AddSceneToBuildSettings(string path)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.RemoveAll(s => s.path == path);
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;
            string parent = Path.GetDirectoryName(path)!.Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static void SetField(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field) ?? throw new System.ArgumentException($"{target.GetType().Name} has no field '{field}'");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArray<T>(Object target, string field, T[] values) where T : Object
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

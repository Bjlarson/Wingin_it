using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using WingingIt.Effects;
using WingingIt.World;

namespace WingingIt.Editor
{
    /// <summary>
    /// Builds the VFX pack (Design/03-Art/final-asset-list.md, Pack 8): a particle prefab per <see cref="VfxKind"/> under
    /// Prefabs/VFX, emitting the chunky meshes from ArtSource/VFX/build_vfx.py (Art/Models/VFX/VFX.fbx), and the
    /// <see cref="VfxLibrary"/> that <see cref="Vfx"/> loads from Resources. Without the FBX it builds nothing, and the
    /// game simply plays no effects.
    /// </summary>
    internal static class VfxBuilder
    {
        const string ModelPath = ClassModels.ModelsFolder + "/VFX/VFX.fbx";
        const string PrefabFolder = FlightTestSceneBuilder.Root + "/Prefabs/VFX";
        const string LibraryPath = FlightTestSceneBuilder.Root + "/Resources/" + VfxLibrary.ResourcePath + ".asset";

        // Tints for the neutral-ramp meshes (Unity's particle colour multiplies the palette).
        static readonly Color Flash = new(1f, 0.97f, 0.82f);
        static readonly (Color, Color) SmokeDark = (new Color(0.14f, 0.14f, 0.16f), new Color(0.24f, 0.24f, 0.27f));
        static readonly (Color, Color) SmokeMid = (new Color(0.32f, 0.32f, 0.35f), new Color(0.42f, 0.42f, 0.45f));
        static readonly (Color, Color) SmokeLight = (new Color(0.7f, 0.71f, 0.75f), new Color(0.86f, 0.86f, 0.89f));
        static readonly (Color, Color) Dust = (new Color(0.8f, 0.7f, 0.52f), new Color(0.64f, 0.56f, 0.44f));
        static readonly (Color, Color) Spray = (new Color(0.95f, 0.98f, 1f), new Color(0.78f, 0.9f, 0.98f));
        static readonly (Color, Color) Metal = (new Color(0.42f, 0.45f, 0.52f), new Color(0.94f, 0.9f, 0.82f));
        static readonly (Color, Color) Dirt = (new Color(0.46f, 0.39f, 0.3f), new Color(0.32f, 0.32f, 0.34f));
        static readonly (Color, Color) White = (Color.white, Color.white);

        /// <summary>A fireball's life on the white puff: white-hot, orange, red, then a charred grey as it shrinks away.</summary>
        static readonly Gradient Fire = Grad((0f, new Color(1f, 0.96f, 0.72f)), (0.22f, new Color(1f, 0.66f, 0.16f)),
            (0.55f, new Color(0.9f, 0.3f, 0.08f)), (1f, new Color(0.26f, 0.22f, 0.22f)));

        // Size over lifetime.
        static readonly AnimationCurve Pop = Curve((0f, 0f), (0.12f, 1f), (0.75f, 0.85f), (1f, 0f));
        static readonly AnimationCurve Swell = Curve((0f, 0.35f), (0.4f, 1f), (1f, 0f));
        static readonly AnimationCurve Flare = Curve((0f, 0.6f), (0.15f, 1.1f), (1f, 0f));
        static readonly AnimationCurve Burst = Curve((0f, 0.5f), (1f, 1f));
        static readonly AnimationCurve Expand = Curve((0f, 0.12f), (1f, 1f));
        static readonly AnimationCurve BoomPop = Curve((0f, 0f), (0.1f, 1.3f), (0.22f, 1f), (0.8f, 1f), (1f, 0f));
        static readonly AnimationCurve Crown = Curve((0f, 0.3f), (0.35f, 1f), (0.8f, 0.9f), (1f, 0f));

        enum Shape { Point, Sphere, Up, Ring }

        /// <summary>One particle system of an effect.</summary>
        class Spec
        {
            public string name;
            public Mesh[] meshes;
            public int burst;
            public float delay, rate, ratePerMetre;
            public Vector2 life = new(1f, 1f), size = new(1f, 1f), speed;
            public (Color, Color) color = White;
            public Gradient overLife;
            public AnimationCurve sizeCurve = Pop;
            public Shape shape = Shape.Sphere;
            public float radius, coneAngle = 25f;
            public float gravity, drag;
            public ParticleSystemRenderSpace align = ParticleSystemRenderSpace.World;
            /// <summary>Spin about the vertical only, so the baked top-light / belly-shadow tones stay the right way up.</summary>
            public bool spin = true;
            /// <summary>Tumble freely (debris), instead of spinning.</summary>
            public bool tumble;
            /// <summary>Fixed start rotation (degrees), e.g. a flat ring stood up to face the camera.</summary>
            public Vector3? rotation;
            /// <summary>Random roll in degrees (view-aligned icons).</summary>
            public float roll;
            /// <summary>Length for a velocity-aligned spark (its size is the width).</summary>
            public Vector2 length;
            public Vector3 velocity;
            /// <summary>How much of the emitter's own velocity a puff starts with (it keeps up with a plane, then drags behind).</summary>
            public float inherit;
            /// <summary>Drag the same on every puff, instead of Unity's default (harder on big and fast ones).</summary>
            public bool plainDrag;
            public int max = 64;
        }

        /// <summary>Builds the prefabs and the library. Returns the library, or null if the FBX hasn't been generated.</summary>
        public static VfxLibrary BuildAll()
        {
            FlightTestSceneBuilder.EnsureFolder(PrefabFolder);
            FlightTestSceneBuilder.EnsureFolder(System.IO.Path.GetDirectoryName(LibraryPath)!.Replace('\\', '/'));
            var palette = ClassModels.PaletteTexture();
            var fxMat = ParticleMat("FX_Mesh", palette);
            var bubbleMat = BubbleMat("FX_ShieldBubble", palette);
            var planeMats = new ClassModels.PlaneMaterials();
            var source = ClassModels.LoadModel(ModelPath, planeMats, new[] { fxMat, bubbleMat }, readable: true);
            if (source == null)
                return null;

            Mesh M(string n) => (source.transform.Find(n) ?? throw new System.ArgumentException($"VFX.fbx has no '{n}'"))
                .GetComponent<MeshFilter>().sharedMesh;
            var puffs = new[] { M("PuffA"), M("PuffB"), M("PuffC") };
            var chunks = new[] { M("ChunkA"), M("ChunkB") };
            var spark = new[] { M("Spark") };
            var sparkle = new[] { M("Sparkle") };
            var ring = new[] { M("Ring") };
            var crown = new[] { M("SplashCrown") };
            var boom = new[] { M("Boom") };
            var wrench = new[] { M("Wrench") };

            Spec FlashPuff(float size, float life, Color? color = null) => new()
            {
                name = "Flash", meshes = puffs, burst = 1, life = new(life, life), size = new(size, size),
                color = (color ?? Flash, color ?? Flash), sizeCurve = Burst, shape = Shape.Point,
            };
            Spec Sparks(int count, Vector2 speed, Vector2 length, float width = 1f, Shape shape = Shape.Sphere) => new()
            {
                name = "Sparks", meshes = spark, burst = count, life = new(0.2f, 0.45f), size = new(width * 0.8f, width * 1.2f),
                length = length, speed = speed, drag = 2f, gravity = 0.4f, shape = shape, coneAngle = 50f,
                align = ParticleSystemRenderSpace.Velocity, spin = false, sizeCurve = Curve((0f, 1f), (1f, 0.2f)),
            };
            Spec Chips(int count, (Color, Color) color, Vector2 speed, Vector2 size) => new()
            {
                name = "Chips", meshes = chunks, burst = count, life = new(0.8f, 1.6f), size = size, speed = speed,
                gravity = 1f, drag = 0.4f, color = color, tumble = true, spin = false, sizeCurve = Curve((0f, 1f), (0.8f, 1f), (1f, 0f)),
            };
            // Rising, quickly blooming puffs thrown along with the plane, then dragging behind it and drifting down the
            // wind: they keep up long enough to show from the chase camera (which can't see under the plane itself),
            // instead of falling behind it at once.
            Spec Kickup(string name, float rate, int max, float size = 1f) => new()
            {
                name = name, meshes = puffs, rate = rate, life = new(0.9f, 1.5f), size = new(4.5f * size, 7.5f * size),
                speed = new(1.5f, 3.5f), shape = Shape.Up, coneAngle = 35f, radius = 2f * size, drag = 1.6f, gravity = -0.03f,
                velocity = Wind.Velocity, inherit = 0.45f, plainDrag = true, color = Dust, sizeCurve = Curve((0f, 0.7f), (0.2f, 1f), (0.7f, 0.9f), (1f, 0f)),
                max = max,
            };
            Spec FacingRing(float size, float life, Color color) => new()
            {
                name = "Ring", meshes = ring, burst = 1, life = new(life, life), size = new(size, size), color = (color, color),
                sizeCurve = Expand, shape = Shape.Point, align = ParticleSystemRenderSpace.View, spin = false,
                rotation = new Vector3(90f, 0f, 0f),
            };

            var effects = new Dictionary<VfxKind, Spec[]>
            {
                [VfxKind.ExplosionPlane] = new[]
                {
                    FlashPuff(14f, 0.18f),
                    new Spec { name = "Fireball", meshes = puffs, burst = 16, life = new(0.7f, 1.2f), size = new(5f, 9f),
                        speed = new(8f, 18f), radius = 3f, drag = 1.4f, gravity = -0.15f, overLife = Fire, sizeCurve = Flare },
                    new Spec { name = "Smoke", meshes = puffs, burst = 14, delay = 0.12f, life = new(2.2f, 3.6f), size = new(6f, 11f),
                        speed = new(4f, 9f), radius = 4f, drag = 0.9f, gravity = -0.06f, color = SmokeDark, sizeCurve = Swell },
                    Chips(10, Metal, new(14f, 30f), new(0.8f, 1.6f)),
                    Sparks(22, new(30f, 55f), new(2f, 4f), 1.4f),
                    FacingRing(26f, 0.35f, Color.white),
                    new Spec { name = "Boom", meshes = boom, burst = 1, delay = 0.05f, life = new(1.1f, 1.1f), size = new(2.6f, 2.6f),
                        sizeCurve = BoomPop, shape = Shape.Point, align = ParticleSystemRenderSpace.View, spin = false, roll = 10f },
                },
                [VfxKind.ExplosionAir] = new[]
                {
                    FlashPuff(7f, 0.12f),
                    new Spec { name = "Fireball", meshes = puffs, burst = 9, life = new(0.45f, 0.8f), size = new(2.5f, 4.5f),
                        speed = new(4f, 9f), radius = 1.2f, drag = 2f, gravity = -0.1f, overLife = Fire, sizeCurve = Flare },
                    new Spec { name = "Smoke", meshes = puffs, burst = 7, delay = 0.08f, life = new(1.2f, 2f), size = new(3f, 5f),
                        speed = new(1.5f, 4f), radius = 1f, drag = 1.5f, gravity = -0.05f, color = SmokeMid, sizeCurve = Swell },
                    Sparks(12, new(18f, 30f), new(1f, 2f)),
                    FacingRing(10f, 0.25f, Color.white),
                },
                [VfxKind.ExplosionGround] = new[]
                {
                    FlashPuff(12f, 0.15f),
                    new Spec { name = "Fireball", meshes = puffs, burst = 12, life = new(0.6f, 1.1f), size = new(4f, 8f),
                        speed = new(8f, 16f), shape = Shape.Up, coneAngle = 35f, radius = 2f, drag = 1.8f, overLife = Fire, sizeCurve = Flare },
                    new Spec { name = "Dust", meshes = puffs, burst = 16, life = new(1.2f, 2f), size = new(3.5f, 6f),
                        speed = new(12f, 22f), shape = Shape.Ring, radius = 2f, drag = 2.5f, gravity = -0.02f, color = Dust, sizeCurve = Swell },
                    new Spec { name = "Dirt", meshes = chunks, burst = 12, life = new(1.5f, 2.5f), size = new(0.7f, 1.4f),
                        speed = new(12f, 24f), shape = Shape.Up, coneAngle = 40f, radius = 1f, gravity = 1.2f, color = Dirt, tumble = true,
                        spin = false, sizeCurve = Curve((0f, 1f), (0.85f, 1f), (1f, 0f)) },
                    new Spec { name = "Smoke Column", meshes = puffs, burst = 9, delay = 0.2f, life = new(2.5f, 4f), size = new(5f, 9f),
                        speed = new(4f, 9f), shape = Shape.Up, coneAngle = 12f, radius = 2f, drag = 0.6f, color = SmokeDark, sizeCurve = Swell },
                    new Spec { name = "Ground Ring", meshes = ring, burst = 1, life = new(0.4f, 0.4f), size = new(24f, 24f),
                        sizeCurve = Expand, shape = Shape.Point, spin = false },
                    Sparks(10, new(15f, 30f), new(1.2f, 2.4f), shape: Shape.Up),
                },
                [VfxKind.FlakBurst] = new[]
                {
                    FlashPuff(2.5f, 0.08f, new Color(1f, 0.75f, 0.35f)),
                    new Spec { name = "Flak", meshes = puffs, burst = 5, life = new(1.6f, 2.6f), size = new(2.4f, 3.6f),
                        speed = new(1f, 3.5f), radius = 0.8f, drag = 1.5f, color = (new Color(0.1f, 0.1f, 0.12f), new Color(0.17f, 0.17f, 0.19f)),
                        sizeCurve = Curve((0f, 0.3f), (0.1f, 1f), (0.85f, 0.85f), (1f, 0f)) },
                    Sparks(6, new(12f, 22f), new(0.8f, 1.4f)),
                },
                [VfxKind.BulletHitPlane] = new[]
                {
                    FlashPuff(1.2f, 0.06f),
                    Sparks(7, new(8f, 18f), new(0.6f, 1.2f), 0.7f),
                    Chips(2, Metal, new(4f, 8f), new(0.25f, 0.45f)),
                },
                [VfxKind.BulletHitGround] = new[]
                {
                    Sparks(4, new(8f, 16f), new(0.5f, 1f), 0.7f, Shape.Up),
                    new Spec { name = "Dust", meshes = puffs, burst = 2, life = new(0.5f, 0.8f), size = new(0.9f, 1.6f),
                        speed = new(0.5f, 2f), shape = Shape.Up, color = Dust, sizeCurve = Swell, drag = 1f },
                    Chips(3, Dirt, new(4f, 9f), new(0.25f, 0.45f)),
                },
                [VfxKind.BulletHitWater] = new[]
                {
                    new Spec { name = "Spray", meshes = puffs, burst = 4, life = new(0.5f, 0.8f), size = new(0.5f, 0.9f),
                        speed = new(4f, 8f), shape = Shape.Up, coneAngle = 15f, gravity = 1.2f, color = Spray },
                    new Spec { name = "Crown", meshes = crown, burst = 1, life = new(0.4f, 0.4f), size = new(1.2f, 1.2f),
                        sizeCurve = Crown, shape = Shape.Point, color = White },
                },
                [VfxKind.WaterSplash] = new[]
                {
                    new Spec { name = "Crown", meshes = crown, burst = 1, life = new(1.1f, 1.1f), size = new(12f, 12f),
                        sizeCurve = Crown, shape = Shape.Point },
                    new Spec { name = "Spray", meshes = puffs, burst = 18, life = new(1.1f, 1.7f), size = new(2f, 3.6f),
                        speed = new(10f, 20f), shape = Shape.Up, coneAngle = 25f, radius = 1.5f, gravity = 1.3f, color = Spray },
                    new Spec { name = "Foam", meshes = puffs, burst = 10, life = new(1.4f, 2.2f), size = new(2f, 3.5f),
                        speed = new(5f, 9f), shape = Shape.Ring, radius = 2f, drag = 2f, color = Spray, sizeCurve = Swell },
                },
                [VfxKind.PartDetach] = new[]
                {
                    FlashPuff(2.2f, 0.08f),
                    Sparks(14, new(10f, 25f), new(0.8f, 1.6f)),
                    new Spec { name = "Smoke", meshes = puffs, burst = 4, life = new(0.8f, 1.3f), size = new(1.2f, 2.2f),
                        speed = new(1f, 3f), radius = 0.5f, drag = 1.5f, color = SmokeMid, sizeCurve = Swell },
                    Chips(4, Metal, new(5f, 12f), new(0.35f, 0.6f)),
                },
                [VfxKind.Touchdown] = new[]
                {
                    new Spec { name = "Tyre Smoke", meshes = puffs, burst = 4, life = new(0.7f, 1.1f), size = new(0.8f, 1.3f),
                        speed = new(0.5f, 2f), radius = 0.3f, drag = 1.5f, gravity = -0.03f,
                        color = (new Color(0.85f, 0.85f, 0.88f), new Color(0.94f, 0.94f, 0.96f)), sizeCurve = Swell },
                },
                [VfxKind.RespawnShimmer] = new[]
                {
                    new Spec { name = "Sparkles", meshes = sparkle, burst = 16, life = new(0.4f, 0.8f), size = new(0.6f, 1.2f),
                        speed = new(0.5f, 2f), radius = 5f, align = ParticleSystemRenderSpace.View, spin = false, roll = 180f },
                },
                [VfxKind.ShieldHit] = new[]
                {
                    new Spec { name = "Sparkles", meshes = sparkle, burst = 6, life = new(0.25f, 0.45f), size = new(0.5f, 0.9f),
                        speed = new(2f, 6f), radius = 0.4f, align = ParticleSystemRenderSpace.View, spin = false, roll = 180f,
                        color = (new Color(0.7f, 0.9f, 1f), Color.white) },
                    FacingRing(3f, 0.2f, new Color(0.6f, 0.85f, 1f)),
                },
                [VfxKind.DamageSmokeLight] = new[]
                {
                    new Spec { name = "Wisps", meshes = puffs, ratePerMetre = 0.3f, rate = 3f, life = new(1f, 1.5f), size = new(0.9f, 1.6f),
                        speed = new(0.3f, 1f), radius = 0.3f, color = SmokeLight, sizeCurve = Swell, max = 80 },
                },
                [VfxKind.DamageSmokeHeavy] = new[]
                {
                    new Spec { name = "Black Smoke", meshes = puffs, ratePerMetre = 0.45f, rate = 8f, life = new(1.8f, 2.6f),
                        size = new(1.8f, 3f), speed = new(0.5f, 1.5f), radius = 0.5f, color = SmokeDark, sizeCurve = Swell, max = 150 },
                },
                [VfxKind.DamageFire] = new[]
                {
                    new Spec { name = "Flames", meshes = puffs, ratePerMetre = 0.7f, rate = 16f, life = new(0.22f, 0.4f), size = new(0.9f, 1.7f),
                        speed = new(0.5f, 2f), radius = 0.35f, overLife = Fire, sizeCurve = Flare, max = 60 },
                },
                [VfxKind.WreckSmoke] = new[]
                {
                    new Spec { name = "Column", meshes = puffs, rate = 3f, life = new(3f, 4f), size = new(3f, 6f), radius = 2f,
                        velocity = new Vector3(0f, 8f, 0f), color = SmokeDark, sizeCurve = Swell, max = 16 },
                },
                [VfxKind.DebrisSmoke] = new[]
                {
                    new Spec { name = "Smoke", meshes = puffs, ratePerMetre = 0.35f, rate = 5f, life = new(1f, 1.4f), size = new(0.9f, 1.6f),
                        speed = new(0.2f, 0.8f), radius = 0.3f, color = SmokeMid, sizeCurve = Swell, max = 60 },
                },
                [VfxKind.MissileTrail] = new[]
                {
                    new Spec { name = "Puffs", meshes = puffs, ratePerMetre = 0.8f, life = new(0.7f, 1f), size = new(0.55f, 0.9f),
                        speed = new(0f, 0.3f), radius = 0.1f, color = (new Color(0.88f, 0.88f, 0.9f), new Color(0.97f, 0.97f, 0.98f)),
                        sizeCurve = Curve((0f, 0.5f), (0.25f, 1.1f), (1f, 0f)), max = 120 },
                },
                [VfxKind.RepairSparkles] = new[]
                {
                    new Spec { name = "Wrenches", meshes = wrench, rate = 2.2f, life = new(1.1f, 1.4f), size = new(1.1f, 1.5f),
                        radius = 3.5f, velocity = new Vector3(0f, 3f, 0f), align = ParticleSystemRenderSpace.View, spin = false, roll = 20f, max = 8 },
                    new Spec { name = "Sparkles", meshes = sparkle, rate = 9f, life = new(0.4f, 0.7f), size = new(0.6f, 1.1f),
                        radius = 4f, align = ParticleSystemRenderSpace.View, spin = false, roll = 180f, max = 16 },
                },
                // 40 puffs at most (living-world.md, section 1). GroundKickup moves the systems under the wings and
                // nose, and sets their rate, size and colour from the height, speed and surface; these are full strength.
                [VfxKind.GroundKickup] = new[]
                {
                    Kickup("Left", 11f, 14), Kickup("Right", 11f, 14), Kickup("Nose", 6f, 7, size: 0.8f),
                    new Spec { name = "Droplets", meshes = chunks, rate = 5f, life = new(0.7f, 1f), size = new(0.4f, 0.8f),
                        speed = new(8f, 13f), shape = Shape.Up, coneAngle = 25f, radius = 2f, gravity = 1.3f, color = Spray,
                        tumble = true, spin = false, sizeCurve = Curve((0f, 1f), (0.8f, 1f), (1f, 0f)), inherit = 0.7f, max = 5 },
                },
            };

            var entries = new List<VfxLibrary.Entry>();
            foreach (var (kind, specs) in effects)
            {
                bool local = kind is VfxKind.RespawnShimmer or VfxKind.RepairSparkles; // stay round the (moving) plane
                bool loop = kind >= VfxKind.DamageSmokeLight;
                entries.Add(new VfxLibrary.Entry { kind = kind, prefab = BuildPrefab(kind, specs, fxMat, loop, local) });
            }

            var library = AssetDatabase.LoadAssetAtPath<VfxLibrary>(LibraryPath);
            if (library == null)
            {
                library = ScriptableObject.CreateInstance<VfxLibrary>();
                AssetDatabase.CreateAsset(library, LibraryPath);
            }
            library.SetEffects(entries.ToArray());
            library.shieldMesh = M("ShieldBubble");
            library.shieldMaterial = bubbleMat;
            library.afterburnerMesh = M("AfterburnerFlame");
            library.afterburnerMaterial = planeMats.Glow;
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
            return library;
        }

        static VfxEffect BuildPrefab(VfxKind kind, Spec[] specs, Material material, bool loop, bool local)
        {
            var root = new GameObject(kind.ToString());
            try
            {
                var effect = root.AddComponent<VfxEffect>();
                Transform boom = null;
                foreach (var spec in specs)
                {
                    var ps = AddSystem(root.transform, spec, material, loop, local);
                    if (spec.name == "Boom")
                        boom = ps.transform;
                }
                if (boom != null)
                {
                    var so = new SerializedObject(effect);
                    var pulls = so.FindProperty("pullTowardCamera");
                    pulls.arraySize = 1;
                    pulls.GetArrayElementAtIndex(0).objectReferenceValue = boom;
                    so.FindProperty("pullDistance").floatValue = 12f;
                    so.ApplyModifiedPropertiesWithoutUndo();
                }
                var prefab = PrefabUtility.SaveAsPrefabAsset(root, $"{PrefabFolder}/{kind}.prefab");
                return prefab.GetComponent<VfxEffect>();
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        static ParticleSystem AddSystem(Transform parent, Spec s, Material material, bool loop, bool local)
        {
            var go = new GameObject(s.name);
            go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = loop;
            main.duration = loop ? 1f : 0.1f;
            main.startDelay = s.delay;
            main.startLifetime = new ParticleSystem.MinMaxCurve(s.life.x, s.life.y);
            main.startSpeed = new ParticleSystem.MinMaxCurve(s.speed.x, s.speed.y);
            if (s.length != Vector2.zero)
            {
                // Velocity-aligned spark: width on x/y, length down its z (the spark mesh is 1 m long).
                main.startSize3D = true;
                main.startSizeX = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
                main.startSizeY = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
                main.startSizeZ = new ParticleSystem.MinMaxCurve(s.length.x, s.length.y);
            }
            else
            {
                main.startSize = new ParticleSystem.MinMaxCurve(s.size.x, s.size.y);
            }
            if (s.rotation is { } rot)
            {
                main.startRotation3D = true;
                main.startRotationX = rot.x * Mathf.Deg2Rad;
                main.startRotationY = rot.y * Mathf.Deg2Rad;
                main.startRotationZ = new ParticleSystem.MinMaxCurve((rot.z - s.roll) * Mathf.Deg2Rad, (rot.z + s.roll) * Mathf.Deg2Rad);
            }
            else if (s.tumble)
            {
                main.startRotation3D = true;
                main.startRotationX = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            }
            else if (s.spin)
            {
                main.startRotation3D = true;
                main.startRotationX = 0f;
                main.startRotationY = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
                main.startRotationZ = 0f;
            }
            else
            {
                main.startRotation = new ParticleSystem.MinMaxCurve(-s.roll * Mathf.Deg2Rad, s.roll * Mathf.Deg2Rad);
            }
            main.startColor = new ParticleSystem.MinMaxGradient(s.color.Item1, s.color.Item2);
            main.gravityModifier = s.gravity;
            main.simulationSpace = local ? ParticleSystemSimulationSpace.Local : ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = s.burst > 0 ? s.burst : s.max;
            main.stopAction = ParticleSystemStopAction.None;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate;

            var emission = ps.emission;
            emission.rateOverTime = s.rate;
            emission.rateOverDistance = s.ratePerMetre;
            emission.SetBursts(s.burst > 0 ? new[] { new ParticleSystem.Burst(0f, (short)s.burst) } : System.Array.Empty<ParticleSystem.Burst>());

            var shape = ps.shape;
            shape.enabled = s.shape != Shape.Point;
            shape.radius = Mathf.Max(0.0001f, s.radius);
            shape.radiusThickness = 1f;
            switch (s.shape)
            {
                case Shape.Sphere:
                    shape.shapeType = ParticleSystemShapeType.Sphere;
                    break;
                case Shape.Up:
                    shape.shapeType = ParticleSystemShapeType.Cone;
                    shape.angle = s.coneAngle;
                    shape.rotation = new Vector3(-90f, 0f, 0f); // the cone points down +Z: stand it up
                    break;
                case Shape.Ring:
                    shape.shapeType = ParticleSystemShapeType.Circle;
                    shape.radiusThickness = 0f;
                    shape.rotation = new Vector3(90f, 0f, 0f); // lie the circle flat on the ground
                    break;
            }

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, s.sizeCurve);

            if (s.overLife != null)
            {
                var col = ps.colorOverLifetime;
                col.enabled = true;
                col.color = new ParticleSystem.MinMaxGradient(s.overLife);
            }

            if (s.tumble || s.spin)
            {
                var rotation = ps.rotationOverLifetime;
                rotation.enabled = true;
                rotation.separateAxes = true;
                float r = s.tumble ? 6f : 0.6f;
                rotation.x = s.tumble ? new ParticleSystem.MinMaxCurve(-r, r) : new ParticleSystem.MinMaxCurve(0f);
                rotation.y = new ParticleSystem.MinMaxCurve(-r, r);
                rotation.z = s.tumble ? new ParticleSystem.MinMaxCurve(-r, r) : new ParticleSystem.MinMaxCurve(0f);
            }

            if (s.drag > 0f)
            {
                var limit = ps.limitVelocityOverLifetime;
                limit.enabled = true;
                limit.limit = 10000f; // drag only
                limit.dampen = 0f;
                limit.drag = s.drag;
                if (s.plainDrag)
                {
                    limit.multiplyDragByParticleSize = false;
                    limit.multiplyDragByParticleVelocity = false;
                }
            }

            if (s.velocity != Vector3.zero)
            {
                var vel = ps.velocityOverLifetime;
                vel.enabled = true;
                vel.space = ParticleSystemSimulationSpace.World;
                vel.x = s.velocity.x;
                vel.y = s.velocity.y;
                vel.z = s.velocity.z;
            }

            if (s.inherit > 0f)
            {
                var inherit = ps.inheritVelocity;
                inherit.enabled = true;
                inherit.mode = ParticleSystemInheritVelocityMode.Initial;
                inherit.curve = s.inherit;
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Mesh;
            renderer.SetMeshes(s.meshes);
            renderer.sharedMaterial = material;
            renderer.alignment = s.align;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.enableGPUInstancing = true;
            return ps;
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Opaque, unlit, culled: the palette times the particle colour. Every particle mesh uses it.</summary>
        static Material ParticleMat(string name, Texture2D palette)
        {
            string path = $"{FlightTestSceneBuilder.MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", palette);
            mat.SetColor("_BaseColor", Color.white);
            mat.SetFloat("_Surface", 0f); // opaque
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        /// <summary>Alpha-blended unlit palette, for the faceted shield bubble (tinted per use with _BaseColor).</summary>
        static Material BubbleMat(string name, Texture2D palette)
        {
            string path = $"{FlightTestSceneBuilder.MaterialsFolder}/{name}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.SetTexture("_BaseMap", palette);
            mat.SetColor("_BaseColor", new Color(0.45f, 0.75f, 1f, 0.3f));
            mat.SetFloat("_Surface", 1f); // transparent
            mat.SetFloat("_Blend", 0f);   // alpha
            mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.SetFloat("_Cull", (float)UnityEngine.Rendering.CullMode.Back);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------------ preview

        /// <summary>
        /// Renders every effect, simulated <paramref name="time"/> seconds in, as tiles of one PNG (6 per row) in a preview
        /// scene, so the look can be checked without entering play mode. Looping effects run from their time rate only.
        /// </summary>
        internal static void RenderContactSheet(string pngPath, float time)
        {
            var library = AssetDatabase.LoadAssetAtPath<VfxLibrary>(LibraryPath);
            var kinds = (VfxKind[])System.Enum.GetValues(typeof(VfxKind));
            const int tileW = 400, tileH = 300, columns = 6;
            int rows = (kinds.Length + columns - 1) / columns;
            var sheet = new Texture2D(tileW * columns, tileH * rows, TextureFormat.RGB24, false);
            var scene = UnityEditor.SceneManagement.EditorSceneManager.NewPreviewScene();
            var rt = new RenderTexture(tileW, tileH, 24);
            var camGo = new GameObject("Preview Camera");
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(camGo, scene);
            var cam = camGo.AddComponent<Camera>();
            cam.scene = scene;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.75f, 0.9f, 1f);
            cam.fieldOfView = 50f;
            cam.targetTexture = rt;
            try
            {
                for (int i = 0; i < kinds.Length; i++)
                {
                    var prefab = library.Get(kinds[i]);
                    if (prefab == null)
                        continue;
                    var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab.gameObject, scene);
                    float extent = kinds[i] switch
                    {
                        VfxKind.ExplosionPlane or VfxKind.ExplosionGround or VfxKind.WaterSplash => 34f,
                        VfxKind.ExplosionAir or VfxKind.RespawnShimmer or VfxKind.RepairSparkles or VfxKind.GroundKickup => 18f,
                        VfxKind.FlakBurst or VfxKind.PartDetach => 12f,
                        _ => 6f,
                    };
                    cam.transform.position = new Vector3(0f, extent * 0.25f, -extent);
                    cam.transform.LookAt(new Vector3(0f, extent * 0.12f, 0f));
                    var boom = go.transform.Find("Boom");
                    if (boom != null)
                        boom.position = (cam.transform.position - go.transform.position).normalized * 7f;
                    foreach (var ps in go.GetComponentsInChildren<ParticleSystem>())
                    {
                        ps.useAutoRandomSeed = false;
                        ps.randomSeed = 7;
                        ps.Simulate(time, false, true);
                    }
                    cam.Render();
                    RenderTexture.active = rt;
                    sheet.ReadPixels(new Rect(0, 0, tileW, tileH), (i % columns) * tileW, (rows - 1 - i / columns) * tileH);
                    RenderTexture.active = null;
                    Object.DestroyImmediate(go);
                }
                sheet.Apply();
                System.IO.File.WriteAllBytes(pngPath, sheet.EncodeToPNG());
            }
            finally
            {
                cam.targetTexture = null;
                Object.DestroyImmediate(rt);
                Object.DestroyImmediate(sheet);
                UnityEditor.SceneManagement.EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        // ------------------------------------------------------------------ helpers

        static AnimationCurve Curve(params (float t, float v)[] keys)
        {
            var curve = new AnimationCurve();
            foreach (var (t, v) in keys)
                curve.AddKey(t, v);
            return curve;
        }

        static Gradient Grad(params (float t, Color c)[] keys)
        {
            var colors = new GradientColorKey[keys.Length];
            for (int i = 0; i < keys.Length; i++)
                colors[i] = new GradientColorKey(keys[i].c, keys[i].t);
            var g = new Gradient();
            g.SetKeys(colors, new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}

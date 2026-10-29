using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Editor
{
    /// <summary>
    /// The pilot and the parachute (ArtSource/Pilot/build_pilot.py → Art/Models/Pilot/Pilot.fbx and PilotMoves.json), made
    /// into Prefabs/Pilot/Pilot.prefab (a <see cref="PilotFigure"/>: a rig of <see cref="PilotJoint"/> transforms rebuilt
    /// from the model's Joint markers, every gear variant's pieces on it, and an Animator playing one clip per
    /// <see cref="PilotMove"/>) and Prefabs/Pilot/Parachute.prefab (a <see cref="PilotParachute"/>, origin where the risers
    /// meet: its lines, and a canopy per <see cref="CanopyPattern"/> under Canopy). The clips are written to
    /// Art/Models/Pilot/Moves, the controller next to the prefab.
    /// </summary>
    internal class PilotModels : PackModels
    {
        internal const string PrefabFolder = FlightTestSceneBuilder.Root + "/Prefabs/Pilot";
        internal const string PilotPrefabPath = PrefabFolder + "/Pilot.prefab";
        internal const string ParachutePrefabPath = PrefabFolder + "/Parachute.prefab";
        internal const string ControllerPath = PrefabFolder + "/Pilot.controller";
        internal const string MovesPath = ClassModels.ModelsFolder + "/Pilot/PilotMoves.json";
        internal const string ClipFolder = ClassModels.ModelsFolder + "/Pilot/Moves";

        /// <summary>One-shot moves and the move each hands on to when it's done.</summary>
        static readonly Dictionary<PilotMove, PilotMove> s_then = new()
        {
            { PilotMove.LookAround, PilotMove.Idle }, { PilotMove.Stretch, PilotMove.Idle },
            { PilotMove.CheckWatch, PilotMove.Idle }, { PilotMove.Wave, PilotMove.Idle },
            { PilotMove.Deploy, PilotMove.Hang }, { PilotMove.Land, PilotMove.Idle },
        };

        const float HandOver = 0.25f; // seconds a one-shot blends into the move after it

#pragma warning disable 0649 // filled by JsonUtility
        [System.Serializable]
        class MovesFile
        {
            public string[] joints;
            public string[] parents;
            public MoveData[] moves;
        }

        [System.Serializable]
        class MoveData
        {
            public string name;
            public bool loop;
            public KeyData[] keys;
        }

        [System.Serializable]
        class KeyData
        {
            public float t;
            public float[] hips;
            public float[] euler;
        }
#pragma warning restore 0649

        PilotModels(GameObject source) : base(source) { }

        public static PilotModels Load()
        {
            var source = LoadSource("Pilot");
            return source == null ? null : new PilotModels(source);
        }

        /// <summary>Writes (or refreshes) the clips, the controller and both prefabs. False when the FBX hasn't been generated.</summary>
        [MenuItem("Winging It/Build Pilot Prefabs")]
        public static bool BuildPrefabs()
        {
            var models = Load();
            if (models == null || !File.Exists(MovesPath))
                return false;
            FlightTestSceneBuilder.EnsureFolder(PrefabFolder);
            FlightTestSceneBuilder.EnsureFolder(ClipFolder);
            var moves = JsonUtility.FromJson<MovesFile>(File.ReadAllText(MovesPath));
            var controller = BuildController(moves);
            var materials = new ClassModels.PlaneMaterials();
            models.SavePilot(materials.Team, moves, controller);
            models.SaveParachute(materials.Team, materials.Trim);
            Debug.Log($"[Winging It] Built {PilotPrefabPath} ({moves.moves.Length} moves) and {ParachutePrefabPath}");
            return true;
        }

        // ------------------------------------------------------------------------------------------ moves

        /// <summary>The path from the figure's root to <paramref name="joint"/> (Hips/Chest/Head...).</summary>
        static string PathOf(MovesFile file, int joint)
        {
            string path = file.joints[joint];
            for (string parent = file.parents[joint]; parent != ""; parent = file.parents[System.Array.IndexOf(file.joints, parent)])
                path = parent + "/" + path;
            return path;
        }

        /// <summary>
        /// A clip for <paramref name="move"/>: Euler curves on every joint and the hips' position, with build_pilot.py's
        /// tangents (<see cref="Slope"/>, so the clip moves as its previews and feet checks do), looping if the move loops.
        /// Reuses the clip asset if it exists, so references to it hold.
        /// </summary>
        static AnimationClip BuildClip(MovesFile file, MoveData move)
        {
            string path = $"{ClipFolder}/{move.name}.anim";
            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip == null)
            {
                clip = new AnimationClip { name = move.name };
                AssetDatabase.CreateAsset(clip, path);
            }
            clip.ClearCurves();
            clip.frameRate = 30f;
            string[] axes = { "x", "y", "z" };
            var times = System.Array.ConvertAll(move.keys, k => k.t);
            for (int j = 0; j < file.joints.Length; j++)
            {
                string jointPath = PathOf(file, j);
                for (int a = 0; a < 3; a++)
                {
                    int axis = j * 3 + a;
                    AnimationUtility.SetEditorCurve(clip,
                        EditorCurveBinding.FloatCurve(jointPath, typeof(Transform), "localEulerAnglesRaw." + axes[a]),
                        Curve(times, System.Array.ConvertAll(move.keys, k => k.euler[axis])));
                    if (j == 0)
                        AnimationUtility.SetEditorCurve(clip,
                            EditorCurveBinding.FloatCurve(jointPath, typeof(Transform), "m_LocalPosition." + axes[a]),
                            Curve(times, System.Array.ConvertAll(move.keys, k => k.hips[a])));
                }
            }
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = move.loop;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            EditorUtility.SetDirty(clip);
            return clip;
        }

        static AnimationCurve Curve(float[] times, float[] values)
        {
            var keys = new Keyframe[times.Length];
            for (int k = 0; k < keys.Length; k++)
            {
                float slope = Slope(times, values, k);
                keys[k] = new Keyframe(times[k], values[k], slope, slope);
            }
            return new AnimationCurve(keys);
        }

        /// <summary>
        /// A key's tangent (build_pilot.py slope): flat at the ends and at a peak or dip, so nothing overshoots a pose;
        /// else the slope from the key before to the key after, so a move flows through its middle keys.
        /// </summary>
        internal static float Slope(float[] times, float[] values, int k)
        {
            if (k == 0 || k == values.Length - 1 || (values[k] - values[k - 1]) * (values[k + 1] - values[k]) <= 0f)
                return 0f;
            return (values[k + 1] - values[k - 1]) / (times[k + 1] - times[k - 1]);
        }

        /// <summary>
        /// The controller: a state per <see cref="PilotMove"/> (named after it, Idle the default), each one-shot handing on
        /// to the move after it (<see cref="s_then"/>) as it ends. Rewritten from scratch each build.
        /// </summary>
        static AnimatorController BuildController(MovesFile file)
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var move in file.moves)
                clips[move.name] = BuildClip(file, move);
            AssetDatabase.SaveAssets();

            AssetDatabase.DeleteAsset(ControllerPath);
            var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
            var machine = controller.layers[0].stateMachine;
            var states = new Dictionary<PilotMove, AnimatorState>();
            foreach (PilotMove move in System.Enum.GetValues(typeof(PilotMove)))
            {
                if (!clips.TryGetValue(move.ToString(), out var clip))
                    throw new System.InvalidOperationException($"{MovesPath} has no move {move}: re-run build_pilot.py");
                var state = machine.AddState(move.ToString());
                state.motion = clip;
                state.writeDefaultValues = true;
                states[move] = state;
            }
            machine.defaultState = states[PilotMove.Idle];
            foreach (var (move, then) in s_then)
            {
                float length = clips[move.ToString()].length;
                var t = states[move].AddTransition(states[then]);
                t.hasExitTime = true;
                t.exitTime = Mathf.Clamp01(1f - HandOver / Mathf.Max(0.01f, length));
                t.hasFixedDuration = true;
                t.duration = HandOver;
            }
            AssetDatabase.SaveAssets();
            return controller;
        }

        // ------------------------------------------------------------------------------------------ prefabs

        void SavePilot(Material team, MovesFile file, AnimatorController controller)
        {
            var root = new GameObject("Pilot");
            var joints = new Transform[file.joints.Length];
            for (int j = 0; j < joints.Length; j++)
            {
                string name = file.joints[j];
                string parent = file.parents[j];
                var t = new GameObject(name).transform;
                t.SetParent(parent == "" ? root.transform : joints[System.Array.IndexOf(file.joints, parent)], false);
                t.localPosition = Pivot("Joint" + name) - (parent == "" ? Vector3.zero : Pivot("Joint" + parent));
                joints[j] = t;
            }
            for (int j = 0; j < joints.Length; j++)
                if (joints[j].name != ((PilotJoint)j).ToString())
                    throw new System.InvalidOperationException($"joint {j} is {joints[j].name}, PilotJoint says {(PilotJoint)j}");

            // Every piece on its joint: the bare body, then each slot's variants.
            var gear = new List<PilotFigure.GearPieces>();
            var byVariant = new Dictionary<string, PilotFigure.GearPieces>();
            foreach (Transform child in Source.transform)
            {
                string[] parts = child.name.Split('_');
                if (child.GetComponent<MeshFilter>() == null || parts.Length < 2 ||
                    (parts[0] != "Body" && !System.Enum.TryParse<PilotGearSlot>(parts[0], out _)))
                    continue;
                string jointName = parts[^1];
                var joint = System.Array.Find(joints, t => t.name == jointName)
                            ?? throw new System.InvalidOperationException($"{child.name}: no joint {jointName}");
                var go = Child(child.name, joint, Pivot(child.name) - Pivot("Joint" + jointName));
                go.isStatic = false;
                if (parts[0] == "Body")
                    continue;
                string key = parts[0] + "_" + parts[1];
                if (!byVariant.TryGetValue(key, out var entry))
                {
                    entry = new PilotFigure.GearPieces
                    {
                        slot = System.Enum.Parse<PilotGearSlot>(parts[0]), variant = parts[1],
                        pieces = System.Array.Empty<Renderer>(),
                    };
                    byVariant[key] = entry;
                    gear.Add(entry);
                }
                entry.pieces = ArrayAppend(entry.pieces, go.GetComponent<Renderer>());
            }

            var riser = new GameObject("RiserAttach").transform;
            riser.SetParent(joints[(int)PilotJoint.Chest], false);
            riser.localPosition = Pivot("PilotRiserAttach") - Pivot("JointChest");

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            root.AddComponent<PilotFigure>().Configure(team, joints, gear.ToArray(), riser, Pivot("PilotSeat"), animator);
            PrefabUtility.SaveAsPrefabAsset(root, PilotPrefabPath);
            Object.DestroyImmediate(root);
        }

        static Renderer[] ArrayAppend(Renderer[] list, Renderer item)
        {
            var grown = new Renderer[list.Length + 1];
            list.CopyTo(grown, 0);
            grown[^1] = item;
            return grown;
        }

        void SaveParachute(Material team, Material trim)
        {
            var root = new GameObject("Parachute");
            Moving("Parachute", root.transform, Vector3.zero).name = "Lines";
            var canopy = new GameObject("Canopy").transform;
            canopy.SetParent(root.transform, false);
            var patterns = new Transform[System.Enum.GetValues(typeof(CanopyPattern)).Length];
            for (int i = 0; i < patterns.Length; i++)
                patterns[i] = Moving("Canopy" + (CanopyPattern)i, canopy, Vector3.zero);
            root.AddComponent<PilotParachute>().Configure(team, trim, canopy, patterns);
            PrefabUtility.SaveAsPrefabAsset(root, ParachutePrefabPath);
            Object.DestroyImmediate(root);
        }

        Transform Moving(string name, Transform parent, Vector3 localPosition)
        {
            var go = Child(name, parent, localPosition);
            go.isStatic = false;
            return go.transform;
        }
    }
}

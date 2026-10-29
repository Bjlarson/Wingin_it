using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// The menu pilot's idle fidgets (<see cref="PilotIdle.Fidget"/>): every one comes round and none plays twice in a
    /// row; and where it stands on the menu stage.
    /// </summary>
    public class PilotIdleTests
    {
        [Test]
        public void Fidgets_EachComesRound_AndNoneFollowsItself()
        {
            for (int start = 0; start < 8; start++)
            {
                var seen = new System.Collections.Generic.HashSet<PilotMove>();
                for (int i = start; i < start + PilotIdle.Fidgets.Length; i++)
                    seen.Add(PilotIdle.Fidget(i));
                Assert.AreEqual(PilotIdle.Fidgets.Length, seen.Count, $"from {start}");
            }
            for (int i = 0; i < 40; i++)
                Assert.AreNotEqual(PilotIdle.Fidget(i), PilotIdle.Fidget(i + 1), $"fidget {i}");
        }

        [Test]
        public void Fidgets_AreOneShotStandingMoves()
        {
            foreach (var move in PilotIdle.Fidgets)
                Assert.That(move, Is.Not.EqualTo(PilotMove.Idle).And.Not.EqualTo(PilotMove.Sit).And.Not.EqualTo(PilotMove.Hang));
        }

        [Test]
        public void MenuPilot_StandsClearOfEveryClassesWing()
        {
            const string menu = "Assets/_Project/Scenes/MainMenu.unity";
            var scene = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(menu);
            bool opened = !scene.isLoaded; // the Editor may have it open already: leave that one be
            if (opened)
                scene = EditorSceneManager.OpenScene(menu, OpenSceneMode.Additive);
            try
            {
                PilotIdle pilot = null;
                Transform spot = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    pilot ??= root.GetComponentInChildren<PilotIdle>(true);
                    foreach (var t in root.GetComponentsInChildren<Transform>(true))
                        if (t.name == "Hangar Spot")
                            spot = t;
                }
                Assert.IsNotNull(pilot, "the menu stage has a pilot");
                Assert.IsNotNull(spot, "the menu stage has a hangar spot");

                // The pilot's nearest point to the plane's centreline, sideways, in the spot's space.
                float inner = float.MaxValue;
                foreach (var r in pilot.GetComponentsInChildren<Renderer>())
                {
                    var b = r.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        var corner = new Vector3(i % 2 == 0 ? b.min.x : b.max.x, (i / 2) % 2 == 0 ? b.min.y : b.max.y,
                            i / 4 == 0 ? b.min.z : b.max.z);
                        inner = Mathf.Min(inner, Mathf.Abs(spot.InverseTransformPoint(corner).x));
                    }
                }
                foreach (var model in new[] { "Fighter", "HeavyFighter", "Interceptor", "Support" })
                {
                    var fbx = AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/_Project/Art/Models/{model}/{model}.fbx");
                    Assert.IsNotNull(fbx, model);
                    float halfSpan = 0f;
                    foreach (var f in fbx.GetComponentsInChildren<MeshFilter>(true))
                    foreach (var v in f.sharedMesh.vertices)
                        halfSpan = Mathf.Max(halfSpan, Mathf.Abs(fbx.transform.InverseTransformPoint(f.transform.TransformPoint(v)).x));
                    Assert.Greater(inner, halfSpan + 0.3f, $"the pilot stands clear of the {model}'s wingtip");
                }
                // Inside the menu camera's 12 m orbit, so the camera never passes through it.
                Assert.Less(Vector3.ProjectOnPlane(pilot.transform.position - spot.position, Vector3.up).magnitude, 10f);
            }
            finally
            {
                if (opened)
                    EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}

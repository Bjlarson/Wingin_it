using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The pilot and parachute prefabs (ArtSource/Pilot/build_pilot.py, made by the editor's PilotModels).</summary>
    public class PilotModelTests
    {
        const string PilotPath = "Assets/_Project/Prefabs/Pilot/Pilot.prefab";
        const string ParachutePath = "Assets/_Project/Prefabs/Pilot/Parachute.prefab";

        static GameObject Load(string path)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            Assert.IsNotNull(prefab, $"{path} is missing: run Winging It/Build Pilot Prefabs");
            return prefab;
        }

        static int Triangles(GameObject root)
        {
            int tris = 0;
            foreach (var f in root.GetComponentsInChildren<MeshFilter>(true))
                for (int i = 0; i < f.sharedMesh.subMeshCount; i++)
                    tris += (int)(f.sharedMesh.GetIndexCount(i) / 3);
            return tris;
        }

        [Test]
        public void Pilot_IsARigOfJointedPieces_StandingOnItsOrigin()
        {
            var figure = Object.Instantiate(Load(PilotPath)).GetComponent<PilotFigure>();
            try
            {
                CheckFigure(figure);
            }
            finally
            {
                Object.DestroyImmediate(figure.gameObject);
            }
        }

        static void CheckFigure(PilotFigure figure)
        {
            Assert.IsNotNull(figure);
            foreach (PilotJoint joint in System.Enum.GetValues(typeof(PilotJoint)))
                Assert.IsNotNull(figure.Joint(joint), joint.ToString());

            // The chain a move turns: chest on the hips, head and arms on the chest, each limb down from its root.
            Assert.AreEqual(figure.transform, figure.Hips.parent);
            Assert.AreEqual(figure.Hips, figure.Chest.parent);
            Assert.AreEqual(figure.Chest, figure.Head.parent);
            Assert.AreEqual(figure.Joint(PilotJoint.ForearmL), figure.HandLeft.parent);
            Assert.AreEqual(figure.Joint(PilotJoint.UpperArmR), figure.Joint(PilotJoint.ForearmR).parent);
            Assert.AreEqual(figure.Hips, figure.Joint(PilotJoint.ThighL).parent);
            Assert.AreEqual(figure.Joint(PilotJoint.ShinR), figure.Joint(PilotJoint.FootR).parent);
            Assert.AreEqual(figure.Chest, figure.RiserAttach.parent);
            Assert.IsNotNull(figure.Animator);
            Assert.IsNotNull(figure.Animator.runtimeAnimatorController);

            // Joints where a 1.8 m person's are: hips, neck, shoulders either side, knees.
            Assert.AreEqual(0.98f, figure.Hips.position.y, 0.05f);
            Assert.AreEqual(1.55f, figure.Head.position.y, 0.05f);
            Assert.Less(figure.Joint(PilotJoint.UpperArmL).position.x, 0f);
            Assert.Greater(figure.Joint(PilotJoint.UpperArmR).position.x, 0f);
            Assert.AreEqual(figure.Joint(PilotJoint.UpperArmL).position.y, figure.Joint(PilotJoint.UpperArmR).position.y, 1e-4f);
            Assert.AreEqual(0.53f, figure.Joint(PilotJoint.ShinL).position.y, 0.05f);
            Assert.Less(figure.RiserAttach.position.z, 0.05f, "the risers hook on at the shoulders, not out in front");

            var bounds = Bounds(figure.gameObject);
            Assert.AreEqual(0f, bounds.min.y, 0.03f, "feet on the origin");
            Assert.AreEqual(1.86f, bounds.max.y, 0.08f, "about 1.8 m tall, helmet on");
        }

        static Bounds Bounds(GameObject root)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if (first)
                    bounds = r.bounds;
                else
                    bounds.Encapsulate(r.bounds);
                first = false;
            }
            return bounds;
        }

        [Test]
        public void Pilot_HasEveryGearVariant_WearingTheDefaults()
        {
            var figure = Object.Instantiate(Load(PilotPath)).GetComponent<PilotFigure>();
            try
            {
                foreach (var slot in PilotGear.Slots)
                {
                    CollectionAssert.AreEquivalent(PilotGear.Variants(slot), figure.Variants(slot), slot.ToString());
                    foreach (var variant in PilotGear.Variants(slot))
                    {
                        var pieces = figure.Pieces(slot, variant);
                        Assert.IsNotEmpty(pieces, $"{slot} {variant}");
                        bool worn = variant == PilotGear.Default(slot);
                        foreach (var r in pieces)
                            Assert.AreEqual(worn, r.gameObject.activeSelf, $"{r.name}: only the default shows");
                    }
                }
                // Each slot's pieces sit on the joints that slot dresses.
                Assert.AreEqual(figure.Head, figure.Pieces(PilotGearSlot.Helmet, "Jet")[0].transform.parent);
                Assert.AreEqual(figure.Chest, figure.Pieces(PilotGearSlot.Backpack, "Oxygen")[0].transform.parent);
                Assert.AreEqual(2, figure.Pieces(PilotGearSlot.Gloves, "Flight").Length, "a glove per hand");
                Assert.AreEqual(2, figure.Pieces(PilotGearSlot.Boots, "Combat").Length, "a boot per foot");
                Assert.AreEqual(10, figure.Pieces(PilotGearSlot.Suit, "Flight").Length, "hips, chest and two pieces a limb");
            }
            finally
            {
                Object.DestroyImmediate(figure.gameObject);
            }
        }

        [Test]
        public void Pilot_SwapsOneSlot_LeavingTheOthers()
        {
            var figure = Object.Instantiate(Load(PilotPath)).GetComponent<PilotFigure>();
            try
            {
                Assert.IsTrue(figure.SetGear(PilotGearSlot.Helmet, "Leather"));
                Assert.AreEqual("Leather", figure.Wearing(PilotGearSlot.Helmet));
                Assert.IsTrue(figure.Pieces(PilotGearSlot.Helmet, "Leather")[0].gameObject.activeSelf);
                Assert.IsFalse(figure.Pieces(PilotGearSlot.Helmet, "Jet")[0].gameObject.activeSelf);
                Assert.IsTrue(figure.Pieces(PilotGearSlot.Boots, "Combat")[0].gameObject.activeSelf, "the boots stay on");

                Assert.IsFalse(figure.SetGear(PilotGearSlot.Helmet, "Top Hat"), "an unknown variant changes nothing");
                Assert.AreEqual("Leather", figure.Wearing(PilotGearSlot.Helmet));
            }
            finally
            {
                Object.DestroyImmediate(figure.gameObject);
            }
        }

        static void AssertTint(Color expected, Material material) =>
            Assert.Less(Vector4.Distance(Flight.PlaneVisuals.TeamTint(expected), material.GetVector("_BaseColor")), 1e-3f);

        [Test]
        public void Pilot_SuitAndHelmetTakeTheirOwnColours()
        {
            var figure = Object.Instantiate(Load(PilotPath)).GetComponent<PilotFigure>();
            try
            {
                var suit = new Color(0.4f, 0.5f, 0.3f);
                var helmet = new Color(0.9f, 0.3f, 0.2f);
                figure.SetColors(suit, helmet);
                Material Team(Renderer r) => System.Array.Find(r.sharedMaterials, m => m.name == "Plane_Team");

                var chest = Team(System.Array.Find(figure.Pieces(PilotGearSlot.Suit, "Flight"), r => r.name.EndsWith("Chest")));
                Assert.IsNotNull(chest);
                foreach (var r in figure.Pieces(PilotGearSlot.Suit, "Coverall"))
                    Assert.AreSame(chest, Team(r), $"{r.name}: one shared suit material, so pilots batch");
                var shell = Team(figure.Pieces(PilotGearSlot.Helmet, "Jet")[0]);
                Assert.AreNotSame(chest, shell);
                AssertTint(suit, chest);
                AssertTint(helmet, shell);
                AssertTint(helmet, Team(figure.Pieces(PilotGearSlot.Helmet, "Leather")[0]));

                figure.SetColors(helmet, suit);  // repainting finds the already-tinted slots
                AssertTint(helmet, Team(figure.Pieces(PilotGearSlot.Suit, "Flight")[0]));
            }
            finally
            {
                Object.DestroyImmediate(figure.gameObject);
            }
        }

        [Test]
        public void Parachute_HangsFromItsOrigin_AndStaysInBudget()
        {
            var chute = Load(ParachutePath).GetComponent<PilotParachute>();
            Assert.IsNotNull(chute);
            Assert.AreEqual(Vector3.zero, chute.Canopy.localPosition);
            var lines = chute.transform.Find("Lines");
            Assert.IsNotNull(lines);
            foreach (CanopyPattern pattern in System.Enum.GetValues(typeof(CanopyPattern)))
            {
                var piece = chute.PatternPiece(pattern);
                Assert.IsNotNull(piece, pattern.ToString());
                Assert.AreEqual(Vector3.zero, piece.localPosition, "every canopy sits on the origin");
                var bounds = piece.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.Greater(bounds.min.y, 4f, "the canopy is well over the pilot's head");
                Assert.Greater(bounds.size.x, 6f, "a canopy wide enough to read from a distance");
                // Low-poly budget (asset-style-as-built.md): the rigging and one canopy, ink hull included.
                Assert.Less(Triangles(lines.gameObject) + Triangles(piece.gameObject), 800, pattern.ToString());
            }
            // The pilot as worn (default gear, ink included): a hero piece on the menu stage, so over a plane's floor,
            // its ball joints and visor shield the most of it.
            int worn = 0;
            foreach (var f in Load(PilotPath).GetComponentsInChildren<MeshFilter>(false))
                for (int i = 0; i < f.sharedMesh.subMeshCount; i++)
                    worn += (int)(f.sharedMesh.GetIndexCount(i) / 3);
            Assert.Less(worn, 3800);
        }

        [Test]
        public void Parachute_ShowsOnePattern_AndTintsMainAndTrimApart()
        {
            var chute = Object.Instantiate(Load(ParachutePath)).GetComponent<PilotParachute>();
            try
            {
                chute.SetPattern(CanopyPattern.Crown);
                foreach (CanopyPattern pattern in System.Enum.GetValues(typeof(CanopyPattern)))
                    Assert.AreEqual(pattern == CanopyPattern.Crown, chute.PatternPiece(pattern).gameObject.activeSelf, pattern.ToString());

                var main = new Color(0.9f, 0.3f, 0.2f);
                var trim = new Color(1f, 0.8f, 0.2f);
                chute.SetColors(main, trim);
                foreach (CanopyPattern pattern in System.Enum.GetValues(typeof(CanopyPattern)))
                {
                    var mats = chute.PatternPiece(pattern).GetComponent<Renderer>().sharedMaterials;
                    var team = System.Array.Find(mats, m => m.name == "Plane_Team");
                    var trimMat = System.Array.Find(mats, m => m.name == "Plane_Trim");
                    Assert.IsNotNull(team, pattern.ToString());
                    Assert.IsNotNull(trimMat, $"{pattern} has trim gores on their own material");
                    AssertTint(main, team);
                    AssertTint(trim, trimMat);
                }
            }
            finally
            {
                Object.DestroyImmediate(chute.gameObject);
            }
        }
    }
}

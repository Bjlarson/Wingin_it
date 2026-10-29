using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>
    /// The pilot's moves (ArtSource/Pilot/build_pilot.py MOVES, made into clips and Prefabs/Pilot/Pilot.controller by the
    /// editor's PilotModels): one state per <see cref="PilotMove"/>, the one-shots handing on by themselves, and each pose
    /// sampled through its length to check it stands, sits and hangs where it should.
    /// </summary>
    public class PilotMoveTests
    {
        const string PilotPath = "Assets/_Project/Prefabs/Pilot/Pilot.prefab";
        const string ControllerPath = "Assets/_Project/Prefabs/Pilot/Pilot.controller";

        static readonly PilotMove[] Loops = { PilotMove.Idle, PilotMove.Sit, PilotMove.Freefall, PilotMove.Hang, PilotMove.Risers };
        static readonly PilotMove[] OnFoot =
        {
            PilotMove.Idle, PilotMove.LookAround, PilotMove.Stretch, PilotMove.CheckWatch, PilotMove.Wave, PilotMove.Land,
            PilotMove.Risers,
        };

        static AnimatorController Controller()
        {
            var c = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.IsNotNull(c, $"{ControllerPath} is missing: run Winging It/Build Pilot Prefabs");
            return c;
        }

        static AnimatorState State(PilotMove move) =>
            Controller().layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == move.ToString());

        static AnimationClip Clip(PilotMove move) => (AnimationClip)State(move).motion;

        [Test]
        public void EveryMove_HasAState_IdleFirst()
        {
            foreach (PilotMove move in System.Enum.GetValues(typeof(PilotMove)))
            {
                var state = State(move);
                Assert.IsNotNull(state, move.ToString());
                Assert.IsNotNull(state.motion as AnimationClip, move.ToString());
                Assert.AreEqual(Loops.Contains(move), Clip(move).isLooping, $"{move} loops?");
            }
            Assert.AreEqual("Idle", Controller().layers[0].stateMachine.defaultState.name);
        }

        [Test]
        public void OneShots_HandOnByThemselves()
        {
            void Next(PilotMove move, PilotMove then)
            {
                var t = State(move).transitions.Single();
                Assert.AreEqual(then.ToString(), t.destinationState.name, move.ToString());
                Assert.IsTrue(t.hasExitTime, move.ToString());
            }

            foreach (var fidget in PilotIdle.Fidgets)
                Next(fidget, PilotMove.Idle);
            Next(PilotMove.Deploy, PilotMove.Hang);
            Next(PilotMove.Land, PilotMove.Idle);
            foreach (var loop in Loops)
                Assert.IsEmpty(State(loop).transitions, $"{loop} plays until told otherwise");
        }

        /// <summary>Runs <paramref name="check"/> on a pilot posed every 0.1 s through <paramref name="move"/>.</summary>
        static void Through(PilotMove move, System.Action<PilotFigure, float> check)
        {
            var figure = Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>(PilotPath)).GetComponent<PilotFigure>();
            try
            {
                var clip = Clip(move);
                for (float t = 0f; t <= clip.length + 1e-4f; t += 0.1f)
                {
                    clip.SampleAnimation(figure.gameObject, t);
                    check(figure, t);
                }
            }
            finally
            {
                Object.DestroyImmediate(figure.gameObject);
            }
        }

        static float Lowest(PilotFigure figure, PilotGearSlot slot) =>
            figure.Pieces(slot, figure.Wearing(slot)).Min(r => r.bounds.min.y);

        [Test]
        public void OnFoot_TheBootsStayOnTheGround()
        {
            foreach (var move in OnFoot)
                Through(move, (figure, t) =>
                    Assert.AreEqual(0f, Lowest(figure, PilotGearSlot.Boots), 0.05f, $"{move} at {t:0.0} s: a sole on the ground"));
        }

        [Test]
        public void Seated_TheThighsRestOnTheSeat_FeetForward()
        {
            Through(PilotMove.Sit, (figure, t) =>
            {
                float seat = figure.SeatOffset.y;
                float under = figure.Pieces(PilotGearSlot.Suit, figure.Wearing(PilotGearSlot.Suit))
                    .Where(r => r.name.EndsWith("Hips") || r.name.Contains("Thigh")).Min(r => r.bounds.min.y);
                Assert.AreEqual(seat, under, 0.04f, $"at {t:0.0} s: seat and thighs on the seat pan");
                Assert.Greater(figure.Joint(PilotJoint.FootL).position.z, 0.3f, "feet out on the pedals");
                Assert.Less(figure.Joint(PilotJoint.FootR).position.y, seat - 0.2f, "feet well below the seat");
            });
        }

        [Test]
        public void UnderTheCanopy_TheHandsHoldTheRisers()
        {
            // The risers run from the harness on each shoulder up and out to the lines (build_pilot.py riser_tops); the
            // hands hold them about 0.45 m above the harness.
            foreach (var move in new[] { PilotMove.Hang, PilotMove.Risers })
                Through(move, (figure, t) =>
                {
                    var attach = figure.RiserAttach.position;
                    foreach (var (hand, side) in new[] { (figure.HandLeft, -1f), (figure.HandRight, 1f) })
                    {
                        var grip = attach + new Vector3(side * 0.24f, 0.4f, 0f); // where the wrist sits holding on
                        Assert.Less(Vector3.Distance(hand.position, grip), 0.2f, $"{move} at {t:0.0} s: {hand.name} on its riser");
                    }
                });
        }

        [Test]
        public void Freefall_SpreadsTheArmsOut()
        {
            Through(PilotMove.Freefall, (figure, t) =>
            {
                Assert.Less(figure.HandLeft.position.x, -0.5f, $"at {t:0.0} s");
                Assert.Greater(figure.HandRight.position.x, 0.5f, $"at {t:0.0} s");
            });
        }
    }
}

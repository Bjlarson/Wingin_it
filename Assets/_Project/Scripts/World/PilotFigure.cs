using System;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>The figure's joints, root to tips (ArtSource/Pilot/build_pilot.py JOINTS, same names and order).</summary>
    public enum PilotJoint
    {
        Hips, Chest, Head,
        UpperArmL, ForearmL, HandL,
        UpperArmR, ForearmR, HandR,
        ThighL, ShinL, FootL,
        ThighR, ShinR, FootR,
    }

    /// <summary>The pilot's moves (build_pilot.py MOVES, same names and order): one Animator state each.</summary>
    public enum PilotMove
    {
        /// <summary>Standing, breathing (loops).</summary>
        Idle,
        /// <summary>A glance left and right, then back to <see cref="Idle"/>.</summary>
        LookAround,
        /// <summary>Arms up overhead, then back to <see cref="Idle"/>.</summary>
        Stretch,
        /// <summary>A look at the watch on the left wrist, then back to <see cref="Idle"/>.</summary>
        CheckWatch,
        /// <summary>A wave with the right hand, then back to <see cref="Idle"/>.</summary>
        Wave,
        /// <summary>Seated in a cockpit, hands on the stick and throttle (loops); see <see cref="PilotFigure.SeatOffset"/>.</summary>
        Sit,
        /// <summary>Spread out, tumbling a little, before the canopy opens (loops).</summary>
        Freefall,
        /// <summary>The canopy's jolt: arms up to the risers, then <see cref="Hang"/>.</summary>
        Deploy,
        /// <summary>Under the canopy, hands on the risers, legs swinging (loops).</summary>
        Hang,
        /// <summary>Feet down, a deep knee bend, standing up: then <see cref="Idle"/>.</summary>
        Land,
        /// <summary>Standing, hands up on the risers of a canopy overhead (loops; the menu's PILOT screen).</summary>
        Risers,
    }

    /// <summary>The gear a pilot wears, each swappable on its own.</summary>
    public enum PilotGearSlot
    {
        Suit,
        Helmet,
        Backpack,
        Gloves,
        Boots,
    }

    /// <summary>
    /// The variants each <see cref="PilotGearSlot"/> comes in (build_pilot.py GEAR; the model has a
    /// <c>&lt;Slot&gt;_&lt;Variant&gt;_&lt;Joint&gt;</c> piece per joint a variant covers). The first is the default.
    /// Ids are the save format: never rename one.
    /// </summary>
    public static class PilotGear
    {
        static readonly string[][] s_variants =
        {
            new[] { "Flight", "Coverall" },
            new[] { "Jet", "Leather" },
            new[] { "Oxygen", "Chute" },
            new[] { "Flight", "Leather" },
            new[] { "Combat", "Leather" },
        };

        public static readonly PilotGearSlot[] Slots = (PilotGearSlot[])Enum.GetValues(typeof(PilotGearSlot));

        public static string[] Variants(PilotGearSlot slot) => s_variants[(int)slot];

        public static string Default(PilotGearSlot slot) => s_variants[(int)slot][0];

        public static bool IsKnown(PilotGearSlot slot, string variant) => Array.IndexOf(s_variants[(int)slot], variant) >= 0;

        /// <summary>The next (<paramref name="dir"/> +1) or previous (-1) variant, wrapping round.</summary>
        public static string Step(PilotGearSlot slot, string variant, int dir)
        {
            var all = s_variants[(int)slot];
            int n = all.Length;
            return all[((Mathf.Max(0, Array.IndexOf(all, variant)) + dir) % n + n) % n];
        }
    }

    /// <summary>
    /// The low-poly pilot (ArtSource/Pilot/build_pilot.py, prefab Prefabs/Pilot/Pilot.prefab): rigid pieces on a rig of
    /// <see cref="PilotJoint"/>s, animated by an Animator with one state per <see cref="PilotMove"/>. Every
    /// <see cref="PilotGearSlot"/> has its variants' pieces on the rig and shows one of them, so gear swaps without
    /// touching the rig or the moves. The suit's cloth and the helmet's shell are on the plane team material, so each
    /// takes its own colour the way a plane takes its team's.
    /// </summary>
    public class PilotFigure : MonoBehaviour
    {
        /// <summary>One variant of one slot: the pieces it puts on the rig.</summary>
        [Serializable]
        public class GearPieces
        {
            public PilotGearSlot slot;
            public string variant;
            public Renderer[] pieces;
        }

        [Tooltip("The plane team material (Plane_Team) the suit and helmet are painted in.")]
        [SerializeField] Material teamMaterial;
        [Tooltip("One per PilotJoint, in its order.")]
        [SerializeField] Transform[] joints;
        [Tooltip("Every variant of every slot.")]
        [SerializeField] GearPieces[] gear;
        [Tooltip("The harness point between the shoulders a parachute's risers hook to (on the chest).")]
        [SerializeField] Transform riserAttach;
        [Tooltip("Where a seat pan sits in the figure's space when it plays Sit: put the figure at the seat minus this.")]
        [SerializeField] Vector3 seatOffset;
        [SerializeField] Animator animator;

        static readonly int[] s_moveHashes = Array.ConvertAll((PilotMove[])Enum.GetValues(typeof(PilotMove)),
            m => Animator.StringToHash(m.ToString()));

        readonly string[] _wearing = new string[PilotGear.Slots.Length];
        PilotMove _requested;

        public Transform Joint(PilotJoint joint) => joints != null && (int)joint < joints.Length ? joints[(int)joint] : null;
        public Transform Hips => Joint(PilotJoint.Hips);
        public Transform Chest => Joint(PilotJoint.Chest);
        public Transform Head => Joint(PilotJoint.Head);
        public Transform HandLeft => Joint(PilotJoint.HandL);
        public Transform HandRight => Joint(PilotJoint.HandR);
        public Transform RiserAttach => riserAttach;
        public Vector3 SeatOffset => seatOffset;
        public Animator Animator => animator;

        /// <summary>
        /// The move showing: the Animator's state once it's running (a one-shot hands on to Idle or Hang by itself),
        /// else the last one asked for.
        /// </summary>
        public PilotMove Current
        {
            get
            {
                if (animator != null && animator.isActiveAndEnabled && animator.runtimeAnimatorController != null)
                {
                    var state = animator.IsInTransition(0) ? animator.GetNextAnimatorStateInfo(0) : animator.GetCurrentAnimatorStateInfo(0);
                    int i = Array.IndexOf(s_moveHashes, state.shortNameHash);
                    if (i >= 0)
                        return (PilotMove)i;
                }
                return _requested;
            }
        }

        public void Configure(Material team, Transform[] joints, GearPieces[] gear, Transform riserAttach, Vector3 seatOffset,
            Animator animator)
        {
            teamMaterial = team;
            this.joints = joints;
            this.gear = gear;
            this.riserAttach = riserAttach;
            this.seatOffset = seatOffset;
            this.animator = animator;
            foreach (var slot in PilotGear.Slots)
                SetGear(slot, PilotGear.Default(slot));
        }

        /// <summary>Cross-fades to <paramref name="move"/> over <paramref name="fade"/> seconds (0: cuts straight to it).</summary>
        public void Play(PilotMove move, float fade = 0.25f)
        {
            _requested = move;
            if (animator == null || !animator.isActiveAndEnabled || animator.runtimeAnimatorController == null)
                return;
            if (fade <= 0f)
                animator.Play(s_moveHashes[(int)move], 0, 0f);
            else
                animator.CrossFadeInFixedTime(s_moveHashes[(int)move], fade, 0);
        }

        /// <summary>The variants this figure has for <paramref name="slot"/>, as built.</summary>
        public string[] Variants(PilotGearSlot slot)
        {
            var list = new System.Collections.Generic.List<string>();
            if (gear != null)
                foreach (var g in gear)
                    if (g.slot == slot)
                        list.Add(g.variant);
            return list.ToArray();
        }

        /// <summary>The variant showing in <paramref name="slot"/>.</summary>
        public string Wearing(PilotGearSlot slot) => _wearing[(int)slot] ?? PilotGear.Default(slot);

        /// <summary>
        /// Shows <paramref name="variant"/>'s pieces in <paramref name="slot"/> and hides the others; false (and nothing
        /// changes) when the figure has no such variant.
        /// </summary>
        public bool SetGear(PilotGearSlot slot, string variant)
        {
            if (gear == null || Array.FindIndex(gear, g => g.slot == slot && g.variant == variant) < 0)
                return false;
            foreach (var g in gear)
                if (g.slot == slot && g.pieces != null)
                    foreach (var r in g.pieces)
                        if (r != null)
                            r.gameObject.SetActive(g.variant == variant);
            _wearing[(int)slot] = variant;
            return true;
        }

        /// <summary>The renderers of <paramref name="slot"/>'s <paramref name="variant"/> (empty if there's none).</summary>
        public Renderer[] Pieces(PilotGearSlot slot, string variant)
        {
            if (gear != null)
                foreach (var g in gear)
                    if (g.slot == slot && g.variant == variant)
                        return g.pieces ?? Array.Empty<Renderer>();
            return Array.Empty<Renderer>();
        }

        /// <summary>Paints the flight suit and the helmet (every variant, so a swap keeps the colours).</summary>
        public void SetColors(Color suit, Color helmet)
        {
            if (gear == null)
                return;
            foreach (var g in gear)
            {
                if (g.slot != PilotGearSlot.Suit && g.slot != PilotGearSlot.Helmet)
                    continue;
                foreach (var r in g.pieces)
                    Tint(r, teamMaterial, g.slot == PilotGearSlot.Suit ? suit : helmet);
            }
        }

        /// <summary>
        /// Swaps <paramref name="team"/> on <paramref name="piece"/>'s renderer for the shared copy tinted
        /// <paramref name="color"/> (<see cref="PlaneVisuals.TeamMaterial"/>), so pilots and canopies stay batched.
        /// </summary>
        public static void Tint(Transform piece, Material team, Color color)
        {
            if (piece != null && piece.TryGetComponent<Renderer>(out var r))
                Tint(r, team, color);
        }

        static void Tint(Renderer r, Material team, Color color)
        {
            if (r == null || team == null)
                return;
            var tinted = PlaneVisuals.TeamMaterial(team, color);
            var mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)  // tinted copies keep the source's name, so a repaint finds them too
                if (mats[i] == team || (mats[i] != null && mats[i].name == team.name))
                    mats[i] = tinted;
            r.sharedMaterials = mats;
        }
    }
}

using System;
using UnityEngine;
using WingingIt.World;

namespace WingingIt.Maps
{
    /// <summary>A mesh and its materials, for a piece the mode builder puts together at runtime.</summary>
    [Serializable]
    public class KitMesh
    {
        public Mesh mesh;
        public Material[] materials;

        public KitMesh(Mesh mesh, Material[] materials)
        {
            this.mesh = mesh;
            this.materials = materials;
        }

        public bool IsSet => mesh != null;
    }

    /// <summary>
    /// The assets <see cref="ModePieceBuilder"/> spawns mode pieces from: the airfield prefab, Domination's light AA,
    /// the lane gate pylon (or its greybox) and the banner stand's materials. Written by the editor
    /// (<c>ModePieceKitBuilder</c>) so the runtime never needs the editor's model and material loaders.
    /// </summary>
    [CreateAssetMenu(menuName = "Winging It/Mode Piece Kit", fileName = "ModePieceKit")]
    public class ModePieceKit : ScriptableObject
    {
        [Header("Airfields")]
        public GameObject airfieldPrefab;
        [Tooltip("Capture fields' light AA: shorter reach, slower and less accurate than a home field's guns.")]
        public AAStats lightAA;

        [Header("Approach lanes")]
        [Tooltip("The markers pack's pylon footing, marker top and stretched pole; unset = greybox pole and sphere.")]
        public KitMesh pylonBase;
        public KitMesh pylonTop;
        public KitMesh pylonPole;
        [Tooltip("The pylon's glow, copied per lane and tinted the lane's colour (replaces the top's Weapon_Glow).")]
        public Material laneGlow;
        public Material greyboxPole;
        [Tooltip("Greybox gate marker, copied per lane in its colour.")]
        public Material greyboxLane;

        [Header("Banner stands")]
        public Material bannerPole;
        [Tooltip("Banner cloth and pole caps, by team.")]
        public Material[] bannerCloth = { };

        public bool HasPylon => pylonBase != null && pylonBase.IsSet && pylonTop != null && pylonTop.IsSet && pylonPole != null && pylonPole.IsSet;
    }
}

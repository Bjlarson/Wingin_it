using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.World
{
    /// <summary>
    /// Paints an airfield's team zones (hangar roof and doors, the tower's bands, the hangar's flag) in its owner's colour,
    /// the same way <see cref="PlaneVisuals"/> tints a plane: per-material property blocks on every slot that uses the
    /// shared team material. Follows captures, and the player's team-colour setting. A neutral field is light grey.
    /// </summary>
    [RequireComponent(typeof(Airfield))]
    public class AirfieldColors : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("The plane team material (Plane_Team) the airfield's models use for their team zones.")]
        [SerializeField] Material teamMaterial;
        [SerializeField] Color neutralColor = new(0.82f, 0.83f, 0.85f);
        [Tooltip("Used when no match supplies team colours (PlaneVisuals.TeamColors).")]
        [SerializeField] Color[] fallbackTeamColors = { new(0.35f, 0.6f, 1f), new(1f, 0.38f, 0.32f) };

        readonly List<(Renderer renderer, int index)> _slots = new();
        Airfield _airfield;
        Color _applied;
        bool _tinted;

        public void Configure(Material team) => teamMaterial = team;

        void Awake()
        {
            _airfield = GetComponent<Airfield>();
            if (teamMaterial == null)
                return;
            foreach (var r in GetComponentsInChildren<Renderer>(true))
            {
                var mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] == teamMaterial)
                        _slots.Add((r, i));
            }
        }

        void LateUpdate()
        {
            if (_slots.Count == 0)
                return;
            int team = _airfield.Team;
            Color color = team == Airfield.Neutral ? neutralColor
                : PlaneVisuals.TeamColors != null ? PlaneVisuals.TeamColors(team)
                : fallbackTeamColors.Length > 0 ? fallbackTeamColors[Mathf.Clamp(team, 0, fallbackTeamColors.Length - 1)]
                : neutralColor;
            if (_tinted && color == _applied)
                return;
            _applied = color;
            _tinted = true;

            var tint = PlaneVisuals.TeamTint(color);
            var block = new MaterialPropertyBlock();
            foreach (var (r, index) in _slots)
            {
                if (r == null)
                    continue;
                r.GetPropertyBlock(block, index);
                block.SetVector(BaseColorId, tint);
                r.SetPropertyBlock(block, index);
            }
        }
    }
}

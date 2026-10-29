using UnityEngine;

namespace WingingIt.World
{
    /// <summary>
    /// The pilot's parachute (Prefabs/Pilot/Parachute.prefab): the lines and risers, and one canopy per
    /// <see cref="CanopyPattern"/> under <see cref="Canopy"/>, only the chosen one showing. Its origin is where the risers
    /// meet, so it hangs from a <see cref="PilotFigure.RiserAttach"/>. Each canopy's main gores are on the plane team
    /// material and its trim gores on the trim material, so the two colours tint apart.
    /// </summary>
    public class PilotParachute : MonoBehaviour
    {
        [Tooltip("The plane team material (Plane_Team) the main gores are painted in.")]
        [SerializeField] Material teamMaterial;
        [Tooltip("The trim material (Plane_Trim) the other gores are painted in.")]
        [SerializeField] Material trimMaterial;
        [Tooltip("Holds the canopies, one per pattern.")]
        [SerializeField] Transform canopy;
        [Tooltip("One canopy per CanopyPattern, in its order.")]
        [SerializeField] Transform[] patterns;

        CanopyPattern _pattern;

        /// <summary>The canopies' parent, on the origin.</summary>
        public Transform Canopy => canopy;
        public CanopyPattern Pattern => _pattern;

        /// <summary>The canopy model for <paramref name="pattern"/>, or null.</summary>
        public Transform PatternPiece(CanopyPattern pattern) =>
            patterns != null && (int)pattern < patterns.Length ? patterns[(int)pattern] : null;

        public void Configure(Material team, Material trim, Transform canopy, Transform[] patterns)
        {
            teamMaterial = team;
            trimMaterial = trim;
            this.canopy = canopy;
            this.patterns = patterns;
            SetPattern(CanopyPattern.Stripes);
        }

        /// <summary>Shows <paramref name="pattern"/>'s canopy and hides the others.</summary>
        public void SetPattern(CanopyPattern pattern)
        {
            _pattern = pattern;
            if (patterns == null)
                return;
            for (int i = 0; i < patterns.Length; i++)
                if (patterns[i] != null)
                    patterns[i].gameObject.SetActive(i == (int)pattern);
        }

        /// <summary>Paints every pattern's main gores <paramref name="main"/> and its trim gores <paramref name="trim"/>.</summary>
        public void SetColors(Color main, Color trim)
        {
            if (patterns == null)
                return;
            foreach (var piece in patterns)
            {
                PilotFigure.Tint(piece, teamMaterial, main);
                PilotFigure.Tint(piece, trimMaterial, trim);
            }
        }

        /// <summary>Paints the main gores (the trim stays white): a bot's canopy.</summary>
        public void SetColor(Color color) => SetColors(color, Color.white);

        /// <summary>
        /// Opens the canopy to <paramref name="open"/> (0 packed, 1 full) at <paramref name="time"/> seconds: the lines
        /// pay out first, then the canopy fills with a little overshoot, and once full it breathes in the air.
        /// </summary>
        public void SetOpen(float open, float time) => transform.localScale = OpenScale(open, time);

        /// <summary>The parachute's scale at <paramref name="open"/> (0..1) and <paramref name="time"/> (see <see cref="SetOpen"/>).</summary>
        public static Vector3 OpenScale(float open, float time)
        {
            open = Mathf.Clamp01(open);
            float lines = Mathf.Clamp01(open / 0.45f);
            lines = lines * lines * (3f - 2f * lines);
            float fill = Mathf.Clamp01((open - 0.25f) / 0.75f);
            const float back = 1.6f; // ease-out-back: about 10% past full, then settling
            float f1 = fill - 1f;
            float filled = 1f + (back + 1f) * f1 * f1 * f1 + back * f1 * f1;
            float breath = Mathf.Sin(time * 2.3f) * 0.03f * fill;
            float width = Mathf.Lerp(0.08f, 1f, filled) * (1f + breath);
            float height = Mathf.Lerp(0.35f, 1f, lines) * (1f - breath * 0.6f);
            return new Vector3(width, height, width);
        }
    }
}

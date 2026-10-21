using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Match
{
    /// <summary>
    /// Free-for-all and one-life helpers (see Design/01-Game-Design/match-modes.md#shared-mode-framework-prototype):
    /// each pilot's colour when every pilot is its own team, and which survivor a spectator watches. Pure, so the rules
    /// are EditMode-testable.
    /// </summary>
    public static class FreeForAll
    {
        /// <summary>
        /// Sixteen pilot colours, far apart in hue and brightness. Slot 0 is the player's (the usual West blue) and slot 1
        /// the usual East red, so a two-pilot match looks like Teams.
        /// </summary>
        static readonly Color[] s_colors =
        {
            new(0.35f, 0.6f, 1f), new(1f, 0.38f, 0.32f), new(1f, 0.82f, 0.25f), new(0.4f, 0.9f, 0.45f),
            new(0.75f, 0.45f, 1f), new(1f, 0.6f, 0.2f), new(0.3f, 0.9f, 0.9f), new(1f, 0.45f, 0.75f),
            new(0.7f, 0.85f, 0.3f), new(0.95f, 0.95f, 0.95f), new(0.55f, 0.35f, 0.25f), new(0.2f, 0.45f, 0.75f),
            new(0.8f, 0.2f, 0.45f), new(0.25f, 0.6f, 0.4f), new(0.95f, 0.75f, 0.6f), new(0.5f, 0.5f, 0.55f),
        };

        public static int ColorCount => s_colors.Length;

        /// <summary>The colour of the pilot on <paramref name="team"/> (wraps past 16).</summary>
        public static Color PilotColor(int team) => s_colors[((team % s_colors.Length) + s_colors.Length) % s_colors.Length];

        /// <summary>
        /// The survivor a new spectator watches first: the one nearest <paramref name="from"/> (where the spectator went
        /// down), or -1 if nobody's left.
        /// </summary>
        public static int NearestSurvivor(IReadOnlyList<Vector3> survivors, Vector3 from)
        {
            int best = -1;
            float bestSqr = float.MaxValue;
            for (int i = 0; i < survivors.Count; i++)
            {
                float sqr = (survivors[i] - from).sqrMagnitude;
                if (sqr < bestSqr)
                    (best, bestSqr) = (i, sqr);
            }
            return best;
        }

        /// <summary>
        /// The next (<paramref name="dir"/> = 1) or previous (-1) of <paramref name="count"/> survivors after
        /// <paramref name="current"/>, wrapping round. A spectator watching nobody (-1) starts at the first, or the last
        /// going back. -1 with nobody left.
        /// </summary>
        public static int Cycle(int current, int count, int dir)
        {
            if (count <= 0)
                return -1;
            if (current < 0 || current >= count)
                return dir < 0 ? count - 1 : 0;
            return ((current + (dir < 0 ? -1 : 1)) % count + count) % count;
        }
    }
}

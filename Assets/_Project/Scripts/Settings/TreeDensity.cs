using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Settings
{
    public enum TreeDetail
    {
        /// <summary>One tree in three.</summary>
        Low,
        /// <summary>Two trees in three.</summary>
        Medium,
        /// <summary>Every tree.</summary>
        High,
    }

    /// <summary>
    /// What each Trees setting keeps of a terrain's trees (and the props it draws as trees). The terrain spends its time
    /// on every tree in range, one by one, so the count is the cost. Each kind is thinned on its own (every third oak,
    /// every third pine), which keeps the mix and spreads the cut over the map instead of clearing a corner. Kinds with
    /// fewer than <see cref="RareCount"/> on a terrain (the Alpine snowmen, the odd palm) are always kept: too few to
    /// cost anything, and missed when gone. Pure, so it's EditMode-testable; <see cref="TerrainScenery"/> applies it.
    /// </summary>
    public static class TreeDensity
    {
        public const int RareCount = 50;

        /// <summary>How many of every three trees of a kind are drawn.</summary>
        public static int KeptOfThree(TreeDetail detail) => detail switch
        {
            TreeDetail.Low => 1,
            TreeDetail.Medium => 2,
            _ => 3,
        };

        /// <summary><paramref name="trees"/> thinned for <paramref name="detail"/>, in their original order (the same array on High).</summary>
        public static TreeInstance[] Thin(TreeInstance[] trees, int prototypeCount, TreeDetail detail)
        {
            int keep = KeptOfThree(detail);
            if (keep >= 3)
                return trees;
            var counts = new int[prototypeCount];
            foreach (var tree in trees)
                if (tree.prototypeIndex >= 0 && tree.prototypeIndex < prototypeCount)
                    counts[tree.prototypeIndex]++;
            var seen = new int[prototypeCount];
            var kept = new List<TreeInstance>(trees.Length * keep / 3 + RareCount);
            foreach (var tree in trees)
            {
                int kind = tree.prototypeIndex;
                if (kind < 0 || kind >= prototypeCount || counts[kind] < RareCount || seen[kind]++ % 3 < keep)
                    kept.Add(tree);
            }
            return kept.ToArray();
        }
    }
}

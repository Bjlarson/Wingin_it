using System;
using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>
    /// How a map's ground is painted and planted from its shape alone: height above the sea and slope, no hand-painted
    /// splat. The defaults reproduce the Island's mountains; a map overrides what it needs. Pure, so EditMode-tested.
    /// </summary>
    [Serializable]
    public struct TerrainRules
    {
        [Tooltip("Sand (and the seabed) up to this height above the sea, fading out over beachFade.")]
        public float beachHeight;
        public float beachFade;
        [Tooltip("Slope in degrees where grass turns to rock (from, to).")]
        public Vector2 rockSlope;
        [Tooltip("Height above the sea where grass turns to rock (from, to).")]
        public Vector2 rockHeight;
        [Tooltip("Height above the sea where rock turns to snow (from, to). Steep faces stay bare.")]
        public Vector2 snowHeight;
        [Tooltip("Slope in degrees where snow stops settling (from, to).")]
        public Vector2 snowSlope;
        [Tooltip("Trees stop growing above this height above the sea.")]
        public float treeLine;
        [Tooltip("Pines instead of oaks above this height above the sea.")]
        public float pineHeight;
        [Tooltip("Meters between jittered tree cells; each grows at most one.")]
        public float treeSpacing;

        public static TerrainRules Default => new()
        {
            beachHeight = 6f,
            beachFade = 6f,
            rockSlope = new Vector2(22f, 34f),
            rockHeight = new Vector2(140f, 220f),
            snowHeight = new Vector2(380f, 440f),
            snowSlope = new Vector2(40f, 55f),
            treeLine = 430f,
            pineHeight = 50f,
            treeSpacing = 24f,
        };

        public static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        /// <summary>Splat weights at a spot (they sum to 1): grass, sand, rock, snow. <paramref name="aboveSea"/> in meters.</summary>
        public Vector4 Layers(float aboveSea, float slopeDegrees)
        {
            float snow = Smooth(snowHeight.x, snowHeight.y, aboveSea) * (1f - Smooth(snowSlope.x, snowSlope.y, slopeDegrees));
            float rock = Mathf.Max(Smooth(rockSlope.x, rockSlope.y, slopeDegrees), Smooth(rockHeight.x, rockHeight.y, aboveSea)) * (1f - snow);
            float rest = 1f - snow - rock;
            float sandMask = Smooth(beachHeight + beachFade, beachHeight, aboveSea);
            float sand = rest * sandMask;
            return new Vector4(rest - sand, sand, rock, snow);
        }

        /// <summary>What grows at a spot (a nature-pack prop name and its scale range), or null for nothing.</summary>
        /// <param name="roll">A random 0..1 for this cell.</param>
        /// <param name="grove">Low-frequency noise 0..1: clumps trees into woods with clearings between.</param>
        public (string kind, float minScale, float maxScale)? PickTree(float aboveSea, float slopeDegrees, float roll, float grove)
        {
            if (aboveSea < 1.5f)
                return null;
            if (slopeDegrees > 58f) // the steepest faces: boulders and the odd crag
                return roll < 0.12f ? ("Rock", 2f, 3.5f) : roll < 0.18f ? ("CliffRock", 1.2f, 1.8f) : null;
            if (aboveSea > treeLine)
                return roll < 0.1f ? ("RockSnow", 1.8f, 3.5f) : null;
            if (aboveSea > snowHeight.x - 50f)
                return roll < 0.4f ? ("TreePineSnow", 1.5f, 2.1f) : roll < 0.46f ? ("RockSnow", 1.8f, 3f) : null;
            if (aboveSea < beachHeight + beachFade && aboveSea < 10f) // the low strip along the coast
                return roll < 0.07f ? ("TreePalm", 1.5f, 2.1f) : roll < 0.12f ? ("TreePalmBent", 1.5f, 2.1f)
                    : roll < 0.15f ? ("Driftwood", 1.5f, 2.2f) : roll < 0.18f ? ("BeachRocks", 1.5f, 2.6f) : null;
            if (aboveSea > pineHeight)
            {
                float forest = 0.45f + 0.45f * Smooth(0.42f, 0.6f, grove);
                if (roll < forest)
                    return roll < forest * 0.6f ? ("TreePine", 1.5f, 2.2f) : ("TreePineTall", 1.5f, 2.2f);
                return roll < forest + 0.04f ? ("Rock", 1.6f, 3f) : roll < forest + 0.06f ? ("RockFlat", 1.6f, 2.6f) : null;
            }

            // Grassland: woods and copses, bushes at their edges, the odd boulder in the open.
            float wood = 0.75f * Smooth(0.47f, 0.62f, grove) + 0.03f;
            if (roll < wood)
                return roll < wood * 0.6f ? ("TreeOak", 1.5f, 2.2f) : ("TreeOakTall", 1.5f, 2.1f);
            return roll < wood + 0.05f ? ("Bush", 1.5f, 2.5f) : roll < wood + 0.065f ? ("Rock", 1.4f, 2.6f)
                : roll < wood + 0.07f ? ("RockFlat", 1.4f, 2.5f) : null;
        }

        /// <summary>Grass tufts (0 none, 1 sparse, 2 thick) and whether flowers grow with them, on open low ground.</summary>
        public (int grass, bool flowers) Detail(float aboveSea, float slopeDegrees, float meadow, float roll)
        {
            if (aboveSea < 2f || aboveSea > 250f || slopeDegrees > 25f || aboveSea < beachHeight)
                return (0, false);
            int grass = meadow > 0.45f ? 2 : roll < 0.5f ? 1 : 0;
            return (grass, meadow > 0.55f && roll < 0.25f);
        }
    }
}

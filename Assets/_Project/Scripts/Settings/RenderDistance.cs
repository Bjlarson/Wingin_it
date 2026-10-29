namespace WingingIt.Settings
{
    public enum RenderDistance
    {
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// What each Render Distance setting means, as shares of what the maps are built with: how far the terrain draws
    /// trees and grass, and the LOD bias, which moves every LOD switch at once (trees to their billboards and out,
    /// airfield props and vehicles out). High is the maps as built. Pure data, so it's EditMode-testable.
    /// </summary>
    public readonly struct RenderDistanceProfile
    {
        /// <summary>Share of each terrain's built tree distance.</summary>
        public readonly float Trees;
        /// <summary>Share of each terrain's built grass and flower distance.</summary>
        public readonly float Details;
        /// <summary>Share of the quality level's LOD bias.</summary>
        public readonly float LodBias;

        RenderDistanceProfile(float trees, float details, float lodBias)
        {
            Trees = trees;
            Details = details;
            LodBias = lodBias;
        }

        public static RenderDistanceProfile For(RenderDistance distance) => distance switch
        {
            RenderDistance.Low => new RenderDistanceProfile(0.5f, 0.6f, 0.5f),
            RenderDistance.Medium => new RenderDistanceProfile(0.75f, 0.8f, 0.75f),
            _ => new RenderDistanceProfile(1f, 1f, 1f),
        };
    }
}

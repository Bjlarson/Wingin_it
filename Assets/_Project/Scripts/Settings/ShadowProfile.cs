using UnityEngine;

namespace WingingIt.Settings
{
    public enum ShadowDetail
    {
        /// <summary>No sun shadows: planes keep only their altitude blob.</summary>
        Off,
        Low,
        Medium,
        High,
    }

    /// <summary>
    /// What each Shadows setting means for the sun's shadow map. Edge softness comes from the soft-shadow filter, which
    /// is a fixed number of shadow-map texels wide, so the near cascades are kept generous on purpose: the edges stay a
    /// little soft instead of razor sharp. Pure data, so it's EditMode-testable.
    /// </summary>
    public readonly struct ShadowProfile
    {
        public readonly bool Enabled;
        public readonly int Resolution;
        public readonly float Distance;
        public readonly int Cascades;
        /// <summary>Cascade ends as fractions of <see cref="Distance"/> (x for 2 cascades, x/y/z for 4).</summary>
        public readonly Vector3 Splits;
        /// <summary>1 low, 2 medium, 3 high (UnityEngine.Rendering.Universal.SoftShadowQuality).</summary>
        public readonly int SoftQuality;

        ShadowProfile(bool enabled, int resolution, float distance, int cascades, Vector3 splits, int softQuality)
        {
            Enabled = enabled;
            Resolution = resolution;
            Distance = distance;
            Cascades = cascades;
            Splits = splits;
            SoftQuality = softQuality;
        }

        public static ShadowProfile For(ShadowDetail quality) => quality switch
        {
            ShadowDetail.Off => new ShadowProfile(false, 1024, 0f, 1, Vector3.zero, 1),
            ShadowDetail.Low => new ShadowProfile(true, 1024, 60f, 1, Vector3.zero, 1),
            ShadowDetail.Medium => new ShadowProfile(true, 2048, 100f, 2, new Vector3(0.25f, 0f, 0f), 2),
            _ => new ShadowProfile(true, 2048, 150f, 4, new Vector3(0.12f, 0.3f, 0.55f), 3),
        };
    }
}

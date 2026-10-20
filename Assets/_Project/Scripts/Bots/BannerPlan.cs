using UnityEngine;
using WingingIt.Match;

namespace WingingIt.Bots
{
    /// <summary>A bot's part in Capture the Banner (Design/02-Mechanics/bots.md#capture-the-banner).</summary>
    public enum BannerRole
    {
        /// <summary>Normal bot: patrols and fights. Every bot outside a Banner match.</summary>
        None,
        /// <summary>Goes for the enemy banner and tows it home.</summary>
        Runner,
        /// <summary>Flies with the team's carrier and fights whatever comes for it.</summary>
        Escort,
        /// <summary>Guards its own stand; chases the enemy carrier or returns a dropped banner.</summary>
        Defender,
    }

    /// <summary>What a bot is doing about the banners right now.</summary>
    public enum BannerDuty
    {
        /// <summary>Nothing banner-related: patrol and fight as usual.</summary>
        None,
        /// <summary>Fly low through the enemy banner's zone (its stand, or where it lies dropped).</summary>
        Grab,
        /// <summary>Towing a banner: fly low home through our own stand.</summary>
        Deliver,
        /// <summary>Fly low through our dropped banner to send it home.</summary>
        ReturnDropped,
        /// <summary>Hunt the enemy carrier towing our banner.</summary>
        ChaseCarrier,
        /// <summary>Hold formation on our own carrier.</summary>
        Escort,
        /// <summary>Circle our own stand, fighting what comes near.</summary>
        Guard,
    }

    /// <summary>The pure decisions behind the banner bots, so they can be tested without a scene.</summary>
    public static class BannerPlan
    {
        /// <summary>Height above the ground (m) of a grab or delivery run: inside the 40 m zone, clear of the ground.</summary>
        public const float RunHeight = 25f;
        /// <summary>Sideways offset (m) of the run line from the pole, inside the 20 m zone and clear of the pole and its base.</summary>
        public const float PoleOffset = 12f;
        /// <summary>A guarding bot only takes on enemies this close (m) to its stand.</summary>
        public const float GuardLeash = 1500f;
        /// <summary>A guarding bot circles its stand at this radius (m).</summary>
        public const float GuardRadius = 450f;

        static readonly BannerRole[] s_roles = { BannerRole.Runner, BannerRole.Defender, BannerRole.Escort, BannerRole.Escort };

        /// <summary>The role of the <paramref name="slot"/>th bot on a team: 2 runners, 2 defenders, 3 escorts in 7.</summary>
        public static BannerRole RoleForSlot(int slot) => s_roles[((slot % s_roles.Length) + s_roles.Length) % s_roles.Length];

        /// <summary>
        /// What a bot with <paramref name="role"/> should do. <paramref name="allyCarries"/> is true if a teammate is
        /// towing the enemy banner.
        /// </summary>
        public static BannerDuty Decide(BannerRole role, bool carrying, BannerState ownBanner, bool allyCarries)
        {
            if (carrying)
                return BannerDuty.Deliver;
            switch (role)
            {
                case BannerRole.Runner:
                    return allyCarries ? BannerDuty.Escort : BannerDuty.Grab;
                case BannerRole.Escort:
                    if (allyCarries)
                        return BannerDuty.Escort;
                    return ownBanner == BannerState.Carried ? BannerDuty.ChaseCarrier : BannerDuty.None;
                case BannerRole.Defender:
                    if (ownBanner == BannerState.Dropped)
                        return BannerDuty.ReturnDropped;
                    return ownBanner == BannerState.Carried ? BannerDuty.ChaseCarrier : BannerDuty.Guard;
                default:
                    return BannerDuty.None;
            }
        }

        /// <summary>
        /// The point to fly at to pass through the zone round <paramref name="center"/> (a pole's foot): offset
        /// <see cref="PoleOffset"/> sideways from the line in from <paramref name="from"/>, <see cref="RunHeight"/>
        /// above <paramref name="ground"/>.
        /// </summary>
        public static Vector3 RunPoint(Vector3 center, Vector3 from, float ground)
        {
            Vector3 dir = center - from;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 1f ? dir.normalized : Vector3.forward;
            Vector3 side = new Vector3(dir.z, 0f, -dir.x);
            Vector3 p = center + side * PoleOffset;
            p.y = ground + RunHeight;
            return p;
        }
    }
}

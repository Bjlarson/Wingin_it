using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Match
{
    /// <summary>Where a team's banner is: on its stand, towed by an enemy, or lying where its carrier went down.</summary>
    public enum BannerState
    {
        Home,
        Carried,
        Dropped,
    }

    public enum BannerEventKind
    {
        /// <summary>An enemy pilot took the banner (from its stand or off the ground).</summary>
        Grabbed,
        /// <summary>The carrier was destroyed; the banner fell.</summary>
        Dropped,
        /// <summary>A defender flew through a dropped banner: straight home.</summary>
        ReturnedByDefender,
        /// <summary>Nobody touched a dropped banner for the return time: home.</summary>
        ReturnedByTimeout,
        /// <summary>The carrier flew the banner through their own stand.</summary>
        Captured,
    }

    /// <summary>Something that happened to a banner: for score, callouts and the banner's visuals.</summary>
    public readonly struct BannerEvent
    {
        public readonly BannerEventKind Kind;
        /// <summary>The team whose banner it is.</summary>
        public readonly int BannerTeam;
        /// <summary>The carrier, or the defender who returned it (null on a timeout).</summary>
        public readonly object Pilot;
        /// <summary>The pilot's team, or -1 with no pilot.</summary>
        public readonly int PilotTeam;

        public BannerEvent(BannerEventKind kind, int bannerTeam, object pilot, int pilotTeam)
        {
            Kind = kind;
            BannerTeam = bannerTeam;
            Pilot = pilot;
            PilotTeam = pilotTeam;
        }
    }

    /// <summary>A live plane as the banners see it: who, where, and how high above the ground.</summary>
    public readonly struct BannerSample
    {
        public readonly object Pilot;
        public readonly int Team;
        public readonly Vector3 Position;
        /// <summary>Height above the ground (or the sea) under the plane; the grab zone only counts low.</summary>
        public readonly float Height;

        public BannerSample(object pilot, int team, Vector3 position, float height)
        {
            Pilot = pilot;
            Team = team;
            Position = position;
            Height = height;
        }
    }

    /// <summary>
    /// The banners of a Capture the Banner match (see Design/01-Game-Design/match-modes.md#capture-the-banner-prototype):
    /// one per team, standing at that team's home. An enemy pilot who flies low through its zone takes it and tows it;
    /// it only comes loose when the carrier is destroyed, then lies for <see cref="ReturnTime"/> seconds until an
    /// enemy takes it again, a defender returns it, or it goes home by itself. Flying it low through your own stand
    /// is a capture. Pure (positions and heights in, events out), so the rules are EditMode-testable.
    /// </summary>
    public sealed class BannerBoard
    {
        public sealed class Banner
        {
            public BannerState State;
            public Vector3 Stand;
            /// <summary>Where it lies while Dropped.</summary>
            public Vector3 DropPosition;
            public object Carrier;
            public int CarrierTeam = -1;
            /// <summary>Seconds until a dropped banner goes home.</summary>
            public float ReturnLeft;
        }

        readonly Banner[] _banners;
        readonly int[] _captures;

        /// <summary>Horizontal radius of a grab / capture zone (about 40 m across).</summary>
        public float ZoneRadius = 20f;
        /// <summary>The zone only counts below this height above the ground.</summary>
        public float ZoneHeight = 40f;
        /// <summary>Seconds a dropped banner lies before it goes home.</summary>
        public float ReturnTime = 20f;
        /// <summary>Captures that end the match at once.</summary>
        public int CapturesToWin = 3;

        public BannerBoard(IReadOnlyList<Vector3> stands)
        {
            _banners = new Banner[stands.Count];
            _captures = new int[stands.Count];
            for (int t = 0; t < stands.Count; t++)
                _banners[t] = new Banner { Stand = stands[t], DropPosition = stands[t] };
        }

        public int TeamCount => _banners.Length;
        public Banner Get(int team) => _banners[team];
        public int Captures(int team) => _captures[team];

        /// <summary>The banner this pilot is towing, or -1.</summary>
        public int CarriedBy(object pilot)
        {
            if (pilot == null)
                return -1;
            for (int t = 0; t < _banners.Length; t++)
                if (_banners[t].State == BannerState.Carried && ReferenceEquals(_banners[t].Carrier, pilot))
                    return t;
            return -1;
        }

        /// <summary>The team with <see cref="CapturesToWin"/> captures, or null.</summary>
        public int? EarlyWinner()
        {
            for (int t = 0; t < _captures.Length; t++)
                if (_captures[t] >= CapturesToWin)
                    return t;
            return null;
        }

        /// <summary>The team with the most captures, or <see cref="MatchScore.Draw"/> if the top count is shared.</summary>
        public int LeaderByCaptures()
        {
            int best = MatchScore.Draw, bestCount = -1;
            for (int t = 0; t < _captures.Length; t++)
            {
                if (_captures[t] > bestCount)
                    (best, bestCount) = (t, _captures[t]);
                else if (_captures[t] == bestCount)
                    best = MatchScore.Draw;
            }
            return best;
        }

        /// <summary>True if a plane at <paramref name="p"/> is inside the zone around <paramref name="center"/>.</summary>
        public bool InZone(Vector3 center, in BannerSample p)
        {
            float dx = p.Position.x - center.x, dz = p.Position.z - center.z;
            return p.Height <= ZoneHeight && dx * dx + dz * dz <= ZoneRadius * ZoneRadius;
        }

        /// <summary>The carrier was destroyed (any cause): its banner falls where it went down. True if it was carrying one.</summary>
        public bool CarrierDown(object pilot, Vector3 position, List<BannerEvent> events)
        {
            int t = CarriedBy(pilot);
            if (t < 0)
                return false;
            var b = _banners[t];
            events?.Add(new BannerEvent(BannerEventKind.Dropped, t, pilot, b.CarrierTeam));
            b.State = BannerState.Dropped;
            b.DropPosition = position;
            b.Carrier = null;
            b.CarrierTeam = -1;
            b.ReturnLeft = ReturnTime;
            return true;
        }

        /// <summary>
        /// Runs <paramref name="dt"/> seconds: counts the return timers down, then lets each sampled live plane grab,
        /// return or capture. Order within a frame: captures first, then returns, then grabs.
        /// </summary>
        public void Update(float dt, IReadOnlyList<BannerSample> planes, List<BannerEvent> events)
        {
            for (int t = 0; t < _banners.Length; t++)
            {
                var b = _banners[t];
                if (b.State != BannerState.Dropped)
                    continue;
                b.ReturnLeft -= dt;
                if (b.ReturnLeft <= 0f)
                {
                    SendHome(b);
                    events?.Add(new BannerEvent(BannerEventKind.ReturnedByTimeout, t, null, -1));
                }
            }

            foreach (var p in planes)
            {
                int carried = CarriedBy(p.Pilot);
                if (carried >= 0 && p.Team >= 0 && p.Team < _banners.Length && InZone(_banners[p.Team].Stand, p))
                {
                    SendHome(_banners[carried]);
                    _captures[p.Team]++;
                    events?.Add(new BannerEvent(BannerEventKind.Captured, carried, p.Pilot, p.Team));
                }
            }

            foreach (var p in planes)
            {
                for (int t = 0; t < _banners.Length; t++)
                {
                    var b = _banners[t];
                    if (b.State == BannerState.Carried)
                        continue;
                    var at = b.State == BannerState.Home ? b.Stand : b.DropPosition;
                    if (!InZone(at, p))
                        continue;
                    if (p.Team == t)
                    {
                        if (b.State != BannerState.Dropped)
                            continue;
                        SendHome(b);
                        events?.Add(new BannerEvent(BannerEventKind.ReturnedByDefender, t, p.Pilot, p.Team));
                    }
                    else if (CarriedBy(p.Pilot) < 0)
                    {
                        b.State = BannerState.Carried;
                        b.Carrier = p.Pilot;
                        b.CarrierTeam = p.Team;
                        events?.Add(new BannerEvent(BannerEventKind.Grabbed, t, p.Pilot, p.Team));
                    }
                }
            }
        }

        static void SendHome(Banner b)
        {
            b.State = BannerState.Home;
            b.Carrier = null;
            b.CarrierTeam = -1;
            b.DropPosition = b.Stand;
            b.ReturnLeft = 0f;
        }
    }
}

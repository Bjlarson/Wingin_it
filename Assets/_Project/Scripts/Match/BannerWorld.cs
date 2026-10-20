using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Match
{
    /// <summary>
    /// Runs Capture the Banner in the level (Design/01-Game-Design/match-modes.md#capture-the-banner-prototype): feeds the
    /// <see cref="BannerBoard"/> every plane's place each frame and turns what it reports into the world: the carrier's drag
    /// and shut-off Boost, Afterburner and repair, the cloth towed on its cable, and the capture score. A carrier that is
    /// destroyed in any way drops its banner (<see cref="CarrierDown"/>). <see cref="MatchManager"/> owns one while a Banner match is on.
    /// </summary>
    public sealed class BannerWorld
    {
        readonly BannerMode _mode;
        readonly BannerDrag _drag = new();
        readonly TowedBanner[] _cloths;
        readonly List<BannerSample> _samples = new();
        readonly List<BannerEvent> _events = new();

        BannerWorld(BannerMode mode, TowedBanner[] cloths)
        {
            _mode = mode;
            _cloths = cloths;
        }

        /// <summary>The banners of the match in progress, or null (the bots read it).</summary>
        public static BannerWorld Active { get; private set; }

        public BannerBoard Board => _mode.Board;
        /// <summary>Raised for every banner event as the world applies it (the HUD's callouts).</summary>
        public event System.Action<BannerEvent> EventApplied;
        /// <summary>The cloth of <paramref name="team"/>'s banner.</summary>
        public TowedBanner Cloth(int team) => _cloths[team];

        /// <summary>The banners of <paramref name="mode"/> in the level, or null if it has no stand for every team.</summary>
        public static BannerWorld Create(BannerMode mode, int teamCount)
        {
            var positions = BannerStand.PositionsByTeam(teamCount);
            if (positions == null)
                return null;
            mode.SetStands(positions);
            var cloths = new TowedBanner[teamCount];
            for (int t = 0; t < teamCount; t++)
                cloths[t] = TowedBanner.Create(BannerStand.Of(t));
            return Active = new BannerWorld(mode, cloths);
        }

        /// <summary>One frame of play: planes into the board, its events into the world and the score.</summary>
        public void Tick(MatchScore score, float dt)
        {
            _samples.Clear();
            foreach (var plane in PlaneController.All)
            {
                if (!plane.IsAlive || plane.Team < 0 || plane.Team >= _cloths.Length)
                    continue;
                float height = LandingGear.ProbeGround(plane.Position, out var ground) ? ground.distance : float.PositiveInfinity;
                _samples.Add(new BannerSample(plane, plane.Team, plane.Position, height));
            }

            _events.Clear();
            Board.Update(dt, _samples, _events);
            foreach (var e in _events)
                Apply(e);
            if (_events.Count > 0)
                _mode.Apply(score, _events);
        }

        /// <summary>A plane was destroyed: if it was towing a banner, the banner falls where it went down.</summary>
        public void CarrierDown(PlaneController victim)
        {
            if (victim == null || Board.CarriedBy(victim) < 0)
                return;
            Vector3 down = LandingGear.ProbeGround(victim.Position + Vector3.up * 5f, out var ground)
                ? ground.point
                : new Vector3(victim.Position.x, 0f, victim.Position.z);
            _events.Clear();
            Board.CarrierDown(victim, down, _events);
            foreach (var e in _events)
                Apply(e);
        }

        void Apply(in BannerEvent e)
        {
            EventApplied?.Invoke(e);
            var plane = e.Pilot as PlaneController;
            switch (e.Kind)
            {
                case BannerEventKind.Grabbed:
                    if (plane != null)
                    {
                        plane.CarryingBanner = true;
                        _drag.Apply(plane.Modifiers);
                    }
                    _cloths[e.BannerTeam].Tow(plane);
                    break;
                case BannerEventKind.Dropped:
                    Release(plane);
                    _cloths[e.BannerTeam].Drop(Board.Get(e.BannerTeam).DropPosition);
                    break;
                case BannerEventKind.Captured:
                    Release(plane);
                    _cloths[e.BannerTeam].GoHome();
                    break;
                case BannerEventKind.ReturnedByDefender:
                case BannerEventKind.ReturnedByTimeout:
                    _cloths[e.BannerTeam].GoHome();
                    break;
            }
        }

        void Release(PlaneController plane)
        {
            if (plane == null)
                return;
            plane.CarryingBanner = false;
            BannerDrag.Remove(plane.Modifiers);
        }

        /// <summary>Match over (or the mode changed): every carrier is free again and every banner is home.</summary>
        public void Dispose()
        {
            if (Active == this)
                Active = null;
            foreach (var plane in PlaneController.All)
                if (plane.CarryingBanner)
                    Release(plane);
            foreach (var cloth in _cloths)
                if (cloth != null)
                {
                    cloth.GoHome();
                    Object.Destroy(cloth.gameObject);
                }
        }
    }
}

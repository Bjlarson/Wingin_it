using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Match;

namespace WingingIt.Tests
{
    public class BannerRulesTests
    {
        static readonly Vector3 WestStand = new(-1000f, 0f, 0f), EastStand = new(1000f, 0f, 0f);

        readonly object _west = new(), _west2 = new(), _east = new();
        BannerBoard _board;
        List<BannerEvent> _events;

        [SetUp]
        public void SetUp()
        {
            _board = new BannerBoard(new[] { WestStand, EastStand });
            _events = new List<BannerEvent>();
        }

        static BannerSample At(object pilot, int team, Vector3 pos, float height = 10f) => new(pilot, team, pos, height);

        void Step(float dt, params BannerSample[] planes) => _board.Update(dt, planes, _events);

        [Test]
        public void EnemyFlyingLowThroughTheStand_GrabsTheBanner()
        {
            Step(0.1f, At(_east, 1, WestStand + new Vector3(5f, 0f, 5f)));
            Assert.AreEqual(BannerState.Carried, _board.Get(0).State);
            Assert.AreSame(_east, _board.Get(0).Carrier);
            Assert.AreEqual(0, _board.CarriedBy(_east));
            Assert.AreEqual(BannerEventKind.Grabbed, _events.Single().Kind);
        }

        [Test]
        public void TooHighOrTooWide_GrabsNothing()
        {
            Step(0.1f, At(_east, 1, WestStand, 80f));
            Step(0.1f, At(_east, 1, WestStand + new Vector3(30f, 0f, 0f)));
            Assert.AreEqual(BannerState.Home, _board.Get(0).State);
            Assert.IsEmpty(_events);
        }

        [Test]
        public void OwnPilotAtTheirOwnHomeStand_DoesNothing()
        {
            Step(0.1f, At(_west, 0, WestStand));
            Assert.AreEqual(BannerState.Home, _board.Get(0).State);
            Assert.IsEmpty(_events);
        }

        [Test]
        public void ACarrierCannotTakeASecondBanner()
        {
            var board = new BannerBoard(new[] { WestStand, EastStand, new Vector3(0f, 0f, 1000f) });
            board.Update(0.1f, new[] { At(_east, 1, WestStand) }, _events);
            _events.Clear();
            board.Update(0.1f, new[] { At(_east, 1, board.Get(2).Stand) }, _events);
            Assert.AreEqual(BannerState.Home, board.Get(2).State);
            Assert.IsEmpty(_events);
        }

        [Test]
        public void CarryingItThroughYourOwnStand_IsACapture_AndTheBannerGoesHome()
        {
            Step(0.1f, At(_east, 1, WestStand));
            _events.Clear();
            Step(0.1f, At(_east, 1, EastStand));
            Assert.AreEqual(1, _board.Captures(1));
            Assert.AreEqual(BannerState.Home, _board.Get(0).State);
            var e = _events.Single();
            Assert.AreEqual(BannerEventKind.Captured, e.Kind);
            Assert.AreEqual(0, e.BannerTeam);
            Assert.AreSame(_east, e.Pilot);
            Assert.AreEqual(-1, _board.CarriedBy(_east));
        }

        [Test]
        public void YourOwnBannerNeedNotBeHomeToScore()
        {
            Step(0.1f, At(_east, 1, WestStand));
            Step(0.1f, At(_west, 0, EastStand));
            Assert.AreEqual(BannerState.Carried, _board.Get(1).State, "west grabbed the east banner while east towed the west one");
            Step(0.1f, At(_east, 1, EastStand));
            Assert.AreEqual(1, _board.Captures(1), "east captures even with its own banner away");
        }

        [Test]
        public void CarrierDestroyed_DropsTheBannerWhereItWentDown()
        {
            Step(0.1f, At(_east, 1, WestStand));
            _events.Clear();
            Assert.IsTrue(_board.CarrierDown(_east, new Vector3(0f, 3f, 200f), _events));
            var b = _board.Get(0);
            Assert.AreEqual(BannerState.Dropped, b.State);
            Assert.AreEqual(new Vector3(0f, 3f, 200f), b.DropPosition);
            Assert.AreEqual(20f, b.ReturnLeft);
            Assert.AreEqual(BannerEventKind.Dropped, _events.Single().Kind);
            Assert.IsFalse(_board.CarrierDown(_east, Vector3.zero, _events), "no longer carrying");
        }

        [Test]
        public void AnEnemyFlyingThroughADroppedBanner_TakesItAgain()
        {
            Step(0.1f, At(_east, 1, WestStand));
            var spot = new Vector3(0f, 0f, 300f);
            _board.CarrierDown(_east, spot, _events);
            _events.Clear();
            var other = new object();
            Step(0.1f, At(other, 1, spot));
            Assert.AreEqual(BannerState.Carried, _board.Get(0).State);
            Assert.AreSame(other, _board.Get(0).Carrier);
        }

        [Test]
        public void ADefenderFlyingThroughADroppedBanner_SendsItHome()
        {
            Step(0.1f, At(_east, 1, WestStand));
            var spot = new Vector3(0f, 0f, 300f);
            _board.CarrierDown(_east, spot, _events);
            _events.Clear();
            Step(0.1f, At(_west2, 0, spot));
            Assert.AreEqual(BannerState.Home, _board.Get(0).State);
            Assert.AreEqual(BannerEventKind.ReturnedByDefender, _events.Single().Kind);
            Assert.AreSame(_west2, _events.Single().Pilot);
        }

        [Test]
        public void ADroppedBannerNobodyTouches_GoesHomeAfterTheReturnTime()
        {
            Step(0.1f, At(_east, 1, WestStand));
            _board.CarrierDown(_east, new Vector3(0f, 0f, 300f), _events);
            _events.Clear();
            Step(19f);
            Assert.AreEqual(BannerState.Dropped, _board.Get(0).State);
            Step(1.5f);
            Assert.AreEqual(BannerState.Home, _board.Get(0).State);
            Assert.AreEqual(BannerEventKind.ReturnedByTimeout, _events.Single().Kind);
        }

        [Test]
        public void ThreeCapturesWinAtOnce_OtherwiseMostCapturesLeads()
        {
            Assert.IsNull(_board.EarlyWinner());
            Assert.AreEqual(MatchScore.Draw, _board.LeaderByCaptures());
            for (int i = 0; i < 3; i++)
            {
                Step(0.1f, At(_east, 1, WestStand));
                if (i == 0)
                    Assert.AreEqual(MatchScore.Draw, _board.LeaderByCaptures());
                Step(0.1f, At(_east, 1, EastStand));
                if (i == 0)
                    Assert.AreEqual(1, _board.LeaderByCaptures());
            }
            Assert.AreEqual(1, _board.EarlyWinner());
        }
    }

    public class BannerModeTests
    {
        static readonly Vector3 WestStand = new(-1000f, 0f, 0f), EastStand = new(1000f, 0f, 0f);

        [Test]
        public void ACapture_ScoresThreeHundredAndCountsOnThePilot()
        {
            var mode = new BannerMode();
            mode.SetStands(new[] { WestStand, EastStand });
            var score = mode.CreateScore(2);
            var pilot = new object();
            score.Register(pilot, "Ace", 1);

            var events = new List<BannerEvent>();
            mode.Board.Update(0.1f, new[] { new BannerSample(pilot, 1, WestStand, 5f) }, events);
            mode.Board.Update(0.1f, new[] { new BannerSample(pilot, 1, EastStand, 5f) }, events);
            mode.Apply(score, events);

            Assert.AreEqual(300, score.TeamScore(1));
            Assert.AreEqual(1, score.Get(pilot).Captures);
            Assert.AreEqual(300, score.Get(pilot).Points);
        }

        [Test]
        public void Winner_MostCapturesThenTeamScoreThenDraw()
        {
            var mode = new BannerMode();
            mode.SetStands(new[] { WestStand, EastStand });
            var score = mode.CreateScore(2);
            Assert.AreEqual(MatchScore.Draw, mode.Winner(score));

            score.AwardPoints(0, null, null, 100, "Kill");
            Assert.AreEqual(0, mode.Winner(score), "equal captures: the higher team score");

            var pilot = new object();
            var events = new List<BannerEvent>();
            mode.Board.Update(0.1f, new[] { new BannerSample(pilot, 1, WestStand, 5f) }, events);
            mode.Board.Update(0.1f, new[] { new BannerSample(pilot, 1, EastStand, 5f) }, events);
            Assert.AreEqual(1, mode.Winner(score), "captures beat score");
        }

        [Test]
        public void Scoreboard_HasACaptureColumn()
        {
            Assert.IsTrue(new BannerMode().Columns.Any(c => c.Header == "CAP"));
        }
    }
}

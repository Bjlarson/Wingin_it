using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using WingingIt.Match;

namespace WingingIt.Tests
{
    public class DominationRulesTests
    {
        const int West = 0, East = 1;

        readonly object _west = new(), _west2 = new(), _west3 = new(), _west4 = new(), _east = new();
        FieldBoard _board;
        List<FieldCapture> _captures;

        [SetUp]
        public void SetUp()
        {
            _board = new FieldBoard(3);
            _captures = new List<FieldCapture>();
        }

        static FieldSample On(object pilot, int team, int field) => new(pilot, team, field);

        /// <summary>Runs <paramref name="seconds"/> in 0.1 s frames with the same planes parked.</summary>
        void Run(float seconds, params FieldSample[] planes)
        {
            for (int i = 0; i < UnityEngine.Mathf.RoundToInt(seconds * 10f); i++)
                _board.Update(0.1f, planes, _captures);
        }

        [Test]
        public void OnePlaneParkedOnANeutralField_CapturesItInTenSeconds()
        {
            Run(9.8f, On(_west, West, 1));
            Assert.AreEqual(FieldBoard.Neutral, _board.Get(1).Owner);
            Assert.AreEqual(West, _board.Get(1).CapturingTeam);
            Assert.AreEqual(0.98f, _board.Get(1).Progress, 0.001f);

            Run(0.3f, On(_west, West, 1));
            Assert.AreEqual(West, _board.Get(1).Owner);
            var c = _captures.Single();
            Assert.AreEqual(1, c.Field);
            Assert.AreEqual(West, c.Team);
            Assert.AreEqual(FieldBoard.Neutral, c.OldOwner);
            CollectionAssert.AreEqual(new[] { _west }, c.Pilots);
            Assert.AreEqual(0f, _board.Get(1).Progress);
            Assert.AreEqual(FieldBoard.Neutral, _board.Get(0).Owner, "other fields untouched");
        }

        [Test]
        public void MorePlanesCaptureFaster_UpToThree()
        {
            var f = _board.Get(0);
            Assert.AreEqual(0.1f, _board.CaptureRate(f, 1), 1e-5f);
            Assert.AreEqual(0.15f, _board.CaptureRate(f, 2), 1e-5f);
            Assert.AreEqual(0.2f, _board.CaptureRate(f, 3), 1e-5f);
            Assert.AreEqual(0.2f, _board.CaptureRate(f, 4), 1e-5f, "a fourth plane adds nothing");

            Run(5.1f, On(_west, West, 0), On(_west2, West, 0), On(_west3, West, 0), On(_west4, West, 0));
            Assert.AreEqual(West, _board.Get(0).Owner);
            Assert.AreEqual(4, _captures.Single().Pilots.Count, "every parked pilot shares the capture");
        }

        [Test]
        public void ALandedEnemy_ContestsIt_AndProgressHolds()
        {
            Run(5f, On(_west, West, 2));
            Run(20f, On(_west, West, 2), On(_east, East, 2));
            var f = _board.Get(2);
            Assert.IsTrue(f.Contested);
            Assert.AreEqual(0.5f, f.Progress, 0.001f);
            Assert.AreEqual(FieldBoard.Neutral, f.Owner);

            Run(5.1f, On(_west, West, 2));
            Assert.IsFalse(f.Contested);
            Assert.AreEqual(West, f.Owner, "once the enemy's gone it picks up where it stopped");
        }

        [Test]
        public void PlanesOnOtherFieldsOrInTheAir_DontContest()
        {
            Run(10.1f, On(_west, West, 0), On(_east, East, 1), On(_east, East, -1));
            Assert.AreEqual(West, _board.Get(0).Owner);
            Assert.AreEqual(East, _board.Get(1).Owner);
        }

        [Test]
        public void LeavingTheZone_ProgressDrainsBack()
        {
            Run(4f, On(_west, West, 0));
            Run(2f);
            Assert.AreEqual(0.2f, _board.Get(0).Progress, 0.001f);
            Run(3f);
            Assert.AreEqual(0f, _board.Get(0).Progress);
            Assert.AreEqual(-1, _board.Get(0).CapturingTeam);
        }

        [Test]
        public void AnotherTeam_FirstUndoesTheProgress_ThenBuildsItsOwn()
        {
            Run(3f, On(_west, West, 0));
            Run(2f, On(_east, East, 0));
            Assert.AreEqual(West, _board.Get(0).CapturingTeam);
            Assert.AreEqual(0.1f, _board.Get(0).Progress, 0.001f);

            Run(2f, On(_east, East, 0));
            Assert.AreEqual(East, _board.Get(0).CapturingTeam);
            Assert.AreEqual(0.1f, _board.Get(0).Progress, 0.001f);
            Assert.IsEmpty(_captures);
        }

        [Test]
        public void AnEnemyCapturesAnOwnedField_ItChangesHands()
        {
            Run(10.1f, On(_west, West, 1));
            _captures.Clear();
            Run(10.1f, On(_east, East, 1));
            Assert.AreEqual(East, _board.Get(1).Owner);
            Assert.AreEqual(West, _captures.Single().OldOwner);
        }

        [Test]
        public void TheOwnersOwnPlanes_CaptureNothing_AndPushBackAnEnemysProgress()
        {
            Run(10.1f, On(_west, West, 1));
            _captures.Clear();
            Run(20f, On(_west, West, 1), On(_west2, West, 1));
            Assert.AreEqual(West, _board.Get(1).Owner);
            Assert.AreEqual(0f, _board.Get(1).Progress);
            Assert.IsEmpty(_captures);

            Run(5f, On(_east, East, 1));
            Run(2f, On(_west, West, 1));
            Assert.AreEqual(0.3f, _board.Get(1).Progress, 0.001f, "a defender alone lets it drain");
        }

        [Test]
        public void AStandingTower_MakesEnemiesCaptureThreeTimesSlower()
        {
            Run(10.1f, On(_west, West, 0));
            _board.Get(0).TowerUp = true;
            Assert.AreEqual(0.1f / 3f, _board.CaptureRate(_board.Get(0), 1), 1e-5f);
            Run(29.8f, On(_east, East, 0));
            Assert.AreEqual(West, _board.Get(0).Owner);

            _board.Get(0).TowerUp = false;
            Run(0.3f, On(_east, East, 0));
            Assert.AreEqual(East, _board.Get(0).Owner, "tower down: full speed");
        }

        [Test]
        public void ATowerOnANeutralField_DoesntSlowAnything()
        {
            _board.Get(0).TowerUp = true;
            Assert.AreEqual(0.1f, _board.CaptureRate(_board.Get(0), 1), 1e-5f);
        }

        [Test]
        public void Parked_MeansAliveOnTheWheelsStoppedAndNotBackingUp()
        {
            Assert.IsTrue(FieldBoard.IsParked(true, true, 0.5f, 1f, false));
            Assert.IsFalse(FieldBoard.IsParked(false, true, 0f, 1f, false), "dead");
            Assert.IsFalse(FieldBoard.IsParked(true, false, 0f, 1f, false), "in the air");
            Assert.IsFalse(FieldBoard.IsParked(true, true, 5f, 1f, false), "still rolling");
            Assert.IsFalse(FieldBoard.IsParked(true, true, 0.5f, 1f, true), "backing up");
        }
    }
}

using NUnit.Framework;
using WingingIt.Match;

namespace WingingIt.Tests
{
    public class MatchClockTests
    {
        [Test]
        public void Phases_GoLobby_ClassSelect_Countdown_Playing_Ended()
        {
            var clock = new MatchClock(300f, 3f);
            Assert.AreEqual(MatchPhase.Lobby, clock.Phase);
            Assert.IsFalse(clock.StartCountdown(), "can't skip class select");
            Assert.IsTrue(clock.OpenClassSelect());
            Assert.IsTrue(clock.BackToLobby());
            Assert.IsTrue(clock.OpenClassSelect());
            Assert.IsTrue(clock.StartCountdown());
            Assert.AreEqual(MatchPhase.Countdown, clock.Phase);
            Assert.AreEqual(3f, clock.TimeLeft);

            Assert.IsFalse(clock.Tick(2.5f));
            Assert.IsTrue(clock.Tick(0.5f));
            Assert.AreEqual(MatchPhase.Playing, clock.Phase);
            Assert.AreEqual(300f, clock.TimeLeft);

            Assert.IsFalse(clock.Tick(299f));
            Assert.AreEqual(1f, clock.TimeLeft, 1e-4f);
            Assert.IsTrue(clock.Tick(5f));
            Assert.AreEqual(MatchPhase.Ended, clock.Phase);
            Assert.AreEqual(0f, clock.TimeLeft);
            Assert.IsFalse(clock.Tick(1f), "nothing runs after the end");
        }

        [Test]
        public void Lobby_AndClassSelect_DontTick()
        {
            var clock = new MatchClock(300f, 3f);
            Assert.IsFalse(clock.Tick(1000f));
            clock.OpenClassSelect();
            Assert.IsFalse(clock.Tick(1000f));
            Assert.AreEqual(MatchPhase.ClassSelect, clock.Phase);
        }

        [Test]
        public void NoCountdown_GoesStraightToPlay()
        {
            var clock = new MatchClock(60f, 0f);
            clock.OpenClassSelect();
            clock.StartCountdown();
            Assert.AreEqual(MatchPhase.Playing, clock.Phase);
            Assert.AreEqual(60f, clock.TimeLeft);
        }

        [Test]
        public void End_OnlyEndsAMatchInPlay()
        {
            var clock = new MatchClock(60f, 0f);
            Assert.IsFalse(clock.End());
            clock.OpenClassSelect();
            clock.StartCountdown();
            Assert.IsTrue(clock.End());
            Assert.AreEqual(MatchPhase.Ended, clock.Phase);
        }
    }

    public class MatchScoreTests
    {
        readonly object _west1 = new(), _west2 = new(), _east1 = new();

        [Test]
        public void AnEnemyKill_ScoresForTheKillersTeam()
        {
            var score = new MatchScore(2);
            Assert.IsTrue(score.RecordDeath(_east1, "e1", 1, _west1, "w1", 0));
            Assert.AreEqual(50, score.TeamScore(0));
            Assert.AreEqual(0, score.TeamScore(1));
            Assert.AreEqual(1, score.Get(_west1).Kills);
            Assert.AreEqual(50, score.Get(_west1).Points);
            Assert.AreEqual(0, score.Get(_east1).Points);
            Assert.AreEqual(1, score.Get(_east1).Deaths);
        }

        [Test]
        public void AA_ScoresForItsTeam_WithNoPilotCredit()
        {
            var score = new MatchScore(2);
            Assert.IsTrue(score.RecordDeath(_west1, "w1", 0, null, null, 1));
            Assert.AreEqual(50, score.TeamScore(1));
            Assert.AreEqual(1, score.Get(_west1).Deaths);
            Assert.AreEqual(1, score.Pilots.Count, "no pilot for the AA gun");
        }

        [Test]
        public void UncreditedCrashes_TeamKills_AndNeutralSources_ScoreNothing()
        {
            var score = new MatchScore(2);
            Assert.IsFalse(score.RecordDeath(_west1, "w1", 0, null, null, null), "crash, nobody credited");
            Assert.IsFalse(score.RecordDeath(_west1, "w1", 0, _west2, "w2", 0), "team kill");
            Assert.IsFalse(score.RecordDeath(_west1, "w1", 0, null, null, -1), "neutral airfield");
            Assert.AreEqual(0, score.TeamScore(0));
            Assert.AreEqual(0, score.TeamScore(1));
            Assert.AreEqual(3, score.Get(_west1).Deaths, "every death still counts");
            Assert.AreEqual(0, score.Get(_west2).Kills);
        }

        [Test]
        public void Winner_IsTheHighestTeam_ATieIsADraw()
        {
            var score = new MatchScore(2);
            Assert.AreEqual(MatchScore.Draw, score.Winner(), "0-0");
            score.RecordDeath(_east1, "e1", 1, _west1, "w1", 0);
            Assert.AreEqual(0, score.Winner());
            score.RecordDeath(_west1, "w1", 0, _east1, "e1", 1);
            Assert.AreEqual(MatchScore.Draw, score.Winner(), "1-1");
            score.RecordDeath(_west2, "w2", 0, _east1, "e1", 1);
            Assert.AreEqual(1, score.Winner());
        }

        [Test]
        public void Assists_ScoreForTheHelpersTeam_ButNotOnATeammate()
        {
            var score = new MatchScore(2, killPoints: 50, assistPoints: 20);
            score.RecordDeath(_east1, "e1", 1, _west1, "w1", 0);
            Assert.IsTrue(score.RecordAssist(_west2, "w2", 0, 1));
            Assert.AreEqual(70, score.TeamScore(0));
            Assert.AreEqual(1, score.Get(_west2).Assists);
            Assert.AreEqual(20, score.Get(_west2).Points);
            Assert.AreEqual(0, score.Get(_west2).Kills);

            Assert.IsFalse(score.RecordAssist(_west2, "w2", 0, 0), "helped kill a teammate");
            Assert.IsFalse(score.RecordAssist(null, null, 0, 1));
            Assert.AreEqual(70, score.TeamScore(0));
            Assert.AreEqual(1, score.Get(_west2).Assists);
        }

        [Test]
        public void AnAAKill_StillGivesTheHelpersAssists()
        {
            var score = new MatchScore(2);
            score.RecordDeath(_west1, "w1", 0, null, null, 1);
            score.RecordAssist(_east1, "e1", 1, 0);
            Assert.AreEqual(70, score.TeamScore(1));
            Assert.AreEqual(20, score.Get(_east1).Points);
        }

        [Test]
        public void Objectives_ScoreForTheTeam_AndThePilotIfAny()
        {
            var score = new MatchScore(2);
            Assert.IsTrue(score.AwardPoints(1, null, null, 100, "Convoy destroyed"));
            Assert.IsTrue(score.AwardPoints(1, _east1, "e1", 30, "Supply drop"));
            Assert.IsFalse(score.AwardPoints(5, null, null, 30, "no such team"));
            Assert.IsFalse(score.AwardPoints(0, null, null, 0, "nothing"));
            Assert.AreEqual(130, score.TeamScore(1));
            Assert.AreEqual(0, score.TeamScore(0));
            Assert.AreEqual(30, score.Get(_east1).Points);
            Assert.AreEqual(1, score.Pilots.Count);
        }

        [Test]
        public void Awarded_ReportsEveryAward()
        {
            var score = new MatchScore(2);
            var awards = new System.Collections.Generic.List<ScoreAward>();
            score.Awarded += awards.Add;
            score.RecordDeath(_east1, "e1", 1, _west1, "w1", 0);
            score.RecordAssist(_west2, "w2", 0, 1);
            score.AwardPoints(0, null, null, 100, "Radar captured");
            score.RecordDeath(_west1, "w1", 0, null, null, null); // scores nothing
            Assert.AreEqual(3, awards.Count);
            Assert.AreEqual(ScoreKind.Kill, awards[0].Kind);
            Assert.AreSame(_west1, awards[0].Pilot);
            Assert.AreEqual(50, awards[0].Points);
            Assert.AreEqual(ScoreKind.Assist, awards[1].Kind);
            Assert.AreEqual(ScoreKind.Objective, awards[2].Kind);
            Assert.AreEqual("Radar captured", awards[2].Label);
        }

        [Test]
        public void Winner_CountsAssistsAndObjectives_NotJustKills()
        {
            var score = new MatchScore(2);
            score.RecordDeath(_east1, "e1", 1, _west1, "w1", 0); // 50 - 0
            score.AwardPoints(1, null, null, 60, "Convoy destroyed"); // 50 - 60
            Assert.AreEqual(1, score.Winner());
            score.RecordAssist(_west2, "w2", 0, 1); // 70 - 60
            Assert.AreEqual(0, score.Winner());
        }

        [Test]
        public void Scoreboard_SortsByPoints_ThenKills_ThenFewestDeaths()
        {
            var a = new PilotScore { Name = "a", Points = 40, Kills = 0, Deaths = 0 };
            var b = new PilotScore { Name = "b", Points = 50, Kills = 1, Deaths = 3 };
            var c = new PilotScore { Name = "c", Points = 50, Kills = 1, Deaths = 1 };
            var list = new System.Collections.Generic.List<PilotScore> { a, b, c };
            list.Sort(MatchScore.CompareForScoreboard);
            CollectionAssert.AreEqual(new[] { c, b, a }, list);
        }

        [Test]
        public void Register_IsIdempotent_AndKeepsJoinOrder()
        {
            var score = new MatchScore(2);
            score.Register(_west1, "w1", 0);
            score.Register(_east1, "e1", 1);
            score.Register(_west1, "w1 renamed", 0);
            Assert.AreEqual(2, score.Pilots.Count);
            Assert.AreEqual("w1 renamed", score.Pilots[0].Name);
        }
    }
}

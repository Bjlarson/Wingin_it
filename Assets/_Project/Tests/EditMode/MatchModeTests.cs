using System.Linq;
using NUnit.Framework;
using WingingIt.Match;

namespace WingingIt.Tests
{
    public class MatchModeTests
    {
        readonly object _west1 = new(), _west2 = new(), _east1 = new();

        static PilotRef Ref(object pilot, string name, int team) => new(pilot, name, team);

        [Test]
        public void EveryCard_MakesAFreshModeThatKnowsItsCard()
        {
            foreach (var info in GameModeInfo.All)
            {
                if (!info.IsPlayable)
                {
                    Assert.IsNull(info.CreateMode(), info.Name + ": no rules yet, no mode");
                    continue;
                }
                var a = info.CreateMode();
                Assert.AreSame(info, a.Info, info.Name);
                Assert.AreNotSame(a, info.CreateMode(), "one rules object per match");
                Assert.IsNotEmpty(a.Columns, info.Name);
            }
            Assert.IsInstanceOf<TeamsMode>(GameModeInfo.All[0].CreateMode(), "Teams is the first card");
        }

        [Test]
        public void TheCards_TeamsThenThePrototypes_EachWithItsOwnBotDifficulty()
        {
            CollectionAssert.AreEqual(new[] { "Teams", "Banner", "Domination", "RamRoyale" }, GameModeInfo.All.Select(m => m.Id));
            Assert.AreEqual(GameModeInfo.All.Count, GameModeInfo.All.Select(m => m.DifficultyPref).Distinct().Count(),
                "every mode remembers its own bot difficulty");
            Assert.AreEqual("WingingIt.Teams.BotDifficulty", GameModeInfo.All[0].DifficultyPref, "Teams keeps the saved setting");
            foreach (var info in GameModeInfo.All)
            {
                Assert.IsTrue(info.HasBotDifficulty, info.Name);
                Assert.IsNotEmpty(info.Pitch, info.Name);
                Assert.IsNotEmpty(info.Players, info.Name);
                Assert.Greater(info.Length, 0f, info.Name);
            }
            Assert.AreEqual(180f, GameModeInfo.All.Single(m => m.Id == "RamRoyale").Length, "Ram Royale is up to 3 minutes");
        }

        [Test]
        public void ModesInDevelopment_ShowOnlyInDevelopmentBuilds()
        {
            var release = GameModeInfo.MenuCards(false);
            var dev = GameModeInfo.MenuCards(true);
            Assert.AreEqual(Enumerable.Range(0, GameModeInfo.All.Count), dev, "dev builds show every card");
            Assert.Contains(0, release, "Teams is always there");
            foreach (int i in release)
                Assert.IsTrue(GameModeInfo.All[i].IsPlayable, GameModeInfo.All[i].Name + ": no locked cards for players");
            Assert.AreEqual(GameModeInfo.All.Count(m => m.IsPlayable), release.Count);
        }

        [Test]
        public void Teams_AKill_ScoresTheKillerAndTheHelpers()
        {
            var mode = new TeamsMode();
            var score = mode.CreateScore(2);
            mode.RecordKill(score, new KillRecord(Ref(_east1, "e1", 1), Ref(_west1, "w1", 0), 0,
                new[] { Ref(_west2, "w2", 0) }));
            Assert.AreEqual(50 + 20, score.TeamScore(0));
            Assert.AreEqual(1, score.Get(_west1).Kills);
            Assert.AreEqual(1, score.Get(_west2).Assists);
            Assert.AreEqual(1, score.Get(_east1).Deaths);
        }

        [Test]
        public void Teams_AnAAKill_ScoresTheTeam_ACrashNobody()
        {
            var mode = new TeamsMode();
            var score = mode.CreateScore(2);
            mode.RecordKill(score, new KillRecord(Ref(_west1, "w1", 0), default, 1));
            Assert.AreEqual(50, score.TeamScore(1));
            mode.RecordKill(score, new KillRecord(Ref(_east1, "e1", 1), default, null));
            Assert.AreEqual(0, score.TeamScore(0));
            Assert.AreEqual(1, score.Get(_east1).Deaths);
        }

        [Test]
        public void Teams_PointValues_SetTheScore()
        {
            var mode = new TeamsMode { KillPoints = 100, AssistPoints = 5 };
            var score = mode.CreateScore(2);
            Assert.AreEqual(100, score.KillPoints);
            Assert.AreEqual(5, score.AssistPoints);
        }

        [Test]
        public void Teams_NoEarlyWin_HighestScoreWins_ATieIsADraw()
        {
            var mode = new TeamsMode();
            var score = mode.CreateScore(2);
            Assert.AreEqual(MatchScore.Draw, mode.Winner(score));
            for (int i = 0; i < 20; i++)
                mode.RecordKill(score, new KillRecord(Ref(new object(), "e", 1), Ref(_west1, "w1", 0), 0));
            Assert.IsNull(mode.EarlyWinner(score), "Teams only ends on time");
            Assert.AreEqual(0, mode.Winner(score));
        }

        [Test]
        public void Teams_UnlimitedLives_EveryWidget_KADScoreColumns()
        {
            var mode = new TeamsMode();
            Assert.IsTrue(mode.CanRespawn(null));
            Assert.IsTrue(mode.CanRespawn(new PilotScore { Deaths = 99 }));
            Assert.IsFalse(mode.TryGetSpawnPose(null, default, out _, out _), "usual spawns");
            Assert.AreEqual(MatchWidgets.All, mode.Widgets);

            var p = new PilotScore { Kills = 3, Assists = 2, Deaths = 1, Points = 190 };
            CollectionAssert.AreEqual(new[] { "K", "A", "D", "SCORE" }, mode.Columns.Select(c => c.Header));
            CollectionAssert.AreEqual(new[] { "3", "2", "1", "190" }, mode.Columns.Select(c => c.Value(p)));
        }
    }
}

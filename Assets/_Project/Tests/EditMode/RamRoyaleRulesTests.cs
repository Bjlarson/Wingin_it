using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Match;

namespace WingingIt.Tests
{
    /// <summary>Ram Royale's rules so far: free for all, one life, and who a spectator watches.</summary>
    public class RamRoyaleRulesTests
    {
        [Test]
        public void RamRoyale_IsSixteenPilotFreeForAll_InItsArena()
        {
            var mode = new RamRoyaleMode();
            Assert.IsTrue(mode.IsFreeForAll);
            Assert.AreEqual(16, mode.FreeForAllSize);
            Assert.AreEqual("RamRoyale", mode.ArenaId, "the arena applies even off the card (tests)");
            Assert.AreEqual(0, mode.Widgets & MatchWidgets.TeamScores, "no West / East scores in the bar");
            Assert.IsFalse(new TeamsMode().IsFreeForAll);
        }

        [Test]
        public void OneLife_NoRespawnAfterTheFirstDeath()
        {
            var mode = new RamRoyaleMode();
            Assert.IsTrue(mode.CanRespawn(null), "a pilot that never died");
            Assert.IsTrue(mode.CanRespawn(new PilotScore()));
            Assert.IsFalse(mode.CanRespawn(new PilotScore { Deaths = 1 }));
        }

        [Test]
        public void UnlimitedLives_InTeams()
        {
            Assert.AreEqual(0, new TeamsMode().Lives);
            Assert.IsTrue(new TeamsMode().CanRespawn(new PilotScore { Deaths = 12 }));
        }

        [Test]
        public void KnockOut_ScoresTheKiller_NoAssists()
        {
            var mode = new RamRoyaleMode();
            var score = mode.CreateScore(16);
            object a = new(), b = new(), c = new();
            mode.RecordKill(score, new KillRecord(new PilotRef(b, "B", 1), new PilotRef(a, "A", 0), 0,
                new[] { new PilotRef(c, "C", 2) }));
            Assert.AreEqual(1, score.Get(a).Kills);
            Assert.AreEqual(mode.KnockOutPoints, score.Get(a).Points);
            Assert.AreEqual(1, score.Get(b).Deaths);
            Assert.IsNull(score.Get(c), "no assists in a free for all");
        }

        [Test]
        public void Scoreboard_StillFlyingFirst_ThenScore()
        {
            var mode = new RamRoyaleMode();
            var outHigh = new PilotScore { Name = "Out", Points = 500, Deaths = 1 };
            var inLow = new PilotScore { Name = "In", Points = 0 };
            var inHigh = new PilotScore { Name = "In2", Points = 100 };
            var list = new List<PilotScore> { outHigh, inLow, inHigh };
            list.Sort(mode.CompareForScoreboard);
            CollectionAssert.AreEqual(new[] { inHigh, inLow, outHigh }, list);
        }

        [Test]
        public void ResultLine_CountsWhoIsStillFlying()
        {
            var mode = new RamRoyaleMode();
            var score = mode.CreateScore(16);
            for (int i = 0; i < 4; i++)
                score.Register(new object(), $"P{i}", i);
            score.RecordDeath(new object(), "Gone", 5, null, null, null);
            Assert.AreEqual("4 OF 5 STILL FLYING", mode.ResultLine(score, t => ""));
        }

        [Test]
        public void PilotColors_AreAllDifferent_AndMatchTeamsForTheFirstTwo()
        {
            var seen = new HashSet<Color>();
            for (int t = 0; t < 16; t++)
                Assert.IsTrue(seen.Add(FreeForAll.PilotColor(t)), $"pilot {t}'s colour is taken");
            Assert.AreEqual(new Color(0.35f, 0.6f, 1f), FreeForAll.PilotColor(0));
            Assert.AreEqual(new Color(1f, 0.38f, 0.32f), FreeForAll.PilotColor(1));
            Assert.AreEqual(FreeForAll.PilotColor(0), FreeForAll.PilotColor(16), "wraps");
        }

        [Test]
        public void Spectator_StartsOnTheNearestSurvivor()
        {
            var survivors = new[] { new Vector3(500f, 0f, 0f), new Vector3(0f, 0f, 90f), new Vector3(-200f, 0f, 0f) };
            Assert.AreEqual(1, FreeForAll.NearestSurvivor(survivors, Vector3.zero));
            Assert.AreEqual(-1, FreeForAll.NearestSurvivor(new Vector3[0], Vector3.zero));
        }

        [Test]
        public void Spectator_CyclesRoundTheSurvivors()
        {
            Assert.AreEqual(1, FreeForAll.Cycle(0, 3, 1));
            Assert.AreEqual(0, FreeForAll.Cycle(2, 3, 1), "wraps forward");
            Assert.AreEqual(2, FreeForAll.Cycle(0, 3, -1), "wraps back");
            Assert.AreEqual(0, FreeForAll.Cycle(-1, 3, 1), "watching nobody: the first");
            Assert.AreEqual(2, FreeForAll.Cycle(-1, 3, -1), "...or the last going back");
            Assert.AreEqual(-1, FreeForAll.Cycle(0, 0, 1), "nobody left");
        }
    }
}

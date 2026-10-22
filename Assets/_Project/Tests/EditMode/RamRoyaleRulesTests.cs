using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Match;

namespace WingingIt.Tests
{
    /// <summary>Ram Royale's rules: free for all, one life, the win and its tiebreaks, and who a spectator watches.</summary>
    public class RamRoyaleRulesTests
    {
        [Test]
        public void RamRoyale_IsSixteenPilotFreeForAll_InItsArena()
        {
            var mode = new RamRoyaleMode();
            Assert.IsTrue(mode.IsFreeForAll);
            Assert.AreEqual(16, mode.FreeForAllSize);
            Assert.AreEqual("RamRoyale", mode.ArenaId, "the arena applies even off the card (tests)");
            Assert.AreEqual(MatchWidgets.None, mode.Widgets & MatchWidgets.TeamScores, "no West / East scores in the bar");
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

        /// <summary>A Ram Royale score with <paramref name="count"/> pilots P0.. on teams 0..</summary>
        static (RamRoyaleMode mode, MatchScore score, object[] pilots) Match(int count)
        {
            var mode = new RamRoyaleMode();
            var score = mode.CreateScore(16);
            var pilots = new object[count];
            for (int i = 0; i < count; i++)
            {
                pilots[i] = new object();
                score.Register(pilots[i], $"P{i}", i);
            }
            return (mode, score, pilots);
        }

        static void KnockOut(RamRoyaleMode mode, MatchScore score, object[] pilots, int victim, int? killer = null) =>
            mode.RecordKill(score, new KillRecord(new PilotRef(pilots[victim], $"P{victim}", victim),
                killer is int k ? new PilotRef(pilots[k], $"P{k}", k) : default, killer));

        [Test]
        public void LastPlaneFlying_WinsAtOnce()
        {
            var (mode, score, pilots) = Match(3);
            KnockOut(mode, score, pilots, 1, 0);
            mode.Tick(score, 0.02f);
            Assert.IsNull(mode.EarlyWinner(score), "two still flying");
            KnockOut(mode, score, pilots, 2);
            Assert.AreEqual(0, mode.EarlyWinner(score), "P0 is the last plane flying");
            Assert.AreEqual(0, mode.Winner(score));
            Assert.AreEqual(3, score.Get(pilots[1]).Place);
            Assert.AreEqual(2, score.Get(pilots[2]).Place);
            Assert.AreEqual(0, score.Get(pilots[0]).Place, "still flying: no place yet");
            Assert.AreEqual("P0 · LAST PLANE FLYING", mode.ResultLine(score, t => $"P{t}"));
        }

        [Test]
        public void AlonePilot_DoesNotWinBeforeAnyoneJoins()
        {
            var (mode, score, _) = Match(1);
            Assert.IsNull(mode.EarlyWinner(score));
        }

        [Test]
        public void LastTwoDownTogether_ShareFirst_MoreRamsWins_ElseDraw()
        {
            var (mode, score, pilots) = Match(3);
            KnockOut(mode, score, pilots, 2);
            mode.Tick(score, 0.02f);
            mode.RecordRam(score, new PilotRef(pilots[0], "P0", 0), new PilotRef(pilots[1], "P1", 1), 240f, 240f);
            KnockOut(mode, score, pilots, 0, 1); // the head-on: both out on one frame
            KnockOut(mode, score, pilots, 1, 0);
            Assert.AreEqual(1, score.Get(pilots[0]).Place, "both 1st");
            Assert.AreEqual(1, score.Get(pilots[1]).Place);
            Assert.AreEqual(MatchScore.Draw, mode.EarlyWinner(score), "over, level on rams: a draw");

            score.Get(pilots[1]).Rams++;
            Assert.AreEqual(1, mode.EarlyWinner(score), "P1 had more rams");
        }

        [Test]
        public void Ram_CountsForEachPlaneThatDidDamage()
        {
            var (mode, score, pilots) = Match(2);
            var a = new PilotRef(pilots[0], "P0", 0);
            var b = new PilotRef(pilots[1], "P1", 1);
            mode.RecordRam(score, a, b, 30f, 100f); // a flew into b from behind: both hurt
            mode.RecordRam(score, a, b, 0f, 0f);    // a bump
            mode.RecordRam(score, b, a, 0f, 50f);   // b hit a, took nothing
            Assert.AreEqual(1, score.Get(pilots[0]).Rams);
            Assert.AreEqual(2, score.Get(pilots[1]).Rams);
        }

        [Test]
        public void AtTime_MostBodyHp_ThenMostRams()
        {
            var (mode, score, pilots) = Match(4);
            var hp = new Dictionary<object, float> { [pilots[0]] = 80f, [pilots[1]] = 200f, [pilots[2]] = 200f, [pilots[3]] = 300f };
            mode.BodyHealth = p => hp[p];
            KnockOut(mode, score, pilots, 3); // out: its HP doesn't count
            Assert.AreEqual(MatchScore.Draw, mode.Winner(score), "P1 and P2 level on HP and rams");
            score.Get(pilots[2]).Rams = 2;
            Assert.AreEqual(2, mode.Winner(score), "rams break the tie");
            hp[pilots[1]] = 210f;
            Assert.AreEqual(1, mode.Winner(score), "body HP comes first");
            Assert.IsNull(mode.EarlyWinner(score), "three still flying: only time ends it");
        }

        [Test]
        public void Scoreboard_FlyingByBodyHp_ThenOutByPlace()
        {
            var (mode, score, pilots) = Match(4);
            mode.BodyHealth = p => p == pilots[0] ? 50f : 150f;
            KnockOut(mode, score, pilots, 2);
            mode.Tick(score, 0.02f);
            KnockOut(mode, score, pilots, 3); // out later: the better place
            var list = new List<PilotScore>(score.Pilots);
            list.Sort(mode.CompareForScoreboard);
            CollectionAssert.AreEqual(new[] { "P1", "P0", "P3", "P2" }, list.ConvertAll(p => p.Name));
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

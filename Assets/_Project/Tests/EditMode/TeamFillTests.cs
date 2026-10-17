using System.Collections.Generic;
using NUnit.Framework;
using WingingIt.Bots;

namespace WingingIt.Tests
{
    public class TeamFillTests
    {
        static readonly int[] Mix = { 3, 2, 2, 1 }; // Fighter, Interceptor, Heavy, Support

        /// <summary>Picks <paramref name="bots"/> classes in a row, starting from <paramref name="counts"/>; returns the final counts.</summary>
        static int[] Fill(int[] counts, int bots, int[] weights = null)
        {
            weights ??= Mix;
            for (int i = 0; i < bots; i++)
                counts[TeamFill.PickClass(counts, weights)]++;
            return counts;
        }

        [Test]
        public void AnEmptyTeam_FillsToTheMix()
        {
            CollectionAssert.AreEqual(Mix, Fill(new int[4], 8));
        }

        [Test]
        public void EveryPrefix_IsAsCloseToTheMixAsItCanBe()
        {
            // 4 bots: one of each, so a small team still gets every role.
            CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, Fill(new int[4], 4));
            // First pick is a Fighter (the biggest share).
            CollectionAssert.AreEqual(new[] { 1, 0, 0, 0 }, Fill(new int[4], 1));
        }

        [Test]
        public void HumansCount_TowardTheMix()
        {
            // A human Fighter and a human Support: the 6 bots make up the rest.
            CollectionAssert.AreEqual(Mix, Fill(new[] { 1, 0, 0, 1 }, 6));
            // Three human Heavies: the bots skip Heavy entirely.
            var counts = Fill(new[] { 0, 0, 3, 0 }, 5);
            Assert.AreEqual(3, counts[2], "no Heavy bots on a team with three human Heavies");
            Assert.AreEqual(8, counts[0] + counts[1] + counts[2] + counts[3]);
        }

        [Test]
        public void AZeroWeight_IsNeverPicked()
        {
            var counts = Fill(new int[4], 16, new[] { 1, 0, 1, 0 });
            Assert.AreEqual(0, counts[1]);
            Assert.AreEqual(0, counts[3]);
            Assert.AreEqual(-1, TeamFill.PickClass(new int[2], new[] { 0, 0 }));
        }

        [Test]
        public void PatrolPairs_CoverEveryArea_AndNeverRepeatInATeam()
        {
            const int areas = 5;
            var visits = new int[areas];
            var pairs = new HashSet<(int, int)>();
            for (int i = 0; i < 8; i++)
            {
                var (from, to) = TeamFill.PatrolPair(i, areas);
                Assert.AreNotEqual(from, to, $"bot {i} patrols a single point");
                Assert.IsTrue(pairs.Add((from, to)), $"bot {i} repeats a route");
                visits[from]++;
                visits[to]++;
            }
            foreach (int v in visits)
                Assert.GreaterOrEqual(v, 2, "every area gets patrolled by at least two bots");
        }

        [Test]
        public void PatrolPair_HandlesFewAreas()
        {
            Assert.AreEqual((0, 0), TeamFill.PatrolPair(3, 0));
            Assert.AreEqual((0, 0), TeamFill.PatrolPair(3, 1));
            Assert.AreEqual((0, 1), TeamFill.PatrolPair(0, 2));
            Assert.AreEqual((1, 0), TeamFill.PatrolPair(1, 2));
        }

        [Test]
        public void BotsWanted_FillsTheSlotsHumansLeave()
        {
            Assert.AreEqual(8, TeamFill.BotsWanted(8, 0));
            Assert.AreEqual(7, TeamFill.BotsWanted(8, 1));
            Assert.AreEqual(0, TeamFill.BotsWanted(8, 8));
            Assert.AreEqual(0, TeamFill.BotsWanted(8, 10));
        }
    }
}

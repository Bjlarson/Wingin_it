using System.Linq;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Bots;
using WingingIt.World;

namespace WingingIt.Tests
{
    public class ModeArenaTests
    {
        static readonly Vector3 Center = new(100f, 0f, -2000f);

        [Test]
        public void RingSlots_AreEvenlyRoundTheRing_LevelAndFacingTheMiddle()
        {
            for (int i = 0; i < 16; i++)
            {
                Pose pose = ArenaRules.RingSlot(Center, 550f, 220f, i, 16);
                Vector3 flat = pose.position - Center;
                flat.y = 0f;
                Assert.AreEqual(550f, flat.magnitude, 0.01f, "on the ring");
                Assert.AreEqual(220f, pose.position.y, 0.01f, "at the start height");
                Vector3 forward = pose.rotation * Vector3.forward;
                Assert.AreEqual(0f, forward.y, 1e-4f, "level");
                Assert.Greater(Vector3.Dot(forward, -flat.normalized), 0.999f, "nose to the middle");
            }
            Vector3 first = ArenaRules.RingSlot(Center, 550f, 220f, 0, 16).position;
            Assert.AreEqual(Center.z + 550f, first.z, 0.01f, "slot 0 is due north");
            Vector3 second = ArenaRules.RingSlot(Center, 550f, 220f, 1, 16).position;
            Assert.AreEqual(2f * 550f * Mathf.Sin(Mathf.PI / 16f), Vector3.Distance(first, second), 0.01f, "even gaps");
            Assert.Greater(second.x, Center.x, "clockwise seen from above (north, then east)");
        }

        [Test]
        public void SpreadSlot_FillsTheRingFromOppositeSides()
        {
            var slots = Enumerable.Range(0, 16).Select(i => ArenaRules.RingSlot(Center, 550f, 220f, i, 16).position).ToArray();
            var taken = new System.Collections.Generic.List<Vector3>();
            Assert.AreEqual(0, ArenaRules.SpreadSlot(slots, taken), "an empty ring starts at slot 0");
            taken.Add(slots[0]);
            Assert.AreEqual(8, ArenaRules.SpreadSlot(slots, taken), "the second plane takes the far side");
            taken.Add(slots[8]);
            Assert.AreEqual(4, ArenaRules.SpreadSlot(slots, taken), "then halfway between");
            for (int n = taken.Count; n < 16; n++)
            {
                int pick = ArenaRules.SpreadSlot(slots, taken);
                Assert.IsFalse(taken.Contains(slots[pick]), "never a taken slot while one is free");
                taken.Add(slots[pick]);
            }
            Assert.AreEqual(-1, ArenaRules.SpreadSlot(new Vector3[0], taken), "no slots");
        }

        [Test]
        public void Bots_TurnFromAHardEdge_OnlyWhenHeadingOutAndCloseToIt()
        {
            Vector3 c = Vector3.zero;
            Vector3 nearEdge = new(0f, 200f, 600f); // 100 m inside a 700 m edge
            Assert.IsTrue(BotSenses.ShouldTurnFromEdge(nearEdge, new Vector3(0f, 0f, 60f), c, 700f), "heading straight out");
            Assert.IsFalse(BotSenses.ShouldTurnFromEdge(nearEdge, new Vector3(0f, 0f, -60f), c, 700f), "heading in");
            Assert.IsFalse(BotSenses.ShouldTurnFromEdge(nearEdge, new Vector3(60f, 0f, 0f), c, 700f, 50f, 1f), "along the edge, not soon past it");
            Assert.IsFalse(BotSenses.ShouldTurnFromEdge(new Vector3(0f, 200f, 100f), new Vector3(0f, 0f, 60f), c, 700f), "heading out but far from it");
            Assert.IsTrue(BotSenses.ShouldTurnFromEdge(new Vector3(0f, 200f, 750f), new Vector3(0f, 0f, 10f), c, 700f), "already past it");
        }

        [Test]
        public void ABoundary_TakesAModesArea_AndGivesTheLevelsBack()
        {
            var go = new GameObject("Boundary");
            try
            {
                var boundary = go.AddComponent<FlightBoundary>();
                go.transform.position = new Vector3(0f, 0f, 0f);
                boundary.radius = 2600f;
                boundary.ceiling = 1500f;
                int changes = 0;
                boundary.Changed += () => changes++;

                boundary.UseArea(new Vector3(0f, 99f, -1950f), 700f, 150f, 450f, true);
                Assert.IsTrue(boundary.IsOverridden);
                Assert.AreEqual(new Vector3(0f, 0f, -1950f), boundary.Center, "moved in XZ only");
                Assert.AreEqual(700f, boundary.radius);
                Assert.AreEqual(450f, boundary.ceiling);
                Assert.IsTrue(boundary.hardEdge);
                Assert.IsTrue(boundary.IsOutside(new Vector3(0f, 200f, -1200f)));
                Assert.IsFalse(boundary.IsOut(2.9f), "inside the grace");
                Assert.IsTrue(boundary.IsOut(3.1f), "out after it");

                boundary.UseArea(new Vector3(500f, 0f, 0f), 300f, 50f, 300f, false); // a second area still restores to the level's
                boundary.RestoreArea();
                Assert.IsFalse(boundary.IsOverridden);
                Assert.AreEqual(Vector3.zero, boundary.Center);
                Assert.AreEqual(2600f, boundary.radius);
                Assert.AreEqual(1500f, boundary.ceiling);
                Assert.IsFalse(boundary.hardEdge);
                Assert.IsFalse(boundary.IsOut(100f), "a soft edge never puts anyone out");
                Assert.AreEqual(3, changes);
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AClosedAirfield_ServesNobody()
        {
            var go = new GameObject("Field");
            try
            {
                var field = go.AddComponent<Airfield>();
                field.Configure("Home", 0, true, new Vector2(40f, 600f), null, null, null);
                Assert.IsTrue(field.IsFriendlyTo(0));
                field.SetOpen(false);
                Assert.IsFalse(field.IsFriendlyTo(0), "closed: no repairs or spawns");
                field.SetOpen(true);
                Assert.IsTrue(field.IsFriendlyTo(0));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}

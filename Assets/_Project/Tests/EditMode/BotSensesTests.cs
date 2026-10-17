using NUnit.Framework;
using UnityEngine;
using WingingIt.Bots;

namespace WingingIt.Tests
{
    public class BotSensesTests
    {
        [Test]
        public void TargetScore_PrefersATargetAhead_OverASlightlyCloserOneBehind()
        {
            float ahead = BotSenses.TargetScore(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 600f));
            float behind = BotSenses.TargetScore(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, -500f));
            Assert.Less(ahead, behind);
            Assert.AreEqual(600f, ahead, 0.01f, "straight ahead scores its plain distance");
        }

        [Test]
        public void TargetScore_PrefersATailShowingOrDamagedTarget()
        {
            var at = new Vector3(0f, 0f, 600f);
            float tail = BotSenses.TargetScore(Vector3.zero, Vector3.forward, at, Vector3.forward, 1f);
            float beam = BotSenses.TargetScore(Vector3.zero, Vector3.forward, at, Vector3.right, 1f);
            float headOn = BotSenses.TargetScore(Vector3.zero, Vector3.forward, at, Vector3.back, 1f);
            Assert.AreEqual(600f * 0.7f, tail, 0.01f);
            Assert.AreEqual(600f, beam, 0.01f, "a crossing target scores its plain distance");
            Assert.AreEqual(600f * 1.3f, headOn, 0.01f);

            float hurt = BotSenses.TargetScore(Vector3.zero, Vector3.forward, at, Vector3.right, 0.25f);
            Assert.AreEqual(600f * 0.7f, hurt, 0.01f);
            // A tail-on target 800 m out beats a head-on one at 600 m.
            Assert.Less(BotSenses.TargetScore(Vector3.zero, Vector3.forward, new Vector3(0f, 0f, 800f), Vector3.forward, 1f), headOn);
        }

        [Test]
        public void IsGunShot_NarrowsTheConeWithRange_AndStopsAtMaxRange()
        {
            // 2° off the nose: 7 m off at 200 m (a shot), 14 m off at 400 m (not, with a 7 m miss distance).
            Vector3 Off(float distance) => Quaternion.Euler(0f, 2f, 0f) * Vector3.forward * distance;
            Assert.IsTrue(BotSenses.IsGunShot(Vector3.zero, Vector3.forward, Off(200f), 3f, 7f, 550f));
            Assert.IsFalse(BotSenses.IsGunShot(Vector3.zero, Vector3.forward, Off(400f), 3f, 7f, 550f));
            Assert.IsTrue(BotSenses.IsGunShot(Vector3.zero, Vector3.forward, Vector3.forward * 500f, 3f, 7f, 550f));
            Assert.IsFalse(BotSenses.IsGunShot(Vector3.zero, Vector3.forward, Vector3.forward * 600f, 3f, 7f, 550f), "past max range");
            // Close in, the angle cap still applies.
            Assert.IsFalse(BotSenses.IsGunShot(Vector3.zero, Vector3.forward, Quaternion.Euler(0f, 5f, 0f) * Vector3.forward * 50f, 3f, 7f, 550f));
        }

        [Test]
        public void IsAimedAt_ChecksTheAngleOffTheNose()
        {
            Assert.IsTrue(BotSenses.IsAimedAt(Vector3.zero, Vector3.forward, new Vector3(10f, 0f, 400f), 3f)); // ~1.4°
            Assert.IsFalse(BotSenses.IsAimedAt(Vector3.zero, Vector3.forward, new Vector3(40f, 0f, 400f), 3f)); // ~5.7°
        }

        [Test]
        public void GunsCool_StopsBeforeOverheating_AndWaitsToCoolDown()
        {
            Assert.IsTrue(BotSenses.GunsCool(0.5f, wasFiring: true));
            Assert.IsFalse(BotSenses.GunsCool(0.9f, wasFiring: true), "should let go before the overheat lockout");
            Assert.IsFalse(BotSenses.GunsCool(0.6f, wasFiring: false), "should wait until cooled down");
            Assert.IsTrue(BotSenses.GunsCool(0.3f, wasFiring: false));
        }

        [Test]
        public void BreakSide_KeepsTheCurrentBank()
        {
            Vector3 bankedRight = Quaternion.Euler(0f, 0f, -30f) * Vector3.right; // right wing down
            Assert.AreEqual(1f, BotSenses.BreakSide(bankedRight, Vector3.zero, new Vector3(100f, 0f, -300f)));
            Vector3 bankedLeft = Quaternion.Euler(0f, 0f, 30f) * Vector3.right;
            Assert.AreEqual(-1f, BotSenses.BreakSide(bankedLeft, Vector3.zero, new Vector3(-100f, 0f, -300f)));
        }

        [Test]
        public void BreakSide_WingsLevel_TurnsAwayFromTheMissile()
        {
            Assert.AreEqual(-1f, BotSenses.BreakSide(Vector3.right, Vector3.zero, new Vector3(200f, 0f, -300f)));
            Assert.AreEqual(1f, BotSenses.BreakSide(Vector3.right, Vector3.zero, new Vector3(-200f, 0f, -300f)));
        }

        [Test]
        public void BreakTurnPoint_IsWellOffTheNose_OnTheChosenSide_AtOurAltitude()
        {
            var pos = new Vector3(0f, 300f, 0f);
            Vector3 right = BotSenses.BreakTurnPoint(pos, Vector3.forward, 1f);
            Vector3 left = BotSenses.BreakTurnPoint(pos, Vector3.forward, -1f);
            Assert.Greater(right.x, 300f);
            Assert.Less(left.x, -300f);
            Assert.AreEqual(300f, right.y, 0.01f);
            Assert.AreEqual(100f, Vector3.Angle(Vector3.forward, right - pos), 0.1f);
        }

        [Test]
        public void TooLow_BelowClearanceAndNotClimbing()
        {
            Assert.IsTrue(BotSenses.TooLow(30f, 0f, 50f, 3f));
            Assert.IsFalse(BotSenses.TooLow(30f, 10f, 50f, 3f), "climbing away is fine");
            Assert.IsFalse(BotSenses.TooLow(200f, 0f, 50f, 3f));
        }

        [Test]
        public void TooLow_DivingAtTheGround_ReactsEarly()
        {
            Assert.IsTrue(BotSenses.TooLow(200f, -80f, 50f, 3f), "hits the ground in 2.5 s");
            Assert.IsFalse(BotSenses.TooLow(400f, -80f, 50f, 3f));
        }

        [Test]
        public void EscapePoint_FromFlatGround_ClimbsStraightAhead()
        {
            Vector3 down = Quaternion.Euler(30f, 0f, 0f) * Vector3.forward; // diving
            Vector3 p = BotSenses.EscapePoint(Vector3.zero, down, Vector3.up);
            Assert.Greater(p.y, 150f);
            Assert.AreEqual(0f, p.x, 0.01f, "no reason to turn");
            Assert.Greater(p.z, 0f);
        }

        [Test]
        public void EscapePoint_FromAWall_TurnsAsideAndClimbs()
        {
            // Wall ahead, angled so its face points back and to the left.
            Vector3 normal = new Vector3(-1f, 0f, -1f).normalized;
            Vector3 p = BotSenses.EscapePoint(Vector3.zero, Vector3.forward, normal);
            Assert.Less(p.x, -50f, "should turn the way the wall faces");
            Assert.Greater(p.y, 100f);
        }

        [Test]
        public void EscapePoint_StraightAtAWall_StillTurns()
        {
            Vector3 p = BotSenses.EscapePoint(Vector3.zero, Vector3.forward, Vector3.back);
            Assert.Greater(Mathf.Abs(p.x), 50f);
            Assert.Greater(p.y, 100f);
        }

        [Test]
        public void OnCollisionCourse_HeadOn()
        {
            // 400 m apart, closing at 160 m/s, 10 m to the side: hits in 2.5 s.
            Assert.IsTrue(BotSenses.OnCollisionCourse(new Vector3(10f, 0f, 400f), new Vector3(0f, 0f, -160f), 3f, 35f, out var miss));
            Assert.AreEqual(10f, miss.x, 0.01f);
            Assert.IsFalse(BotSenses.OnCollisionCourse(new Vector3(10f, 0f, 400f), new Vector3(0f, 0f, -160f), 2f, 35f, out _),
                "too far off to matter yet");
        }

        [Test]
        public void OnCollisionCourse_PassingWideOrPullingAway_IsFine()
        {
            Assert.IsFalse(BotSenses.OnCollisionCourse(new Vector3(80f, 0f, 200f), new Vector3(0f, 0f, -160f), 3f, 35f, out _));
            Assert.IsFalse(BotSenses.OnCollisionCourse(new Vector3(0f, 0f, 60f), new Vector3(0f, 0f, 10f), 3f, 35f, out _));
        }

        [Test]
        public void OnCollisionCourse_TrailingATargetAtGunRange_IsFine()
        {
            Assert.IsFalse(BotSenses.OnCollisionCourse(new Vector3(0f, 0f, 150f), new Vector3(0f, 0f, -8f), 2f, 35f, out _));
        }

        [Test]
        public void SidestepPoint_GoesAwayFromTheOtherPlane()
        {
            Vector3 p = BotSenses.SidestepPoint(Vector3.zero, Vector3.forward, new Vector3(0f, 10f, 0f));
            Assert.Less(p.y, -50f);
            Assert.Greater(p.z, 0f);
        }

        [Test]
        public void SidestepPoint_DeadOn_BothBreakRight_AndSeparate()
        {
            Vector3 a = BotSenses.SidestepPoint(Vector3.zero, Vector3.forward, Vector3.zero);
            Vector3 b = BotSenses.SidestepPoint(new Vector3(0f, 0f, 400f), Vector3.back, Vector3.zero);
            Assert.Greater(a.x, 50f);
            Assert.Less(b.x, -50f);
        }

        [Test]
        public void NeedsRepair_WhenAPartIsLost_OrTheBodyIsLow()
        {
            Assert.IsFalse(BotSenses.NeedsRepair(1f, 0, 0.4f));
            Assert.IsFalse(BotSenses.NeedsRepair(0.5f, 0, 0.4f), "half a body is still fighting fit");
            Assert.IsTrue(BotSenses.NeedsRepair(0.35f, 0, 0.4f));
            Assert.IsTrue(BotSenses.NeedsRepair(1f, 1 << 3, 0.4f), "any lost part sends it home");
        }

        static readonly Vector3[] Lane =
        {
            new Vector3(0f, 100f, 0f), new Vector3(1000f, 100f, 0f), new Vector3(2000f, 100f, 0f), new Vector3(2000f, 30f, 600f),
        };

        [Test]
        public void LaneJoinIndex_FromBeforeTheEntry_StartsAtTheEntry()
        {
            int i = BotSenses.LaneJoinIndex(Lane, new Vector3(-800f, 100f, 0f), out float cost);
            Assert.AreEqual(0, i);
            Assert.AreEqual(800f + 2000f + Vector3.Distance(Lane[2], Lane[3]), cost, 0.1f);
        }

        [Test]
        public void LaneJoinIndex_FromBesideTheLane_JoinsAtTheNextPoint()
        {
            Assert.AreEqual(1, BotSenses.LaneJoinIndex(Lane, new Vector3(500f, 100f, 300f), out float cost));
            Assert.AreEqual(300f + 1500f + Vector3.Distance(Lane[2], Lane[3]), cost, 0.1f);
            Assert.AreEqual(2, BotSenses.LaneJoinIndex(Lane, new Vector3(1100f, 100f, -300f), out _));
        }

        [Test]
        public void LaneJoinIndex_WellOutOnTheLastLeg_HeadsForTheFix()
        {
            Assert.AreEqual(3, BotSenses.LaneJoinIndex(Lane, new Vector3(2010f, 60f, 100f), out _));
        }

        [Test]
        public void LaneJoinIndex_NeverJoinsAtTheFinalFix()
        {
            // Right next to the fix, it still flies the leg before it, to arrive lined up.
            Assert.AreEqual(2, BotSenses.LaneJoinIndex(Lane, new Vector3(2000f, 30f, 650f), out _));
        }

        [Test]
        public void PickLane_ChoosesAmongTheNearlyShortest()
        {
            var costs = new[] { 3000f, 2000f, 2400f, float.MaxValue };
            Assert.AreEqual(1, BotSenses.PickLane(costs, 0f));
            Assert.AreEqual(2, BotSenses.PickLane(costs, 0.99f), "within 1.3x of the shortest: a fair pick");
            Assert.AreEqual(1, BotSenses.PickLane(costs, 0.99f, slack: 1.1f), "too long with less slack");
            Assert.AreEqual(-1, BotSenses.PickLane(new float[0], 0.5f));
        }

        [Test]
        public void ShouldGoAround_OnlyWhenTheLandingIsBlown()
        {
            Assert.AreEqual(GoAroundReason.None, BotSenses.ShouldGoAround(true, 0f, 400f, 5f, 20f, false), "on the path");
            Assert.AreEqual(GoAroundReason.None, BotSenses.ShouldGoAround(false, 2f, 500f, 80f, 20f, false), "still turning onto final");
            Assert.AreEqual(GoAroundReason.OffApproach, BotSenses.ShouldGoAround(false, 5f, 500f, 80f, 20f, false), "never got onto it");
            Assert.AreEqual(GoAroundReason.RunwayBusy, BotSenses.ShouldGoAround(true, 0f, 500f, 0f, 20f, true), "runway taken while still well out");
            Assert.AreEqual(GoAroundReason.None, BotSenses.ShouldGoAround(true, 0f, 100f, 0f, 20f, true), "too late: commit");
            Assert.AreEqual(GoAroundReason.Overshot, BotSenses.ShouldGoAround(true, 0f, -300f, 0f, 20f, false), "floated way past the aim point");
            Assert.AreEqual(GoAroundReason.OffCenter, BotSenses.ShouldGoAround(true, 0f, 80f, 45f, 20f, false), "about to touch down beside the runway");
        }
    }
}

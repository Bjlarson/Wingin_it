using NUnit.Framework;
using UnityEngine;
using WingingIt.Bots;

namespace WingingIt.Tests
{
    public class BotSteeringTests
    {
        // ------------------------------------------------------------------ throttle

        [Test]
        public void Overtake_ClosesFastFromRange_AndEasesOffAtTheGap()
        {
            Assert.AreEqual(25f, BotSteering.Overtake(900f, 300f, 0.08f, 25f), 0.01f, "capped far out");
            Assert.AreEqual(8f, BotSteering.Overtake(400f, 300f, 0.08f, 25f), 0.01f);
            Assert.AreEqual(0f, BotSteering.Overtake(300f, 300f, 0.08f, 25f), 0.01f, "matches speed at the gap");
            Assert.AreEqual(-8f, BotSteering.Overtake(200f, 300f, 0.08f, 25f), 0.01f, "drops back when too close");
            Assert.AreEqual(-12.5f, BotSteering.Overtake(0f, 300f, 0.08f, 25f), 0.01f, "backs off at most half the cap");
        }

        [Test]
        public void Throttle_PushesUp_WhenTooSlow_AndDown_WhenTooFast()
        {
            // 40..110 m/s: throttle 0.5 flies 75 m/s.
            Assert.Greater(BotSteering.ThrottleCommand(0.5f, 100f, 40f, 110f, 1f, false), 0f);
            Assert.Less(BotSteering.ThrottleCommand(0.5f, 50f, 40f, 110f, 1f, false), 0f);
            Assert.AreEqual(0f, BotSteering.ThrottleCommand(0.5f, 75f, 40f, 110f, 1f, false), 1e-4f);
        }

        [Test]
        public void Throttle_IsAStickInput_ClampedToOne()
        {
            Assert.AreEqual(1f, BotSteering.ThrottleCommand(0f, 500f, 40f, 110f, 1f, false));
            Assert.AreEqual(-1f, BotSteering.ThrottleCommand(1f, 0f, 40f, 110f, 1f, false));
        }

        [Test]
        public void Throttle_AccountsForLostThrust()
        {
            // Half thrust: 75 m/s needs the throttle that would fly 150 m/s at full thrust, i.e. wide open.
            Assert.AreEqual(1f, BotSteering.ThrottleCommand(0.5f, 75f, 40f, 110f, 0.5f, false));
        }

        [Test]
        public void Throttle_DeadEngine_LeavesTheLeverAlone()
        {
            Assert.AreEqual(0f, BotSteering.ThrottleCommand(0.3f, 100f, 40f, 110f, 0f, false));
        }

        [Test]
        public void Throttle_OnTheWheels_IsAFractionOfMaxSpeed()
        {
            // On the ground 0.5 throttle rolls at 55 m/s (half of 110), not 75.
            Assert.AreEqual(0f, BotSteering.ThrottleCommand(0.5f, 55f, 40f, 110f, 1f, true), 1e-4f);
            Assert.Greater(BotSteering.ThrottleCommand(0f, 55f, 40f, 110f, 1f, true), 0f, "a bot on the brake must throttle up to roll");
        }

        // ------------------------------------------------------------------ intercept

        [Test]
        public void Intercept_LeadsAMovingTarget()
        {
            Vector3 p = BotSteering.InterceptPoint(Vector3.zero, 100f, new Vector3(0f, 0f, 200f), new Vector3(50f, 0f, 0f));
            // 2 s to close 200 m at 100 m/s: the target will be 100 m to the right.
            Assert.AreEqual(new Vector3(100f, 0f, 200f).x, p.x, 1e-3f);
            Assert.AreEqual(200f, p.z, 1e-3f);
        }

        [Test]
        public void Intercept_CapsTheLeadTime()
        {
            Vector3 p = BotSteering.InterceptPoint(Vector3.zero, 50f, new Vector3(0f, 0f, 5000f), new Vector3(100f, 0f, 0f), maxLeadTime: 3f);
            Assert.AreEqual(300f, p.x, 1e-3f);
        }

        // ------------------------------------------------------------------ formation

        [Test]
        public void Slot_IsInTheLeadersLevelFrame()
        {
            // Leader heading east (+x), banked 60°: "left and behind" is north-west of it, at its own height.
            Quaternion rot = Quaternion.LookRotation(Vector3.right) * Quaternion.Euler(0f, 0f, 60f);
            Vector3 slot = BotSteering.SlotPosition(new Vector3(0f, 500f, 0f), rot, new Vector3(-40f, 0f, -40f));
            Assert.AreEqual(-40f, slot.x, 1e-3f);
            Assert.AreEqual(500f, slot.y, 1e-3f);
            Assert.AreEqual(40f, slot.z, 1e-3f);
        }

        [Test]
        public void SlotSpeed_SpeedsUpBehind_SlowsDownAhead()
        {
            Vector3 slot = new Vector3(0f, 0f, 100f);
            Assert.Greater(BotSteering.SlotSpeed(Vector3.zero, slot, Vector3.forward, 80f), 80f);
            Assert.Less(BotSteering.SlotSpeed(new Vector3(0f, 0f, 200f), slot, Vector3.forward, 80f), 80f);
            Assert.AreEqual(80f, BotSteering.SlotSpeed(slot, slot, Vector3.forward, 80f), 1e-4f);
        }

        // ------------------------------------------------------------------ orbit

        [Test]
        public void OrbitPoint_IsOnTheCircle_AtTheOrbitAltitude()
        {
            Vector3 p = BotSteering.OrbitPoint(new Vector3(500f, 100f, 0f), Vector3.back, Vector3.zero, 300f, 450f, clockwise: true);
            Assert.AreEqual(300f, new Vector2(p.x, p.z).magnitude, 1e-2f);
            Assert.AreEqual(450f, p.y, 1e-4f);
        }

        [Test]
        public void OrbitPoint_LeadsInTheTurnDirection()
        {
            // East of the center: clockwise (seen from above) leads south, counter-clockwise north.
            Vector3 cw = BotSteering.OrbitPoint(new Vector3(300f, 0f, 0f), Vector3.back, Vector3.zero, 300f, 0f, clockwise: true);
            Vector3 ccw = BotSteering.OrbitPoint(new Vector3(300f, 0f, 0f), Vector3.forward, Vector3.zero, 300f, 0f, clockwise: false);
            Assert.Less(cw.z, 0f);
            Assert.Greater(ccw.z, 0f);
        }

        // ------------------------------------------------------------------ stick

        [Test]
        public void Slew_LimitsHowFastTheStickMoves()
        {
            Assert.AreEqual(0.1f, BotSteering.Slew(0f, 1f, 5f, 0.02f), 1e-5f);
            Assert.AreEqual(1f, BotSteering.Slew(0.95f, 1f, 5f, 0.02f), 1e-5f);
            Assert.AreEqual(-1f, BotSteering.Slew(0f, -1f, 0f, 0.02f), 1e-5f, "rate 0 = instant");
        }

        [Test]
        public void GlideCarrot_SitsOnTheGlidePath_AheadOfThePlane()
        {
            float tan3 = Mathf.Tan(3f * Mathf.Deg2Rad);
            Vector3 p = BotSteering.GlideCarrot(1000f, 250f, 3f, -240f);
            Assert.AreEqual(0f, p.x, 1e-4f, "on the centerline");
            Assert.AreEqual(-240f - 750f, p.z, 1e-3f, "250 m closer to the aim point");
            Assert.AreEqual(750f * tan3, p.y, 1e-3f, "at the glide path's height there");
        }

        [Test]
        public void GlideCarrot_PastTheAimPoint_CarriesOnBelowTheRunway()
        {
            Vector3 p = BotSteering.GlideCarrot(100f, 250f, 3f, -240f);
            Assert.Less(p.y, 0f, "keeps descending into the touchdown instead of flattening out");
            Assert.AreEqual(-240f + 150f, p.z, 1e-3f);
        }

        [Test]
        public void FlightPathAim_RaisesTheNose_ByHowMuchThePathSags()
        {
            // Nose level, but sinking at 10 m/s at 50 m/s: aim above the point so the flight path reaches it.
            Vector3 point = new Vector3(0f, 0f, 500f);
            Vector3 aim = BotSteering.FlightPathAim(Vector3.zero, Vector3.forward, new Vector3(0f, -10f, 49f), point);
            Assert.Greater(aim.y, 50f);
            Assert.AreEqual(point, BotSteering.FlightPathAim(Vector3.zero, Vector3.forward, Vector3.forward * 50f, point),
                "no sag: the point itself");
        }

        [Test]
        public void SplitAim_TurnsLikeOnePoint_AndDescendsLikeTheOther()
        {
            Vector3 aim = BotSteering.SplitAim(Vector3.zero, new Vector3(100f, 0f, 100f), new Vector3(0f, -30f, 600f));
            Assert.AreEqual(45f, Vector3.SignedAngle(Vector3.forward, Vector3.ProjectOnPlane(aim, Vector3.up), Vector3.up), 0.1f);
            Assert.AreEqual(-30f / 600f, aim.y / Vector3.ProjectOnPlane(aim, Vector3.up).magnitude, 1e-4f, "the far point's descent angle");
        }
    }
}

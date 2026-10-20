using NUnit.Framework;
using UnityEngine;
using WingingIt.Bots;
using WingingIt.Match;

namespace WingingIt.Tests
{
    public class BannerPlanTests
    {
        [Test]
        public void Slots_SevenBotsGetTwoRunnersTwoDefendersThreeEscorts()
        {
            int runners = 0, defenders = 0, escorts = 0;
            for (int i = 0; i < 7; i++)
                switch (BannerPlan.RoleForSlot(i))
                {
                    case BannerRole.Runner: runners++; break;
                    case BannerRole.Defender: defenders++; break;
                    case BannerRole.Escort: escorts++; break;
                }
            Assert.AreEqual((2, 2, 3), (runners, defenders, escorts));
        }

        [Test]
        public void Carrier_AlwaysDelivers()
        {
            foreach (BannerRole role in System.Enum.GetValues(typeof(BannerRole)))
                Assert.AreEqual(BannerDuty.Deliver, BannerPlan.Decide(role, true, BannerState.Home, false));
        }

        [Test]
        public void Runner_GrabsUnlessAnAllyIsAlreadyTowing()
        {
            Assert.AreEqual(BannerDuty.Grab, BannerPlan.Decide(BannerRole.Runner, false, BannerState.Home, false));
            Assert.AreEqual(BannerDuty.Escort, BannerPlan.Decide(BannerRole.Runner, false, BannerState.Home, true));
        }

        [Test]
        public void Defender_GuardsReturnsOrChases()
        {
            Assert.AreEqual(BannerDuty.Guard, BannerPlan.Decide(BannerRole.Defender, false, BannerState.Home, false));
            Assert.AreEqual(BannerDuty.ReturnDropped, BannerPlan.Decide(BannerRole.Defender, false, BannerState.Dropped, false));
            Assert.AreEqual(BannerDuty.ChaseCarrier, BannerPlan.Decide(BannerRole.Defender, false, BannerState.Carried, false));
        }

        [Test]
        public void Escort_FollowsOurCarrierElseHuntsTheirs()
        {
            Assert.AreEqual(BannerDuty.Escort, BannerPlan.Decide(BannerRole.Escort, false, BannerState.Carried, true));
            Assert.AreEqual(BannerDuty.ChaseCarrier, BannerPlan.Decide(BannerRole.Escort, false, BannerState.Carried, false));
            Assert.AreEqual(BannerDuty.None, BannerPlan.Decide(BannerRole.Escort, false, BannerState.Home, false));
        }

        [Test]
        public void NoRole_NeverGetsADuty()
        {
            Assert.AreEqual(BannerDuty.None, BannerPlan.Decide(BannerRole.None, false, BannerState.Carried, false));
        }

        [Test]
        public void RunPoint_IsLowAndBesideThePole()
        {
            var p = BannerPlan.RunPoint(new Vector3(0, 5, 0), new Vector3(0, 100, -500), 5f);
            Assert.AreEqual(5f + BannerPlan.RunHeight, p.y, 0.01f);
            Assert.AreEqual(BannerPlan.PoleOffset, new Vector2(p.x, p.z).magnitude, 0.01f);
            Assert.AreEqual(0f, p.z, 0.01f, "offset is sideways to the line of approach");
        }
    }
}

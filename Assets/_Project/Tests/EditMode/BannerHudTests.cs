using NUnit.Framework;
using WingingIt.Match;
using WingingIt.UI;

namespace WingingIt.Tests
{
    public class BannerHudTests
    {
        [Test]
        public void Status_ReadsHomeTakenAndDropped()
        {
            Assert.AreEqual("HOME", HudText.BannerStatus(BannerState.Home, null, 0f));
            Assert.AreEqual("TAKEN BY EAST FIGHTER 2", HudText.BannerStatus(BannerState.Carried, "EAST FIGHTER 2", 0f));
            Assert.AreEqual("DROPPED  12 S", HudText.BannerStatus(BannerState.Dropped, null, 11.2f));
        }

        [Test]
        public void Callout_EnemyGrabbingOurBanner_IsBadNews()
        {
            string text = HudText.BannerCallout(BannerEventKind.Grabbed, 0, 1, 0, "EAST FIGHTER 2", "EAST", out var tone);
            Assert.AreEqual("EAST HAS YOUR BANNER", text);
            Assert.AreEqual(HudText.Tone.Bad, tone);
        }

        [Test]
        public void Callout_WeGrabbing_TellsUsToRun()
        {
            string text = HudText.BannerCallout(BannerEventKind.Grabbed, 1, 0, 0, "YOU", "WEST", out var tone);
            StringAssert.Contains("RUN IT HOME", text);
            Assert.AreEqual(HudText.Tone.Good, tone);
        }

        [Test]
        public void Callout_Capture_IsGoodForTheCapturersAndBadForTheOthers()
        {
            Assert.AreEqual("CAPTURED!", HudText.BannerCallout(BannerEventKind.Captured, 1, 0, 0, "YOU", "WEST", out var mine));
            Assert.AreEqual(HudText.Tone.Good, mine);
            Assert.AreEqual("WEST CAPTURED YOUR BANNER", HudText.BannerCallout(BannerEventKind.Captured, 1, 0, 1, "X", "WEST", out var theirs));
            Assert.AreEqual(HudText.Tone.Bad, theirs);
        }

        [Test]
        public void Callout_CarrierDown_IsGoodWhenItWasTheirCarrier()
        {
            HudText.BannerCallout(BannerEventKind.Dropped, 0, 1, 0, "X", "EAST", out var ours);
            Assert.AreEqual(HudText.Tone.Good, ours, "our banner was loose: we shot the carrier");
            HudText.BannerCallout(BannerEventKind.Dropped, 1, 0, 0, "X", "WEST", out var theirs);
            Assert.AreEqual(HudText.Tone.Bad, theirs, "we lost our carrier");
        }
    }
}

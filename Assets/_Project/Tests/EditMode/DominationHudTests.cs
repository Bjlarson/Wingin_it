using NUnit.Framework;
using WingingIt.UI;

namespace WingingIt.Tests
{
    public class DominationHudTests
    {
        [Test]
        public void FieldLetters_RunAlphabetically_AndNamesDropAirfield()
        {
            Assert.AreEqual("A", HudText.FieldLetter(0));
            Assert.AreEqual("C", HudText.FieldLetter(2));
            Assert.AreEqual("CENTRAL", HudText.FieldName("Central Airfield"));
            Assert.AreEqual("SOUTH EAST", HudText.FieldName("South East Airfield"));
            Assert.AreEqual("HANGAR", HudText.FieldName("Hangar"));
        }

        [Test]
        public void Status_ContestedBeatsProgress_ProgressBeatsTowerDown()
        {
            Assert.AreEqual("CONTESTED", HudText.FieldStatus("WEST", true, "EAST", 0.4f, true));
            Assert.AreEqual("EAST 45%", HudText.FieldStatus("WEST", false, "EAST", 0.459f, true));
            Assert.AreEqual("TOWER DOWN", HudText.FieldStatus("WEST", false, null, 0f, true));
            Assert.AreEqual("WEST", HudText.FieldStatus("WEST", false, null, 0f, false));
            Assert.AreEqual("NEUTRAL", HudText.FieldStatus(null, false, null, 0f, true), "a neutral field's tower means nothing");
        }

        [Test]
        public void CaptureCallout_ReadsForUsAndForThem()
        {
            Assert.AreEqual("YOU CAPTURED CENTRAL", HudText.FieldCallout("CENTRAL", 0, 0, true, "WEST", out var you));
            Assert.AreEqual(HudText.Tone.Good, you);
            Assert.AreEqual("WE CAPTURED CENTRAL", HudText.FieldCallout("CENTRAL", 0, 0, false, "WEST", out _));
            Assert.AreEqual("CENTRAL CAPTURED BY EAST", HudText.FieldCallout("CENTRAL", 1, 0, false, "EAST", out var them));
            Assert.AreEqual(HudText.Tone.Bad, them);
        }

        [Test]
        public void TowerCallout_IsBadForTheOwner_GoodForTheAttackers_AndSilentWhenNeutral()
        {
            StringAssert.Contains("NO AA OR REPAIR", HudText.TowerCallout("CENTRAL", 0, 0, true, out var ours));
            Assert.AreEqual(HudText.Tone.Bad, ours);
            StringAssert.Contains("FULL-SPEED CAPTURE", HudText.TowerCallout("CENTRAL", 1, 0, true, out var theirs));
            Assert.AreEqual(HudText.Tone.Good, theirs);
            Assert.AreEqual("CENTRAL TOWER REBUILT", HudText.TowerCallout("CENTRAL", 0, 0, false, out var rebuilt));
            Assert.AreEqual(HudText.Tone.Good, rebuilt);
            Assert.IsNull(HudText.TowerCallout("CENTRAL", -1, 0, true, out _));
        }

        [Test]
        public void HoldAllCallout_CountsDownForUs_WarnsWhenItsThem()
        {
            Assert.AreEqual("WE HOLD EVERY FIELD: 30 S TO WIN", HudText.HoldAllCallout(0, 0, "WEST", 30f, out var ours));
            Assert.AreEqual(HudText.Tone.Good, ours);
            Assert.AreEqual("EAST HOLDS EVERY FIELD: TAKE ONE BACK", HudText.HoldAllCallout(1, 0, "EAST", 30f, out var theirs));
            Assert.AreEqual(HudText.Tone.Bad, theirs);
        }

        [Test]
        public void CaptureHint_FollowsWhatTheBoardIsDoing()
        {
            Assert.AreEqual("CONTESTED: ENEMY ON THE FIELD", HudText.CaptureHint(0, -1, 0, 0.3f, true, "WEST"));
            Assert.AreEqual("CAPTURING 30%", HudText.CaptureHint(0, -1, 0, 0.3f, false, "WEST"));
            Assert.AreEqual("CAPTURING 0%", HudText.CaptureHint(0, 1, -1, 0f, false, null));
            Assert.AreEqual("UNDOING EAST 60%", HudText.CaptureHint(0, -1, 1, 0.6f, false, "EAST"));
            Assert.AreEqual("HELD BY YOUR TEAM", HudText.CaptureHint(0, 0, -1, 0f, false, null));
        }
    }
}

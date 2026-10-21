using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using WingingIt.Bots;
using WingingIt.Match;

namespace WingingIt.Tests
{
    public class DominationPlanTests
    {
        const int West = 0, East = 1;

        static FieldBoard.Field Field(int owner, bool towerUp = true, int capturing = -1, float progress = 0f, bool contested = false) =>
            new() { Owner = owner, TowerUp = towerUp, CapturingTeam = capturing, Progress = progress, Contested = contested };

        [Test]
        public void Slots_SevenBotsGetFourCapturersThreeCover()
        {
            int capturers = 0, cover = 0;
            for (int i = 0; i < 7; i++)
                if (DominationPlan.RoleForSlot(i) == DominationRole.Capturer)
                    capturers++;
                else
                    cover++;
            Assert.AreEqual((4, 3), (capturers, cover));
        }

        [Test]
        public void FieldValue_OurFieldUnderAttackFirst_OurQuietFieldLast()
        {
            float attacked = DominationPlan.FieldValue(West, Field(West, capturing: East, progress: 0.3f));
            float contested = DominationPlan.FieldValue(West, Field(West, contested: true));
            float neutral = DominationPlan.FieldValue(West, Field(FieldBoard.Neutral));
            float towerDown = DominationPlan.FieldValue(West, Field(East, towerUp: false));
            float defended = DominationPlan.FieldValue(West, Field(East));
            float quiet = DominationPlan.FieldValue(West, Field(West));

            Assert.AreEqual(attacked, contested);
            Assert.Greater(attacked, neutral);
            Assert.AreEqual(neutral, towerDown, "a field with its tower down is as open as a neutral one");
            Assert.Greater(neutral, defended);
            Assert.Greater(defended, quiet);
            Assert.Greater(quiet, 0f, "our quiet field still gets a little cover");
        }

        [Test]
        public void FieldValue_OurOwnProgressDrainingIsNoThreat()
        {
            Assert.AreEqual(DominationPlan.FieldValue(West, Field(West)),
                DominationPlan.FieldValue(West, Field(West, capturing: West, progress: 0.4f)));
        }

        [Test]
        public void PickField_SevenSlotsSplitByValue()
        {
            var values = new List<float> { 3f, 3f, 1f };
            var counts = new int[3];
            for (int slot = 0; slot < 7; slot++)
                counts[DominationPlan.PickField(values, slot)]++;
            Assert.That(counts[0], Is.InRange(2, 4));
            Assert.That(counts[1], Is.InRange(2, 4));
            Assert.That(counts[2], Is.InRange(0, 2));
            Assert.Greater(counts[0] + counts[1], counts[2] * 2);
        }

        [Test]
        public void PickField_EqualValuesCoverEveryField()
        {
            var values = new List<float> { 3f, 3f, 3f };
            var counts = new int[3];
            for (int slot = 0; slot < 7; slot++)
                counts[DominationPlan.PickField(values, slot)]++;
            foreach (int c in counts)
                Assert.That(c, Is.InRange(2, 3));
        }

        [Test]
        public void PickField_ZeroValueFieldIsSkipped_NoFieldsIsMinusOne()
        {
            var values = new List<float> { 0f, 2f, 0f };
            for (int slot = 0; slot < 7; slot++)
                Assert.AreEqual(1, DominationPlan.PickField(values, slot));
            Assert.AreEqual(-1, DominationPlan.PickField(new List<float>(), 0));
        }

        [Test]
        public void BombTarget_NearestDefendedEnemyField()
        {
            var fields = new List<FieldBoard.Field>
            {
                Field(East),                     // defended, far
                Field(East, towerUp: false),     // tower already down
                Field(FieldBoard.Neutral),       // nothing to bomb
                Field(West),                     // ours
                Field(East),                     // defended, near
            };
            var positions = new List<Vector3>
            {
                new(3000f, 0f, 0f), new(100f, 0f, 0f), new(50f, 0f, 0f), new(10f, 0f, 0f), new(800f, 0f, 0f),
            };
            Assert.AreEqual(4, DominationPlan.BombTarget(West, fields, positions, Vector3.zero));
            Assert.AreEqual(3, DominationPlan.BombTarget(East, fields, positions, Vector3.zero), "East bombs West's field");
            fields[3].TowerUp = false;
            Assert.AreEqual(-1, DominationPlan.BombTarget(East, fields, positions, Vector3.zero), "nothing left standing to bomb");
        }

        [Test]
        public void Decide_CapturersLandOnWhatIsntOurs_CoverOtherwise()
        {
            Assert.AreEqual(DominationDuty.Capture, DominationPlan.Decide(DominationRole.Capturer, West, FieldBoard.Neutral));
            Assert.AreEqual(DominationDuty.Capture, DominationPlan.Decide(DominationRole.Capturer, West, East));
            Assert.AreEqual(DominationDuty.Cover, DominationPlan.Decide(DominationRole.Capturer, West, West));
            Assert.AreEqual(DominationDuty.Cover, DominationPlan.Decide(DominationRole.Cover, West, East));
            Assert.AreEqual(DominationDuty.None, DominationPlan.Decide(DominationRole.None, West, East));
        }

        [Test]
        public void LeaveField_WhenTakenOrAnEnemyLands()
        {
            Assert.IsFalse(DominationPlan.ShouldLeaveField(true, false));
            Assert.IsTrue(DominationPlan.ShouldLeaveField(false, false), "captured: ours now");
            Assert.IsTrue(DominationPlan.ShouldLeaveField(true, true), "an enemy landed too");
        }

        [Test]
        public void Drop_OnTheTowerOrCloseToItsFoot()
        {
            var tower = new Vector3(100f, 0f, 100f);
            Assert.IsTrue(DominationPlan.ShouldDrop(new Vector3(500f, 0f, 500f), true, tower));
            Assert.IsTrue(DominationPlan.ShouldDrop(tower + new Vector3(6f, 3f, 6f), false, tower));
            Assert.IsFalse(DominationPlan.ShouldDrop(tower + new Vector3(20f, 0f, 0f), false, tower));
        }

        [Test]
        public void BombRun_FlyLevelAtTheTower_ThenRunOutAndTurnBack()
        {
            var tower = new Vector3(0f, 30f, 0f);
            bool extending = false;

            // Inbound: straight at the tower, at bomb height above the field.
            Vector3 p = DominationPlan.BombRunPoint(tower, 10f, new Vector3(0f, 130f, -800f), new Vector3(0f, 0f, 60f), ref extending);
            Assert.IsFalse(extending);
            Assert.AreEqual(0f, p.x, 0.01f);
            Assert.AreEqual(0f, p.z, 0.01f);
            Assert.AreEqual(10f + DominationPlan.BombHeight, p.y, 0.01f);

            // Just past it: keep going the same way.
            p = DominationPlan.BombRunPoint(tower, 10f, new Vector3(0f, 130f, 50f), new Vector3(0f, 0f, 60f), ref extending);
            Assert.IsTrue(extending);
            Assert.Greater(p.z, 50f);

            // Still running out, now heading back in: stays out until far enough for a lined-up pass.
            p = DominationPlan.BombRunPoint(tower, 10f, new Vector3(0f, 130f, 600f), new Vector3(0f, 0f, -60f), ref extending);
            Assert.IsTrue(extending);
            Assert.Greater(p.z, 600f, "still heading away from the tower");

            // Far enough: turn in at the tower again.
            p = DominationPlan.BombRunPoint(tower, 10f, new Vector3(0f, 130f, DominationPlan.BombRunOut + 10f), new Vector3(0f, 0f, 60f), ref extending);
            Assert.IsFalse(extending);
            Assert.AreEqual(0f, p.z, 0.01f);
        }
    }
}

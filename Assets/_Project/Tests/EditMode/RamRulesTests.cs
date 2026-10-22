using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.UI;

namespace WingingIt.Tests
{
    /// <summary>Ram damage: closing speed, who flew into whom, class mass, the Ram Boost, and which part takes it.</summary>
    public class RamRulesTests
    {
        readonly RamRules _rules = new();

        /// <summary>A plane at <paramref name="z"/> m (or <paramref name="y"/> m up) flying along <paramref name="velocity"/>.</summary>
        static Rammer At(float z, Vector3 velocity, float mass = 1f, float dealt = 1f, float y = 0f) =>
            new(new Vector3(0f, y, z), velocity.normalized, velocity, mass, dealt);

        [Test]
        public void HeadOn_HurtsBothFully_AndKillsTwoFighters()
        {
            var o = _rules.Resolve(At(0f, Vector3.forward * 110f), At(10f, Vector3.back * 110f));
            Assert.AreEqual(220f, o.ClosingSpeed, 1e-3f);
            Assert.AreEqual(o.DamageToA, o.DamageToB, 1e-3f);
            Assert.AreEqual(_rules.damagePerSpeed * 220f, o.DamageToA, 1e-3f, "both take the full hit");
            Assert.Greater(o.DamageToA, 100f, "more than a Fighter's body");
        }

        [Test]
        public void FromBehind_TheHitterTakesOnlyItsShare()
        {
            // A at 140 runs into the tail of B at 80, the same way.
            var o = _rules.Resolve(At(0f, Vector3.forward * 140f), At(10f, Vector3.forward * 80f));
            Assert.AreEqual(60f, o.ClosingSpeed, 1e-3f);
            Assert.AreEqual(_rules.damagePerSpeed * 60f, o.DamageToB, 1e-3f, "the plane hit from behind takes it all");
            Assert.AreEqual(o.DamageToB * _rules.hitterShare, o.DamageToA, 1e-3f);
        }

        [Test]
        public void FromAbove_TheDiverTakesLess()
        {
            // A dives straight down onto B flying level.
            var o = _rules.Resolve(At(0f, Vector3.down * 100f, y: 8f), At(0f, Vector3.forward * 80f));
            Assert.AreEqual(_rules.damagePerSpeed * o.ClosingSpeed, o.DamageToB, 1e-3f, "B never saw it coming");
            Assert.AreEqual(o.DamageToB * _rules.hitterShare, o.DamageToA, 1e-3f);
        }

        [Test]
        public void HeavierPlane_DealsMore_TakesLess()
        {
            var o = _rules.Resolve(At(0f, Vector3.forward * 85f, 1.6f), At(10f, Vector3.back * 140f, 0.8f));
            Assert.Greater(o.DamageToB, o.DamageToA);
            Assert.AreEqual(2f, o.DamageToB / o.DamageToA, 1e-3f, "split by mass: 1.6 against 0.8");
            Assert.Less(o.KnockA.magnitude, o.KnockB.magnitude, "the light plane is thrown further");
        }

        [Test]
        public void RamBoost_MultipliesOnlyTheDamageDealt()
        {
            var plain = _rules.Resolve(At(0f, Vector3.forward * 140f), At(10f, Vector3.forward * 80f));
            var boosted = _rules.Resolve(At(0f, Vector3.forward * 140f, dealt: 1.5f), At(10f, Vector3.forward * 80f));
            Assert.AreEqual(plain.DamageToB * 1.5f, boosted.DamageToB, 1e-3f);
            Assert.AreEqual(plain.DamageToA, boosted.DamageToA, 1e-3f);
        }

        [Test]
        public void SlowContact_IsABump_ThatStillPushesApart()
        {
            var o = _rules.Resolve(At(0f, Vector3.forward * 85f), At(10f, Vector3.forward * 80f));
            Assert.IsTrue(o.IsBump);
            Assert.Less(Vector3.Dot(o.KnockA, Vector3.forward), 0f, "the plane behind is pushed back");
            Assert.Greater(Vector3.Dot(o.KnockB, Vector3.forward), 0f, "the plane in front is pushed on");
        }

        [Test]
        public void Knock_IsCapped()
        {
            var o = _rules.Resolve(At(0f, Vector3.forward * 500f), At(10f, Vector3.back * 500f));
            Assert.AreEqual(_rules.maxKnock, (o.KnockA - o.KnockB).magnitude, 1e-3f);
        }

        [Test]
        public void WingsAndTailTakeTheirOwnHit_EverythingElseTheBody()
        {
            Assert.AreEqual(PlanePartType.WingLeft, RamRules.DamagedPart(PlanePartType.WingLeft));
            Assert.AreEqual(PlanePartType.VerticalStabilizer, RamRules.DamagedPart(PlanePartType.VerticalStabilizer));
            Assert.AreEqual(PlanePartType.Body, RamRules.DamagedPart(PlanePartType.Engine), "a nose-on hit is the body");
            Assert.AreEqual(PlanePartType.Body, RamRules.DamagedPart(null));
        }

        [Test]
        public void DeathLine()
        {
            Assert.AreEqual("RAMMED BY BOT 5", HudText.Rammed("BOT 5"));
            Assert.AreEqual("RAMMED", HudText.Rammed(null));
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class BallisticsTests
    {
        [Test]
        public void StationaryTarget_InterceptTimeIsDistanceOverSpeed()
        {
            Assert.IsTrue(Ballistics.TryInterceptTime(new Vector3(0f, 0f, 500f), Vector3.zero, 500f, out float t));
            Assert.AreEqual(1f, t, 1e-4f);
        }

        [Test]
        public void CrossingTarget_BulletAndTargetMeet()
        {
            var r = new Vector3(0f, 0f, 300f);
            var w = new Vector3(80f, 0f, 0f);
            const float speed = 550f;
            Assert.IsTrue(Ballistics.TryInterceptTime(r, w, speed, out float t));
            Vector3 meet = r + w * t;
            Assert.AreEqual(speed * t, meet.magnitude, 0.01f, "a bullet fired at the meeting point gets there at time t");
        }

        [Test]
        public void FasterTargetFlyingAway_CannotBeCaught()
        {
            Assert.IsFalse(Ballistics.TryInterceptTime(new Vector3(0f, 0f, 300f), new Vector3(0f, 0f, 600f), 550f, out _));
        }

        [Test]
        public void LeadPoint_AccountsForShooterVelocity()
        {
            // Flying in formation: no relative motion, so aim straight at the target.
            var v = new Vector3(0f, 0f, 100f);
            Assert.IsTrue(Ballistics.TryLeadPoint(Vector3.zero, v, new Vector3(5f, 0f, 300f), v, 550f, 2f, out Vector3 lead, out _));
            Assert.AreEqual(new Vector3(5f, 0f, 300f), lead);
        }

        [Test]
        public void LeadPoint_OutOfRange_ReturnsFalse()
        {
            Assert.IsFalse(Ballistics.TryLeadPoint(Vector3.zero, Vector3.zero, new Vector3(0f, 0f, 2000f), Vector3.zero, 550f, 1.5f, out _, out _));
        }
    }
}

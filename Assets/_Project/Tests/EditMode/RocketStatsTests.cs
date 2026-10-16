using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Tests
{
    public class RocketStatsTests
    {
        RocketStats _stats;

        [SetUp]
        public void SetUp()
        {
            _stats = ScriptableObject.CreateInstance<RocketStats>();
            _stats.splashDamage = 10f;
            _stats.splashRadius = 10f;
            _stats.splashFalloffMin = 0.3f;
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_stats);

        [Test]
        public void Splash_IsFullAtTheCenter()
        {
            Assert.AreEqual(10f, _stats.SplashDamageAt(0f), 1e-4f);
        }

        [Test]
        public void Splash_FallsOffToTheMinimumAtTheEdge()
        {
            Assert.AreEqual(6.5f, _stats.SplashDamageAt(5f), 1e-4f);
            Assert.AreEqual(3f, _stats.SplashDamageAt(10f), 1e-4f);
        }

        [Test]
        public void Splash_IsZeroOutsideTheRadius()
        {
            Assert.AreEqual(0f, _stats.SplashDamageAt(10.01f));
        }

        [Test]
        public void Splash_ZeroRadius_OnlyHitsAtTheCenter()
        {
            _stats.splashRadius = 0f;
            Assert.AreEqual(10f, _stats.SplashDamageAt(0f), 1e-4f);
            Assert.AreEqual(0f, _stats.SplashDamageAt(0.5f));
        }
    }
}

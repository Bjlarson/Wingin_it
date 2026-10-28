using NUnit.Framework;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.Match;
using WingingIt.UI;
using WingingIt.World;

namespace WingingIt.Tests
{
    public class HudMathTests
    {
        static readonly Vector2 Cluster = new(804f, 176f);

        [Test]
        public void Cluster_OnDesktop_SitsBottomCenter_FullSize()
        {
            var r = HudMath.ClusterRect(1920f, 1080f, Cluster, false, 20f, out float scale);
            Assert.AreEqual(1f, scale);
            Assert.AreEqual(960f, r.center.x, 1e-3f);
            Assert.AreEqual(1060f, r.yMax, 1e-3f);
        }

        [Test]
        public void Cluster_WithTouch_FitsBetweenTheStickAndTheButtons()
        {
            // 16:9: the gap is narrower than the cluster, so it shrinks to fit.
            var r = HudMath.ClusterRect(1920f, 1080f, Cluster, true, 20f, out float scale);
            Assert.Less(scale, 1f);
            Assert.GreaterOrEqual(r.xMin, HudMath.TouchStickWidth);
            Assert.LessOrEqual(r.xMax, 1920f - HudMath.TouchButtonsWidth);

            // A wide phone has room for the full size.
            r = HudMath.ClusterRect(2340f, 1080f, Cluster, true, 20f, out scale);
            Assert.AreEqual(1f, scale);
            Assert.GreaterOrEqual(r.xMin, HudMath.TouchStickWidth);
            Assert.LessOrEqual(r.xMax, 2340f - HudMath.TouchButtonsWidth);

            // Never below half size, however narrow.
            HudMath.ClusterRect(1200f, 1080f, Cluster, true, 20f, out scale);
            Assert.AreEqual(0.5f, scale);
        }

        static readonly Rect Inset = new(80f, 80f, 1760f, 920f);

        [Test]
        public void OffScreen_IsFalse_ForAPointInView()
        {
            var p = new Vector2(1000f, 400f);
            Assert.IsFalse(HudMath.OffScreen(p, true, Inset, out Vector2 at, out _));
            Assert.AreEqual(p, at);
        }

        [Test]
        public void OffScreen_ClampsToTheEdge_InItsDirection()
        {
            Assert.IsTrue(HudMath.OffScreen(new Vector2(5000f, Inset.center.y), true, Inset, out Vector2 at, out Vector2 dir));
            Assert.AreEqual(Inset.xMax, at.x, 1e-3f);
            Assert.AreEqual(Inset.center.y, at.y, 1e-3f);
            Assert.AreEqual(1f, dir.x, 1e-4f);

            // Up and to the right, past the corner's diagonal: lands on the top edge.
            Assert.IsTrue(HudMath.OffScreen(Inset.center + new Vector2(1000f, -4000f), true, Inset, out at, out _));
            Assert.AreEqual(Inset.yMin, at.y, 1e-3f);
            Assert.Greater(at.x, Inset.center.x);
            Assert.LessOrEqual(at.x, Inset.xMax);
        }

        [Test]
        public void OffScreen_BehindTheCamera_IsMirrored_AndDeadAsternPointsDown()
        {
            // Behind and projected to the left means it's actually off to the right.
            Assert.IsTrue(HudMath.OffScreen(Inset.center + new Vector2(-300f, 0f), false, Inset, out Vector2 at, out Vector2 dir));
            Assert.AreEqual(Inset.xMax, at.x, 1e-3f);
            Assert.Greater(dir.x, 0.99f);

            Assert.IsTrue(HudMath.OffScreen(Inset.center, false, Inset, out at, out dir));
            Assert.AreEqual(Vector2.up, dir, "GUI y is down");
            Assert.AreEqual(Inset.yMax, at.y, 1e-3f);
        }

        [Test]
        public void MapPoint_IsNorthUp_EastRight()
        {
            var map = new Rect(30f, 30f, 240f, 240f);
            var center = new Vector2(100f, -200f);
            const float radius = 2000f;
            Assert.AreEqual(map.center, HudMath.MapPoint(new Vector3(100f, 500f, -200f), center, radius, map));
            Vector2 east = HudMath.MapPoint(new Vector3(100f + radius, 0f, -200f), center, radius, map);
            Assert.AreEqual(map.xMax, east.x, 1e-3f);
            Assert.AreEqual(map.center.y, east.y, 1e-3f);
            Vector2 north = HudMath.MapPoint(new Vector3(100f, 0f, -200f + radius), center, radius, map);
            Assert.AreEqual(map.yMin, north.y, 1e-3f);
        }

        [Test]
        public void MapPoint_HeadingUp_PutsWhereWeAreFlyingAtTheTop()
        {
            var map = new Rect(30f, 30f, 240f, 240f);
            var center = Vector2.zero;
            const float radius = 2000f;
            // Flying east (heading 90): the map turns -90, so east is up and north is left.
            Vector2 east = HudMath.MapPoint(new Vector3(radius, 0f, 0f), center, radius, map, -90f);
            Assert.AreEqual(map.center.x, east.x, 1e-3f);
            Assert.AreEqual(map.yMin, east.y, 1e-3f);
            Vector2 north = HudMath.MapPoint(new Vector3(0f, 0f, radius), center, radius, map, -90f);
            Assert.AreEqual(map.xMin, north.x, 1e-3f);
            Assert.AreEqual(map.center.y, north.y, 1e-3f);
            Assert.AreEqual(map.center, HudMath.MapPoint(Vector3.zero, center, radius, map, -90f));
        }

        [Test]
        public void Heading_IsClockwiseFromNorth()
        {
            Assert.AreEqual(0f, HudMath.Heading(Vector3.forward), 1e-3f);
            Assert.AreEqual(90f, HudMath.Heading(Vector3.right), 1e-3f);
            Assert.AreEqual(180f, HudMath.Heading(Vector3.back), 1e-3f);
            Assert.AreEqual(270f, HudMath.Heading(Vector3.left), 1e-3f);
        }

        [Test]
        public void PartColor_GreenYellowRed_GreyWhenGone()
        {
            Color green = HudMath.PartColor(1f, false), yellow = HudMath.PartColor(0.5f, false), red = HudMath.PartColor(0.1f, false);
            Assert.Greater(green.g, green.r);
            Assert.Greater(red.r, red.g);
            Assert.AreNotEqual(green, yellow);
            Assert.AreNotEqual(yellow, red);
            Color gone = HudMath.PartColor(1f, true);
            Assert.AreEqual(gone.r, gone.g, 0.05f, "grey");
            Assert.AreEqual(HudMath.BodyStateColor(BodyDamageState.Critical), red);
        }

        [Test]
        public void Texts()
        {
            Assert.AreEqual("+50  KILL", HudText.Award(new ScoreAward(0, null, 50, ScoreKind.Kill, "Kill")));
            Assert.AreEqual("+120  CONVOY DESTROYED", HudText.Award(new ScoreAward(0, null, 120, ScoreKind.Objective, "Convoy destroyed")));
            Assert.AreEqual("WING SHOT OFF!", HudText.PartLost(PlanePartType.WingRight));
            Assert.AreEqual("ENGINE KNOCKED OUT!", HudText.PartLost(PlanePartType.Engine));
            Assert.IsNull(HudText.PartLost(PlanePartType.Body), "the body going is the kill");
            Assert.AreEqual("640 M", HudText.Distance(640f));
            Assert.AreEqual("2.4 KM", HudText.Distance(2410f));
        }

        [Test]
        public void Distance_Cached_MatchesTheFormattedText()
        {
            foreach (float m in new[] { 0f, 0.4f, 0.5f, 12.5f, 640.49f, 999.4f, 999.6f, 1000f, 1049f, 1050f, 2410f, 9999.4f, 12345f, 99000f })
            {
                string expected = m < 1000f ? $"{m:0} M" : $"{m / 1000f:0.0} KM";
                Assert.AreEqual(expected.Replace(',', '.'), HudText.Distance(m), $"{m} m");
                Assert.AreSame(HudText.Distance(m), HudText.Distance(m), "made once");
            }
            Assert.AreEqual("640 M", HudText.Metres(640.2f));
        }

        static int Bit(PlanePartType t) => PartFlightEffects.Bit(t);

        [Test]
        public void DamageAlert_FollowsTheRepairTiers()
        {
            Assert.AreEqual(DamageAlert.None, DamageFeedbackMath.Alert(BodyDamageState.Healthy, 0));
            Assert.AreEqual(DamageAlert.Warning, DamageFeedbackMath.Alert(BodyDamageState.Damaged, 0));
            Assert.AreEqual(DamageAlert.Warning, DamageFeedbackMath.Alert(BodyDamageState.Healthy, Bit(PlanePartType.Engine)));
            Assert.AreEqual(DamageAlert.Warning, DamageFeedbackMath.Alert(BodyDamageState.Healthy, Bit(PlanePartType.VerticalStabilizer)));
            Assert.AreEqual(DamageAlert.Critical, DamageFeedbackMath.Alert(BodyDamageState.Critical, 0));
            Assert.AreEqual(DamageAlert.Critical, DamageFeedbackMath.Alert(BodyDamageState.Healthy, Bit(PlanePartType.WingLeft)), "a lost wing is Critical");
        }

        [Test]
        public void ReturnToBase_OnlyWhenCritical_AliveAndAirborne_AndNotAlreadyLanding()
        {
            Assert.IsTrue(DamageFeedbackMath.ShouldReturnToBase(DamageAlert.Critical, true, false, false));
            Assert.IsFalse(DamageFeedbackMath.ShouldReturnToBase(DamageAlert.Warning, true, false, false));
            Assert.IsFalse(DamageFeedbackMath.ShouldReturnToBase(DamageAlert.Critical, false, false, false), "dead");
            Assert.IsFalse(DamageFeedbackMath.ShouldReturnToBase(DamageAlert.Critical, true, true, false), "on the ground");
            Assert.IsFalse(DamageFeedbackMath.ShouldReturnToBase(DamageAlert.Critical, true, false, true), "on approach");
        }

        [Test]
        public void Vignette_NoneWhenHealthy_StrongerWhenCritical()
        {
            for (float t = 0f; t < 3f; t += 0.05f)
            {
                Assert.AreEqual(0f, DamageFeedbackMath.VignetteAlpha(DamageAlert.None, t));
                float warn = DamageFeedbackMath.VignetteAlpha(DamageAlert.Warning, t);
                float crit = DamageFeedbackMath.VignetteAlpha(DamageAlert.Critical, t);
                Assert.That(warn, Is.InRange(0.15f, 0.45f));
                Assert.That(crit, Is.InRange(0.4f, 0.8f));
            }
            Assert.Greater(DamageFeedbackMath.VignetteAlpha(DamageAlert.Critical, 0.02f),
                DamageFeedbackMath.VignetteAlpha(DamageAlert.Critical, 0.8f), "beats, then rests");
        }

        [Test]
        public void HitFlash_ScalesWithTheHit_AndFadesOut()
        {
            float small = DamageFeedbackMath.HitStrength(2f, 100f, false);
            float big = DamageFeedbackMath.HitStrength(20f, 100f, false);
            Assert.Less(small, big);
            Assert.LessOrEqual(big, 0.75f);
            Assert.AreEqual(1f, DamageFeedbackMath.HitStrength(2f, 100f, true), "a part shot off flashes fully");

            Assert.AreEqual(big, DamageFeedbackMath.HitFlashAlpha(0f, big), 1e-5f);
            Assert.Less(DamageFeedbackMath.HitFlashAlpha(0.2f, big), big);
            Assert.AreEqual(0f, DamageFeedbackMath.HitFlashAlpha(DamageFeedbackMath.HitFlashTime, big));
        }

        [Test]
        public void Death_NamesTheKiller_OrTheCrashFault()
        {
            Assert.AreEqual("SHOT DOWN BY EAST FIGHTER 2", HudText.Death(true, null, "EAST FIGHTER 2"));
            Assert.AreEqual("SHOT DOWN", HudText.Death(true, null, null));
            Assert.AreEqual("CRASHED: TOO FAST", HudText.Death(false, TouchdownFault.TooFast, null));
            Assert.AreEqual("CRASHED  (WEST AA GETS THE KILL)", HudText.Death(false, null, "WEST AA"));
        }

        [Test]
        public void ApproachHint_SaysWhichWayToFix_OrOnPath()
        {
            Assert.AreEqual("ON GLIDE PATH", HudText.ApproachHint(new ApproachGuide(true, 800f, 0f, 40f, 2f, 10f)));
            Assert.AreEqual("HIGH 25 M", HudText.ApproachHint(new ApproachGuide(true, 800f, 0f, 40f, 25f, 10f)));
            Assert.AreEqual("LOW 15 M  FLY LEFT", HudText.ApproachHint(new ApproachGuide(true, 800f, 30f, 40f, -15f, 10f)));
            Assert.AreEqual("FLY RIGHT", HudText.ApproachHint(new ApproachGuide(true, 800f, -30f, 40f, 0f, 10f)));
        }
    }
}


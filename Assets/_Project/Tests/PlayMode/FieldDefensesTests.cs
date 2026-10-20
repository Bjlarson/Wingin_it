using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using WingingIt.Bots;
using WingingIt.Match;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>Airfield Domination's capture-field defenses on the Island: a bombable tower and two light AA guns.</summary>
    public class FieldDefensesTests
    {
        [UnitySetUp]
        public IEnumerator LoadScene()
        {
            yield return SceneManager.LoadSceneAsync("Island", LoadSceneMode.Single);
            yield return null;
            Object.FindAnyObjectByType<MatchManager>()?.Dismiss();
            Object.FindAnyObjectByType<BotRoster>()?.Clear();
            yield return null;
        }

        static FieldDefenses Central => FieldDefenses.All.Single(d => d.Airfield.DisplayName == "Central Airfield");

        [Test]
        public void EachCaptureField_HasATowerAndTwoLightAA_DormantOutsideDomination()
        {
            Assert.AreEqual(3, FieldDefenses.All.Count);
            foreach (var d in FieldDefenses.All)
            {
                Assert.IsFalse(d.Airfield.IsHome, $"{d.name}: a home field has capture defenses");
                Assert.IsNotNull(d.Tower, $"{d.name}: no tower");
                Assert.AreEqual(2, d.AAGuns.Count, $"{d.name}: not two AA guns");
                Assert.AreEqual(2, d.Airfield.GetComponentsInChildren<AAGun>().Length, $"{d.name}: the other AA guns are still there");
                Assert.IsTrue(d.AAGuns.All(g => g.Body != null), $"{d.name}: an AA gun can't be destroyed");
                Assert.IsFalse(d.IsActive);
                Assert.IsFalse(GroundTarget.All.Contains(d.Tower), "a dormant tower can be hit");
                d.Airfield.SetTeam(0);
                Assert.IsTrue(d.AAGuns.All(g => !g.IsArmed), $"{d.name}: dormant AA is armed on an owned field");
            }
            foreach (var home in Airfield.All.Where(f => f.IsHome))
                Assert.IsTrue(home.GetComponentsInChildren<AAGun>().All(g => g.Body == null && g.IsArmed), "home AA changed");
        }

        [UnityTest]
        public IEnumerator OwnedField_FiresAndRepairs_UntilItsTowerIsBombed_ThenRebuilds()
        {
            var d = Central;
            d.SetActive(true);
            Assert.IsTrue(d.AAGuns.All(g => !g.IsArmed), "neutral field's AA is armed");
            d.Airfield.SetTeam(1);
            yield return null;
            Assert.IsTrue(d.AAGuns.All(g => g.IsArmed), "owned field's AA isn't armed");
            Assert.IsTrue(d.Airfield.IsFriendlyTo(1));
            Assert.IsTrue(d.TowerUp);

            d.Tower.ApplyDamage(d.Tower.MaxHealth * 0.5f, d.Tower.transform.position);
            Assert.IsFalse(d.Tower.IsDestroyed, "one bomb's worth flattened the tower");
            d.Tower.ApplyDamage(d.Tower.MaxHealth, d.Tower.transform.position);
            yield return null;
            Assert.IsFalse(d.TowerUp);
            Assert.IsTrue(d.Airfield.TowerDown);
            Assert.IsTrue(d.AAGuns.All(g => !g.IsArmed), "AA fires with the tower down");
            Assert.IsFalse(d.Airfield.IsFriendlyTo(1), "repairs with the tower down");
            Assert.AreEqual(45f, d.Tower.RebuildTimeLeft, 0.5f);

            d.Tower.Rebuild();
            yield return null;
            Assert.IsFalse(d.Airfield.TowerDown);
            Assert.IsTrue(d.AAGuns.All(g => g.IsArmed), "AA didn't come back with the tower");
        }

        [UnityTest]
        public IEnumerator DestroyedLightAA_GoesQuiet_TheOtherKeepsFiring()
        {
            var d = Central;
            d.SetActive(true);
            d.Airfield.SetTeam(0);
            var (hit, other) = (d.AAGuns[0], d.AAGuns[1]);
            hit.Body.ApplyDamage(hit.Body.MaxHealth, hit.transform.position);
            yield return null;
            Assert.IsFalse(hit.IsArmed);
            Assert.IsTrue(other.IsArmed);
            Assert.IsTrue(d.TowerUp, "an AA gun took the tower with it");
            Assert.AreEqual(30f, hit.Body.RebuildTimeLeft, 0.5f);

            d.SetActive(false);
            Assert.IsFalse(hit.Body.IsDestroyed, "switching off didn't rebuild it");
            Assert.IsFalse(GroundTarget.All.Contains(hit.Body), "dormant AA can still be hit");
        }

        [Test]
        public void HomeFields_CantChangeHands()
        {
            var home = Airfield.HomeOf(0);
            home.SetTeam(1);
            Assert.AreEqual(0, home.Team);
        }
    }
}

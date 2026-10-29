using NUnit.Framework;
using UnityEngine;
using WingingIt.Settings;
using WingingIt.UI;
using WingingIt.World;

namespace WingingIt.Tests
{
    /// <summary>The player's pilot look (playtest G4): swatches, saving, the PILOT screen's rows.</summary>
    public class PilotLookTests
    {
        sealed class MemoryStore : ISettingsStore
        {
            public string Json;
            public string Load() => Json;
            public void Save(string json) => Json = json;
        }

        [TearDown]
        public void TearDown() => GameSettings.UseForTests(null);

        [Test]
        public void TheDefaultLook_IsAllTeamWithAWhiteStripedCanopy_WhatEveryBotWears()
        {
            var look = PilotLook.Default;
            var team = new Color(1f, 0.38f, 0.32f);
            Assert.AreEqual(team, PilotColors.Resolve(look.suit, team));
            Assert.AreEqual(team, PilotColors.Resolve(look.helmet, team));
            Assert.AreEqual(team, PilotColors.Resolve(look.canopy, team));
            Assert.AreEqual("white", look.trim);
            Assert.AreEqual(CanopyPattern.Stripes, look.pattern);
            Assert.IsTrue(new GameSettings(false, new MemoryStore()).Data.PilotLook.Equals(look), "a new player starts on it");
        }

        [Test]
        public void TheGear_DefaultsToTheJetAgeKit_SavesAndLoads_AndAnOldSaveGetsTheDefaults()
        {
            foreach (var slot in PilotGear.Slots)
                Assert.AreEqual(PilotGear.Default(slot), PilotLook.Default.Gear(slot), slot.ToString());
            Assert.AreEqual("Jet", PilotLook.Default.gearHelmet);

            var store = new MemoryStore();
            var settings = new GameSettings(false, store);
            settings.Set(s => s.PilotLook = PilotLook.Default.WithGear(PilotGearSlot.Helmet, "Leather")
                .WithGear(PilotGearSlot.Boots, "Leather"));
            var loaded = new GameSettings(false, store).Data.PilotLook;
            Assert.AreEqual("Leather", loaded.gearHelmet);
            Assert.AreEqual("Leather", loaded.gearBoots);
            Assert.AreEqual(PilotGear.Default(PilotGearSlot.Suit), loaded.gearSuit);

            // A save from before gear has no gear fields; one with an unknown variant is corrupt: both get the defaults.
            store.Json = store.Json.Replace("\"gearHelmet\":\"Leather\",", "").Replace("\"Leather\"", "\"Kilt\"");
            var old = new GameSettings(false, store).Data.PilotLook;
            foreach (var slot in PilotGear.Slots)
                Assert.AreEqual(PilotGear.Default(slot), old.Gear(slot), slot.ToString());

            Assert.AreEqual("Coverall", PilotGear.Step(PilotGearSlot.Suit, "Flight", 1));
            Assert.AreEqual("Flight", PilotGear.Step(PilotGearSlot.Suit, "Flight", 2), "wraps round");
        }

        [Test]
        public void Swatches_StepRoundAndBack_AndEveryIdIsUnique()
        {
            Assert.AreEqual(PilotColors.Team, PilotColors.All[0].Id, "TEAM comes first");
            for (int i = 0; i < PilotColors.All.Length; i++)
                for (int j = i + 1; j < PilotColors.All.Length; j++)
                    Assert.AreNotEqual(PilotColors.All[i].Id, PilotColors.All[j].Id);

            Assert.AreEqual(PilotColors.All[1].Id, PilotColors.Step(PilotColors.Team, 1));
            Assert.AreEqual(PilotColors.All[^1].Id, PilotColors.Step(PilotColors.Team, -1), "wraps round");
            Assert.AreEqual(PilotColors.Team, PilotColors.Step(PilotColors.All[^1].Id, 1));
            Assert.AreEqual(PilotColors.Team, PilotColors.Step("not-a-colour", 0), "an unknown id counts as TEAM");
            Assert.AreEqual(Color.green, PilotColors.Resolve("not-a-colour", Color.green));
            Assert.AreNotEqual(Color.green, PilotColors.Resolve("red", Color.green), "a named swatch ignores the team");
        }

        [Test]
        public void TheLook_SavesAndLoads_AndAnUnknownSwatchOrPatternResets()
        {
            var store = new MemoryStore();
            var settings = new GameSettings(false, store);
            settings.Set(s => s.PilotLook = new PilotLook
            {
                suit = "olive", helmet = "charcoal", canopy = "orange", trim = "navy", pattern = CanopyPattern.Quarters,
            });

            var loaded = new GameSettings(false, store).Data.PilotLook;
            Assert.AreEqual("olive", loaded.suit);
            Assert.AreEqual("charcoal", loaded.helmet);
            Assert.AreEqual("orange", loaded.canopy);
            Assert.AreEqual("navy", loaded.trim);
            Assert.AreEqual(CanopyPattern.Quarters, loaded.pattern);

            store.Json = store.Json.Replace("\"olive\"", "\"plaid\"").Replace("\"canopyPattern\":1", "\"canopyPattern\":42");
            var repaired = new GameSettings(false, store).Data.PilotLook;
            Assert.AreEqual(PilotColors.Team, repaired.suit, "an unknown swatch (an old or corrupt save) goes back to TEAM");
            Assert.AreEqual(CanopyPattern.Stripes, repaired.pattern);
            Assert.AreEqual("charcoal", repaired.helmet, "the rest of the look is kept");

            settings.ResetTab(SettingsTab.Hud);
            Assert.AreEqual("olive", settings.Data.pilotSuit, "no settings tab resets the pilot");
            settings.ResetPilotLook();
            Assert.IsTrue(settings.Data.PilotLook.Equals(PilotLook.Default));
        }

        [Test]
        public void ThePilotScreen_StepsEachRow_AndResetPutsTheDefaultBack()
        {
            var settings = new GameSettings(false, new MemoryStore());
            GameSettings.UseForTests(settings);

            PilotPanel.Change(0, 1); // suit
            PilotPanel.Change(1, -1); // helmet
            PilotPanel.Change(2, 1); // canopy
            PilotPanel.Change(2, 1);
            PilotPanel.Change(3, 1); // trim
            PilotPanel.Change(4, -1); // pattern
            var look = settings.Data.PilotLook;
            Assert.AreEqual(PilotColors.All[1].Id, look.suit);
            Assert.AreEqual(PilotColors.All[^1].Id, look.helmet);
            Assert.AreEqual(PilotColors.All[2].Id, look.canopy);
            Assert.AreEqual(PilotColors.Step("white", 1), look.trim);
            Assert.AreEqual(CanopyPattern.Solid, look.pattern, "the pattern wraps round too");

            PilotPanel.Change(5, 1); // RESET PILOT
            Assert.IsTrue(settings.Data.PilotLook.Equals(PilotLook.Default));
        }
    }
}

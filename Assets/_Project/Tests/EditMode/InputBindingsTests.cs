using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using WingingIt.Input;
using WingingIt.Settings;

namespace WingingIt.Tests
{
    /// <summary>Which bindings the player can change, swap-on-conflict, and that rebinds survive a save and load.</summary>
    public class InputBindingsTests
    {
        sealed class MemoryStore : ISettingsStore
        {
            public string Json;
            public string Load() => Json;
            public void Save(string json) => Json = json;
        }

        InputActionAsset _asset;
        InputActionMap _map;
        MemoryStore _store;

        [SetUp]
        public void SetUp()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Settings/FlightControls.inputactions");
            _asset = Object.Instantiate(shipped); // a copy: the project's asset stays untouched
            _map = _asset.FindActionMap("Flight", true);
            _store = new MemoryStore();
            GameSettings.UseForTests(new GameSettings(false, _store));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_asset);
            GameSettings.UseForTests(null);
        }

        [Test]
        public void KeyboardSlots_CoverKeysAndButtons_NotTheMouseDelta()
        {
            var slots = InputBindings.Slots(_map, InputBindings.KeyboardGroup);
            var labels = slots.Select(s => s.Label).ToList();
            CollectionAssert.Contains(labels, "PITCH UP");
            CollectionAssert.Contains(labels, "ROLL LEFT");
            CollectionAssert.Contains(labels, "ROLL LEFT (ALT)"); // the arrow keys
            CollectionAssert.Contains(labels, "FIRE GUNS");
            CollectionAssert.Contains(labels, "FIRE GUNS (ALT)"); // the mouse button
            Assert.IsFalse(slots.Any(s => s.Action.name == "MouseLook"));
            CollectionAssert.AllItemsAreUnique(labels);
        }

        [Test]
        public void GamepadSlots_LeaveTheSticksFixed()
        {
            var slots = InputBindings.Slots(_map, InputBindings.GamepadGroup);
            Assert.IsFalse(slots.Any(s => s.Action.name == "StickLook"));
            Assert.IsFalse(slots.Any(s => s.Action.name == "Pitch")); // left stick Y only
            Assert.IsFalse(slots.Any(s => s.Path.EndsWith("/x") || s.Path.EndsWith("/y")));
            Assert.IsTrue(slots.Any(s => s.Label == "FIRE MISSILE"));
        }

        [Test]
        public void Assign_ToAKeyInUse_SwapsThem()
        {
            var slots = InputBindings.Slots(_map, InputBindings.KeyboardGroup);
            var ability = slots.First(s => s.Label == "ABILITY");   // F
            var bomb = slots.First(s => s.Label == "DROP BOMB");    // B
            InputBindings.Assign(_map, InputBindings.KeyboardGroup, ability, "<Keyboard>/b");
            Assert.AreEqual("<Keyboard>/b", ability.Path);
            Assert.AreEqual("<Keyboard>/f", bomb.Path);
        }

        [Test]
        public void Assign_ToAFreeKey_JustMoves()
        {
            var ability = InputBindings.Slots(_map, InputBindings.KeyboardGroup).First(s => s.Label == "ABILITY");
            InputBindings.Assign(_map, InputBindings.KeyboardGroup, ability, "<Keyboard>/g");
            Assert.AreEqual("<Keyboard>/g", ability.Path);
        }

        [Test]
        public void Rebinds_SurviveSaveAndLoad_AndResetClearsThem()
        {
            var ability = InputBindings.Slots(_map, InputBindings.KeyboardGroup).First(s => s.Label == "ABILITY");
            InputBindings.Assign(_map, InputBindings.KeyboardGroup, ability, "<Keyboard>/g");
            InputBindings.Save(_asset);
            StringAssert.Contains("<Keyboard>/g", _store.Json);

            // a fresh copy of the shipped asset, with the saved overrides put on it
            var fresh = Object.Instantiate(AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Settings/FlightControls.inputactions"));
            try
            {
                InputBindings.Apply(fresh);
                var loaded = InputBindings.Slots(fresh.FindActionMap("Flight", true), InputBindings.KeyboardGroup).First(s => s.Label == "ABILITY");
                Assert.AreEqual("<Keyboard>/g", loaded.Path);
            }
            finally
            {
                Object.DestroyImmediate(fresh);
            }

            InputBindings.ResetGroup(_map, InputBindings.KeyboardGroup);
            Assert.AreEqual("<Keyboard>/f", ability.Path);
        }

        [Test]
        public void ResettingTheControlsTab_ClearsSavedBindings()
        {
            GameSettings.Current.Set(s => s.bindingOverrides = "{\"bindings\":[]}");
            GameSettings.Current.ResetTab(SettingsTab.Controls);
            Assert.AreEqual("", GameSettings.Current.Data.bindingOverrides);
        }
    }
}

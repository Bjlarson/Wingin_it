using System;
using System.Collections.Generic;
using UnityEngine.InputSystem;
using WingingIt.Settings;

namespace WingingIt.Input
{
    /// <summary>One rebindable control: a single button / key binding (or one half of an axis composite) of an action.</summary>
    public readonly struct BindingSlot
    {
        public readonly InputAction Action;
        /// <summary>Index into <see cref="InputAction.bindings"/>.</summary>
        public readonly int Index;
        /// <summary>"PITCH UP", "FIRE GUNS"; a second binding for the same thing reads "… (ALT)".</summary>
        public readonly string Label;

        public BindingSlot(InputAction action, int index, string label)
        {
            Action = action;
            Index = index;
            Label = label;
        }

        public string Display => Action.GetBindingDisplayString(Index);
        public string Path => Action.bindings[Index].effectivePath;
    }

    /// <summary>
    /// Player rebinding for the Flight action map: which bindings can change, interactive rebinding with swap-on-conflict,
    /// and saving the overrides in <see cref="GameSettings"/> (so they're per device, like every other setting). Sticks,
    /// stick axes and the mouse delta stay fixed: they're analog and a button can't stand in for them.
    /// </summary>
    public static class InputBindings
    {
        public const string KeyboardGroup = "Keyboard&Mouse", GamepadGroup = "Gamepad";

        /// <summary>Esc cancels a keyboard rebind, Start a gamepad one (neither can be bound).</summary>
        const string KeyboardCancel = "<Keyboard>/escape", GamepadCancel = "<Gamepad>/start";

        /// <summary>The names of an axis composite's negative / positive halves, per action.</summary>
        static readonly Dictionary<string, (string negative, string positive)> AxisWords = new()
        {
            ["Pitch"] = ("DOWN", "UP"),
            ["Roll"] = ("LEFT", "RIGHT"),
            ["Yaw"] = ("LEFT", "RIGHT"),
            ["Throttle"] = ("DECREASE", "INCREASE"),
        };

        /// <summary>Puts the saved overrides on <paramref name="asset"/> (and clears any it has that aren't saved).</summary>
        public static void Apply(InputActionAsset asset)
        {
            if (asset == null)
                return;
            asset.RemoveAllBindingOverrides();
            string json = GameSettings.Current.Data.bindingOverrides;
            if (string.IsNullOrEmpty(json))
                return;
            try
            {
                asset.LoadBindingOverridesFromJson(json);
            }
            catch (Exception)
            {
                // a corrupt save: fly on the defaults
            }
        }

        /// <summary>Saves the asset's current overrides.</summary>
        public static void Save(InputActionAsset asset)
        {
            if (asset != null)
                GameSettings.Current.Set(s => s.bindingOverrides = asset.SaveBindingOverridesAsJson());
        }

        /// <summary>Every rebindable binding of <paramref name="map"/> for a device group, in action order.</summary>
        public static List<BindingSlot> Slots(InputActionMap map, string group)
        {
            var slots = new List<BindingSlot>();
            var used = new HashSet<string>();
            foreach (var action in map.actions)
            {
                var bindings = action.bindings;
                for (int i = 0; i < bindings.Count; i++)
                {
                    var b = bindings[i];
                    if (!InGroup(b, group) || !IsRebindable(b))
                        continue;
                    string label = Label(action.name, b);
                    while (!used.Add(label))
                        label += " (ALT)";
                    slots.Add(new BindingSlot(action, i, label));
                }
            }
            return slots;
        }

        static bool InGroup(InputBinding b, string group)
        {
            if (string.IsNullOrEmpty(b.groups))
                return false;
            foreach (string g in b.groups.Split(InputBinding.Separator))
                if (g == group)
                    return true;
            return false;
        }

        /// <summary>Buttons and keys only: no stick axes, sticks or the mouse delta.</summary>
        public static bool IsRebindable(InputBinding b)
        {
            string p = b.path;
            if (string.IsNullOrEmpty(p) || b.isComposite)
                return false;
            return !(p.EndsWith("/x") || p.EndsWith("/y") || p.EndsWith("Stick") || p.EndsWith("/delta"));
        }

        static string Label(string actionName, InputBinding b)
        {
            string name = Nice(actionName);
            if (b.isPartOfComposite && AxisWords.TryGetValue(actionName, out var words))
                return $"{name} {(b.name == "negative" ? words.negative : words.positive)}";
            return name;
        }

        /// <summary>"FireSecondary" → "FIRE SECONDARY".</summary>
        public static string Nice(string name)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < name.Length; i++)
            {
                if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]))
                    sb.Append(' ');
                sb.Append(char.ToUpperInvariant(name[i]));
            }
            return sb.ToString();
        }

        /// <summary>
        /// Gives <paramref name="slot"/> the control <paramref name="path"/>. Another binding of the same device already on
        /// it takes the slot's old control instead (a swap), so nothing ends up doubled or unbound.
        /// </summary>
        public static void Assign(InputActionMap map, string group, BindingSlot slot, string path)
        {
            string old = slot.Path;
            if (old == path)
                return;
            foreach (var other in Slots(map, group))
            {
                if (other.Action == slot.Action && other.Index == slot.Index)
                    continue;
                if (other.Path == path)
                    other.Action.ApplyBindingOverride(other.Index, old);
            }
            slot.Action.ApplyBindingOverride(slot.Index, path);
        }

        /// <summary>Puts one slot, or every slot of a device group, back to the shipped control.</summary>
        public static void Reset(BindingSlot slot) => slot.Action.RemoveBindingOverride(slot.Index);

        public static void ResetGroup(InputActionMap map, string group)
        {
            foreach (var slot in Slots(map, group))
                Reset(slot);
        }

        /// <summary>
        /// Waits for the player to press something and binds it to <paramref name="slot"/> (swapping on a conflict), then
        /// saves. <paramref name="finished"/> gets true when bound, false when cancelled. The caller keeps the returned
        /// operation to cancel it (<c>Cancel()</c>), and drops it when it finishes.
        /// </summary>
        public static InputActionRebindingExtensions.RebindingOperation Begin(
            InputActionAsset asset, InputActionMap map, string group, BindingSlot slot, Action<bool> finished)
        {
            bool pad = group == GamepadGroup;
            var action = slot.Action;
            string old = slot.Path;
            bool wasEnabled = action.enabled;
            action.Disable(); // an action can't be rebound while it's live

            var op = action.PerformInteractiveRebinding(slot.Index)
                .WithExpectedControlType("Button")
                .WithCancelingThrough(pad ? GamepadCancel : KeyboardCancel)
                .OnMatchWaitForAnother(0.1f);
            if (pad)
                op.WithControlsHavingToMatchPath("<Gamepad>");
            else
                op.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>");

            void Done(bool bound)
            {
                // The operation wrote the new control onto the binding; put the old one back and route through Assign
                // so a conflicting binding swaps instead of doubling up.
                string chosen = action.bindings[slot.Index].effectivePath;
                action.ApplyBindingOverride(slot.Index, old);
                op.Dispose();
                if (bound)
                {
                    Assign(map, group, slot, chosen);
                    Save(asset);
                }
                if (wasEnabled)
                    action.Enable();
                finished?.Invoke(bound);
            }

            op.OnComplete(_ => Done(true));
            op.OnCancel(_ => Done(false));
            return op.Start();
        }
    }
}

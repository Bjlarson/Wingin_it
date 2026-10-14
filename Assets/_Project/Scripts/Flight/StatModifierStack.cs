using System;
using System.Collections.Generic;

namespace WingingIt.Flight
{
    public enum ModifierOp
    {
        /// <summary>Added to the base value before multipliers.</summary>
        Add,
        /// <summary>Multiplies (base + adds).</summary>
        Multiply,
    }

    public readonly struct StatModifier
    {
        public readonly FlightStat Stat;
        public readonly ModifierOp Op;
        public readonly float Value;
        /// <summary>Who applied it (a lost part, a damage state, an ability) so it can be removed as a group.</summary>
        public readonly object Source;

        public StatModifier(FlightStat stat, ModifierOp op, float value, object source)
        {
            Stat = stat;
            Op = op;
            Value = value;
            Source = source;
        }
    }

    /// <summary>
    /// Base class stats plus a stack of modifiers. Final value = (base + sum of adds) * product of multipliers.
    /// Part damage, body damage states, and abilities all push modifiers here, so effects combine cleanly.
    /// </summary>
    public class StatModifierStack
    {
        readonly List<StatModifier> _modifiers = new();
        readonly float[] _values = new float[FlightStats.StatCount];
        Func<FlightStat, float> _baseValue;
        bool _dirty = true;

        public event Action Changed;

        public StatModifierStack(Func<FlightStat, float> baseValue)
        {
            _baseValue = baseValue ?? throw new ArgumentNullException(nameof(baseValue));
        }

        public IReadOnlyList<StatModifier> Modifiers => _modifiers;

        public void SetBase(Func<FlightStat, float> baseValue)
        {
            _baseValue = baseValue ?? throw new ArgumentNullException(nameof(baseValue));
            MarkDirty();
        }

        public void Add(StatModifier modifier)
        {
            _modifiers.Add(modifier);
            MarkDirty();
        }

        public void Add(FlightStat stat, ModifierOp op, float value, object source) =>
            Add(new StatModifier(stat, op, value, source));

        /// <summary>Removes every modifier applied by <paramref name="source"/>. Returns how many were removed.</summary>
        public int RemoveAllFromSource(object source)
        {
            int removed = _modifiers.RemoveAll(m => Equals(m.Source, source));
            if (removed > 0)
                MarkDirty();
            return removed;
        }

        public void Clear()
        {
            if (_modifiers.Count == 0)
                return;
            _modifiers.Clear();
            MarkDirty();
        }

        public float Get(FlightStat stat)
        {
            if (_dirty)
                Recalculate();
            return _values[(int)stat];
        }

        void MarkDirty()
        {
            _dirty = true;
            Changed?.Invoke();
        }

        void Recalculate()
        {
            for (int i = 0; i < _values.Length; i++)
            {
                var stat = (FlightStat)i;
                float add = 0f;
                float mul = 1f;
                foreach (var m in _modifiers)
                {
                    if (m.Stat != stat)
                        continue;
                    if (m.Op == ModifierOp.Add)
                        add += m.Value;
                    else
                        mul *= m.Value;
                }
                _values[i] = (_baseValue(stat) + add) * mul;
            }
            _dirty = false;
        }
    }
}

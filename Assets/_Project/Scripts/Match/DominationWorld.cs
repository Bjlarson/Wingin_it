using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;
using WingingIt.World;

namespace WingingIt.Match
{
    /// <summary>
    /// Runs Airfield Domination in the level (Design/01-Game-Design/match-modes.md#airfield-domination-prototype): every
    /// capture field (an <see cref="Airfield"/> with <see cref="FieldDefenses"/>) starts neutral with its defenses on,
    /// the planes parked in its zone go to the <see cref="FieldBoard"/> each frame, and a field the board flips is handed
    /// to the capturing team (<see cref="Airfield.SetTeam"/>). From then on that team repairs and respawns there through
    /// the usual airfield rules (<see cref="Airfield.IsFriendlyTo"/>) while its tower stands. <see cref="MatchManager"/>
    /// owns one while a Domination match is on; <see cref="Dispose"/> puts the fields back as built.
    /// </summary>
    public sealed class DominationWorld
    {
        readonly DominationMode _mode;
        readonly Airfield[] _fields;
        readonly FieldDefenses[] _defenses;
        readonly List<FieldSample> _samples = new();
        readonly List<FieldCapture> _captures = new();

        DominationWorld(DominationMode mode, Airfield[] fields, FieldDefenses[] defenses)
        {
            _mode = mode;
            _fields = fields;
            _defenses = defenses;
        }

        /// <summary>The capture fields of the match in progress, or null (the bots and HUD read it).</summary>
        public static DominationWorld Active { get; private set; }

        public FieldBoard Board => _mode.Board;
        /// <summary>The match's rules (the hold-every-field clock for the HUD).</summary>
        public DominationMode Mode => _mode;
        /// <summary>The capture fields, in board order (by name).</summary>
        public IReadOnlyList<Airfield> Fields => _fields;
        /// <summary>Raised for every field that changes hands (the HUD's callouts, the score).</summary>
        public event System.Action<FieldCapture> Captured;

        /// <summary>The board index of <paramref name="field"/>, or -1 if it isn't a capture field.</summary>
        public int IndexOf(Airfield field) => System.Array.IndexOf(_fields, field);

        /// <summary>The capture fields of <paramref name="mode"/> in the level (open, not home), or null if it has none.</summary>
        public static DominationWorld Create(DominationMode mode)
        {
            var defenses = new List<FieldDefenses>();
            foreach (var d in FieldDefenses.All)
                if (d.Airfield != null && !d.Airfield.IsHome && d.Airfield.IsOpen)
                    defenses.Add(d);
            if (defenses.Count == 0)
                return null;
            defenses.Sort((a, b) => string.CompareOrdinal(a.Airfield.DisplayName, b.Airfield.DisplayName));

            var fields = new Airfield[defenses.Count];
            for (int i = 0; i < fields.Length; i++)
            {
                fields[i] = defenses[i].Airfield;
                fields[i].SetTeam(Airfield.Neutral);
                fields[i].SetCapturable(true);
                defenses[i].SetActive(true);
            }
            mode.SetFields(fields.Length);
            return Active = new DominationWorld(mode, fields, defenses.ToArray());
        }

        /// <summary>One frame of play: parked planes into the board, captured fields to their new owners and the score.</summary>
        public void Tick(MatchScore score, float dt)
        {
            for (int i = 0; i < _fields.Length; i++)
                Board.Get(i).TowerUp = _defenses[i].TowerUp;

            _samples.Clear();
            foreach (var plane in PlaneController.All)
            {
                if (!plane.IsAlive || plane.Team < 0)
                    continue;
                int field = ParkedField(plane);
                if (field >= 0)
                    _samples.Add(new FieldSample(plane, plane.Team, field));
            }

            _captures.Clear();
            Board.Update(dt, _samples, _captures);
            foreach (var c in _captures)
            {
                _fields[c.Field].SetTeam(c.Team);
                Captured?.Invoke(c);
            }
            if (_captures.Count > 0 && score != null)
                _mode.Apply(score, _captures);
        }

        /// <summary>The capture field whose zone <paramref name="plane"/> is landed and stopped in, or -1.</summary>
        int ParkedField(PlaneController plane)
        {
            float stopped = plane.TryGetComponent<PlaneRepair>(out var repair) ? repair.Rules.stoppedSpeed : 0.5f;
            if (!FieldBoard.IsParked(plane.IsAlive, plane.IsGrounded, plane.Speed, stopped, plane.IsReversing))
                return -1;
            for (int i = 0; i < _fields.Length; i++)
            {
                var zone = _fields[i].RepairZone;
                if (_fields[i].IsOpen && zone != null && zone.Contains(plane.Position))
                    return i;
            }
            return -1;
        }

        /// <summary>Match over (or the mode changed): every capture field is neutral, dormant scenery again.</summary>
        public void Dispose()
        {
            if (Active == this)
                Active = null;
            for (int i = 0; i < _fields.Length; i++)
            {
                if (_fields[i] == null)
                    continue;
                _fields[i].SetTeam(Airfield.Neutral);
                _fields[i].SetCapturable(false);
                if (_defenses[i] != null)
                    _defenses[i].SetActive(false);
            }
        }
    }
}

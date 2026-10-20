using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Match
{
    /// <summary>A live plane as the capture fields see it: who, and which capture field it's parked in.</summary>
    public readonly struct FieldSample
    {
        public readonly object Pilot;
        public readonly int Team;
        /// <summary>The capture field whose zone the plane is landed and stopped in (<see cref="FieldBoard.IsParked"/>), or -1.</summary>
        public readonly int Field;

        public FieldSample(object pilot, int team, int field)
        {
            Pilot = pilot;
            Team = team;
            Field = field;
        }
    }

    /// <summary>A field changed hands: for score, callouts and the field's colors.</summary>
    public readonly struct FieldCapture
    {
        public readonly int Field;
        public readonly int Team;
        /// <summary>The team that held it before, or <see cref="FieldBoard.Neutral"/>.</summary>
        public readonly int OldOwner;
        /// <summary>The capturing team's pilots parked in the zone when it flipped (each scores a share).</summary>
        public readonly IReadOnlyList<object> Pilots;

        public FieldCapture(int field, int team, int oldOwner, IReadOnlyList<object> pilots)
        {
            Field = field;
            Team = team;
            OldOwner = oldOwner;
            Pilots = pilots;
        }
    }

    /// <summary>
    /// The capture fields of an Airfield Domination match (see Design/01-Game-Design/match-modes.md#airfield-domination-prototype).
    /// A plane captures a field by landing and stopping in its zone: <see cref="CaptureTime"/> seconds alone, faster with
    /// more of its team parked there (+<see cref="ExtraPlaneSpeed"/> each, counting up to <see cref="MaxCapturers"/>), and
    /// <see cref="TowerSlowdown"/>× slower while the owner's control tower stands. A landed plane of any other team
    /// contests it: progress holds. With nobody capturing, progress drains back; a different team first undoes it, then
    /// builds its own. Capturing gives the plane nothing (no repair, no invulnerability): it's a target on the runway.
    /// Home fields aren't on the board. Pure (parked planes in, captures out), so the rules are EditMode-testable.
    /// </summary>
    public sealed class FieldBoard
    {
        public const int Neutral = -1;

        public sealed class Field
        {
            /// <summary>The owning team, or <see cref="Neutral"/>.</summary>
            public int Owner = Neutral;
            /// <summary>The team whose progress is on the field, or -1.</summary>
            public int CapturingTeam = -1;
            /// <summary>0..1 toward <see cref="CapturingTeam"/> owning it.</summary>
            public float Progress;
            /// <summary>Planes of more than one team are parked in the zone: progress holds.</summary>
            public bool Contested;
            /// <summary>The owner's control tower stands (only slows captures on an owned field).</summary>
            public bool TowerUp;
        }

        readonly Field[] _fields;
        // Per-frame scratch: parked planes of each team on the field being updated.
        readonly Dictionary<int, List<object>> _parked = new();

        /// <summary>Seconds for one plane to capture an undefended field.</summary>
        public float CaptureTime = 10f;
        /// <summary>Capture speed added per extra plane of the same team (+50%).</summary>
        public float ExtraPlaneSpeed = 0.5f;
        /// <summary>Planes beyond this many add no more speed.</summary>
        public int MaxCapturers = 3;
        /// <summary>How many times slower a capture is while the owner's tower stands.</summary>
        public float TowerSlowdown = 3f;

        public FieldBoard(int fieldCount)
        {
            _fields = new Field[fieldCount];
            for (int i = 0; i < fieldCount; i++)
                _fields[i] = new Field();
        }

        public int FieldCount => _fields.Length;
        public Field Get(int field) => _fields[field];

        /// <summary>True if a plane counts as parked in a zone it's inside: alive, on its wheels, stopped and not backing up.</summary>
        public static bool IsParked(bool alive, bool grounded, float speed, float stoppedSpeed, bool reversing) =>
            alive && grounded && speed <= stoppedSpeed && !reversing;

        /// <summary>Progress per second for <paramref name="planes"/> parked planes of a team capturing <paramref name="field"/>.</summary>
        public float CaptureRate(Field field, int planes)
        {
            if (planes <= 0 || CaptureTime <= 0f)
                return 0f;
            float rate = (1f + ExtraPlaneSpeed * (Mathf.Min(planes, MaxCapturers) - 1)) / CaptureTime;
            if (field.Owner != Neutral && field.TowerUp && TowerSlowdown > 0f)
                rate /= TowerSlowdown;
            return rate;
        }

        /// <summary>Runs <paramref name="dt"/> seconds of capturing with the planes parked this frame.</summary>
        public void Update(float dt, IReadOnlyList<FieldSample> planes, List<FieldCapture> captures)
        {
            for (int i = 0; i < _fields.Length; i++)
            {
                var f = _fields[i];
                foreach (var list in _parked.Values)
                    list.Clear();
                int teamsHere = 0, capturer = -1;
                foreach (var p in planes)
                {
                    if (p.Field != i || p.Team < 0)
                        continue;
                    if (!_parked.TryGetValue(p.Team, out var list))
                        _parked[p.Team] = list = new List<object>();
                    if (list.Count == 0)
                    {
                        teamsHere++;
                        if (p.Team != f.Owner)
                            capturer = p.Team;
                    }
                    list.Add(p.Pilot);
                }

                f.Contested = teamsHere > 1;
                if (f.Contested)
                    continue;

                if (capturer < 0)
                {
                    // Nobody capturing (empty, or only the owner's planes): the progress drains away.
                    f.Progress -= dt / Mathf.Max(CaptureTime, 0.0001f);
                    if (f.Progress <= 0f)
                    {
                        f.Progress = 0f;
                        f.CapturingTeam = -1;
                    }
                    continue;
                }

                var pilots = _parked[capturer];
                float step = CaptureRate(f, pilots.Count) * dt;
                if (f.CapturingTeam == capturer || f.Progress <= 0f)
                {
                    f.CapturingTeam = capturer;
                    f.Progress += step;
                }
                else
                {
                    // Another team's progress has to come off first; whatever's left over counts for the newcomer.
                    f.Progress -= step;
                    if (f.Progress < 0f)
                    {
                        f.CapturingTeam = capturer;
                        f.Progress = -f.Progress;
                    }
                }

                if (f.CapturingTeam == capturer && f.Progress >= 1f)
                {
                    captures?.Add(new FieldCapture(i, capturer, f.Owner, pilots.ToArray()));
                    f.Owner = capturer;
                    f.CapturingTeam = -1;
                    f.Progress = 0f;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;

namespace WingingIt.Match
{
    /// <summary>Where a Teams match is: menu → class select → countdown → playing → ended.</summary>
    public enum MatchPhase
    {
        /// <summary>The main menu and game mode select (and settings), over the player's plane in its hangar. The world is frozen.</summary>
        Menu,
        /// <summary>Picking a class. The world is frozen.</summary>
        ClassSelect,
        /// <summary>Everyone is in place, frozen, while 3-2-1 counts down (real time).</summary>
        Countdown,
        /// <summary>The match clock runs (game time) and kills score.</summary>
        Playing,
        /// <summary>Time's up: the scoreboard. The world is frozen.</summary>
        Ended,
    }

    /// <summary>
    /// The phase order and the two timers of a match. Menu ↔ class select move on the player's say-so; the countdown
    /// and the match clock run out on <see cref="Tick"/>. Pure, so the rules are EditMode-testable.
    /// </summary>
    public sealed class MatchClock
    {
        public MatchClock(float matchLength, float countdownLength)
        {
            MatchLength = matchLength;
            CountdownLength = countdownLength;
        }

        public MatchPhase Phase { get; private set; } = MatchPhase.Menu;
        /// <summary>Seconds left on the countdown or the match clock; 0 in the other phases.</summary>
        public float TimeLeft { get; private set; }
        /// <summary>Seconds of play. Read when the match starts, so changing it mid-match doesn't move the clock.</summary>
        public float MatchLength { get; set; }
        public float CountdownLength { get; set; }

        /// <summary>Menu → class select (a game mode was confirmed). False (and no change) from any other phase.</summary>
        public bool OpenClassSelect() => Move(MatchPhase.Menu, MatchPhase.ClassSelect);

        /// <summary>Class select → menu (back to the game mode screen).</summary>
        public bool BackToMenu() => Move(MatchPhase.ClassSelect, MatchPhase.Menu);

        /// <summary>Class select → countdown (straight to playing with no countdown).</summary>
        public bool StartCountdown()
        {
            if (!Move(MatchPhase.ClassSelect, MatchPhase.Countdown))
                return false;
            TimeLeft = CountdownLength;
            if (TimeLeft <= 0f)
                StartPlaying();
            return true;
        }

        /// <summary>Runs the countdown or the match clock down by <paramref name="dt"/>. True if the phase changed.</summary>
        public bool Tick(float dt)
        {
            if (Phase != MatchPhase.Countdown && Phase != MatchPhase.Playing)
                return false;
            TimeLeft = Math.Max(0f, TimeLeft - dt);
            if (TimeLeft > 0f)
                return false;
            if (Phase == MatchPhase.Countdown)
                StartPlaying();
            else
                Phase = MatchPhase.Ended;
            return true;
        }

        /// <summary>Ends a match in play now (time's up early). False from any other phase.</summary>
        public bool End()
        {
            if (!Move(MatchPhase.Playing, MatchPhase.Ended))
                return false;
            TimeLeft = 0f;
            return true;
        }

        void StartPlaying()
        {
            Phase = MatchPhase.Playing;
            TimeLeft = MatchLength;
        }

        bool Move(MatchPhase from, MatchPhase to)
        {
            if (Phase != from)
                return false;
            Phase = to;
            return true;
        }
    }

    /// <summary>One pilot's line on the scoreboard.</summary>
    public sealed class PilotScore
    {
        /// <summary>The pilot this line is for (the plane), as registered; null for a line made by hand.</summary>
        public object Pilot;
        public string Name;
        public int Team;
        public int Kills;
        public int Assists;
        public int Deaths;
        /// <summary>Banners this pilot flew home (Capture the Banner) or fields it was on the capture of (Airfield Domination).</summary>
        public int Captures;
        /// <summary>Rams that did this pilot's target damage (Ram Royale's tiebreak).</summary>
        public int Rams;
        /// <summary>Finishing place in a last-plane-standing mode, given when the pilot goes out; 0 while still flying.</summary>
        public int Place;
        /// <summary>Personal score: every point this pilot earned for the team (kills, assists, objectives).</summary>
        public int Points;
    }

    /// <summary>What a score award was for.</summary>
    public enum ScoreKind
    {
        Kill,
        Assist,
        Objective,
    }

    /// <summary>Points that went to a team (and to a pilot, if one earned them). What score popups will listen to.</summary>
    public readonly struct ScoreAward
    {
        public readonly int Team;
        /// <summary>The pilot credited, or null (an AA kill, an objective nobody in particular took).</summary>
        public readonly object Pilot;
        public readonly int Points;
        public readonly ScoreKind Kind;
        /// <summary>What it was for, for a popup or the kill feed ("Kill", "Assist", "Convoy destroyed").</summary>
        public readonly string Label;

        public ScoreAward(int team, object pilot, int points, ScoreKind kind, string label)
        {
            Team = team;
            Pilot = pilot;
            Points = points;
            Kind = kind;
            Label = label;
        }
    }

    /// <summary>
    /// Team score and each pilot's kills, assists, deaths and points for one match. A team scores
    /// <see cref="KillPoints"/> per enemy plane it brings down (by a pilot or its airfield's AA),
    /// <see cref="AssistPoints"/> per pilot who helped, and whatever objectives award (<see cref="AwardPoints"/>).
    /// Crashes nobody gets credit for, and team kills, score nothing. Highest team score wins when the clock runs out;
    /// a tie is a draw. Pilots are any object (the plane).
    /// </summary>
    public sealed class MatchScore
    {
        public const int Draw = -1;

        readonly int[] _teamScores;
        readonly Dictionary<object, PilotScore> _pilots = new();
        readonly List<PilotScore> _order = new();

        public MatchScore(int teamCount, int killPoints = 50, int assistPoints = 20)
        {
            _teamScores = new int[Math.Max(1, teamCount)];
            KillPoints = killPoints;
            AssistPoints = assistPoints;
        }

        /// <summary>Raised for every award, after the scores have changed.</summary>
        public event Action<ScoreAward> Awarded;

        public int KillPoints { get; set; }
        public int AssistPoints { get; set; }
        public int TeamCount => _teamScores.Length;
        public int TeamScore(int team) => IsTeam(team) ? _teamScores[team] : 0;
        /// <summary>Every pilot seen this match, in the order they joined.</summary>
        public IReadOnlyList<PilotScore> Pilots => _order;

        /// <summary>Adds a pilot (or updates its name and team). Pilots also join on their first kill, assist or death.</summary>
        public PilotScore Register(object pilot, string name, int team)
        {
            if (!_pilots.TryGetValue(pilot, out var score))
            {
                score = new PilotScore { Pilot = pilot };
                _pilots.Add(pilot, score);
                _order.Add(score);
            }
            score.Name = name;
            score.Team = team;
            return score;
        }

        public PilotScore Get(object pilot) => pilot != null && _pilots.TryGetValue(pilot, out var s) ? s : null;

        /// <summary>
        /// A plane went down. <paramref name="killer"/> is the credited pilot (null for AA or nobody) and
        /// <paramref name="killerTeam"/> the side credited (null for nobody). True if it scored.
        /// </summary>
        public bool RecordDeath(object victim, string victimName, int victimTeam, object killer, string killerName, int? killerTeam)
        {
            Register(victim, victimName, victimTeam).Deaths++;
            var killerScore = killer != null && killer != victim ? Register(killer, killerName, killerTeam ?? victimTeam) : null;
            if (killerTeam is not int team || !IsTeam(team) || team == victimTeam)
                return false;
            if (killerScore != null)
                killerScore.Kills++;
            Award(team, killerScore != null ? killer : null, KillPoints, ScoreKind.Kill, "Kill");
            return true;
        }

        /// <summary>
        /// <paramref name="pilot"/> helped bring down a plane of <paramref name="victimTeam"/>. Call after
        /// <see cref="RecordDeath"/>. Helping kill a teammate scores nothing. True if it scored.
        /// </summary>
        public bool RecordAssist(object pilot, string name, int team, int victimTeam)
        {
            if (pilot == null || !IsTeam(team) || team == victimTeam)
                return false;
            Register(pilot, name, team).Assists++;
            Award(team, pilot, AssistPoints, ScoreKind.Assist, "Assist");
            return true;
        }

        /// <summary>
        /// Objective points (a convoy, a supply drop, a capture) for <paramref name="team"/>, and for
        /// <paramref name="pilot"/> if one earned them (null for none). True if it scored.
        /// </summary>
        public bool AwardPoints(int team, object pilot, string pilotName, int points, string label)
        {
            if (!IsTeam(team) || points <= 0)
                return false;
            if (pilot != null)
                Register(pilot, pilotName, team);
            Award(team, pilot, points, ScoreKind.Objective, label);
            return true;
        }

        /// <summary>The team with the highest score, or <see cref="Draw"/> if the top score is shared.</summary>
        public int Winner()
        {
            int best = Draw, bestScore = int.MinValue;
            for (int t = 0; t < _teamScores.Length; t++)
            {
                if (_teamScores[t] > bestScore)
                    (best, bestScore) = (t, _teamScores[t]);
                else if (_teamScores[t] == bestScore)
                    best = Draw;
            }
            return best;
        }

        /// <summary>Scoreboard order: points, then kills, then fewest deaths.</summary>
        public static int CompareForScoreboard(PilotScore a, PilotScore b) =>
            a.Points != b.Points ? b.Points.CompareTo(a.Points)
            : a.Kills != b.Kills ? b.Kills.CompareTo(a.Kills)
            : a.Deaths.CompareTo(b.Deaths);

        void Award(int team, object pilot, int points, ScoreKind kind, string label)
        {
            _teamScores[team] += points;
            if (Get(pilot) is { } score)
                score.Points += points;
            Awarded?.Invoke(new ScoreAward(team, pilot, points, kind, label));
        }

        bool IsTeam(int team) => team >= 0 && team < _teamScores.Length;
    }
    /// <summary>
    /// When a pilot may change class mid-match: on every respawn, or parked at a home airfield once repaired (see
    /// Design/02-Mechanics/aircraft-classes.md#class-selection). Pure, so the rule is EditMode-testable.
    /// </summary>
    public static class ClassSwap
    {
        /// <summary>
        /// True if a pilot can swap class where they are: alive, on the wheels in a home airfield's repair zone,
        /// stopped (<paramref name="speed"/> ≤ <paramref name="stoppedSpeed"/>), not mid-repair, and at full health.
        /// </summary>
        public static bool CanSwapAtAirfield(bool alive, bool grounded, bool inHomeRepairZone, float speed, float stoppedSpeed,
            bool repairing, bool fullHealth) =>
            alive && grounded && inHomeRepairZone && speed <= stoppedSpeed && !repairing && fullHealth;

        /// <summary>How many of <paramref name="classIndexes"/> fly each of <paramref name="classCount"/> classes (out-of-range indexes are skipped).</summary>
        public static int[] Composition(IEnumerable<int> classIndexes, int classCount)
        {
            var counts = new int[Math.Max(0, classCount)];
            foreach (int i in classIndexes)
                if (i >= 0 && i < counts.Length)
                    counts[i]++;
            return counts;
        }
    }
}

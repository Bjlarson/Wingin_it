using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Flight;

namespace WingingIt.Match
{
    /// <summary>A pilot as the score sees it: the plane (any object), its scoreboard name and its team.</summary>
    public readonly struct PilotRef
    {
        public readonly object Pilot;
        public readonly string Name;
        public readonly int Team;

        public PilotRef(object pilot, string name, int team)
        {
            Pilot = pilot;
            Name = name;
            Team = team;
        }
    }

    /// <summary>A plane went down in play: who, who gets the credit and who helped. Pure, for the modes' scoring.</summary>
    public readonly struct KillRecord
    {
        public readonly PilotRef Victim;
        /// <summary>The credited pilot; <see cref="PilotRef.Pilot"/> is null for an AA kill or nobody.</summary>
        public readonly PilotRef Killer;
        /// <summary>The side credited (a pilot's or an AA gun's), or null for nobody.</summary>
        public readonly int? KillerTeam;
        public readonly IReadOnlyList<PilotRef> Assisters;

        public KillRecord(PilotRef victim, PilotRef killer, int? killerTeam, IReadOnlyList<PilotRef> assisters = null)
        {
            Victim = victim;
            Killer = killer;
            KillerTeam = killerTeam;
            Assisters = assisters ?? Array.Empty<PilotRef>();
        }
    }

    /// <summary>The match widgets a mode shows on top of the flight HUD.</summary>
    [Flags]
    public enum MatchWidgets
    {
        None = 0,
        /// <summary>Both team names and scores either side of the clock (the clock always shows).</summary>
        TeamScores = 1,
        /// <summary>Top-right list of who brought down whom.</summary>
        KillFeed = 2,
        /// <summary>+50 KILL, +20 ASSIST, objective popups.</summary>
        ScorePopups = 4,
        All = TeamScores | KillFeed | ScorePopups,
    }

    /// <summary>One number column on the scoreboard, right of the pilot's name.</summary>
    public sealed class ScoreColumn
    {
        public string Header;
        /// <summary>Width of the number (reference pixels), right-aligned.</summary>
        public float Width = 40f;
        /// <summary>Space left of the column.</summary>
        public float Gap;
        /// <summary>How bright the numbers are (1 = as bright as the name).</summary>
        public float Alpha = 1f;
        public Func<PilotScore, string> Value;

        public ScoreColumn(string header, Func<PilotScore, string> value, float alpha = 1f, float width = 40f, float gap = 0f)
        {
            Header = header;
            Value = value;
            Alpha = alpha;
            Width = width;
            Gap = gap;
        }
    }

    /// <summary>
    /// The rules of one game mode (see Design/01-Game-Design/match-modes.md#shared-mode-framework-prototype):
    /// scoring, the win check, lives and spawns, loadout limits, which match widgets show and the scoreboard columns.
    /// <see cref="MatchManager"/> runs the phases (menu → class select → countdown → play → end) and asks the mode
    /// everything else. The rule methods take plain values, so a mode's rules are EditMode-testable.
    /// </summary>
    public abstract class MatchMode
    {
        /// <summary>The card this mode was made from (its name and length).</summary>
        public GameModeInfo Info { get; internal set; }

        /// <summary>
        /// Which of the level's arenas (<see cref="World.ModeArena.ModeId"/>) the mode plays in: its own card's id by
        /// default. A level with no arena of that id is played as built.
        /// </summary>
        public virtual string ArenaId => Info?.Id;

        /// <summary>A fresh score for a match of <paramref name="teamCount"/> teams.</summary>
        public virtual MatchScore CreateScore(int teamCount) => new MatchScore(teamCount);

        /// <summary>A plane went down in play: score it.</summary>
        public abstract void RecordKill(MatchScore score, in KillRecord kill);

        /// <summary>Runs every frame of play (game time): field ticks, carried banners.</summary>
        public virtual void Tick(MatchScore score, float dt) { }

        /// <summary>A team that has won before time is up (3 captures, every field held, last plane), or null to play on.</summary>
        public virtual int? EarlyWinner(MatchScore score) => null;

        /// <summary>The winning team when the match ends, or <see cref="MatchScore.Draw"/>.</summary>
        public virtual int Winner(MatchScore score) => score.Winner();

        /// <summary>What the scoreboard says decides the winner.</summary>
        public virtual string WinRule => "WINNER IS THE HIGHEST TEAM SCORE";

        /// <summary>The headline under VICTORY / DEFEAT and on the live scoreboard: both sides and what they're scored on.</summary>
        public virtual string ResultLine(MatchScore score, Func<int, string> teamName) =>
            $"{teamName(0)}  {score.TeamScore(0)}  –  {score.TeamScore(1)}  {teamName(1)}";

        /// <summary>Whether a dead pilot comes back (lives). <paramref name="pilot"/> is null if it never scored or died.</summary>
        public virtual bool CanRespawn(PilotScore pilot) => true;

        /// <summary>A spawn pose of the mode's own; false for the usual one (over or on the nearest friendly field).</summary>
        public virtual bool TryGetSpawnPose(PlaneController plane, Vector3 deathPosition, out Pose pose, out bool onRunway)
        {
            pose = default;
            onRunway = false;
            return false;
        }

        /// <summary>A plane was just put back in play: apply the mode's loadout limits (weapons off, a drag modifier).</summary>
        public virtual void OnSpawned(PlaneController plane) { }

        public virtual MatchWidgets Widgets => MatchWidgets.All;

        /// <summary>The scoreboard's number columns, left to right.</summary>
        public abstract IReadOnlyList<ScoreColumn> Columns { get; }

        /// <summary>Scoreboard row order, best first.</summary>
        public virtual int CompareForScoreboard(PilotScore a, PilotScore b) => MatchScore.CompareForScoreboard(a, b);
    }

    /// <summary>
    /// Teams: two sides, unlimited respawns, every weapon. A team scores <see cref="KillPoints"/> per enemy plane its
    /// pilots or its airfield's AA bring down and <see cref="AssistPoints"/> per pilot who helped; objectives score
    /// what they award. Highest team score at the end wins, a tie is a draw.
    /// </summary>
    public sealed class TeamsMode : MatchMode
    {
        static readonly ScoreColumn[] s_columns =
        {
            new("K", p => p.Kills.ToString()),
            new("A", p => p.Assists.ToString(), 0.75f),
            new("D", p => p.Deaths.ToString(), 0.6f),
            new("SCORE", p => p.Points.ToString(), 1f, 80f, 10f),
        };

        public int KillPoints = 50;
        public int AssistPoints = 20;

        public override MatchScore CreateScore(int teamCount) => new(teamCount, KillPoints, AssistPoints);

        public override void RecordKill(MatchScore score, in KillRecord kill)
        {
            var v = kill.Victim;
            score.RecordDeath(v.Pilot, v.Name, v.Team, kill.Killer.Pilot, kill.Killer.Name, kill.KillerTeam);
            foreach (var helper in kill.Assisters)
                score.RecordAssist(helper.Pilot, helper.Name, helper.Team, v.Team);
        }

        public override IReadOnlyList<ScoreColumn> Columns => s_columns;
    }

    /// <summary>
    /// Capture the Banner: Teams scoring plus <see cref="CapturePoints"/> per banner flown home. <see cref="CapturesToWin"/>
    /// captures end it at once; otherwise most captures wins, then the higher team score, then a draw. The world layer
    /// (stands, towed banners, drag) feeds the <see cref="Board"/> with the planes each frame and the mode scores what it reports.
    /// </summary>
    public sealed class BannerMode : MatchMode
    {
        static readonly ScoreColumn[] s_columns =
        {
            new("K", p => p.Kills.ToString()),
            new("A", p => p.Assists.ToString(), 0.75f),
            new("D", p => p.Deaths.ToString(), 0.6f),
            new("CAP", p => p.Captures.ToString(), 1f, 44f, 6f),
            new("SCORE", p => p.Points.ToString(), 1f, 80f, 10f),
        };

        readonly TeamsMode _teams = new();

        public int CapturePoints = 300;
        public int CapturesToWin = 3;

        /// <summary>The banners of this match; null until the level's stands are known (<see cref="SetStands"/>).</summary>
        public BannerBoard Board { get; private set; }

        public void SetStands(IReadOnlyList<Vector3> stands) => Board = new BannerBoard(stands) { CapturesToWin = CapturesToWin };

        public override MatchScore CreateScore(int teamCount) => _teams.CreateScore(teamCount);

        public override void RecordKill(MatchScore score, in KillRecord kill) => _teams.RecordKill(score, kill);

        /// <summary>Scores what the board reported this frame: 300 to the team and the carrier's personal score per capture.</summary>
        public void Apply(MatchScore score, IReadOnlyList<BannerEvent> events)
        {
            foreach (var e in events)
            {
                if (e.Kind != BannerEventKind.Captured)
                    continue;
                var pilot = score.Get(e.Pilot);
                if (pilot != null)
                    pilot.Captures++;
                score.AwardPoints(e.PilotTeam, e.Pilot, pilot?.Name, CapturePoints, "Capture");
            }
        }

        public override int? EarlyWinner(MatchScore score) => Board?.EarlyWinner();

        public override int Winner(MatchScore score)
        {
            if (Board == null)
                return score.Winner();
            int byCaptures = Board.LeaderByCaptures();
            return byCaptures != MatchScore.Draw ? byCaptures : score.Winner();
        }

        /// <summary>Captures lead the headline (what wins it); the team score follows as the tiebreak.</summary>
        public override string ResultLine(MatchScore score, Func<int, string> teamName)
        {
            if (Board == null)
                return base.ResultLine(score, teamName);
            return $"{teamName(0)}  {Board.Captures(0)}  –  {Board.Captures(1)}  {teamName(1)}  ·  CAPTURES  ·  SCORE {score.TeamScore(0)} – {score.TeamScore(1)}";
        }

        public override string WinRule => $"{CapturesToWin} CAPTURES WINS · THEN MOST CAPTURES, THEN TEAM SCORE";

        public override IReadOnlyList<ScoreColumn> Columns => s_columns;
    }

    /// <summary>
    /// Airfield Domination: Teams scoring on top of the capture fields. A plane takes a field by landing and stopping in
    /// its zone (the <see cref="Board"/>'s rules); the team that holds it repairs and can respawn there. The world layer
    /// (<see cref="DominationWorld"/>) feeds the board with the parked planes each frame and hands captured fields over.
    /// Capture and holding points, the early win and the HUD come next (see the development plan).
    /// </summary>
    public sealed class DominationMode : MatchMode
    {
        static readonly ScoreColumn[] s_columns =
        {
            new("K", p => p.Kills.ToString()),
            new("A", p => p.Assists.ToString(), 0.75f),
            new("D", p => p.Deaths.ToString(), 0.6f),
            new("SCORE", p => p.Points.ToString(), 1f, 80f, 10f),
        };

        readonly TeamsMode _teams = new();

        /// <summary>The capture fields of this match; null until the level's are known (<see cref="SetFields"/>).</summary>
        public FieldBoard Board { get; private set; }

        public void SetFields(int count) => Board = new FieldBoard(count);

        public override MatchScore CreateScore(int teamCount) => _teams.CreateScore(teamCount);

        public override void RecordKill(MatchScore score, in KillRecord kill) => _teams.RecordKill(score, kill);

        public override IReadOnlyList<ScoreColumn> Columns => s_columns;
    }
}

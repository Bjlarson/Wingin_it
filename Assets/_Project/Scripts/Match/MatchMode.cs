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
        /// <summary>Free for all: planes still flying out of everyone in the match ("7 / 16"), left of the clock.</summary>
        PlanesLeft = 8,
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

        /// <summary>
        /// Two planes rammed with ram damage on: what each took. Only modes that count rams (Ram Royale) do anything.
        /// </summary>
        public virtual void RecordRam(MatchScore score, PilotRef hitter, PilotRef victim, float hitterTook, float victimTook) { }

        /// <summary>Runs every frame of play (game time): field ticks, carried banners.</summary>
        public virtual void Tick(MatchScore score, float dt) { }

        /// <summary>
        /// A team that has won before time is up (3 captures, every field held, last plane), <see cref="MatchScore.Draw"/>
        /// if it's over with nobody winning (the last planes went down together), or null to play on.
        /// </summary>
        public virtual int? EarlyWinner(MatchScore score) => null;

        /// <summary>The winning team when the match ends, or <see cref="MatchScore.Draw"/>.</summary>
        public virtual int Winner(MatchScore score) => score.Winner();

        /// <summary>What the scoreboard says decides the winner.</summary>
        public virtual string WinRule => "WINNER IS THE HIGHEST TEAM SCORE";

        /// <summary>The headline under VICTORY / DEFEAT and on the live scoreboard: both sides and what they're scored on.</summary>
        public virtual string ResultLine(MatchScore score, Func<int, string> teamName) =>
            $"{teamName(0)}  {score.TeamScore(0)}  –  {score.TeamScore(1)}  {teamName(1)}";

        /// <summary>
        /// Free for all: how many pilots, each on a team of its own (no wingmen, a colour each); 0 for the level's teams.
        /// Bots fill every pilot but the player's.
        /// </summary>
        public virtual int FreeForAllSize => 0;

        public bool IsFreeForAll => FreeForAllSize > 0;

        /// <summary>Lives per pilot; 0 for unlimited. A pilot out of lives spectates (see <see cref="MatchManager.IsSpectating"/>).</summary>
        public virtual int Lives => 0;

        /// <summary>Whether a dead pilot comes back (lives). <paramref name="pilot"/> is null if it never scored or died.</summary>
        public virtual bool CanRespawn(PilotScore pilot) => Lives <= 0 || pilot == null || pilot.Deaths < Lives;

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
    /// (<see cref="DominationWorld"/>) feeds the board with the parked planes each frame, hands captured fields over and
    /// passes the captures here to score: <see cref="CapturePoints"/> to the team plus <see cref="PilotCapturePoints"/>
    /// per capturing pilot, and <see cref="HoldPoints"/> per held field every <see cref="HoldInterval"/> seconds. Holding
    /// every capture field for <see cref="HoldAllToWin"/> seconds wins at once; otherwise the higher team score wins.
    /// </summary>
    public sealed class DominationMode : MatchMode
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
        float _holdClock;

        public int CapturePoints = 100;
        public int PilotCapturePoints = 50;
        public int HoldPoints = 5;
        public float HoldInterval = 2f;
        public float HoldAllToWin = 30f;

        /// <summary>The capture fields of this match; null until the level's are known (<see cref="SetFields"/>).</summary>
        public FieldBoard Board { get; private set; }

        /// <summary>The team that owns every capture field right now, or -1.</summary>
        public int HoldingAll { get; private set; } = -1;

        /// <summary>How long <see cref="HoldingAll"/> has held every field without a break (seconds).</summary>
        public float HoldingAllTime { get; private set; }

        public void SetFields(int count)
        {
            Board = new FieldBoard(count);
            _holdClock = 0f;
            HoldingAll = -1;
            HoldingAllTime = 0f;
        }

        /// <summary>The capture fields <paramref name="team"/> owns.</summary>
        public int FieldsHeld(int team)
        {
            int held = 0;
            if (Board != null)
                for (int i = 0; i < Board.FieldCount; i++)
                    if (Board.Get(i).Owner == team)
                        held++;
            return held;
        }

        public override MatchScore CreateScore(int teamCount) => _teams.CreateScore(teamCount);

        public override void RecordKill(MatchScore score, in KillRecord kill) => _teams.RecordKill(score, kill);

        /// <summary>Scores the fields the board flipped this frame: 100 to the team and 50 to each capturing pilot.</summary>
        public void Apply(MatchScore score, IReadOnlyList<FieldCapture> captures)
        {
            foreach (var c in captures)
            {
                score.AwardPoints(c.Team, null, null, CapturePoints, "Field captured");
                foreach (var p in c.Pilots)
                {
                    var pilot = score.Get(p);
                    if (pilot != null)
                        pilot.Captures++;
                    score.AwardPoints(c.Team, p, pilot?.Name, PilotCapturePoints, "Capture");
                }
            }
        }

        /// <summary>Held fields pay out every <see cref="HoldInterval"/> seconds; the clock on holding every field runs.</summary>
        public override void Tick(MatchScore score, float dt)
        {
            if (Board == null || Board.FieldCount == 0)
                return;

            _holdClock += dt;
            while (HoldInterval > 0f && _holdClock >= HoldInterval)
            {
                _holdClock -= HoldInterval;
                for (int team = 0; team < score.TeamCount; team++)
                    score.AwardPoints(team, null, null, HoldPoints * FieldsHeld(team), "Fields held");
            }

            int all = Board.Get(0).Owner;
            for (int i = 1; i < Board.FieldCount && all != FieldBoard.Neutral; i++)
                if (Board.Get(i).Owner != all)
                    all = FieldBoard.Neutral;
            if (all != HoldingAll)
                HoldingAllTime = 0f;
            else if (all != FieldBoard.Neutral)
                HoldingAllTime += dt;
            HoldingAll = all;
        }

        public override int? EarlyWinner(MatchScore score) =>
            HoldingAll != FieldBoard.Neutral && HoldingAllTime >= HoldAllToWin ? HoldingAll : null;

        /// <summary>The team score leads the headline (what wins it); fields held right now follow.</summary>
        public override string ResultLine(MatchScore score, Func<int, string> teamName)
        {
            if (Board == null)
                return base.ResultLine(score, teamName);
            return $"{base.ResultLine(score, teamName)}  ·  FIELDS {FieldsHeld(0)} – {FieldsHeld(1)}";
        }

        public override string WinRule => $"HOLD EVERY FIELD {HoldAllToWin:0} S WINS · THEN HIGHEST TEAM SCORE";

        public override IReadOnlyList<ScoreColumn> Columns => s_columns;
    }

    /// <summary>
    /// Ram Royale (prototype): free for all, <see cref="Pilots"/> planes, one life each, weapons off and ram damage on;
    /// a pilot who goes down watches the survivors. Plays in the level's Ram Royale arena (the small ring over the south
    /// sea, air start). A knock-out scores <see cref="KnockOutPoints"/> to whoever gets the credit. The last plane flying
    /// wins at once; if the last ones go down together, the one with more rams among them wins (else a draw). If time
    /// runs out first, the survivor with the most body HP (<see cref="BodyHealth"/>) wins, then the most rams.
    /// </summary>
    public sealed class RamRoyaleMode : MatchMode
    {
        public const string Id = "RamRoyale";

        static readonly ScoreColumn[] s_columns =
        {
            new("KO", p => p.Kills.ToString()),
            new("RAMS", p => p.Rams.ToString(), 0.75f, 50f, 6f),
            new("", p => p.Deaths > 0 ? "OUT" : "", 0.6f, 60f, 6f),
            new("SCORE", p => p.Points.ToString(), 1f, 80f, 10f),
        };

        public int Pilots = 16;
        public int KnockOutPoints = 100;

        /// <summary>
        /// A pilot's body HP right now (the world layer sets it; 0 for a plane that's down). Null counts everyone level,
        /// so the time-out goes to rams alone.
        /// </summary>
        public Func<object, float> BodyHealth;

        int _ticks, _outTick = -1;
        readonly List<PilotScore> _outTogether = new(); // went out on frame _outTick

        /// <summary>The Ram Royale arena even when the mode isn't played from its card (tests).</summary>
        public override string ArenaId => Info?.Id ?? Id;

        public override int FreeForAllSize => Pilots;

        public override int Lives => 1;

        /// <summary>No assists: with everyone on their own, the knock-out is the one that counts.</summary>
        public override MatchScore CreateScore(int teamCount) => new(teamCount, KnockOutPoints, 0);

        /// <summary>
        /// Scores the knock-out and places the pilot who went out. Everyone out on the same frame (a head-on that kills
        /// both) shares the best of their places, so the last two going down together are both 1st.
        /// </summary>
        public override void RecordKill(MatchScore score, in KillRecord kill)
        {
            var v = kill.Victim;
            var victim = score.Register(v.Pilot, v.Name, v.Team);
            bool wasFlying = IsFlying(victim);
            score.RecordDeath(v.Pilot, v.Name, v.Team, kill.Killer.Pilot, kill.Killer.Name, kill.KillerTeam);
            if (!wasFlying || IsFlying(victim))
                return;
            if (_outTick != _ticks)
            {
                _outTick = _ticks;
                _outTogether.Clear();
            }
            _outTogether.Add(victim);
            int place = Flying(score) + 1;
            foreach (var p in _outTogether)
                p.Place = place;
        }

        /// <summary>A ram counts for each plane that did the other damage (a head-on counts for both).</summary>
        public override void RecordRam(MatchScore score, PilotRef hitter, PilotRef victim, float hitterTook, float victimTook)
        {
            if (victimTook > 0f && hitter.Pilot != null)
                score.Register(hitter.Pilot, hitter.Name, hitter.Team).Rams++;
            if (hitterTook > 0f && victim.Pilot != null)
                score.Register(victim.Pilot, victim.Name, victim.Team).Rams++;
        }

        public override void Tick(MatchScore score, float dt) => _ticks++;

        bool IsFlying(PilotScore p) => p.Deaths < Lives;

        /// <summary>How many pilots are still in it.</summary>
        public int Flying(MatchScore score)
        {
            int flying = 0;
            foreach (var p in score.Pilots)
                if (IsFlying(p))
                    flying++;
            return flying;
        }

        float Health(PilotScore p) => IsFlying(p) && BodyHealth != null && p.Pilot != null ? BodyHealth(p.Pilot) : 0f;

        /// <summary>
        /// Who's ahead: still flying beats out, then (flying) more body HP or (out) a better place, then more rams.
        /// Negative if <paramref name="a"/> is ahead, 0 if level.
        /// </summary>
        int CompareStanding(PilotScore a, PilotScore b)
        {
            bool aIn = IsFlying(a), bIn = IsFlying(b);
            if (aIn != bIn)
                return aIn ? -1 : 1;
            if (aIn)
            {
                int hp = Health(b).CompareTo(Health(a));
                if (hp != 0)
                    return hp;
            }
            else if (a.Place != b.Place)
                return a.Place.CompareTo(b.Place);
            return b.Rams.CompareTo(a.Rams);
        }

        /// <summary>The last plane flying (or the better of the last ones down together) wins as soon as there's one.</summary>
        public override int? EarlyWinner(MatchScore score) =>
            score.Pilots.Count >= 2 && Flying(score) <= 1 ? Winner(score) : null;

        /// <summary>The pilot ahead of everyone else (<see cref="CompareStanding"/>), or <see cref="MatchScore.Draw"/> if two are level.</summary>
        public override int Winner(MatchScore score)
        {
            PilotScore best = null;
            bool level = false;
            foreach (var p in score.Pilots)
            {
                int c = best == null ? -1 : CompareStanding(p, best);
                if (c < 0)
                    (best, level) = (p, false);
                else if (c == 0)
                    level = true;
            }
            return best == null || level ? MatchScore.Draw : best.Team;
        }

        /// <summary>Planes left in place of the team scores; the kill feed and popups stay.</summary>
        public override MatchWidgets Widgets => MatchWidgets.PlanesLeft | MatchWidgets.KillFeed | MatchWidgets.ScorePopups;

        static RamBoostAbility s_ramBoost;

        /// <summary>The ability Support flies with here in place of the Repair Aura (no allies to heal).</summary>
        public static RamBoostAbility RamBoost
        {
            get
            {
                if (s_ramBoost == null)
                {
                    s_ramBoost = ScriptableObject.CreateInstance<RamBoostAbility>();
                    s_ramBoost.hideFlags = HideFlags.DontSave;
                }
                return s_ramBoost;
            }
        }

        /// <summary>Weapons off, ram damage on; Support's aura becomes a Ram Boost. Boost and the other abilities stay.</summary>
        public override void OnSpawned(PlaneController plane)
        {
            plane.WeaponsOff = true;
            plane.RamDamage = true;
            if (plane.Stats != null && plane.Stats.ability is RepairAuraAbility && plane.TryGetComponent(out PlaneAbility ability))
                ability.Override = RamBoost;
        }

        /// <summary>Who's still flying out of how many; the last plane flying by name.</summary>
        public override string ResultLine(MatchScore score, Func<int, string> teamName)
        {
            int flying = Flying(score);
            if (flying == 1 && score.Pilots.Count >= 2)
                foreach (var p in score.Pilots)
                    if (IsFlying(p))
                        return $"{teamName(p.Team).ToUpperInvariant()} · LAST PLANE FLYING";
            return $"{flying} OF {score.Pilots.Count} STILL FLYING";
        }

        public override string WinRule => "ONE LIFE · LAST PLANE FLYING WINS · AT TIME, MOST BODY HP, THEN MOST RAMS";

        public override IReadOnlyList<ScoreColumn> Columns => s_columns;

        /// <summary>The standing (still flying by body HP, then the out by place, rams breaking ties), then score.</summary>
        public override int CompareForScoreboard(PilotScore a, PilotScore b)
        {
            int c = CompareStanding(a, b);
            return c != 0 ? c : MatchScore.CompareForScoreboard(a, b);
        }
    }
}

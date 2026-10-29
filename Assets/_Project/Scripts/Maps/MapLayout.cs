using System;
using System.Collections.Generic;
using UnityEngine;

namespace WingingIt.Maps
{
    /// <summary>A world XZ position and the height to fly it above the highest thing nearby.</summary>
    [Serializable]
    public struct LayoutPoint
    {
        public Vector2 xz;
        public float agl;
        public LayoutPoint(Vector2 xz, float agl) { this.xz = xz; this.agl = agl; }
    }

    /// <summary>One airfield on a map. Yaw 0 = landing heading north.</summary>
    [Serializable]
    public class AirfieldAnchor
    {
        public string name;
        [Tooltip("0 or 1 for a home field, -1 (Airfield.Neutral) for a capturable one.")]
        public int team = -1;
        public bool home;
        public bool twoWay;
        [Tooltip("Gets Domination's tower and light AA.")]
        public bool capturable;
        public Vector2 position;
        [Tooltip("World height of the runway; the ground under the field should be flat at this height.")]
        public float elevation;
        public float yaw;
    }

    /// <summary>An approach lane into an airfield: waypoints ending on the field's final fix.</summary>
    [Serializable]
    public class LaneAnchor
    {
        public string airfield;
        public string name;
        public Color color = Color.white;
        [Tooltip("Lands from the far end (a two-way field's second lane).")]
        public bool reverseEnd;
        public LayoutPoint[] points;
    }

    /// <summary>A banner stand past a home field's far runway end.</summary>
    [Serializable]
    public class BannerStandAnchor
    {
        public string airfield;
        [Tooltip("Meters past the runway's far end.")]
        public float distance = 150f;
    }

    /// <summary>A mode's play area (mirrors <c>ModeArena</c>): the level carries the geometry, the mode names it.</summary>
    [Serializable]
    public class ArenaAnchor
    {
        public string modeId;
        public Vector2 center;
        public float radius, warning, ceiling;
        public bool hardEdge = true;
        public bool closeAirfields = true;
        public bool airStart = true;
        public float startRadius, startAltitude;
        public int startSlots = 16;
        [Tooltip("Bot patrol points; agl here is height above the sea, like the arena's ceiling.")]
        public LayoutPoint[] fightAreas;
    }

    /// <summary>A patrol point for bots.</summary>
    [Serializable]
    public class FightAreaAnchor
    {
        public string name;
        public LayoutPoint point;
    }

    /// <summary>What one mode builds on a map: the names of the airfields, lanes and pieces it uses.</summary>
    [Serializable]
    public class ModeLayout
    {
        [Tooltip("The GameModeInfo.Id.")]
        public string modeId;
        public string[] airfields;
        public bool bannerStands;
        [Tooltip("ArenaAnchor.modeId to apply, or empty to play the whole map.")]
        public string arena;
        [Tooltip("Patrol points for bots; empty = the map's.")]
        public FightAreaAnchor[] fightAreas;
    }

    /// <summary>The pieces a <see cref="MapLayout"/> spawns for one mode: airfields (each with its lanes), stands, arenas.</summary>
    public sealed class MapPieces
    {
        public readonly List<AirfieldAnchor> airfields = new();
        public readonly List<BannerStandAnchor> bannerStands = new();
        public readonly List<ArenaAnchor> arenas = new();
    }

    /// <summary>
    /// Everything mode-specific about a map, as anchor data (positions and names, no scene objects): a mode builder
    /// reads the entry for the mode being played and spawns only those pieces. Airfields, lanes and arenas are
    /// listed once; <see cref="modes"/> picks which of them each mode uses.
    /// </summary>
    [Serializable]
    public class MapLayout
    {
        public List<AirfieldAnchor> airfields = new();
        public List<LaneAnchor> lanes = new();
        public List<BannerStandAnchor> bannerStands = new();
        public List<ArenaAnchor> arenas = new();
        public List<FightAreaAnchor> fightAreas = new();
        public List<ModeLayout> modes = new();

        public ModeLayout Mode(string modeId) => modes.Find(m => m.modeId == modeId);
        public AirfieldAnchor Airfield(string name) => airfields.Find(a => a.name == name);
        public ArenaAnchor Arena(string modeId) => arenas.Find(a => a.modeId == modeId);

        /// <summary>The airfields a mode uses, in layout order (empty for an unknown mode).</summary>
        public IEnumerable<AirfieldAnchor> AirfieldsFor(string modeId)
        {
            var mode = Mode(modeId);
            if (mode?.airfields == null)
                yield break;
            foreach (var a in airfields)
                if (Array.IndexOf(mode.airfields, a.name) >= 0)
                    yield return a;
        }

        /// <summary>Where bots patrol in <paramref name="modeId"/>: the mode's own fight areas, or the map's when it lists none (or isn't listed).</summary>
        public IReadOnlyList<FightAreaAnchor> FightAreasFor(string modeId)
        {
            var mode = modeId != null ? Mode(modeId) : null;
            return mode?.fightAreas is { Length: > 0 } own ? own : fightAreas;
        }

        public IEnumerable<LaneAnchor> LanesFor(string modeId)
        {
            var used = new HashSet<string>();
            foreach (var a in AirfieldsFor(modeId))
                used.Add(a.name);
            foreach (var l in lanes)
                if (used.Contains(l.airfield))
                    yield return l;
        }

        /// <summary>
        /// What to spawn for <paramref name="modeId"/>: its airfields, its banner stands (if it uses them, and only at its
        /// airfields) and its arena. Null or a mode the layout doesn't list gets every piece: the map as one sandbox.
        /// </summary>
        public MapPieces PiecesFor(string modeId)
        {
            var pieces = new MapPieces();
            var mode = modeId != null ? Mode(modeId) : null;
            if (mode == null)
            {
                pieces.airfields.AddRange(airfields);
                pieces.bannerStands.AddRange(bannerStands);
                pieces.arenas.AddRange(arenas);
                return pieces;
            }
            pieces.airfields.AddRange(AirfieldsFor(modeId));
            if (mode.bannerStands)
                foreach (var s in bannerStands)
                    if (pieces.airfields.Exists(a => a.name == s.airfield))
                        pieces.bannerStands.Add(s);
            if (!string.IsNullOrEmpty(mode.arena) && Arena(mode.arena) is { } arena)
                pieces.arenas.Add(arena);
            return pieces;
        }

        /// <summary>Problems that would stop a mode builder: unknown names, missing arenas. Empty when sound.</summary>
        public List<string> Validate()
        {
            var problems = new List<string>();
            foreach (var l in lanes)
                if (Airfield(l.airfield) == null)
                    problems.Add($"lane '{l.name}' names unknown airfield '{l.airfield}'");
            foreach (var s in bannerStands)
                if (Airfield(s.airfield) == null)
                    problems.Add($"banner stand names unknown airfield '{s.airfield}'");
            foreach (var m in modes)
            {
                if (m.airfields != null)
                    foreach (var n in m.airfields)
                        if (Airfield(n) == null)
                            problems.Add($"mode '{m.modeId}' names unknown airfield '{n}'");
                if (!string.IsNullOrEmpty(m.arena) && Arena(m.arena) == null)
                    problems.Add($"mode '{m.modeId}' names unknown arena '{m.arena}'");
            }
            return problems;
        }
    }
}

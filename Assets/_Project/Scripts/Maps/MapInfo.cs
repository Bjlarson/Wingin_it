using System.Collections.Generic;

namespace WingingIt.Maps
{
    /// <summary>Which top-down sketch a map card draws.</summary>
    public enum MapSketch
    {
        Island,
        BroadReach,
        Archipelago,
        Mountains,
    }

    /// <summary>
    /// One card on the map select screen (Design/06-UI-UX/front-end-menus.md#map-select): the map scene to load and
    /// what the card says about it. Every map plays every mode (Design/05-Levels/island-map.md#map-list-decided-2026-10-26).
    /// </summary>
    public sealed class MapInfo
    {
        /// <summary>The map scene (<c>Scenes/Maps</c>, listed in Build Settings) that <see cref="MapLoader"/> loads.</summary>
        public string Scene;
        public string Name;
        public string Pitch;
        /// <summary>Across the flight boundary, for the card.</summary>
        public string Size;
        /// <summary>A few words on its ground, for the card.</summary>
        public string Terrain;
        public MapSketch Sketch;

        /// <summary>The maps, in card order. The first is the default.</summary>
        public static readonly IReadOnlyList<MapInfo> All = new[]
        {
            new MapInfo
            {
                Scene = "Map_IslandV2",
                Name = "The Island",
                Pitch = "A long island split by a strait: a green plateau north, sharp peaks south. The all-rounder.",
                Size = "6.6 KM",
                Terrain = "MIXED",
                Sketch = MapSketch.Island,
            },
            new MapInfo
            {
                Scene = "Map_BroadReach",
                Name = "Broad Reach",
                Pitch = "A long valley between ridges and open plains. Long sightlines, far-flung fields.",
                Size = "7.6 KM",
                Terrain = "OPEN",
                Sketch = MapSketch.BroadReach,
            },
            new MapInfo
            {
                Scene = "Map_ShatteredArchipelago",
                Name = "Shattered Archipelago",
                Pitch = "A chain of steep islands and narrow sounds. Close, fast and low.",
                Size = "3.8 KM",
                Terrain = "TIGHT",
                Sketch = MapSketch.Archipelago,
            },
            new MapInfo
            {
                Scene = "Map_MountainRange",
                Name = "Mountain Range",
                Pitch = "Snowbound peaks and frozen passes under falling snow. Thread the valleys or climb above them.",
                Size = "4.2 KM",
                Terrain = "SNOW",
                Sketch = MapSketch.Mountains,
            },
        };

        /// <summary>The index in <see cref="All"/> of the map with this scene name, or -1.</summary>
        public static int IndexOf(string scene)
        {
            for (int i = 0; i < All.Count; i++)
                if (All[i].Scene == scene)
                    return i;
            return -1;
        }
    }
}

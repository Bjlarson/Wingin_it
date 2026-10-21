using System;
using System.Collections.Generic;
using UnityEngine;
using WingingIt.Bots;

namespace WingingIt.Match
{
    /// <summary>The picture on a game mode card (drawn by the menu, not an image).</summary>
    public enum ModeSketch
    {
        Teams,
        Banner,
        Domination,
        RamRoyale,
    }

    /// <summary>
    /// One card on the game mode screen: what the mode is called, its pitch, players and length, whether it has the
    /// bot difficulty setting, and the <see cref="MatchMode"/> that runs it. Adding a mode means adding an entry (and
    /// its card art and rules), not changing the menu or the match flow.
    /// </summary>
    public sealed class GameModeInfo
    {
        /// <summary>Stable key for saved settings (never shown, never renamed).</summary>
        public string Id;
        public string Name;
        public string Pitch;
        public string Players;
        /// <summary>Seconds of play.</summary>
        public float Length;
        /// <summary>Shows the BOTS: EASY / NORMAL / HARD row on its card.</summary>
        public bool HasBotDifficulty;
        /// <summary>The bot difficulty until the player picks one for this mode.</summary>
        public BotDifficulty DefaultDifficulty = BotDifficulty.Normal;
        public ModeSketch Sketch;
        /// <summary>Makes a fresh rules object for one match of this mode; null while the mode is still being built.</summary>
        public Func<MatchMode> Rules;

        /// <summary>Its rules exist, so it can be played.</summary>
        public bool IsPlayable => Rules != null;

        /// <summary>Where this mode's bot difficulty is remembered (<see cref="PlayerPrefs"/>).</summary>
        public string DifficultyPref => $"WingingIt.{Id}.BotDifficulty";

        /// <summary>
        /// Whether the card shows on the game mode screen. A mode still being built shows only in the Editor and
        /// development builds (tagged IN DEVELOPMENT, no PLAY), so players never see a locked or "coming soon" card.
        /// </summary>
        public bool ShowsInMenu(bool developmentBuild) => IsPlayable || developmentBuild;

        /// <summary>A fresh <see cref="MatchMode"/> for this card, knowing its card; null if it isn't playable yet.</summary>
        public MatchMode CreateMode()
        {
            if (Rules == null)
                return null;
            var mode = Rules();
            mode.Info = this;
            return mode;
        }

        /// <summary>Indices into <see cref="All"/> of the cards the menu shows, in card order.</summary>
        public static List<int> MenuCards(bool developmentBuild)
        {
            var cards = new List<int>();
            for (int i = 0; i < All.Count; i++)
                if (All[i].ShowsInMenu(developmentBuild))
                    cards.Add(i);
            return cards;
        }

        /// <summary>The modes that exist, in card order. A mode's <see cref="Rules"/> are set once it's playable.</summary>
        public static readonly IReadOnlyList<GameModeInfo> All = new[]
        {
            new GameModeInfo
            {
                Id = "Teams",
                Name = "Teams",
                Pitch = "Two teams of eight. Bring down planes, help your wingmen, outscore the other side.",
                Players = "8 v 8",
                Length = 300f,
                HasBotDifficulty = true,
                Sketch = ModeSketch.Teams,
                Rules = () => new TeamsMode(),
            },
            new GameModeInfo
            {
                Id = "Banner",
                Name = "Capture the Banner",
                Pitch = "Snatch the enemy banner with a low pass over its stand and tow it home. The banner drags; your team escorts.",
                Players = "8 v 8",
                Length = 300f,
                HasBotDifficulty = true,
                Sketch = ModeSketch.Banner,
                Rules = () => new BannerMode(),
            },
            new GameModeInfo
            {
                Id = "Domination",
                Name = "Airfield Domination",
                Pitch = "Land on an airfield to take it. Bomb the control towers to crack a defended field. Hold them all to win.",
                Players = "8 v 8",
                Length = 300f,
                HasBotDifficulty = true,
                Sketch = ModeSketch.Domination,
                Rules = () => new DominationMode(),
            },
            new GameModeInfo
            {
                Id = "RamRoyale",
                Name = "Ram Royale",
                Pitch = "No guns, one life, a small arena over the sea. Ram everyone else out of the sky. Last plane flying wins.",
                Players = "16 FFA",
                Length = 180f,
                HasBotDifficulty = true,
                Sketch = ModeSketch.RamRoyale,
            },
        };
    }
}

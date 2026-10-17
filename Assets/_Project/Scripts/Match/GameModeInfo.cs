using System.Collections.Generic;

namespace WingingIt.Match
{
    /// <summary>
    /// One card on the game mode screen: what the mode is called, its pitch, players and length, and whether it has
    /// the bot difficulty setting. Adding a mode means adding an entry (and its card art), not changing the menu.
    /// </summary>
    public sealed class GameModeInfo
    {
        public string Name;
        public string Pitch;
        public string Players;
        /// <summary>Seconds of play.</summary>
        public float Length;
        /// <summary>Shows the BOTS: EASY / NORMAL / HARD row on its card.</summary>
        public bool HasBotDifficulty;

        /// <summary>The modes that exist, in card order. Planned modes join only once they're playable.</summary>
        public static readonly IReadOnlyList<GameModeInfo> All = new[]
        {
            new GameModeInfo
            {
                Name = "Teams",
                Pitch = "Two teams of eight. Bring down planes, help your wingmen, outscore the other side.",
                Players = "8 v 8",
                Length = 300f,
                HasBotDifficulty = true,
            },
        };
    }
}

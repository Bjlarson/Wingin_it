using System;
using UnityEngine;

namespace WingingIt.World
{
    /// <summary>The canopy's gore layout (one model each, ArtSource/Pilot/build_pilot.py PATTERNS).</summary>
    public enum CanopyPattern
    {
        /// <summary>Main colour and trim on every other gore.</summary>
        Stripes,
        /// <summary>Four gores of each, in quarters.</summary>
        Quarters,
        /// <summary>Main colour over the top, trim round the skirt.</summary>
        Crown,
        /// <summary>All main colour, trim on the vent.</summary>
        Solid,
    }

    /// <summary>A named swatch the pilot can wear.</summary>
    public readonly struct PilotSwatch
    {
        public readonly string Id;
        public readonly string Name;
        public readonly Color Color;

        public PilotSwatch(string id, string name, Color color)
        {
            Id = id;
            Name = name;
            Color = color;
        }
    }

    /// <summary>
    /// How the player's pilot looks (playtest G4, Design/06-UI-UX/front-end-menus.md#pilot): suit, helmet, canopy and
    /// canopy trim colours as swatch ids, the canopy pattern, and which variant of each <see cref="PilotGearSlot"/> the
    /// pilot wears (<see cref="PilotGear"/> ids; a save from before gear reads as the defaults). Saved with the settings; shown on the menu stage's
    /// pilot and worn when the player ejects. <see cref="PilotColors.Team"/> follows the plane's team colour, so the
    /// out-of-the-box look (all team, white trim, stripes) is what every bot wears.
    /// </summary>
    [Serializable]
    public struct PilotLook : IEquatable<PilotLook>
    {
        public string suit;
        public string helmet;
        public string canopy;
        public string trim;
        public CanopyPattern pattern;
        public string gearSuit;
        public string gearHelmet;
        public string gearBackpack;
        public string gearGloves;
        public string gearBoots;

        /// <summary>The look every bot wears, and the player's until they change it.</summary>
        public static PilotLook Default => new()
        {
            suit = PilotColors.Team, helmet = PilotColors.Team, canopy = PilotColors.Team, trim = "white",
            pattern = CanopyPattern.Stripes,
            gearSuit = PilotGear.Default(PilotGearSlot.Suit), gearHelmet = PilotGear.Default(PilotGearSlot.Helmet),
            gearBackpack = PilotGear.Default(PilotGearSlot.Backpack), gearGloves = PilotGear.Default(PilotGearSlot.Gloves),
            gearBoots = PilotGear.Default(PilotGearSlot.Boots),
        };

        /// <summary>The variant worn in <paramref name="slot"/> (may be unknown: see <see cref="Clamped"/>).</summary>
        public string Gear(PilotGearSlot slot) => slot switch
        {
            PilotGearSlot.Suit => gearSuit,
            PilotGearSlot.Helmet => gearHelmet,
            PilotGearSlot.Backpack => gearBackpack,
            PilotGearSlot.Gloves => gearGloves,
            _ => gearBoots,
        };

        /// <summary>This look wearing <paramref name="variant"/> in <paramref name="slot"/>.</summary>
        public PilotLook WithGear(PilotGearSlot slot, string variant)
        {
            var l = this;
            switch (slot)
            {
                case PilotGearSlot.Suit: l.gearSuit = variant; break;
                case PilotGearSlot.Helmet: l.gearHelmet = variant; break;
                case PilotGearSlot.Backpack: l.gearBackpack = variant; break;
                case PilotGearSlot.Gloves: l.gearGloves = variant; break;
                default: l.gearBoots = variant; break;
            }
            return l;
        }

        /// <summary>This look with any unknown swatch, pattern or gear (an old or corrupt save) put back to the default.</summary>
        public PilotLook Clamped()
        {
            var d = Default;
            var clamped = new PilotLook
            {
                suit = PilotColors.IsKnown(suit) ? suit : d.suit,
                helmet = PilotColors.IsKnown(helmet) ? helmet : d.helmet,
                canopy = PilotColors.IsKnown(canopy) ? canopy : d.canopy,
                trim = PilotColors.IsKnown(trim) ? trim : d.trim,
                pattern = Enum.IsDefined(typeof(CanopyPattern), pattern) ? pattern : d.pattern,
            };
            foreach (var slot in PilotGear.Slots)
                clamped = clamped.WithGear(slot, PilotGear.IsKnown(slot, Gear(slot)) ? Gear(slot) : PilotGear.Default(slot));
            return clamped;
        }

        /// <summary>Paints <paramref name="figure"/> and <paramref name="chute"/> (either may be null), TEAM as <paramref name="team"/>.</summary>
        public void ApplyTo(PilotFigure figure, PilotParachute chute, Color team)
        {
            if (figure != null)
            {
                foreach (var slot in PilotGear.Slots)
                    if (!figure.SetGear(slot, Gear(slot)))
                        figure.SetGear(slot, PilotGear.Default(slot));
                figure.SetColors(PilotColors.Resolve(suit, team), PilotColors.Resolve(helmet, team));
            }
            if (chute != null)
            {
                chute.SetPattern(pattern);
                chute.SetColors(PilotColors.Resolve(canopy, team), PilotColors.Resolve(trim, team));
            }
        }

        public bool Equals(PilotLook o) => suit == o.suit && helmet == o.helmet && canopy == o.canopy && trim == o.trim &&
                                            pattern == o.pattern && gearSuit == o.gearSuit && gearHelmet == o.gearHelmet &&
                                            gearBackpack == o.gearBackpack && gearGloves == o.gearGloves && gearBoots == o.gearBoots;
    }

    /// <summary>The swatches a pilot's suit, helmet and canopy can take. Ids are the save format: never rename one.</summary>
    public static class PilotColors
    {
        /// <summary>The plane's own team colour (whatever team the player is on).</summary>
        public const string Team = "team";

        public static readonly PilotSwatch[] All =
        {
            new(Team, "TEAM", Color.white),
            new("white", "WHITE", new Color(0.95f, 0.94f, 0.91f)),
            new("charcoal", "CHARCOAL", new Color(0.2f, 0.21f, 0.24f)),
            new("grey", "GREY", new Color(0.55f, 0.58f, 0.62f)),
            new("olive", "OLIVE", new Color(0.43f, 0.5f, 0.31f)),
            new("khaki", "KHAKI", new Color(0.79f, 0.69f, 0.49f)),
            new("leather", "LEATHER", new Color(0.42f, 0.27f, 0.19f)),
            new("red", "RED", new Color(0.9f, 0.22f, 0.2f)),
            new("orange", "ORANGE", new Color(1f, 0.54f, 0.16f)),
            new("yellow", "YELLOW", new Color(1f, 0.82f, 0.2f)),
            new("green", "GREEN", new Color(0.3f, 0.7f, 0.36f)),
            new("sky", "SKY BLUE", new Color(0.45f, 0.75f, 1f)),
            new("navy", "NAVY", new Color(0.13f, 0.2f, 0.42f)),
            new("purple", "PURPLE", new Color(0.52f, 0.33f, 0.8f)),
            new("pink", "PINK", new Color(1f, 0.48f, 0.67f)),
            new("sage", "FLIGHT GREEN", new Color(0.62f, 0.68f, 0.58f)),
        };

        public static int IndexOf(string id) => Array.FindIndex(All, s => s.Id == id);

        public static bool IsKnown(string id) => IndexOf(id) >= 0;

        /// <summary>The swatch's colour, or <paramref name="team"/> for <see cref="Team"/> (and for an unknown id).</summary>
        public static Color Resolve(string id, Color team)
        {
            int i = IndexOf(id);
            return i <= 0 ? team : All[i].Color;
        }

        /// <summary>The swatch's display name (TEAM for an unknown id).</summary>
        public static string Name(string id) => All[Mathf.Max(0, IndexOf(id))].Name;

        /// <summary>The next (<paramref name="dir"/> +1) or previous (-1) swatch, wrapping round.</summary>
        public static string Step(string id, int dir)
        {
            int n = All.Length;
            return All[((Mathf.Max(0, IndexOf(id)) + dir) % n + n) % n].Id;
        }
    }
}

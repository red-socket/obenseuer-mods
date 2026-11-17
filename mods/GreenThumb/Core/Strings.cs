namespace GreenThumb
{
    // Every player-facing string (spec §14). Run through the humanizer again after any change (CLAUDE.md). Both lines go
    // into the game's TMP prompt font, so they are ASCII only.
    internal static class Strings
    {
        // The game's own hold hint on ManualHarvest plants (SRC/grow.cs:439, GrowItem.cs:227). H2 matches it exactly.
        internal const string VanillaHoldHint = "Hold - Keep harvesting";

        // H2 (D18): replaces the hold hint. Never says "Harvest all", which the game already uses for one plant with
        // several fruits (SRC/grow.cs:272-275).
        internal const string HoldHint = "Hold - Keep harvesting, hold longer for everything in reach";

        // P5 (D8): an extra line under the plant's title while its grower is paused: switched on but unpowered, or switched
        // Off in its panel (CraftingBase.On false).
        internal const string PausedNoPower = "No power: growth paused";
        internal const string PausedOff = "Switched off: growth paused";
    }
}

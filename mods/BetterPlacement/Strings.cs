namespace BetterPlacement
{
    // Every string players see in game (spec §3.8), humanized 2026-11-16. The controls-line segments copy vanilla's
    // "KEY)ACTION   " style and capitals (SRC/BuildingSystemMenu.cs:883-891).
    internal static class Strings
    {
        // ---- Controls line (H1/H2) ----
        internal const string Sep = "   ";
        internal const string AlignHold = "{0})ALIGN";
        internal const string AlignToggle = "{0})ALIGN: {1}";
        internal const string AlignAlways = "ALIGN: ON ({0}: OFF)";
        internal const string AlignAlwaysNoKey = "ALIGN: ON";
        internal const string On = "ON";
        internal const string Off = "OFF";
        internal const string Snap = "{0})STEP {1}°";
        internal const string Coarse = " {0}){1}°";
        internal const string HeldStep = "{0}° (HELD)";
        internal const string PadLeft = "D-PAD LEFT";
        internal const string PadRight = "D-PAD RIGHT";
        internal const string LeftAlt = "Left Alt";
        internal const string RightAlt = "Right Alt";

        // ---- Notifications ----
        internal const string Title = "Better Placement";

        internal static string KeyClash(string feature, string key, string clash) =>
            $"The key for {feature} ({key}) is also bound to {clash}, so it's off for now. Change either one. " +
            "Changes to the game's key settings take effect at the next loading screen.";

        internal static string PadClash(string pad) =>
            $"Aligning and rotation snapping both use {pad}, so the snap button is off for now. Change either one.";
    }
}

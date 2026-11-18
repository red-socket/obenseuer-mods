namespace FieldGuide
{
    // Small null-safe reads of game state shared by the features.
    internal static class Game
    {
        internal const string MainMenuScene = "Main Menu";

        // A gameplay scene has the global state object (the Recipe Book startup check).
        internal static bool InGameplay => GlobalState.instance != null;

        // The trader whose window is open, or null.
        internal static Trade Trading => TradePanel.instance != null ? TradePanel.instance.activeTrade : null;
    }
}

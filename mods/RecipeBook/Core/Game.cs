namespace RecipeBook
{
    // Live accessors over game state, copied from Fair Market (Vital Signs'). Unity objects are null-checked with != null
    // and never cached across scenes.
    internal static class Game
    {
        internal static bool ConsoleOpen
        {
            get
            {
                try { var c = DeveloperConsole.Instance; return c != null && c.consoleOpened; }
                catch { return false; }
            }
        }

        internal static bool Controller
        {
            get
            {
                try { return InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller; }
                catch { return false; }
            }
        }
    }
}

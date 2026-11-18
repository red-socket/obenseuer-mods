using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace NoFloorSpill
{
    // Session summary on quit. Quit to Desktop (pause menu) and Quit (main menu) kill the process, so neither
    // Application.quitting nor OnDestroy runs (docs/README.md item 23). A last-priority prefix on both buttons logs the
    // summary and flushes BepInEx's disk log, whose writer only flushes on a 2 s timer and would lose the line to the
    // kill. Application.quitting stays for the graceful exits: the OS closing the game window (Alt+F4, or the title-bar X
// in windowed mode) and SteamManager's Application.Quit when Steam fails to start. (WindowScript's close button never
// shows: no scene or prefab uses that script.)
    // Logging only: the prefix never changes whether or how the game quits.
    internal static class QuitHook
    {
        private static readonly MethodInfo PrefixMethod = AccessTools.Method(typeof(QuitHook), nameof(Prefix));

        // Not a patch group: a failure here only loses the summary line, never a feature.
        internal static void Apply(Harmony harmony)
        {
            foreach (var target in new[] { Vanilla.QuitDesktop, Vanilla.QuitOK })
            {
                if (target == null) continue; // Vanilla already logged it
                try { harmony.Patch(target, prefix: new HarmonyMethod(PrefixMethod) { priority = Priority.Last }); }
                catch (Exception e) { Log.WarnOnce("quit|" + SwapGroup.Key(target), $"Could not hook {SwapGroup.Key(target)}; " +
                    $"the session summary is not logged when quitting from there ({e.Message})."); }
            }
        }

        // __runOriginal: another mod's prefix may cancel the quit (a confirmation, say); then this is not the end.
        private static void Prefix(bool __runOriginal)
        {
            if (!__runOriginal) return;
            Plugin.Step("quit", () =>
            {
                Tally.SessionSummary();
                FlushDiskLog();
            });
        }

        // Also called from Plugin.OnQuitting: BepInEx's own shutdown flush is not guaranteed to run in time.
        internal static void FlushDiskLog()
        {
            foreach (var l in Logger.Listeners)
                if (l is DiskLogListener disk) disk.LogWriter?.Flush();
        }
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using HarmonyLib;

namespace SaveGuard
{
    // P1: SaveController.SaveGame(string, string, string), public static (SRC/SaveController.cs:481).
    // The prefix opens the save context; the finalizer runs in the same call stack and sees the exact exception.
    [HarmonyPatch]
    internal static class SaveGamePatch
    {
        private static bool _unprotectedWarned;

        // False after a SaveGame that returned normally but whose written slot failed Commit's check. Set by every
        // outermost SaveGame; read by the timed-autosave runner right after its own call.
        internal static bool LastCommitOk = true;

        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(SaveController), "SaveGame", new[] { typeof(string), typeof(string), typeof(string) });

        private static void Prefix(string savename)
        {
            try
            {
                var c = SaveTxn.Ctx;
                if (c != null)
                {
                    Log.Warn("SaveGame called while another SaveGame is running; the inner call is not protected.");
                    c.Nested++;
                    return;
                }
                // A backup still waiting from the previous save copies that state before this save changes it.
                BackupStore.FlushPending();
                SaveTxn.Ctx = new SaveCtx { Kind = KindOf(savename), StartedUtc = DateTime.UtcNow };
            }
            catch (Exception e) { Log.Exception("SaveGame prefix", e); }
        }

        private static string KindOf(string savename)
        {
            if (TimedAutosave.Running) return "timed";
            if (SaveController.Loading) return "door";
            string name = Regex.Replace(savename ?? "", "[^\\w]", "");
            if (name.Length == 0) name = "Autosave";
            return Regex.IsMatch(name, "^Slot\\d+$") ? "manual" : "script";
        }

        // Always returns __exception: vanilla failure behaviour is kept. Swallowing it would let ChangeLevelDelay
        // go on to load a slot that was just rolled back, losing progress silently.
        private static Exception Finalizer(Exception __exception)
        {
            var c = SaveTxn.Ctx;
            if (c == null) return __exception;
            if (c.Nested > 0) { c.Nested--; return __exception; }
            SaveTxn.Ctx = null;
            LastCommitOk = true;
            try
            {
                if (__exception == null)
                {
                    bool good = true;
                    if (c.Begun)
                    {
                        // Not confirmed until Commit returns true: if Commit throws, TimedSave must not report "Autosaved".
                        LastCommitOk = false;
                        good = LastCommitOk = SaveTxn.Commit(c);
                    }
                    else if (Cfg.CrashSafety.Value && !c.BeginAttempted && !_unprotectedWarned)
                    {
                        _unprotectedWarned = true;
                        Log.Warn("Crash-safety hook did not run; this save was unprotected.");
                        GuardWindow.AddNotice("unprotected", "A save ran without Save Guard's crash safety (see the BepInEx log).", false);
                    }
                    if (good)
                    {
                        TimedAutosave.OnSaveSucceeded();
                        string target = c.Begun ? c.Target : CurrentSavePath();
                        if (!string.IsNullOrEmpty(target))
                            BackupStore.Queue(target, Path.GetFileName(Path.GetDirectoryName(target)), Path.GetFileName(target), c.Kind);
                    }
                }
                else if (c.Begun)
                {
                    try { SaveTxn.Rollback(c, __exception); }
                    finally { if (c.Kind != "door") HideSavingUI(); }
                }
                else
                {
                    Log.Error($"SaveGame threw {__exception.GetType().Name}: {__exception.Message} (crash safety was not active for this save)");
                }
            }
            catch (Exception e) { Log.Exception("SaveGame finalizer", e); }
            return __exception;
        }

        // Vanilla's SaveGame shows the saving icon and sets time scale 0 at its start (SavingUI.ShowSavingUI, :485-488)
        // and undoes both only at its end (:554-557), which the exception skipped. After Save Guard has rolled back or
        // kept the save and says so, this does what that end would have done: HideSavingUI hides the icon and, unless
        // the pause menu is open, unpauses (SavingUI.cs). A door save is left alone: the level change is stuck anyway.
        private static void HideSavingUI()
        {
            try
            {
                if (SavingUI.instance != null && PauseMenu.instance != null && GameController.instance != null)
                    SavingUI.instance.HideSavingUI();
            }
            catch (Exception e) { Log.Exception("HideSavingUI after a failed save", e); }
        }

        private static string CurrentSavePath()
        {
            try { return SaveController.instance != null ? SaveController.instance.GetCurrentSavePath() : null; }
            catch (Exception) { return null; }
        }
    }

    // P2: SaveController.IsSameFolder(string, string), private static (:600). Its only caller is :532, after header
    // serialization and right before vanilla's destructive delete (:533-536). __result is vanilla's own `flag`.
    // The method has a try/catch, so Mono does not inline it.
    [HarmonyPatch]
    internal static class IsSameFolderPatch
    {
        private static MethodBase TargetMethod() =>
            AccessTools.Method(typeof(SaveController), "IsSameFolder", new[] { typeof(string), typeof(string) });

        private static void Postfix(string pathB, bool __result)
        {
            try
            {
                var c = SaveTxn.Ctx;
                if (c == null || c.Begun || c.BeginAttempted || c.Nested > 0 || !Cfg.CrashSafety.Value) return;
                c.BeginAttempted = true;
                SaveTxn.Begin(c, pathB, __result);
            }
            catch (Exception e) { Log.Exception("IsSameFolder postfix", e); }
        }
    }
}

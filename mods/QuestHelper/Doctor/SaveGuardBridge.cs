using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace QuestHelper
{
    // Save Guard by reflection (spec §4.6, D2): no compile-time reference, so Quest Helper runs without it. Resolved once,
    // the first time it is needed. BackupStore.AddRecovered(src, character, slot, kind, move, complete, Kv extra) copies a
    // slot into Save Guard's "recovered" list with its entry.txt (SaveGuard/BackupStore.cs:127); SaveTxn.Ctx is set while
    // a save is being written (SaveGuard/SaveTxn.cs:26).
    internal static class SaveGuardBridge
    {
        internal const string Guid = "redsocket.obenseuer.saveguard";

        private static bool _resolved;
        private static MethodInfo _addRecovered;
        private static FieldInfo _ctx;

        internal static bool Installed
        {
            get
            {
                try { return Chainloader.PluginInfos.ContainsKey(Guid); }
                catch { return false; }
            }
        }

        private static void Resolve()
        {
            if (_resolved) return;
            _resolved = true;
            if (!Installed) return;
            try
            {
                var store = AccessTools.TypeByName("SaveGuard.BackupStore");
                var kv = AccessTools.TypeByName("SaveGuard.Kv");
                if (store != null && kv != null)
                    _addRecovered = AccessTools.Method(store, "AddRecovered",
                        new[] { typeof(string), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(bool), kv });
                var txn = AccessTools.TypeByName("SaveGuard.SaveTxn");
                if (txn != null) _ctx = AccessTools.Field(txn, "Ctx");
            }
            catch (Exception e) { Log.Exception("save guard bridge", e); }
            if (_addRecovered == null)
                Log.WarnOnce("sg bridge", "Backup: Save Guard is installed but its backup method was not found; Quest Helper makes its own backups.");
        }

        // Save Guard's backup can be used (installed, the method resolved).
        internal static bool Usable
        {
            get
            {
                Resolve();
                return _addRecovered != null;
            }
        }

        // A save is being written right now (Save Guard's transaction is open). False when Save Guard is absent.
        internal static bool SaveInProgress
        {
            get
            {
                Resolve();
                try { return _ctx != null && _ctx.GetValue(null) != null; }
                catch { return false; }
            }
        }

        // Save Guard's toasts (SaveGuard/Ui/Toasts.cs) use the same rectangle at the top right, so ours start below the
        // ones it is showing. Read by reflection each frame a toast of ours is up; empty when anything is missing.
        private static bool _toastsResolved;
        private static FieldInfo _toastActive, _toastText, _toastUntil;

        internal static IEnumerable<string> VisibleToasts()
        {
            if (!_toastsResolved)
            {
                _toastsResolved = true;
                try
                {
                    if (Installed)
                    {
                        var toasts = AccessTools.TypeByName("SaveGuard.Toasts");
                        var entry = toasts != null ? AccessTools.Inner(toasts, "Toast") : null;
                        _toastActive = toasts != null ? AccessTools.Field(toasts, "Active") : null;
                        _toastText = entry != null ? AccessTools.Field(entry, "Text") : null;
                        _toastUntil = entry != null ? AccessTools.Field(entry, "Until") : null;
                    }
                }
                catch (Exception e) { Log.Exception("save guard toasts", e); }
            }
            if (_toastActive == null || _toastText == null || _toastUntil == null) yield break;
            IList list;
            try { list = _toastActive.GetValue(null) as IList; }
            catch { yield break; }
            if (list == null) yield break;
            float now = UnityEngine.Time.realtimeSinceStartup;
            for (int i = 0; i < list.Count; i++)
            {
                string text = null;
                try
                {
                    var t = list[i];
                    float until = (float)_toastUntil.GetValue(t);
                    if (until < 0f || now <= until) text = _toastText.GetValue(t) as string;
                }
                catch { }
                if (text != null) yield return text;
            }
        }

        // Returns the backup folder; throws (unwrapped) on any failure.
        internal static string AddRecovered(string src, string character, string slot)
        {
            Resolve();
            if (_addRecovered == null) throw new InvalidOperationException("Save Guard's backup method is missing");
            try { return (string)_addRecovered.Invoke(null, new object[] { src, character, slot, "questhelper", false, true, null }); }
            catch (TargetInvocationException e) when (e.InnerException != null) { throw e.InnerException; }
        }
    }
}

using System;
using System.Reflection;
using HarmonyLib;

namespace MessageLog
{
    // N1: Notifications.CreateNotification(string, string, bool) postfix (spec §4.1), default priority. It sees the
    // arguments after Unofficial Patch's ref prefix (ScamInvoiceHold.cs:121-131), so the log shows "Account frozen" as the
    // player saw it, and it logs nothing when another mod's prefix skipped the toast. Read-only: no IO, no reflection, no
    // Notification objects (both lists are saved into Globals.tnmt, SRC/Notifications.cs:35-38) and never a call back
    // into CreateNotification. The load replay (OnLoadingGame, :40-55) shows saved toasts without calling this method, so
    // nothing is logged twice.
    [HarmonyPatch]
    internal static class NotifyCapture
    {
        private static MethodBase TargetMethod() => Vanilla.CreateNotification;

        private static void Postfix(string type, string text, bool silent, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || !Cfg.Hot.Notifications) return;
                if (string.IsNullOrEmpty(type) && string.IsNullOrEmpty(text)) return;
                History.AddNotification(type ?? "", Text.Cap(text ?? ""), silent);
            }
            catch (Exception e) { Log.Once("Capture: notification", e); }
        }
    }
}

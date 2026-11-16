using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Workshop
{
    // The look-at hint (spec §5.6) and the repeat key's short feedback (§5.5). Shown only where the key would act from
    // the world: a machine nobody else owns, not locked, with something remembered, and the key bound and not suspended.
    internal static class LookHint
    {
        internal static bool Patched;
        private const float FlashSeconds = 2f;

        private static readonly Dictionary<CraftingBase, (string text, float until)> Flashes = new Dictionary<CraftingBase, (string, float)>();

        internal static void OnSceneLoaded() => Flashes.Clear();

        // The text appended to the machine's use message, or null for none.
        internal static string Text(CraftingBase cb)
        {
            if (!Patched || !Cfg.Queue.LookHint.Value || !Cfg.Queue.Enabled.Value || cb == null) return null;
            if (Stations.IsOwned(cb) || Stations.IsLocked(cb)) return null;
            var key = Cfg.Queue.RepeatKey;
            if (!Keys.Bound(key) || Keys.Suspended(key)) return null;
            var kind = Stations.KindOf(cb);
            if (kind == Kind.AutoRun) return Strings.HintAuto;
            if (kind != Kind.Plain && kind != Kind.Vessel) return null;
            if (Flashes.TryGetValue(cb, out var f) && Time.unscaledTime <= f.until) return Strings.HintFlash(f.text);
            var e = MachineStore.Get(cb, false);
            if (e == null || !e.HasLast) return null;
            string k = Keys.Label(key);
            if (e.Queued) return Strings.HintQueued(e.QName, e.QLeft, k);
            return Strings.HintRepeat(k, e.LastName, kind == Kind.Vessel ? e.LastMult : e.LastCount);
        }

        // Shows a short note in the machine's look-at text, or as a notification when its panel is open.
        internal static void Flash(CraftingBase cb, string text, bool warn)
        {
            if (cb == null || string.IsNullOrEmpty(text)) return;
            if (Vanilla.PanelOpen(cb) || !Patched || !Cfg.Queue.LookHint.Value)
            {
                Notify.Show(text);
                return;
            }
            Flashes[cb] = (text, Time.unscaledTime + FlashSeconds);
            Refresh(cb);
            if (warn)
                try { if (UseText.instance != null) UseText.instance.WarningUseEffect(); }
                catch (Exception e) { Log.Exception("use text warning", e); }
        }

        // Re-shows the use text when the player is looking at this machine (the game only rebuilds it on a new target).
        internal static void Refresh(CraftingBase cb)
        {
            try
            {
                if (cb == null || InteractObjects.instance == null || Game.MenuVisible) return;
                if (InteractObjects.instance.interactable is CraftingBase looked && looked == cb) cb.UpdateUseMessage();
            }
            catch (Exception e) { Log.Exception("hint refresh", e); }
        }

        // Plugin.Update (inside RepeatKey.Tick): expired flashes give the plain hint back.
        internal static void Tick()
        {
            if (Flashes.Count == 0) return;
            float now = Time.unscaledTime;
            List<CraftingBase> stale = null;
            foreach (var kv in Flashes)
                if (kv.Key == null || now > kv.Value.until) (stale ?? (stale = new List<CraftingBase>())).Add(kv.Key);
            if (stale == null) return;
            foreach (var cb in stale)
            {
                Flashes.Remove(cb);
                Refresh(cb);
            }
        }
    }

    // W8: CraftingBase.UpdateUseMessage() (SRC/CraftingBase.cs:579-595). The prefix appends the hint to UseMessage
    // ([fsIgnore], never saved) for this one call; the finalizer always puts the original back.
    [HarmonyPatch]
    internal static class UseMessagePatch
    {
        private static MethodBase TargetMethod() => Vanilla.UpdateUseMessage;

        private static void Prefix(CraftingBase __instance, out string __state)
        {
            __state = null;
            try
            {
                if (!Cfg.Queue.LookHint.Value) return;
                var hint = LookHint.Text(__instance);
                if (hint == null) return;
                __state = __instance.UseMessage ?? "";
                __instance.UseMessage = __state + hint;
            }
            catch (Exception e)
            {
                Log.Exception("look hint", e);
                if (__state != null) __instance.UseMessage = __state;
                __state = null;
            }
        }

        private static Exception Finalizer(CraftingBase __instance, string __state, Exception __exception)
        {
            if (__state != null)
                try { __instance.UseMessage = __state; }
                catch (Exception e) { Log.Exception("look hint restore", e); }
            return __exception;
        }
    }
}

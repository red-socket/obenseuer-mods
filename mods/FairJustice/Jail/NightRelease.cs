using System;
using System.Reflection;
using HarmonyLib;

namespace FairJustice
{
    // S-037 optional night-release deferral (spec §3.3C, off by default). Prison.DeltaSeconds retries Release() every
    // tick while the release condition holds (SRC/Prison.cs:173-219), so returning false at night simply moves the
    // release to the first tick after MorningHour, with the cell's OnRelease outputs unchanged. Records are already 0,
    // so no pass-out arrest can happen meanwhile. Nothing is stored: without the mod, vanilla releases after its
    // 300 s minimum.
    internal static class NightRelease
    {
        internal static bool Patched;
        private static bool _held;

        private static MethodBase _target;
        internal static MethodBase Target => _target ?? (_target = AccessTools.Method(typeof(Prison), nameof(Prison.Release), Type.EmptyTypes));

        // The panel shows "Time served · released at 06:00" while this holds.
        internal static bool HeldNow => _held && Jail.InPrison;

        internal static void ResetHold() => _held = false;

        internal static bool Live => Patched && Cfg.DeferNightRelease.Value;

        internal static bool Prefix(Prison p)
        {
            if (!Live || !Vanilla.HasActiveCell || Vanilla.ActiveCell(p) == null) return true;
            var t = TimeOfDayAzure.instance != null ? TimeOfDayAzure.instance.currentTimeAndDay : null;
            if (t == null) return true;
            if (!Cfg.InNightWindow(t.hours + t.minutes / 60f)) return true;
            if (Compat.Foreign(Target, "the night-release deferral")) return true;
            if (!_held)
            {
                _held = true;
                Log.Info($"Release held until {Cfg.MorningText}.");
                Toast.Show("night release", "Prison", $"Time served. You will be let out at {Cfg.MorningText}.");
            }
            return false;
        }
    }

    // P9: Prison.Release() bool prefix, its own patch class (its own config group).
    [HarmonyPatch(typeof(Prison), nameof(Prison.Release))]
    internal static class NightReleasePatch
    {
        private static bool Prefix(Prison __instance)
        {
            try { return NightRelease.Prefix(__instance); }
            catch (Exception e)
            {
                Log.Exception("night release", e);
                return true;
            }
        }
    }
}

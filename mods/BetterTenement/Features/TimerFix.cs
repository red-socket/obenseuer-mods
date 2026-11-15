using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace BetterTenement
{
    // S-071 renovation timer (spec §3.4.1). The setter (TenementUpgradeProgress.cs:46-51) subtracts
    // (int)(elapsed * multiplier) raw seconds, so below 1x the timer never moves in normal play and 1.25x behaves
    // like 1x. We carry the fractional remainder in memory; timeSecondsLeft stays an int in the save.
    internal static class TimerFix
    {
        internal static bool Patched;

        private static readonly ConditionalWeakTable<TenementUpgradeProgress, StrongBox<double>> Carry =
            new ConditionalWeakTable<TenementUpgradeProgress, StrongBox<double>>();

        private static MethodInfo _setter, _updateProgresses;
        internal static MethodInfo Setter => _setter ?? (_setter = AccessTools.PropertySetter(typeof(TenementUpgradeProgress), nameof(TenementUpgradeProgress.TimeSecondsLeft)));
        private static MethodInfo UpdateProgresses => _updateProgresses ?? (_updateProgresses = AccessTools.Method(typeof(TenementController), "UpdateProgesses"));

        // Patch-time shape check: the 0.5.19 setter ends with "mul, conv.i4, sub, stfld timeSecondsLeft, ret".
        // Anything else means the game changed (perhaps fixed upstream), and the patch is not applied.
        internal static bool CheckShape()
        {
            try
            {
                if (Setter == null) { Log.Info("Renovation timer: setter not found; the timer fix is off."); return false; }
                var code = new List<CodeInstruction>();
                foreach (var ci in PatchProcessor.GetOriginalInstructions(Setter, (ILGenerator)null))
                    if (ci.opcode != OpCodes.Nop) code.Add(ci);
                int n = code.Count;
                bool ok = n >= 5
                          && code[n - 1].opcode == OpCodes.Ret
                          && code[n - 2].opcode == OpCodes.Stfld && (code[n - 2].operand as FieldInfo)?.Name == nameof(TenementUpgradeProgress.timeSecondsLeft)
                          && code[n - 3].opcode == OpCodes.Sub
                          && code[n - 4].opcode == OpCodes.Conv_I4
                          && code[n - 5].opcode == OpCodes.Mul;
                if (!ok) Log.Info("Renovation timer: the game's timer code changed (maybe fixed upstream); the timer fix is off.");
                else if (UnityEngine.Application.version != Plugin.TestedGameVersion)
                    Log.Warn($"Renovation timer: shape check passed on game {UnityEngine.Application.version} (built for {Plugin.TestedGameVersion}).");
                return ok;
            }
            catch (Exception e)
            {
                Log.Info($"Renovation timer: could not read the game's timer code ({e.Message}); the timer fix is off.");
                return false;
            }
        }

        // Returns true to run vanilla. Only writer: TimeSecondsLeft -= sec at TenementController.cs:475.
        internal static bool Set(TenementUpgradeProgress p, int value)
        {
            if (!Patched || !Cfg.TimerFix.Value) return true;
            if (ReferenceEquals(p, null)) return true;   // never "== null": progresses made with new may be Unity-fake-null
            if (Compat.Foreign(Setter, "the renovation timer fix") || Compat.Foreign(UpdateProgresses, "the renovation timer fix")) return true;
            var info = Game.Difficulty;
            if (info == null) return true;
            float m = DifficultyInfo.GetMultiplierValue(info.renovationSpeed);
            if (!(m > 0f) || float.IsInfinity(m)) return true;
            int elapsed = (int)info.GetRenovationSpeed(p.timeSecondsLeft) - value;
            if (elapsed <= 0) return true;

            var box = Carry.GetOrCreateValue(p);
            double exact = elapsed * (double)m + box.Value;
            int whole = (int)Math.Floor(exact);
            box.Value = exact - whole;
            p.timeSecondsLeft -= whole;
            return false;
        }
    }

    // T1: TenementUpgradeProgress.TimeSecondsLeft setter.
    [HarmonyPatch(typeof(TenementUpgradeProgress), nameof(TenementUpgradeProgress.TimeSecondsLeft), MethodType.Setter)]
    internal static class TimerSetterPatch
    {
        [HarmonyPriority(Priority.Low)]
        private static bool Prefix(TenementUpgradeProgress __instance, int value)
        {
            try { return TimerFix.Set(__instance, value); }
            catch (Exception e)
            {
                Log.Exception("renovation timer", e);
                return true;
            }
        }
    }
}

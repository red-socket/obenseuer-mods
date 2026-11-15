#if SAVEGUARD_FAULTS
using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;

namespace SaveGuard
{
    internal enum Fault
    {
        None, ThrowAfterInfo, ThrowAfterGlobals, KillAfterInfo, KillAfterGlobals, KillAfterAllWrites,
        ThrowInSavingDone, KillDuringRestoreSwap, KillDuringBackup, FailSlotMove,
    }

    // Fault injection for the in-game test plan (spec §13). Compiled only with -p:SaveGuardFaults=true; never shipped.
    // Each fault fires once and then resets itself to None.
    internal static class Faults
    {
        private static ConfigEntry<Fault> _fault;

        internal static void Bind(ConfigFile c) =>
            _fault = c.Bind("Debug", "Fault", Fault.None, "TEST BUILD ONLY. Fires once on the next matching operation, then resets to None.");

        internal static bool Armed(Fault f) => _fault != null && _fault.Value == f;

        internal static void Consume()
        {
            Log.Warn($"FAULT {_fault.Value} fired");
            _fault.Value = Fault.None;
        }

        internal static void Kill(Fault f)
        {
            if (!Armed(f)) return;
            Consume();
            Process.GetCurrentProcess().Kill();
        }

        // ThrowInSavingDone / KillAfterAllWrites: SavingDone fires after all four writes, before the P1 finalizer.
        internal static void Hook()
        {
            SaveController.SavingDone += () =>
            {
                Kill(Fault.KillAfterAllWrites);
                if (Armed(Fault.ThrowInSavingDone)) { Consume(); throw new IOException("SaveGuard fault"); }
            };
        }

        // Private SaveController.WriteSaveFile(string, SaveFileContent): Info, Globals and the level file.
        [HarmonyPatch]
        internal static class WriteSaveFilePatch
        {
            private static MethodBase TargetMethod() =>
                AccessTools.Method(typeof(SaveController), "WriteSaveFile", new[] { typeof(string), AccessTools.Inner(typeof(SaveController), "SaveFileContent") });

            private static void Postfix(string path)
            {
                bool info = path.EndsWith("/Info.tnmt", StringComparison.OrdinalIgnoreCase);
                bool globals = path.EndsWith("/Globals.tnmt", StringComparison.OrdinalIgnoreCase);
                if (info) Kill(Fault.KillAfterInfo);
                if (globals) Kill(Fault.KillAfterGlobals);
                if ((info && Armed(Fault.ThrowAfterInfo)) || (globals && Armed(Fault.ThrowAfterGlobals)))
                {
                    Consume();
                    throw new IOException("SaveGuard fault");
                }
            }
        }
    }
}
#endif

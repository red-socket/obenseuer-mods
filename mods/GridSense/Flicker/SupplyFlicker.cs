using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace GridSense
{
    // The no-contract flicker switch (spec §5, D10-D14). PowerGrid.Flickering (SRC/PowerGrid.cs:200-221) is true for two
    // causes: (A) a running fuel generator on the grid (the listener loop, :208-214) and (B) unstableSupply at the root grid
    // (:217), which TenementController.CheckElectricity sets when there is no electricity contract (SRC/TenementController.cs:1383).
    // Child grids inherit B through the recursive getter (:219). P3 swaps the one read of unstableSupply for Unstable(),
    // so only cause B changes; cause A, the low-fuel dimming (GeneratorFuelLeft), IsPowered, capacity, load shedding, bills
    // and the field itself are untouched. Story, horror and other lamp flicker (LightFlicker, CameraShake, LightsON, ...)
    // never reads Flickering.
    //
    // Lazy (spec §5.2): patched in Awake when the switch is on, else on the first frame after it is turned on, then left in
    // place for the session; Unstable reads the switch live. With the default (off) the game's power code is never touched.
    public static class SupplyFlicker
    {
        internal static bool Patched, Failed;
        private static bool _requested, _refresh;

        // Called from the patched getter in place of `ldfld PowerGrid::unstableSupply`; public for the game's IL.
        public static bool Unstable(PowerGrid g) => g.unstableSupply && !Cfg.Hot.StopNoContractFlicker;

        internal static void InitAtLaunch()
        {
            if (Cfg.Hot.StopNoContractFlicker) EnsurePatched();
        }

        // SettingChanged (after Cfg.Hot.Refresh): patch or refresh on the next frame, on the main thread.
        internal static void OnConfigChanged()
        {
            if (Failed) return;
            if (!Patched && Cfg.Hot.StopNoContractFlicker) _requested = true;
            if (Patched || _requested) _refresh = true;
        }

        // Plugin.Update.
        internal static void Tick()
        {
            if (_requested)
            {
                _requested = false;
                if (!Patched && !Failed && Cfg.Hot.StopNoContractFlicker) EnsurePatched();
                if (Patched) Compat.Overlap(Vanilla.Flickering, "the no-contract flicker switch");
            }
            if (_refresh)
            {
                _refresh = false;
                if (Patched) RefreshAll();
            }
        }

        private static void EnsurePatched()
        {
            if (Patched || Failed) return;
            Patched = Plugin.PatchLazy("supply-flicker", new object[] { Vanilla.Flickering, Vanilla.UnstableSupplyField, Vanilla.UpdateFlicker },
                Vanilla.Flickering, typeof(FlickeringPatch), "The no-contract flicker option is unavailable this session.");
            Failed = !Patched;
        }

        // Every lamp recomputes On && IsPowered && Flickering through vanilla's own UpdateFlicker. PowerLightFlicker.
        // SetFlickering acts on edges only (SRC/PowerLightFlicker.cs:102-118): StopFlicker restores the snapshotted
        // intensities and emission, StartFlicker starts it again. Never UpdateGridStatus or SetBaseBalance, never a write
        // to unstableSupply or to a Light. Nothing is needed on scene load: each lamp calls UpdateFlicker in LateStart and on
        // every grid update.
        internal static void RefreshAll()
        {
            var f = Vanilla.UpdateFlicker;
            if (f == null) return;
            int n = 0;
            foreach (var l in UnityEngine.Object.FindObjectsByType<PowerGridListener>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (l == null) continue;
                try { f(l); n++; }
                catch (Exception e) { Log.Exception("Flicker: refresh", e); }
            }
            Log.Verbose($"Flicker: refreshed {n} lamps.");
        }

        // P3 body. The read is replaced in place, so its labels and exception blocks stay. `ldarg.0; ldfld` becomes
        // `ldarg.0; call Unstable(PowerGrid)`: the same stack effect. Anything but exactly one read means the getter changed;
        // the throw rolls the group back and vanilla flicker stays (no fallback that copies cause A).
        internal static IEnumerable<CodeInstruction> Rewrite(IEnumerable<CodeInstruction> code)
        {
            var helper = AccessTools.Method(typeof(SupplyFlicker), nameof(Unstable), new[] { typeof(PowerGrid) });
            var list = code.ToList();
            int n = 0;
            foreach (var ci in list)
                if (ci.LoadsField(Vanilla.UnstableSupplyField))
                {
                    ci.opcode = OpCodes.Call;
                    ci.operand = helper;
                    n++;
                }
            if (n != 1) throw new InvalidOperationException($"expected 1 read of PowerGrid.unstableSupply, found {n}");
            Log.InfoOnce("flicker-patched", "Flicker: no-contract flicker stopped (replaced 1 read of unstableSupply).");
            return list;
        }
    }

    // P3: PowerGrid.get_Flickering (SRC/PowerGrid.cs:200), 152 B with a try/finally, so not inlined.
    [HarmonyPatch]
    internal static class FlickeringPatch
    {
        private static MethodBase TargetMethod() => Vanilla.Flickering;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> code) => SupplyFlicker.Rewrite(code);
    }
}

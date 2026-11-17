using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace RecipeBook
{
    // P13: ItemReadable.GiveRecipes() transpiler (spec §12.1, D19; off by default, a balance extra). Since 0.5.18 a
    // magazine or book pick takes an unknown recipe with probability 0.8 and a known one otherwise
    // (`UnityEngine.Random.value < 0.8f`, SRC/OS/Items/ItemReadable.cs:300). The transpiler keeps that `ldc.r4 0.8` and
    // passes it through Threshold: with the switch on, 2 (Random.value can be exactly 1.0), so every pick takes the
    // unknown list while it has entries; off returns the game's own operand. The pool, the unknown and known lists, the
    // MaxRecipes == -1 path, the fixed recipes and the notifications are the game's. Exactly one anchor must match, or
    // the group rolls back.
    [HarmonyPatch]
    internal static class GiveRecipesPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GiveRecipes;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var code = instructions.ToList();
            var getValue = AccessTools.PropertyGetter(typeof(UnityEngine.Random), nameof(UnityEngine.Random.value));
            var threshold = AccessTools.Method(typeof(ReadablePicks), nameof(ReadablePicks.Threshold));
            int at = -1, hits = 0;
            for (int i = 0; i + 1 < code.Count; i++)
                if (code[i].Calls(getValue) && code[i + 1].opcode == OpCodes.Ldc_R4)
                {
                    hits++;
                    at = i + 1;
                }
            if (hits != 1 || getValue == null || threshold == null)
                throw new InvalidOperationException($"GiveRecipes: anchor not found ({hits} matches); game updated?");
            if (at + 1 < code.Count && code[at + 1].labels.Count > 0)
                throw new InvalidOperationException("GiveRecipes: the instruction after the anchor is a jump target; game updated?");
            float operand = code[at].operand is float f ? f : float.NaN;
            code.Insert(at + 1, new CodeInstruction(OpCodes.Call, threshold));
            Log.Verbose($"Books: GiveRecipes threshold hooked (operand {operand}).");
            return code;
        }
    }

    // Public: the patched game method calls it.
    public static class ReadablePicks
    {
        // Read live, so turning the switch off restores the game's split at once.
        public static float Threshold(float vanilla) => Cfg.Hot.ReadablesAlwaysNew ? 2f : vanilla;
    }
}

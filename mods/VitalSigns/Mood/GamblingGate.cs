using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace VitalSigns
{
    // G1 (spec §7.4, D10): the gambling term of the needs limit is gated on SmokingNeed > 0 (SRC/PlayerStatsMentalHealth.cs:178,
    // a one-token typo). The transpiler swaps that one ldfld for Value(addictions), which reads the config live, so the
    // fix turns off at once. Value is also what the explainer uses, so the page always matches the game.
    internal static class GamblingGate
    {
        internal static bool Patched;

        private static readonly FieldInfo SmokingNeed = AccessTools.Field(typeof(PlayerStatsAddictions), nameof(PlayerStatsAddictions.SmokingNeed));
        private static readonly FieldInfo GamblingNeed = AccessTools.Field(typeof(PlayerStatsAddictions), nameof(PlayerStatsAddictions.GamblingNeed));
        private static readonly MethodInfo ValueMethod = AccessTools.Method(typeof(GamblingGate), nameof(Value));

        // Called by the patched game code in place of addictions.SmokingNeed.
        public static float Value(PlayerStatsAddictions a) => Cfg.Hot.GamblingFix ? a.GamblingNeed : a.SmokingNeed;

        // The gate the game runs right now.
        internal static float Effective(PlayerStatsAddictions a) => Patched ? Value(a) : a.SmokingNeed;

        // ldfld SmokingNeed; ldc.r4 0; ble.un(.s) L, with ldfld GamblingNeed between the branch and L. Exactly one match
        // in 0.5.19 (IL_026d, GamblingNeed at IL_0289); anything else leaves the method unchanged.
        internal static IEnumerable<CodeInstruction> Transpile(IEnumerable<CodeInstruction> instructions)
        {
            var code = new List<CodeInstruction>(instructions);
            int match = -1, count = 0;
            for (int i = 0; i + 2 < code.Count; i++)
            {
                if (!code[i].LoadsField(SmokingNeed)) continue;
                if (code[i + 1].opcode != OpCodes.Ldc_R4 || !(code[i + 1].operand is float f) || f != 0f) continue;
                var br = code[i + 2];
                if ((br.opcode != OpCodes.Ble_Un_S && br.opcode != OpCodes.Ble_Un) || !(br.operand is Label target)) continue;
                int end = code.FindIndex(i + 3, c => c.labels.Contains(target));
                if (end < 0) continue;
                bool gambling = false;
                for (int j = i + 3; j < end; j++)
                    if (code[j].LoadsField(GamblingNeed)) { gambling = true; break; }
                if (!gambling) continue;
                match = i;
                count++;
            }
            if (count != 1 || ValueMethod == null)
            {
                Patched = false;
                Log.Warn($"Mood: the gambling fix doesn't match this game version ({count} matches); it is off.");
                return code;
            }
            var old = code[match];
            code[match] = new CodeInstruction(OpCodes.Call, ValueMethod) { labels = old.labels, blocks = old.blocks };
            Patched = true;
            Log.Info("Mood: gambling fix applied (1 match).");
            return code;
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace NoFloorSpill
{
    // One feature's patch group (spec §2.1). Matched records, per transpiled target, whether the last transpiler run
    // found exactly the expected calls. Active is set by Plugin.PatchGroup only after every target matched; a later
    // transpiler re-run (another mod patching the same method) can only clear it. Every helper checks Active first and
    // otherwise calls the exact vanilla callee.
    internal sealed class SwapGroup
    {
        internal readonly string Feature;
        internal readonly Dictionary<string, bool> Matched = new Dictionary<string, bool>();
        internal volatile bool Active;

        internal SwapGroup(string feature) => Feature = feature;

        internal static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;
    }

    // Shared call-swap transpiler logic. Replaces every call to `callee` in the method with a call to the static
    // `helper`, which takes the same arguments (plus `this` of the patched method when pushThis). Never throws: Harmony
    // re-runs every transpiler whenever any mod later patches the same method, and a throw there would break that
    // mod's patch. On a count mismatch the IL is returned unchanged and the group switches off softly.
    internal static class IlSwap
    {
        internal static IEnumerable<CodeInstruction> Swap(IEnumerable<CodeInstruction> instructions, MethodBase original,
            MethodInfo callee, MethodInfo helper, int expected, bool pushThis, SwapGroup group)
        {
            var list = instructions.ToList();
            string key = SwapGroup.Key(original);
            int found = 0;
            if (callee != null)
                foreach (var ins in list)
                    if (ins.Calls(callee)) found++;

            if (callee == null || helper == null || found != expected)
            {
                group.Matched[key] = false;
                group.Active = false;
                Log.WarnOnce("il|" + key,
                    $"{key} has {found} call(s) to {Describe(callee)}, expected {expected} (game update or another mod?). " +
                    $"{group.Feature} is off; the game's own behaviour runs.");
                return list;
            }

            var result = new List<CodeInstruction>(list.Count + (pushThis ? expected : 0));
            foreach (var ins in list)
            {
                if (ins.Calls(callee))
                {
                    if (pushThis)
                    {
                        // Defensive: none of the matched calls carries labels or exception blocks in 0.5.19.
                        var self = new CodeInstruction(OpCodes.Ldarg_0);
                        self.MoveLabelsFrom(ins);
                        self.MoveBlocksFrom(ins);
                        result.Add(self);
                    }
                    ins.opcode = OpCodes.Call;
                    ins.operand = helper;
                }
                result.Add(ins);
            }
            group.Matched[key] = true;
            return result;
        }

        private static string Describe(MethodInfo m) => m == null ? "(missing method)" : m.DeclaringType?.Name + "." + m.Name;
    }
}

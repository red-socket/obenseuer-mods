using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace GridSense
{
    // P4 (spec §4.6, D4): InteractableFurnitureDisplay.UpdateUseMessage() (SRC/InteractableFurnitureDisplay.cs:81-118), the
    // look-at text of the furniture display stands: Ankea (267) and O-Iron (10), plus the Mallment's one Shopping Cart stand
    // (no power), all this one class in the exported scenes. InteractObjects.GetInteractable (SRC/InteractObjects.cs:387-407)
    // calls it every frame while the player aims at a stand. Mouse and pad players see the same text.
    //
    // Vanilla first builds the name into one string local (three stores that meet at one label, IL_0094 in 0.5.19) and then
    // passes it to one of six UseText.ChangeUseText(objectName, ...) calls. The transpiler runs that local through
    // DisplayLine.Compose at the join, so ChangeUseText writes the suffixed name once. A postfix would write the label twice
    // a frame (vanilla's name, then ours), and TMP_Text.text only skips its rebuild when the new string equals the current
    // one, so the label would regenerate every frame. Compose returns the same cached string while the player keeps
    // looking, so TMP takes its early return as in vanilla.
    //
    // Read-only: price, stock, furn and the Flatpack item (ItemFurnitureData) are never written, and Interact is not patched.
    // The third-person interactable list shows Interactable.title (empty on these stands) and is left alone.
    [HarmonyPatch]
    internal static class DisplayLinePatch
    {
        // The first run is our own Patch() in Awake: a shape mismatch then throws, so G4 rolls back and is listed as
        // failed. Harmony runs every transpiler again whenever another mod patches this method later; a mismatch then
        // must not break that mod's patch, so the IL is returned unchanged (vanilla text) with one error.
        private static bool _patchedOnce;

        private static MethodBase TargetMethod() => Vanilla.DisplayUseMessage;

        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            var list = instructions.ToList();
            try
            {
                var compose = AccessTools.Method(typeof(DisplayLine), nameof(DisplayLine.Compose),
                    new[] { typeof(InteractableFurnitureDisplay), typeof(string) });
                if (compose == null) throw new MissingMethodException("DisplayLine.Compose");

                // The first store followed by an unconditional branch gives the name's local and the join label.
                int first = -1;
                for (int i = 0; i + 1 < list.Count && first < 0; i++)
                    if (IsStore(list[i]) && IsBr(list[i + 1]) && list[i + 1].operand is Label) first = i;
                if (first < 0) throw new InvalidOperationException("no store of the stand's name followed by a branch");
                var store = list[first];
                var label = (Label)list[first + 1].operand;
                int join = list.FindIndex(ci => ci.labels.Contains(label));
                if (join < 1) throw new InvalidOperationException("the name's branch target is missing");

                // Exactly three stores of that local in the whole method (one per branch of the ternary), all meeting at
                // the join: two jump to it, the last falls through to it.
                var stores = new List<int>();
                for (int i = 0; i < list.Count; i++)
                    if (IsStore(list[i]) && SameLocal(list[i], store)) stores.Add(i);
                if (stores.Count != 3) throw new InvalidOperationException($"expected 3 stores of the stand's name, found {stores.Count}");
                if (stores[2] != join - 1) throw new InvalidOperationException("the last store of the name does not fall through to the join");
                for (int k = 0; k < 2; k++)
                {
                    var br = list[stores[k] + 1];
                    if (!IsBr(br) || !(br.operand is Label l) || l != label)
                        throw new InvalidOperationException("the name's branches don't meet at one label");
                }

                var call = new[]
                {
                    new CodeInstruction(OpCodes.Ldarg_0),
                    Load(store),
                    new CodeInstruction(OpCodes.Call, compose),
                    new CodeInstruction(store.opcode, store.operand),
                };
                call[0].labels.AddRange(list[join].labels);
                list[join].labels.Clear();
                list.InsertRange(join, call);
                _patchedOnce = true;
                return list;
            }
            catch (Exception e)
            {
                if (!_patchedOnce) throw;
                Log.Exception("Display: another mod's patch changed the stand text; the stand wattage is off", e);
                return list;   // every check comes before the insert, so the list is unchanged here
            }
        }

        private static bool IsBr(CodeInstruction ci) => ci.opcode == OpCodes.Br || ci.opcode == OpCodes.Br_S;

        // Short (stloc.0, stloc.s) or long (stloc LocalBuilder) form: Harmony may hand over either.
        private static bool IsStore(CodeInstruction ci) =>
            ci.opcode == OpCodes.Stloc_0 || ci.opcode == OpCodes.Stloc_1 || ci.opcode == OpCodes.Stloc_2 || ci.opcode == OpCodes.Stloc_3 ||
            ci.opcode == OpCodes.Stloc_S || ci.opcode == OpCodes.Stloc;

        private static int LocalIndex(CodeInstruction ci)
        {
            if (ci.opcode == OpCodes.Stloc_0) return 0;
            if (ci.opcode == OpCodes.Stloc_1) return 1;
            if (ci.opcode == OpCodes.Stloc_2) return 2;
            if (ci.opcode == OpCodes.Stloc_3) return 3;
            if (ci.operand is LocalBuilder lb) return lb.LocalIndex;
            if (ci.operand is int n) return n;
            if (ci.operand is byte b) return b;
            return -1;
        }

        private static bool SameLocal(CodeInstruction a, CodeInstruction b) =>
            (a.opcode == b.opcode && ReferenceEquals(a.operand, b.operand)) || (LocalIndex(a) >= 0 && LocalIndex(a) == LocalIndex(b));

        private static CodeInstruction Load(CodeInstruction store)
        {
            if (store.opcode == OpCodes.Stloc_0) return new CodeInstruction(OpCodes.Ldloc_0);
            if (store.opcode == OpCodes.Stloc_1) return new CodeInstruction(OpCodes.Ldloc_1);
            if (store.opcode == OpCodes.Stloc_2) return new CodeInstruction(OpCodes.Ldloc_2);
            if (store.opcode == OpCodes.Stloc_3) return new CodeInstruction(OpCodes.Ldloc_3);
            return new CodeInstruction(store.opcode == OpCodes.Stloc_S ? OpCodes.Ldloc_S : OpCodes.Ldloc, store.operand);
        }
    }

    // Vanilla's name ends in a space when the stock string is empty (Ankea's infiniteStock stands: "Banker's Lamp - 170.00 RM "),
    // so trailing whitespace is trimmed before " - 50 W". Unchanged: "Not available" stands (no furn) and furniture with no
    // grid power. The alt prefab is 0: a stand carries a skin, never an alt. Nothing else in the name is touched. When
    // UseText is overridden, ChangeUseText writes nothing, as in vanilla.
    internal static class DisplayLine
    {
        // The last (vanilla name, power) pair and its result: the same string instance every frame of one look, and no
        // concatenation on a repeat frame.
        private static string _lastIn, _lastOut;
        private static PowerInfo _lastInfo;

        // Called from the transpiled UpdateUseMessage with the name vanilla built. Never throws; returns the name unchanged
        // when there is nothing to add or when the switch is off.
        internal static string Compose(InteractableFurnitureDisplay stand, string name)
        {
            if (!Cfg.Hot.ShopDisplays) return name;
            try
            {
                if ((object)stand == null || string.IsNullOrWhiteSpace(name)) return name;
                var f = Vanilla.DisplayFurn(stand);
                if (f == null) return name;
                var info = PowerInfo.Get(f, 0);
                string suffix = info.StandSuffix;
                if (suffix.Length == 0) return name;
                if (ReferenceEquals(info, _lastInfo) && string.Equals(name, _lastIn, StringComparison.Ordinal)) return _lastOut;
                string text = name.EndsWith(suffix, StringComparison.Ordinal) ? name : name.TrimEnd() + suffix;
                _lastInfo = info;
                _lastIn = name;
                _lastOut = text;
                return text;
            }
            catch (Exception e)
            {
                Log.Exception("Display: suffix", e);
                return name;
            }
        }
    }
}

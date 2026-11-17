using System;
using PixelCrushers.DialogueSystem;

namespace QuestHelper
{
    // Read-only Lua lookups for the fix predicates (spec §4.3). A missing field, or a value of the wrong type, reads as
    // null, and every predicate treats null as "cannot tell" (fail closed). Actor keys are the underscored names exactly
    // as the dialogue database's conditions write them (DialogueLua.StringToTableIndex leaves them unchanged).
    internal static class LuaRead
    {
        internal static bool? ActorBool(string actor, string field)
        {
            try
            {
                var r = DialogueLua.GetActorField(actor, field);
                return r.isBool ? r.asBool : (bool?)null;
            }
            catch (Exception e) { Log.Exception("lua read " + actor + "." + field, e); return null; }
        }

        internal static double? ActorNum(string actor, string field)
        {
            try
            {
                var r = DialogueLua.GetActorField(actor, field);
                return r.isNumber ? r.asFloat : (double?)null;
            }
            catch (Exception e) { Log.Exception("lua read " + actor + "." + field, e); return null; }
        }

        // Variable[name] == value as Lua sees it: nil and other strings are false; a non-string value is null.
        internal static bool? VarIs(string name, string value)
        {
            try
            {
                var r = DialogueLua.GetVariable(name);
                if (r.isString) return r.asString == value;
                if (r.isBool || r.isNumber || r.isTable) return null;
                return false;   // nil
            }
            catch (Exception e) { Log.Exception("lua read " + name, e); return null; }
        }

        internal static string Show(bool? b) => b.HasValue ? (b.Value ? "true" : "false") : "unset";
        internal static string Show(double? d) => d.HasValue ? d.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) : "unset";
    }
}

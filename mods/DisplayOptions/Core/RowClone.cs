using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace DisplayOptions
{
    // Makes a copy of a pause-menu options row safe (spec §7.5 step 6), from Vital Signs' Core/PauseClone.cs (the Safe
    // Travels / Quest Helper pattern). The copy is made while the pause menu's "Hide" parent is inactive, so none of its
    // components has woken yet. Relays are SavableScripts carrying the source's identity: they must never reach a save.
    internal static class RowClone
    {
        internal static void Neutralise(GameObject go)
        {
            // The inactive "Title Background/none" OptionsButton (no slider) has a "<" Relay that calls
            // OptionsMenu.setResolution (PM L111974): the whole object goes.
            foreach (var b in go.GetComponentsInChildren<OptionsButton>(true))
                if (b != null && b.slider == null) Object.DestroyImmediate(b.gameObject);

            foreach (var s in go.GetComponentsInChildren<SavableScript>(true))
            {
                if (s == null) continue;
                if (s is Relay r)
                {
                    if (Vanilla.RelayDisableSaving != null) Vanilla.RelayDisableSaving(r) = true;
                    else Object.DestroyImmediate(r);
                    continue;
                }
                Log.Verbose($"Ui: removed a {s.GetType().Name} from the options row copy.");
                Object.DestroyImmediate(s);
            }

            StripLocalization(go);
        }

        // Localisation would translate our label as if it were the source's text.
        internal static void StripLocalization(GameObject go)
        {
            foreach (var c in go.GetComponentsInChildren<Component>(true))
            {
                if (c == null) continue;
                string tn = c.GetType().Name;
                if (tn == "MenuLocalization" || tn.StartsWith("LocalizeStringEvent", StringComparison.Ordinal)) Object.DestroyImmediate(c);
            }
        }
    }
}

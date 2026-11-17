using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UI.Button;

namespace QuestHelper
{
    // Clones the pause menu's Resume button (look, hover and press sounds, controller highlight) with our action
    // (spec §4.1), copied from Safe Travels. The clone is made under Main while the menu's "Hide" parent is inactive, so
    // none of its components wake before the save-relevant ones are removed. Our entry goes right after "Difficulty"
    // (Safe Travels anchors its entries at "Quit to Menu", so the two never fight over a slot). Returns null when the
    // source has an unexpected shape.
    internal static class PauseClone
    {
        internal static UiButton Clone(GameObject src, string name, string label, UnityAction onClick)
        {
            if (src == null || src.transform.parent == null) return null;
            if (src.GetComponent<UiButton>() == null || src.GetComponentInChildren<TMP_Text>(true) == null)
            {
                Log.WarnOnce("pause shape", "Doctor: the pause menu's Resume button has an unexpected shape; no pause menu entry is added (the Tasks page still opens Quest Helper).");
                return null;
            }
            var parent = src.transform.parent;
            var go = Object.Instantiate(src, parent, false);
            go.name = name;

            // The root Relay calls PauseMenu.Resume and is a SavableScript with the source's identity: remove it at once.
            var rootRelay = go.GetComponent<Relay>();
            if (rootRelay != null) Object.DestroyImmediate(rootRelay);

            // Child SavableScripts (the OnPress/OnUnpress relays that light the "Selected" frame) must never reach a save.
            foreach (var s in go.GetComponentsInChildren<SavableScript>(true))
            {
                if (s == null) continue;
                if (s is Relay r)
                {
                    if (Vanilla.RelayDisableSaving != null) Vanilla.RelayDisableSaving(r) = true;
                    else Object.DestroyImmediate(r);
                    continue;
                }
                if (!string.IsNullOrEmpty(GuidOf(s)))
                {
                    Log.Warn($"Doctor: removed a saved {s.GetType().Name} from the pause menu copy.");
                    Object.DestroyImmediate(s);
                }
            }

            StripLocalization(go);

            var btn = go.GetComponent<UiButton>();
            btn.onClick = new UiButton.ButtonClickedEvent();
            btn.onClick.AddListener(onClick);

            var text = go.GetComponentInChildren<TMP_Text>(true);
            float max = text.fontSize;
            text.enableAutoSizing = true;
            text.fontSizeMax = max;
            text.fontSizeMin = 10f;
            text.text = label;

            var selected = go.transform.Find("Selected");
            if (selected != null) selected.gameObject.SetActive(false);

            var anchor = parent.Find("Difficulty");
            int index;
            if (anchor != null) index = anchor.GetSiblingIndex() + 1;
            else
            {
                var quit = parent.Find("Quit to Menu");
                index = quit != null ? quit.GetSiblingIndex() : Math.Max(0, parent.childCount - 2);
                Log.WarnOnce("pause anchor", "Doctor: the pause menu has no \"Difficulty\" entry; Quest Helper goes above \"Quit to Menu\".");
            }
            go.transform.SetSiblingIndex(index);
            return btn;
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

        internal static void SetLabel(UiButton b, string label)
        {
            if (b == null) return;
            var text = b.GetComponentInChildren<TMP_Text>(true);
            if (text != null && text.text != label) text.text = label;
        }

        internal static string GuidOf(SavableScript s)
        {
            try
            {
                var f = s.GetType().GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
                return f != null ? f.GetValue(s) as string : null;
            }
            catch (Exception) { return null; }
        }
    }
}

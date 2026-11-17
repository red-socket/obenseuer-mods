using System;
using System.Reflection;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UI.Button;

namespace VitalSigns
{
    // Clones the pause menu's Resume button (look, hover and press sounds, controller highlight) with our action
    // (spec §9.4), copied from Quest Helper (Safe Travels' pattern). The clone is made under Main while the menu's "Hide"
    // parent is inactive, so none of its components wake before the save-relevant ones are removed. Placement: after
    // Quest Helper's entry, else after "Difficulty", else above "Quit to Menu", and always above Safe Travels' entries.
    // Returns null when the source has an unexpected shape.
    internal static class PauseClone
    {
        internal const string QuestHelperEntry = "QuestHelper Entry";

        internal static UiButton Clone(GameObject src, string name, string label, UnityAction onClick)
        {
            if (src == null || src.transform.parent == null) return null;
            if (src.GetComponent<UiButton>() == null || src.GetComponentInChildren<TMP_Text>(true) == null)
            {
                Log.WarnOnce("pause shape", "Ui: the pause menu's Resume button has an unexpected shape; no pause menu entry is added.");
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
                    Log.Warn($"Ui: removed a saved {s.GetType().Name} from the pause menu copy.");
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

            Place(go.transform);
            return btn;
        }

        // After Quest Helper's entry or "Difficulty"; never below Safe Travels' entries (they anchor at "Quit to Menu").
        internal static void Place(Transform t)
        {
            var parent = t.parent;
            if (parent == null) return;
            // Positions are counted as if our entry were not in the list; SetSiblingIndex inserts there.
            int own = t.GetSiblingIndex();
            int Without(Transform c) => c.GetSiblingIndex() - (own < c.GetSiblingIndex() ? 1 : 0);
            var anchor = parent.Find(QuestHelperEntry);
            if (anchor == null || anchor == t) anchor = parent.Find("Difficulty");
            int index;
            if (anchor != null) index = Without(anchor) + 1;
            else
            {
                var quit = parent.Find("Quit to Menu");
                index = quit != null ? Without(quit) : Math.Max(0, parent.childCount - 3);
                Log.WarnOnce("pause anchor", "Ui: the pause menu has no \"Difficulty\" entry; Vital Signs goes above \"Quit to Menu\".");
            }
            foreach (Transform c in parent)
                if (c != t && c.name.StartsWith("SafeTravels", StringComparison.Ordinal))
                    index = Math.Min(index, Without(c));
            t.SetSiblingIndex(Math.Max(0, index));
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

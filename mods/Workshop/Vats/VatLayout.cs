using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Workshop
{
    // The vessel panel's slot box for more than 9 slots (spec §6.3). Always on, so a vessel that still holds more stacks
    // after Bigger Vats was turned off shows and reaches all of them. The box ("Manufacture inventory slots", 257×237,
    // anchored top-right) grows down by 69 px per extra row of 3, the "Controls" block under it (257×633, anchored
    // bottom-right) shrinks by as much, and the "Not available slots" backdrop gets one more placeholder per slot, laid
    // out like the authored 9 (55×55 at 49.5 + 69·col, −49.5 − 69·row). The slot count comes from the vessel's own
    // storage, never from the config. The panel is a shared singleton: 9 slots or fewer put the authored layout back.
    // Controller navigation needs nothing: it reads the slot children and constraintCount when the menu opens
    // (SRC/InventoryNavigationHandler.cs:623-634).
    internal static class VatLayout
    {
        internal static bool Patched;
        private const string CloneName = "WS_SlotNotAvailable";
        private const float Row = 69f, First = 49.5f;
        private const int MaxExtraRows = 6;

        private sealed class Authored
        {
            internal RectTransform Box, Controls, Placeholders;
            internal float BoxH, ControlsH;
            internal GameObject Template;
            internal readonly List<GameObject> Clones = new List<GameObject>();
        }

        private static Authored _a;

        internal static void OnSceneLoaded() => _a = null;

        internal static void Apply(manufacturingProcess manu)
        {
            var panel = ManufacturingProcessPanel.instance;
            if (panel == null || manu == null || manu.InputOutputStorage == null) return;
            var a = Capture(panel);
            if (a == null) return;
            try
            {
                int n = manu.InputOutputStorage.Slots != null ? manu.InputOutputStorage.Slots.Length : VatRules.Vanilla9;
                if (n > VatRules.Max) Log.WarnOnce("vat over 27", $"Vats: {Stations.Name(manu)} has {n} slots; the panel shows the 27-slot layout.");
                int extra = Mathf.Clamp(Mathf.CeilToInt((n - VatRules.Vanilla9) / 3f), 0, MaxExtraRows);
                SetHeight(a.Box, a.BoxH + Row * extra);
                SetHeight(a.Controls, a.ControlsH - Row * extra);
                int want = 3 * extra;
                while (a.Clones.Count > want)
                {
                    var last = a.Clones[a.Clones.Count - 1];
                    a.Clones.RemoveAt(a.Clones.Count - 1);
                    if (last != null) UnityEngine.Object.Destroy(last);
                }
                for (int i = a.Clones.Count; i < want; i++)
                {
                    int index = VatRules.Vanilla9 + i;
                    var go = UnityEngine.Object.Instantiate(a.Template, a.Placeholders, false);
                    go.name = CloneName;
                    var rt = (RectTransform)go.transform;
                    rt.anchoredPosition = new Vector2(First + Row * (index % 3), -First - Row * (index / 3));
                    go.SetActive(true);
                    a.Clones.Add(go);
                }
            }
            catch (Exception e)
            {
                Log.Exception("vat layout", e);
                Restore(a);
            }
        }

        private static Authored Capture(ManufacturingProcessPanel panel)
        {
            if (_a != null && _a.Box != null && _a.Controls != null && _a.Placeholders != null && _a.Template != null) return _a;
            _a = null;
            var slots = panel.processPanelUI != null ? panel.processPanelUI.inventorySlotPanel : null;
            var box = slots != null ? slots.transform.parent as RectTransform : null;
            var controls = box != null && box.parent != null ? box.parent.Find("Controls") as RectTransform : null;
            var placeholders = box != null ? box.Find("Not available slots") as RectTransform : null;
            Transform template = null;
            if (placeholders != null)
                for (int i = 0; i < placeholders.childCount; i++)
                    if (placeholders.GetChild(i).name.StartsWith("SlotNotAvailable", StringComparison.Ordinal)) { template = placeholders.GetChild(i); break; }
            if (box == null || controls == null || template == null)
            {
                Log.WarnOnce("vat layout shape", "Vats: the vessel panel has an unexpected shape; it keeps the game's layout.");
                return null;
            }
            _a = new Authored
            {
                Box = box, Controls = controls, Placeholders = placeholders, Template = template.gameObject,
                BoxH = box.sizeDelta.y, ControlsH = controls.sizeDelta.y,
            };
            return _a;
        }

        private static void Restore(Authored a)
        {
            try
            {
                SetHeight(a.Box, a.BoxH);
                SetHeight(a.Controls, a.ControlsH);
                foreach (var c in a.Clones)
                    if (c != null) UnityEngine.Object.Destroy(c);
                a.Clones.Clear();
            }
            catch (Exception e) { Log.Exception("vat layout restore", e); }
        }

        private static void SetHeight(RectTransform rt, float h)
        {
            if (rt == null) return;
            var s = rt.sizeDelta;
            if (!Mathf.Approximately(s.y, h)) rt.sizeDelta = new Vector2(s.x, h);
        }
    }

    // W15: ManufacturingProcessPanel.EnterManu(manufacturingProcess) postfix.
    [HarmonyPatch]
    internal static class EnterManuPatch
    {
        private static MethodBase TargetMethod() => Vanilla.EnterManu;

        private static void Postfix(manufacturingProcess manu)
        {
            try { VatLayout.Apply(manu); }
            catch (Exception e) { Log.Exception("vat layout", e); }
        }
    }
}

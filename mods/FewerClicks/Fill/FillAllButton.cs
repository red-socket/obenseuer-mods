using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace FewerClicks
{
    // The "Fill all" button in the liquid storage panel (spec §2 P6): a clone of the vanilla Fill button at the right
    // end of the title row, the same screen spot as Take all in the storage panel. Never added to the Buttons row,
    // whose layout group (spacing -120, force expand) would reflow and overlap.
    internal static class FillAllButton
    {
        internal const string Name = "FC_FillAllButton";

        private static GameObject _button;
        private static LiquidStoragePanelUI _for;
        private static bool _failedThisScene;

        internal static LiquidStoragePanelUI Panel => _for;

        internal static void OnSceneLoaded()
        {
            _button = null;
            _for = null;
            _failedThisScene = false;
        }

        internal static void OnPanel(LiquidStoragePanelUI panel)
        {
            if (panel == null) return;
            if (_for != panel)
            {
                _button = null;
                _for = panel;
            }
            if (_button == null && !_failedThisScene && Cfg.FillAll.Enabled.Value) Create(panel);
            Refresh();
        }

        private static void Create(LiquidStoragePanelUI panel)
        {
            if (panel.FillButtonGO == null || panel.TitleText == null) { _failedThisScene = true; return; }
            var go = UiClone.CloneButton(panel.FillButtonGO, panel.TitleText.rectTransform, Name, Cfg.FillAll.Label.Value, FillAll.Toggle, "fillall-button");
            if (go == null)
            {
                _failedThisScene = true;
                return;
            }
            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.ignoreLayout = true;
            var rt = go.GetComponent<RectTransform>();
            if (rt != null)
            {
                rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-10f, 0f);
                rt.sizeDelta = new Vector2(110f, 36f);
            }
            go.transform.SetAsLastSibling();
            _button = go;
            Log.Verbose("Fill all: button added to the liquid panel.");
        }

        // Shown while a run is going or while at least one container could be filled now.
        internal static void Refresh()
        {
            if (_button == null || _for == null) return;
            bool visible = Cfg.FillAll.Enabled.Value && !Game.TradeActive && LiquidStorage.currentStorage != null
                           && (FillAll.Running || FillAll.AnyCandidate());
            UiClone.SetLabel(_button, FillAll.Running ? Cfg.FillAll.StopLabel.Value : Cfg.FillAll.Label.Value);
            if (_button.activeSelf != visible) _button.SetActive(visible);
        }

        internal static void Hide()
        {
            _failedThisScene = true;
            if (_button != null) _button.SetActive(false);
        }
    }

    // P6: LiquidStoragePanelUI.OnEnable() (private, SRC/LiquidStoragePanelUI.cs:103).
    [HarmonyPatch]
    internal static class LiquidPanelOnEnablePatch
    {
        private static MethodBase TargetMethod() => Vanilla.LiquidPanelOnEnable;

        private static void Postfix(LiquidStoragePanelUI __instance)
        {
            try { FillAllButton.OnPanel(__instance); }
            catch (Exception e)
            {
                Log.Exception("fill all button", e);
                try { FillAllButton.Hide(); } catch (Exception) { }
            }
        }
    }
}

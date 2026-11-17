using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DisplayOptions
{
    // The "UI Scale" row (spec §7.5, D14, D15, D21): a copy of the FOV row in Pause → Options → Quality, after Post FX
    // and before Back, in the page's one free grid cell (8 rows of 55 in 500 units; ours makes 495). The grid is
    // middle-centre, so the vanilla rows move up 27.5 units and Back one slot down: the row is opt-in (InMenuSlider off
    // by default). Pause menu only: the main menu's options are a world-space sign UI scale can't change. The FOV row's slider is a plain OptionsButton,
    // so the page's own navigation helper drives it with no input patches: up/down reach it in hierarchy order,
    // left/right and LB/RB step one unit (5 %) through OptionsButton.PressLeft/PressRight, pad A does nothing, like FOV.
    internal static class ScaleRow
    {
        internal const string RowName = "DisplayOptions UiScale";
        internal static bool Broken;

        private static float _pending;
        private static float _due;
        private static bool _hasPending;

        // Slider units: 15-30 = 75-150 % in 5 % steps (wholeNumbers, sliderChangeValue left at 1).
        internal static int Steps(float k) => Mathf.RoundToInt(k * 20f);
        internal static int MaxSteps() => Mathf.Clamp(Mathf.FloorToInt(Scaling.FitGame() * 20f + 0.001f), 20, 30);

        // P7 body. StartInit runs twice per pause menu (PauseMenu.Start, then OptionsMenu.Start, which returns on initDone),
        // and once per scene, so it must be idempotent per page.
        internal static void OnStartInit(OptionsMenu menu)
        {
            if (menu == null || menu.mainMenu || !Cfg.Hot.Row || Broken || Scaling.Broken || Scaling.StoodDown) return;
            var fovSlider = Vanilla.FovSlider(menu);
            var fovNumbers = Vanilla.FovNumbers(menu);
            var postFx = Vanilla.PostFX(menu);
            if (fovSlider == null || fovNumbers == null || postFx == null)
            {
                Log.WarnOnce("row refs", "Ui: the options menu has no FOV or Post FX row; no UI scale row is added (the config still works).");
                return;
            }

            // Located through OptionsMenu's own fields, not by name: the pause menu has several objects named "Back" and
            // "Quality".
            var fovRow = RowOf(fovSlider.transform);
            var postRow = RowOf(postFx.transform);
            var page = postRow != null ? postRow.parent : null;
            if (fovRow == null || page == null)
            {
                Shape("the FOV or Post FX row is not in a grid");
                return;
            }
            if (page.Find(RowName) != null) return;

            var grid = page.GetComponent<GridLayoutGroup>();
            var helper = page.GetComponent<OptionsPageNavigationHelper>();
            var pageRect = page as RectTransform;
            if (grid == null || helper == null || pageRect == null)
            {
                Shape("the Quality page has no grid or navigation");
                return;
            }
            int active = 0;
            foreach (Transform c in page)
                if (c.gameObject.activeSelf) active++;
            if ((active + 1) * (grid.cellSize.y + grid.spacing.y) - grid.spacing.y > pageRect.rect.height + 0.5f)
            {
                Shape("the Quality page has no free row (another mod may use it)");
                return;
            }
            OptionsButton srcButton = null;
            foreach (var b in fovRow.GetComponentsInChildren<OptionsButton>(true))
                if (b != null && b.slider == fovSlider) srcButton = b;
            if (srcButton == null)
            {
                Shape("the FOV row has an unexpected shape");
                return;
            }
            string numbersPath = RelativePath(fovNumbers.transform, fovRow);

            var go = Object.Instantiate(fovRow.gameObject, page, false);
            go.name = RowName;
            go.transform.SetSiblingIndex(postRow.GetSiblingIndex() + 1);   // Post FX → UI scale → Back
            try { Wire(go, numbersPath, helper); }
            catch
            {
                Object.Destroy(go);
                throw;
            }
        }

        private static void Wire(GameObject go, string numbersPath, OptionsPageNavigationHelper helper)
        {
            RowClone.Neutralise(go);

            OptionsButton button = null;
            foreach (var b in go.GetComponentsInChildren<OptionsButton>(true))
                if (b != null && b.slider != null) button = b;
            if (button == null) throw new InvalidOperationException("the copied row lost its slider");
            var s = button.slider;

            // A new event, not RemoveAllListeners: that would keep the persistent setFOV call, and the copy would then change
            // and save the player's FOV.
            s.onValueChanged = new Slider.SliderEvent();
            s.wholeNumbers = true;
            s.minValue = 15;
            s.maxValue = MaxSteps();
            s.SetValueWithoutNotify(Steps(Mathf.Min(Cfg.Hot.Scale, Scaling.FitGame())));

            var numbersT = numbersPath != null ? go.transform.Find(numbersPath) : null;
            var numbers = numbersT != null ? numbersT.GetComponent<TMP_Text>() : null;
            if (numbers != null) numbers.text = "100";
            TMP_Text label = null;
            foreach (var t in go.GetComponentsInChildren<TMP_Text>(true))
            {
                if (t == null || t == numbers || t.transform.IsChildOf(s.transform)) continue;
                label = t;
                break;
            }
            if (label != null) label.text = Strings.UiScaleLabel;

            var hook = go.AddComponent<RowHook>();
            hook.Slider = s;
            hook.Numbers = numbers;
            s.onValueChanged.AddListener(hook.OnSlider);

            // The helper fills its button list in OnEnable only when it is empty (OptionsPageNavigationHelper.cs:34-37).
            var list = Vanilla.NavButtons(helper);
            if (list != null && list.Count > 0)
            {
                if (!helper.isActiveAndEnabled) list.Clear();
                else Log.WarnOnce("row nav", "Ui: the Quality page was already open; the UI scale row joins its controller navigation after a level change.");
            }
            Log.Verbose("Ui: UI scale row added to Options > Quality.");
        }

        private static void Shape(string why) =>
            Log.WarnOnce("row shape", $"Ui: {why}; no UI scale row is added. The UiScale settings in the config still work.");

        // Walks up until the parent is the page's grid.
        private static Transform RowOf(Transform t)
        {
            while (t != null && t.parent != null)
            {
                if (t.parent.GetComponent<GridLayoutGroup>() != null) return t;
                t = t.parent;
            }
            return null;
        }

        private static string RelativePath(Transform t, Transform root)
        {
            string p = null;
            while (t != null && t != root)
            {
                p = p == null ? t.name : t.name + "/" + p;
                t = t.parent;
            }
            return t == root ? p : null;
        }

        // ---- Debounce (spec D21) ----

        internal static void Queue(float k)
        {
            _pending = k;
            _due = Time.unscaledTime + 0.5f;
            _hasPending = true;
        }

        // Plugin.Update.
        internal static void Tick()
        {
            if (_hasPending && Time.unscaledTime >= _due) Commit();
        }

        internal static void Commit()
        {
            if (!_hasPending) return;
            _hasPending = false;
            Scaling.EndPreview();
            float before = Cfg.UiScale.Scale.Value;
            Cfg.UiScale.Scale.Value = _pending;   // fires SettingChanged (→ Scaling.ApplyAll) when it changed
            if (Mathf.Approximately(before, Cfg.UiScale.Scale.Value)) Scaling.ApplyAll();
            Log.Verbose($"Ui: UI scale {_pending:0.00} saved from the options menu.");
        }
    }

    // Ours, on the copied row (not a SavableScript; the pause menu instance is never saved).
    internal sealed class RowHook : MonoBehaviour
    {
        internal Slider Slider;
        internal TMP_Text Numbers;
        private bool _quiet;

        private void OnEnable()
        {
            if (Slider == null) return;
            try
            {
                _quiet = true;   // min/max setters re-clamp the value and would call OnSlider
                Slider.maxValue = ScaleRow.MaxSteps();
                Slider.SetValueWithoutNotify(ScaleRow.Steps(Mathf.Min(Cfg.Hot.Scale, Scaling.FitGame())));
                SetNumbers(Slider.value);
            }
            catch (Exception e) { Log.Exception("Ui: row enable", e); }
            finally { _quiet = false; }
        }

        internal void OnSlider(float v)
        {
            if (_quiet) return;
            try
            {
                SetNumbers(v);
                float k = v / 20f;
                Scaling.Preview(k);
                ScaleRow.Queue(k);
            }
            catch (Exception e) { Log.Exception("Ui: row slider", e); }
        }

        private void SetNumbers(float v)
        {
            if (Numbers != null) Numbers.text = (Mathf.RoundToInt(v) * 5).ToString();
        }

        // Page closed, or the pause menu closed (PauseMenu.cs:268): save anything pending. The commit's ApplyAll leaves an
        // open pause menu waiting, and Scaling.Tick resizes it once it closes. Only without hidePause (nothing can tell
        // when the menu closes) does the pause menu follow here.
        private void OnDisable()
        {
            try
            {
                ScaleRow.Commit();
                if (Vanilla.PauseHide == null) Scaling.ApplyPause();
            }
            catch (Exception e) { Log.Exception("Ui: row close", e); }
        }
    }

    // P7 (spec §3.1).
    [HarmonyPatch(typeof(OptionsMenu), nameof(OptionsMenu.StartInit))]
    internal static class StartInitPatch
    {
        private static void Postfix(OptionsMenu __instance)
        {
            if (ScaleRow.Broken) return;
            try { ScaleRow.OnStartInit(__instance); }
            catch (Exception e)
            {
                ScaleRow.Broken = true;
                Log.Exception("Ui: options row", e);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Workshop
{
    // The row above the Craft/Start button (spec §4.1): "Fetch ingredients" and, on machines that run without the
    // player, the repeat count. A child of the RecipeInfo root, never of createButtonPanel, which ShowRecipe empties
    // (SRC/RecipeInfoUI.cs:129-136). It hides with the RecipeInfo (HideRecipe). While it shows, the recipe's scroll view
    // ends 48 px higher so nothing is drawn under it. Only the manufacturing and vessel panels get one, never Growing.
    internal static class RecipeRow
    {
        internal const string RowName = "Workshop Row";
        internal const string FetchName = "Workshop Fetch";
        internal const string RepeatName = "Workshop Repeat";
        private const float Bottom = 84f, Height = 40f, Inset = 20f, ScrollLift = 48f;
        private const float FullWidth = 236f, FetchWidth = 166f, RepeatWidth = 64f;

        internal static bool Patched;

        internal sealed class Row
        {
            internal RecipeInfoUI Info;
            internal RectTransform Root;
            internal GameObject Fetch, Repeat;
            internal CanvasGroup FetchGroup;
            internal RectTransform Scroll;
            internal float AuthoredMinY;
            internal bool Shown, FetchShown, RepeatShown;
            internal FetchState State;
        }

        private static readonly Dictionary<RecipeInfoUI, Row> Rows = new Dictionary<RecipeInfoUI, Row>();
        private static bool _shapeBad;

        internal static void OnSceneLoaded()
        {
            Rows.Clear();
            _shapeBad = false;
        }

        // The row on screen now, if any (controller routes).
        internal static Row Current
        {
            get
            {
                foreach (var row in Rows.Values)
                    if (row.Shown && row.Info != null && row.Info.gameObject.activeInHierarchy && row.Root != null) return row;
                return null;
            }
        }

        internal static bool IsManufacturing(RecipeInfoUI info)
        {
            var p = ManufacturingPanel.instance;
            var ui = p != null ? p.manufacturingPanelUI : null;
            return ui != null && ui.recipeListUI != null && ui.recipeListUI.recipeInfo == info;
        }

        internal static bool IsProcess(RecipeInfoUI info)
        {
            var p = ManufacturingProcessPanel.instance;
            var ui = p != null ? p.processPanelUI : null;
            return ui != null && ui.recipeListUI != null && ui.recipeListUI.recipeInfo == info;
        }

        // The station a RecipeInfo belongs to, when its panel is the one open.
        internal static CraftingBase StationOf(RecipeInfoUI info)
        {
            CraftingBase cb = null;
            if (IsManufacturing(info)) cb = ManufacturingPanel.instance.ActiveManu;
            else if (IsProcess(info)) cb = ManufacturingProcessPanel.instance.ActiveManu;
            return cb != null && Vanilla.PanelOpen(cb) ? cb : null;
        }

        // W1: after the game builds the recipe info. Builds the row once per RecipeInfo and refreshes it.
        internal static void OnShowRecipe(RecipeInfoUI info)
        {
            if (info == null || (!IsManufacturing(info) && !IsProcess(info))) return;
            var row = Ensure(info);
            if (row == null) return;
            Refresh(row, recomputeFetch: true);
        }

        // Plugin.Update: shows or hides the row when the vessel stage or the open station changes, and keeps the repeat
        // label current. Reads state only; the fetch plan is recomputed in W1.
        internal static void Tick()
        {
            if (Rows.Count == 0) return;
            foreach (var row in Rows.Values)
            {
                if (row.Info == null || row.Root == null) continue;
                if (!row.Info.gameObject.activeInHierarchy)
                {
                    if (row.Shown) SetShown(row, false);
                    continue;
                }
                Refresh(row, recomputeFetch: false);
            }
        }

        private static Row Ensure(RecipeInfoUI info)
        {
            if (Rows.TryGetValue(info, out var row) && row.Root != null) return row;
            if (_shapeBad) return null;
            var srcButton = info.createButtonEnabled != null ? info.createButtonEnabled.GetComponentInChildren<DefaultUIButton>(true) : null;
            var scroll = info.transform.Find("UI Scroll View") as RectTransform;
            if (srcButton == null || scroll == null)
            {
                _shapeBad = true;
                Log.WarnOnce("row shape", "Fetch: the recipe panel has an unexpected shape; the Fetch and repeat buttons are not added this scene.");
                return null;
            }
            var go = new GameObject(RowName, typeof(RectTransform));
            var rt = (RectTransform)go.transform;
            rt.SetParent(info.transform, false);
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(Inset, Bottom);
            rt.offsetMax = new Vector2(-Inset, Bottom + Height);
            row = new Row { Info = info, Root = rt, Scroll = scroll, AuthoredMinY = scroll.offsetMin.y };
            var r = row;
            row.Fetch = UiClone.CloneButton(srcButton.gameObject, rt, FetchName, Strings.FetchAll, () => OnFetch(r), "fetch clone");
            row.Repeat = UiClone.CloneButton(srcButton.gameObject, rt, RepeatName, "×1", () => RepeatButton.OnClick(r), "repeat clone");
            if (row.Fetch == null || row.Repeat == null)
            {
                UnityEngine.Object.Destroy(go);
                _shapeBad = true;
                return null;
            }
            row.FetchGroup = row.Fetch.GetComponent<CanvasGroup>();
            if (row.FetchGroup == null) row.FetchGroup = row.Fetch.AddComponent<CanvasGroup>();
            Place(row.Fetch, left: true, FetchWidth);
            Place(row.Repeat, left: false, RepeatWidth);
            go.SetActive(false);
            Rows[info] = row;
            return row;
        }

        private static void Place(GameObject b, bool left, float width)
        {
            var rt = (RectTransform)b.transform;
            float x = left ? 0f : 1f;
            rt.anchorMin = new Vector2(x, 0.5f);
            rt.anchorMax = new Vector2(x, 0.5f);
            rt.pivot = new Vector2(x, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(width, Height);
            rt.localScale = Vector3.one;
        }

        private static void Refresh(Row row, bool recomputeFetch)
        {
            try
            {
                var station = StationOf(row.Info);
                var recipe = row.Info.CurrentRecipe();
                bool fetch = false, repeat = false;
                if (station != null && recipe != null && !Stations.IsOwned(station))
                {
                    var kind = Stations.KindOf(station);
                    fetch = Cfg.Fetch.Enabled.Value && kind != Kind.None && kind != Kind.VanillaAutocraft && kind != Kind.AutoRun
                            && (!(station is manufacturingProcess p) || p.stage == manufacturingProcess.Stage.LoadingItems);
                    repeat = RepeatButton.Available && kind == Kind.Plain && recipe.ID > 0 && IsManufacturing(row.Info);
                }
                if (fetch && (recomputeFetch || !row.FetchShown))
                {
                    row.State = FetchAction.Label(station, recipe, out string label);
                    UiClone.SetLabel(row.Fetch, label);
                    bool dim = row.State == FetchState.NothingMissing || row.State == FetchState.VesselFull || row.State == FetchState.NotFound;
                    row.FetchGroup.alpha = dim ? 0.5f : 1f;
                }
                if (repeat) UiClone.SetLabel(row.Repeat, RepeatButton.Label((manufacturing)station));
                if (fetch != row.FetchShown || repeat != row.RepeatShown)
                {
                    row.FetchShown = fetch;
                    row.RepeatShown = repeat;
                    row.Fetch.SetActive(fetch);
                    row.Repeat.SetActive(repeat);
                    ((RectTransform)row.Fetch.transform).sizeDelta = new Vector2(repeat ? FetchWidth : FullWidth, Height);
                    if (!fetch && repeat) Place(row.Repeat, left: false, RepeatWidth);
                }
                SetShown(row, fetch || repeat);
            }
            catch (Exception e)
            {
                Log.Exception("recipe row", e);
                SetShown(row, false);
            }
        }

        private static void SetShown(Row row, bool shown)
        {
            if (row.Shown == shown && row.Root.gameObject.activeSelf == shown) return;
            row.Shown = shown;
            row.Root.gameObject.SetActive(shown);
            if (row.Scroll != null)
            {
                var min = row.Scroll.offsetMin;
                min.y = shown ? row.AuthoredMinY + ScrollLift : row.AuthoredMinY;
                row.Scroll.offsetMin = min;
            }
        }

        private static void OnFetch(Row row)
        {
            var station = StationOf(row.Info);
            if (station == null) return;
            if (row.State == FetchState.NothingMissing || row.State == FetchState.VesselFull) return;
            FetchAction.Click(station, row.Info);
        }
    }

    // W1: RecipeInfoUI.ShowRecipe(Recipe, int, float, float, bool, bool) postfix (spec §3, §4.1). The recipe is read only.
    [HarmonyPatch]
    internal static class ShowRecipePatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowRecipe;

        private static void Postfix(RecipeInfoUI __instance)
        {
            try { RecipeRow.OnShowRecipe(__instance); }
            catch (Exception e) { Log.Exception("recipe row", e); }
        }
    }
}

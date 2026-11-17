using System;
using System.Collections.Generic;
using UnityEngine;
using Window = InventoryNavigationHandler.CurrentInventoryWindow;

namespace RecipeBook
{
    // The filter model (spec §6.3-§6.5). Visible(i) = text matches and (i is the open recipe or the Show chip matches).
    // It only toggles the buttons' GameObjects: buttons are never removed or reordered, so the game's own reselect loop
    // (SRC/RecipeListUI.cs:58-77), currentRecipes and recipeButtons stay index-aligned. Never calls CheckItems (it crafts
    // on Autocraft stations) and never re-feeds UpdateRecipes (CreateLearnedRecipes mutated ExecutableRecipes).
    internal static class Filter
    {
        internal enum Kind { All, Ready, SkillMet, Skill }

        internal struct ChipValue : IEquatable<ChipValue>
        {
            internal Kind Kind;
            internal PlayerStats.PlayerSkillType Skill;

            internal static readonly ChipValue All = new ChipValue { Kind = Kind.All };
            internal bool IsAll => Kind == Kind.All;

            internal string Name
            {
                get
                {
                    switch (Kind)
                    {
                        case Kind.Ready: return Strings.ChipReady;
                        case Kind.SkillMet: return Strings.ChipSkillMet;
                        case Kind.Skill: return Strings.Skill(Skill);
                        default: return Strings.ChipAll;
                    }
                }
            }

            public bool Equals(ChipValue o) => Kind == o.Kind && (Kind != Kind.Skill || Skill == o.Skill);
            public override bool Equals(object o) => o is ChipValue c && Equals(c);
            public override int GetHashCode() => ((int)Kind * 64) + (int)Skill;
        }

        private static readonly List<ChipValue> Candidates = new List<ChipValue>(8);
        private static readonly List<PlayerStats.PlayerSkillType> Skills = new List<PlayerStats.PlayerSkillType>(4);

        // ---- Live changes (coalesced: FilterBar.Tick applies at most once per frame) ----

        internal static void SetText(FilterBar bar, string s)
        {
            bar.Text = s ?? "";
            bar.Tokens = Fold.Tokens(bar.Text);
            bar.Pending = true;
        }

        // Next or previous Show value (D3). Values that would match nothing with the current text are skipped.
        internal static void Cycle(FilterBar bar, int dir)
        {
            Prepare(bar);
            BuildCandidates(bar);
            int i = Candidates.IndexOf(bar.Chip);
            if (i < 0) i = 0;
            int n = Candidates.Count;
            bar.Chip = Candidates[((i + dir) % n + n) % n];
            bar.Pending = true;
            if (Log.VerboseOn) Log.Verbose($"Filter: Show -> {bar.Chip.Name} ({n} values)");
        }

        // Pad A on the bar: text and chip back to their defaults.
        internal static void Reset(FilterBar bar)
        {
            if (bar.Field != null) bar.Field.SetTextWithoutNotify("");
            bar.Text = "";
            bar.Tokens = new string[0];
            bar.Chip = ChipValue.All;
            bar.Pending = true;
        }

        // ---- Apply (spec §6.4) ----

        // Returns the visible count. live: a text, chip, pad or settings change (re-inits navigation); otherwise a rebuild
        // (P1), whose caller re-inits navigation itself.
        internal static int Apply(FilterBar bar, bool live)
        {
            int n = Prepare(bar);
            var list = bar.List;
            bool filtering = bar.Shown;

            // The open recipe (D4): the first index with its ID, exactly the button the game's reselect loop pressed.
            bar.Sticky = -1;
            var info = list.recipeInfo;
            var open = info != null && info.gameObject.activeInHierarchy ? info.CurrentRecipe() : null;
            if (open != null)
                for (int i = 0; i < n; i++)
                    if (list.currentRecipes[i] != null && list.currentRecipes[i].ID == open.ID) { bar.Sticky = i; break; }

            // Text that excludes the open recipe closes its panel, as the game does when its button disappears.
            if (filtering && bar.Sticky >= 0 && !TextMatch(bar, list.currentRecipes[bar.Sticky])) CloseSticky(bar);

            int count = Compute(bar, n, filtering);
            // Only after a rebuild can the chip alone empty the list (its values are checked when cycled): back to All.
            if (filtering && count == 0 && n > 0 && !bar.Chip.IsAll && bar.Tokens.Length == 0)
            {
                bar.Chip = ChipValue.All;
                count = Compute(bar, n, true);
            }

            bool hid = false;
            for (int i = 0; i < n; i++)
            {
                var go = bar.Buttons[i];
                if (go == null) continue;
                if (go.activeSelf != bar.Visible[i]) go.SetActive(bar.Visible[i]);
                if (!bar.Visible[i]) hid = true;
            }
            bar.HidAny = hid;
            bar.VisibleCount = count;
            bar.SetEmptyNote(filtering && count == 0 && n > 0);
            bar.SetChipLabel(Strings.Chip(bar.Chip.Name, count));

            if (Log.VerboseOn && filtering && (live || hid))
                Log.Verbose($"Filter: '{bar.Text}' + {bar.Chip.Name} -> {count}/{n} ({StationName(list)})");

            if (live) Renav(bar, count);
            return count;
        }

        // Every button visible again (the P1 fallback and the search switch turned off).
        internal static void ShowAll(RecipeListUI list)
        {
            var buttons = Vanilla.RecipeButtons?.Invoke(list);
            if (buttons == null) return;
            foreach (var go in buttons)
                if (go != null && !go.activeSelf) go.SetActive(true);
        }

        // Fills bar.Buttons/Recipe buttons for the current list; returns the aligned length.
        private static int Prepare(FilterBar bar)
        {
            var list = bar.List;
            var buttons = Vanilla.RecipeButtons(list);
            int n = Math.Min(list.currentRecipes.Count, buttons.Count);
            if (bar.Visible.Length < n)
            {
                int size = Math.Max(n, bar.Visible.Length * 2);
                bar.Visible = new bool[size];
                bar.Rbs = new RecipeButton[size];
                bar.Buttons = new GameObject[size];
            }
            for (int i = 0; i < n; i++)
            {
                var go = buttons[i];
                bar.Buttons[i] = go;
                bar.Rbs[i] = go != null ? go.GetComponent<RecipeButton>() : null;
            }
            bar.Count = n;
            return n;
        }

        private static int Compute(FilterBar bar, int n, bool filtering)
        {
            int count = 0;
            for (int i = 0; i < n; i++)
            {
                bool v = !filtering || Visible(bar, i);
                bar.Visible[i] = v;
                if (v) count++;
            }
            return count;
        }

        private static bool Visible(FilterBar bar, int i)
        {
            var r = bar.List.currentRecipes[i];
            return TextMatch(bar, r) && (i == bar.Sticky || ChipMatch(bar.Chip, r, bar.Rbs[i]));
        }

        internal static bool TextMatch(FilterBar bar, Recipe r)
        {
            var tokens = bar.Tokens;
            if (tokens.Length == 0) return true;
            string key = SearchKeys.For(r);
            foreach (var t in tokens)
                if (key.IndexOf(t, StringComparison.Ordinal) < 0) return false;
            return true;
        }

        // D3: every value comes from the button kind or the game's own skill check.
        private static bool ChipMatch(ChipValue chip, Recipe r, RecipeButton rb)
        {
            switch (chip.Kind)
            {
                // The normal prefab: executable and CheckSkillSoft passed (SRC/RecipeListUI.cs:39-45, :111).
                case Kind.Ready: return rb != null && !rb.disabledButton;
                case Kind.SkillMet: return r != null && PlayerStats.instance != null && RecipeInfoUI.CheckSkill(r);
                case Kind.Skill: return r != null && r.RelevantSkill == chip.Skill;
                default: return true;
            }
        }

        // [All, Ready, Skill met] plus one value per skill when the list mixes two or more (None excluded); every value
        // but All is kept only if some recipe matches it and the current text.
        private static void BuildCandidates(FilterBar bar)
        {
            Candidates.Clear();
            Candidates.Add(ChipValue.All);
            int n = bar.Count;
            Skills.Clear();
            for (int i = 0; i < n; i++)
            {
                var r = bar.List.currentRecipes[i];
                if (r != null && r.RelevantSkill != PlayerStats.PlayerSkillType.None && !Skills.Contains(r.RelevantSkill)) Skills.Add(r.RelevantSkill);
            }
            Skills.Sort();
            TryAdd(bar, new ChipValue { Kind = Kind.Ready });
            TryAdd(bar, new ChipValue { Kind = Kind.SkillMet });
            if (Skills.Count >= 2)
                foreach (var s in Skills) TryAdd(bar, new ChipValue { Kind = Kind.Skill, Skill = s });
        }

        private static void TryAdd(FilterBar bar, ChipValue v)
        {
            for (int i = 0; i < bar.Count; i++)
            {
                var r = bar.List.currentRecipes[i];
                if (ChipMatch(v, r, bar.Rbs[i]) && TextMatch(bar, r)) { Candidates.Add(v); return; }
            }
        }

        // The game's behaviour when the open recipe's button disappears (SRC/RecipeListUI.cs:74-77): hide the info.
        private static void CloseSticky(FilterBar bar)
        {
            var data = InventoryNavigationData.Instance;
            if (data.CurrentWindow == Window.OtherPanelRecipeOpened) data.CurrentWindow = Window.OtherPanelRecipe;
            bar.List.recipeInfo.HideRecipe();
            var rb = bar.Sticky < bar.Count ? bar.Rbs[bar.Sticky] : null;
            if (rb != null) rb.ChangePressed(false);
            data.RecipeOpened = false;
            bar.Sticky = -1;
            Log.Verbose("Filter: the open recipe no longer matches the text; its panel is closed.");
        }

        // ---- Navigation (spec §6.4 step 4, §6.5) ----

        private static void Renav(FilterBar bar, int count)
        {
            var h = InventoryNavigationHandler.Instance;
            if (h == null) return;
            InitList(h, bar.Kind);
            bar.ScrollToTop();
            var data = InventoryNavigationData.Instance;
            var mover = InventoryNavigationMovement.Instance;
            if (data.CurrentTab == 0 && data.CurrentWindow == Window.OtherPanelRecipe && !PadRoute.OnBarOf(bar) && mover != null)
            {
                var panel = InventoryNavigationMovement.GetActiveRecipePanel(h.OtherPanelsOfTypeRecipe);
                if (Game.Controller)
                {
                    // The pad's selection goes to the first visible recipe (hover only).
                    Vanilla.Clear(mover, data);
                    data.CurrentCoordinates = Vector2.zero;
                    if (count > 0 && panel != null) Vanilla.SwitchTo(mover, data, Vector2.zero, panel.SlotsTwoDimensional);
                    else NoButtons(bar, data, h);
                }
                else if (count == 0) NoButtons(bar, data, h);
                else if (data.CurrentCoordinates.y >= count)
                    data.CurrentCoordinates = new Vector2(data.CurrentCoordinates.x, count - 1);
            }
            else if (data.CurrentTab == 0 && data.CurrentWindow == Window.OtherPanelRecipeOpened) KeepOpenedRow(bar, data, count);
            h.ChangeControlsInfo(InputManager.instance.inputType, data.CurrentTab, data.CurrentWindow);
        }

        private static void NoButtons(FilterBar bar, InventoryNavigationData data, InventoryNavigationHandler h)
        {
            if (PadRoute.Usable) PadRoute.EnterBar(bar, data);
            else h.ExitRecipeOpenedPanel();
        }

        // After a rebuild (P1), only when our filter hid something (spec §6.5): the caller re-inits navigation right
        // after, and HandleRecipeInteraction (SRC/InventoryNavigationHandler.cs:1793) indexes the grid unchecked.
        internal static void ClampAfterRebuild(FilterBar bar)
        {
            if (!bar.HidAny || FilterBar.Active != bar) return;
            var data = InventoryNavigationData.Instance;
            if (data.CurrentTab != 0) return;
            if (data.CurrentWindow != Window.OtherPanelRecipe && data.CurrentWindow != Window.OtherPanelRecipeOpened) return;
            int n = bar.VisibleCount;
            if (data.CurrentWindow == Window.OtherPanelRecipeOpened)
            {
                KeepOpenedRow(bar, data, n);
                return;
            }
            if (n > 0 && data.CurrentCoordinates.y >= n)
            {
                data.CurrentCoordinates = new Vector2(data.CurrentCoordinates.x, n - 1);
                Log.Verbose($"Pad: selection clamped to row {n - 1} after a rebuild.");
            }
            else if (n == 0 && data.CurrentWindow == Window.OtherPanelRecipe)
            {
                var h = InventoryNavigationHandler.Instance;
                if (PadRoute.Usable) PadRoute.EnterBar(bar, data);
                else if (h != null) h.ExitRecipeOpenedPanel();
            }
        }

        // The pad is on the open recipe's Create button (OtherPanelRecipeOpened). The game kept the coordinates of the row
        // it came from (SwitchToNextCraftingButton doesn't change them, SRC/InventoryNavigationMovement.cs:1278-1284), and
        // its Left goes back with SwitchToNextRecipe(data, data.CurrentCoordinates, grid), which indexes the grid
        // unchecked (:780-783, :1058-1061). After the filter changed which rows are visible, point the coordinates at the
        // same button's new row: the row the pad came from if it is still visible (a live change keeps the buttons),
        // else the open recipe (after a rebuild the buttons are new; the game's reselect pressed the open one), else the
        // last visible row. The grid holds only active buttons, in list order, one column.
        private static void KeepOpenedRow(FilterBar bar, InventoryNavigationData data, int visible)
        {
            int target = -1;
            var cur = data.CurrentRecipeButton;
            if (cur != null)
                for (int i = 0; i < bar.Count; i++)
                    if (ReferenceEquals(bar.Rbs[i], cur)) { if (bar.Visible[i]) target = i; break; }
            if (target < 0 && bar.Sticky >= 0 && bar.Sticky < bar.Count && bar.Visible[bar.Sticky] && bar.Rbs[bar.Sticky] != null)
                target = bar.Sticky;
            if (target >= 0)
            {
                int row = 0;
                for (int i = 0; i < target; i++)
                    if (bar.Visible[i] && bar.Rbs[i] != null) row++;
                data.CurrentCoordinates = new Vector2(data.CurrentCoordinates.x, row);
                data.CurrentRecipeButton = bar.Rbs[target];
                Log.Verbose($"Pad: Create button's row is now {row}.");
            }
            else if (visible > 0 && data.CurrentCoordinates.y >= visible)
                data.CurrentCoordinates = new Vector2(data.CurrentCoordinates.x, visible - 1);
        }

        internal static void InitList(InventoryNavigationHandler h, FilterBar.PanelKind kind)
        {
            switch (kind)
            {
                case FilterBar.PanelKind.Process: h.InitializeManufacturingProcessPanelRecipeList(false); break;
                case FilterBar.PanelKind.Growing: h.InitializeGrowingPanelRecipeList(false); break;
                default: h.InitializeManufacturingPanelRecipeList(false); break;
            }
        }

        private static string StationName(RecipeListUI list)
        {
            try
            {
                var cb = CraftingBase.currentCraftingBase;
                return cb != null ? cb.name : list.name;
            }
            catch { return "?"; }
        }
    }
}

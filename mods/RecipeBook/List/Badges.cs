using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Bootstrap;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace RecipeBook
{
    // Fail-chance and skill badges on recipe buttons (spec §9, D9-D13). Values are the recipe panel's own: the fail
    // chance is RecipeInfoUI's expression (SRC/RecipeInfoUI.cs:274-275) with the station's EquipmentQualityWithUpgrades,
    // truncated the same way; the skill check is RecipeInfoUI.CheckSkill. They refresh per rebuild only, as the panel
    // does, and on a settings change. A badge is a child TMP added after the button's label, so CreateButton
    // (SRC/RecipeListUI.cs:96) and Workshop's UiClone, which take the first TMP in children, still find the label.
    // Buttons are recreated per rebuild, so nothing accumulates; a settings change updates or removes existing badges.
    internal static class Badges
    {
        internal const string BadgeName = "RecipeBook Badge";
        private const string FairMarketGuid = "redsocket.obenseuer.fairmarket";
        private const string Amber = "#FFC94D";

        private static readonly StringBuilder Sb = new StringBuilder(64);
        private static readonly int[] SkillCounts = new int[64];
        private static bool _preciseLogged;
        private static bool? _fmPatched;

        // The last rebuild's station values per list, so a settings change can redo the open list (spec §4).
        private sealed class Last
        {
            internal RecipeListUI List;
            internal CraftingBase Station;
            internal float Quality;
        }

        private static readonly List<Last> Lasts = new List<Last>(3);

        internal static void Apply(RecipeListUI list, CraftingBase station, float quality)
        {
            Remember(list, station, quality);
            Run(list, station, quality);
        }

        // Settings changed: redo the badges of every list on screen.
        internal static void Reapply()
        {
            Lasts.RemoveAll(l => l.List == null);
            foreach (var l in Lasts)
                if (l.List.isActiveAndEnabled) Run(l.List, l.Station, l.Quality);
        }

        private static void Remember(RecipeListUI list, CraftingBase station, float quality)
        {
            foreach (var l in Lasts)
                if (ReferenceEquals(l.List, list)) { l.Station = station; l.Quality = quality; return; }
            Lasts.Add(new Last { List = list, Station = station, Quality = quality });
        }

        private static void Run(RecipeListUI list, CraftingBase station, float quality)
        {
            var buttons = Vanilla.RecipeButtons(list);
            var recipes = list.currentRecipes;
            int n = Math.Min(buttons.Count, recipes.Count);
            bool on = Cfg.Hot.BadgesOn && PlayerStats.instance != null;

            var dominant = on ? Dominant(recipes, n) : PlayerStats.PlayerSkillType.None;
            bool precise = on && station is Dishwasher && FairMarketPrecise();
            for (int i = 0; i < n; i++)
            {
                var go = buttons[i];
                if (go == null) continue;
                string text = null;
                if (on && recipes[i] != null)
                {
                    try { text = Text(recipes[i], quality, dominant, precise); }
                    catch (Exception e) { Log.Exception("Badges: text", e); }
                }
                Set(go, text);
            }
        }

        // The mode of RelevantSkill over the list: its recipes read "Lv", the others name their skill (D11).
        private static PlayerStats.PlayerSkillType Dominant(List<Recipe> recipes, int n)
        {
            Array.Clear(SkillCounts, 0, SkillCounts.Length);
            int best = 0, bestCount = -1;
            for (int i = 0; i < n; i++)
            {
                var r = recipes[i];
                if (r == null) continue;
                int s = (int)r.RelevantSkill;
                if (s < 0 || s >= SkillCounts.Length) continue;
                if (++SkillCounts[s] > bestCount) { bestCount = SkillCounts[s]; best = s; }
            }
            return (PlayerStats.PlayerSkillType)best;
        }

        private static string Text(Recipe r, float quality, PlayerStats.PlayerSkillType dominant, bool precise)
        {
            Sb.Length = 0;
            if (Cfg.Hot.BadgeFail && r.FailChance > 0f)
            {
                float p = r.CurrentFailChance(r.RelevantSkill, quality, r.SkillFactor);
                int n = Fmt.Pct(p);
                bool wash = precise && r.useOnlyStolenItems && r.keepItemOwnerOnFail;
                if (wash ? p > 0f : n > 0)
                {
                    string label = wash ? Strings.FailPrecise(Fmt.Pct1(p)) : Strings.Fail(n);
                    int pct = (int)(p * 100f);
                    if (pct >= Cfg.Hot.StrongAt) Sb.Append("<b><color=").Append(Amber).Append('>').Append(label).Append("</color></b>");
                    else if (pct >= Cfg.Hot.WarnAt) Sb.Append("<color=").Append(Amber).Append('>').Append(label).Append("</color>");
                    else Sb.Append(label);
                }
            }
            if (Cfg.Hot.BadgeSkill && r.MinimumSkill >= 2)
            {
                int have = PlayerStats.instance.GetSkill(r.RelevantSkill);
                bool met = RecipeInfoUI.CheckSkill(r);
                string prefix = r.RelevantSkill == dominant ? Strings.Lv : Strings.SkillShort(r.RelevantSkill);
                if (Sb.Length > 0) Sb.Append('\n');
                if (!met) Sb.Append("<color=").Append(Amber).Append('>');
                Sb.Append(prefix).Append(' ').Append(Fmt.Int(have)).Append('/').Append(Fmt.Int(r.MinimumSkill));
                if (r.HardMinimumSkill && !met) Sb.Append(Strings.HardMark);
                if (!met) Sb.Append("</color>");
            }
            return Sb.Length > 0 ? Sb.ToString() : null;
        }

        // Fair Market's dishwasher line shows one decimal when its PreciseOdds is on (D12); the badge follows it. Its
        // panel rewrite is a RecipeInfoUI.ShowRecipe postfix that Fair Market patches only when PreciseOdds is on at
        // launch (its "wash-info" group), so the config value alone isn't enough: turned on mid-session, or with that
        // group failed, its panel still prints the game's whole number. The postfix's presence is checked once (badges
        // only exist in gameplay, after every plugin has loaded) and cached.
        private static bool FairMarketPrecise()
        {
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(FairMarketGuid, out var pi) || pi?.Instance == null) return false;
                if (!pi.Instance.Config.TryGetEntry<bool>("Dishwasher", "PreciseOdds", out var e) || !e.Value) return false;
                if (_fmPatched == null)
                {
                    _fmPatched = FairMarketPanelPatched();
                    if (_fmPatched == false)
                        Log.Info("Badges: Fair Market's precise odds aren't active in the dishwasher panel this session; dishwasher badges show whole numbers like the panel.");
                }
                if (_fmPatched != true) return false;
                if (!_preciseLogged)
                {
                    _preciseLogged = true;
                    Log.Info("Badges: Fair Market precise odds detected; dishwasher badges show one decimal.");
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("Badges: Fair Market check", e);
                return false;
            }
        }

        private static bool FairMarketPanelPatched()
        {
            var show = AccessTools.DeclaredMethod(typeof(RecipeInfoUI), "ShowRecipe",
                new[] { typeof(Recipe), typeof(int), typeof(float), typeof(float), typeof(bool), typeof(bool) });
            if (show == null) return false;
            var info = Harmony.GetPatchInfo(show);
            if (info == null) return false;
            foreach (var p in info.Postfixes)
                if (p.owner == FairMarketGuid) return true;
            return false;
        }

        // Creates, updates or removes the badge. The label's right margin grows by the badge width only while a badge
        // is there; the original margin is the current one minus the badge's own width.
        private static void Set(GameObject button, string text)
        {
            var existing = button.transform.Find(BadgeName);
            if (text == null && existing == null) return;
            var label = Label(button);
            if (text == null)
            {
                if (label != null)
                {
                    var m = label.margin;
                    m.z -= ((RectTransform)existing).sizeDelta.x;
                    label.margin = m;
                }
                existing.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(existing.gameObject);
                return;
            }

            TextMeshProUGUI badge;
            float width = Cfg.Hot.Width;
            if (existing != null)
            {
                badge = existing.GetComponent<TextMeshProUGUI>();
                var rt = (RectTransform)existing;
                float old = rt.sizeDelta.x;
                if (label != null && !Mathf.Approximately(old, width))
                {
                    var m = label.margin;
                    m.z += width - old;
                    label.margin = m;
                }
                rt.sizeDelta = new Vector2(width, -6f);
            }
            else
            {
                var go = new GameObject(BadgeName, typeof(RectTransform));
                var rt = (RectTransform)go.transform;
                rt.SetParent(button.transform, false);
                rt.SetAsLastSibling();
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 0.5f);
                rt.anchoredPosition = new Vector2(-8f, 0f);
                rt.sizeDelta = new Vector2(width, -6f);
                go.AddComponent<LayoutElement>().ignoreLayout = true;
                badge = go.AddComponent<TextMeshProUGUI>();
                if (label != null)
                {
                    badge.font = label.font;
                    badge.fontSharedMaterial = label.fontSharedMaterial;
                    var m = label.margin;
                    m.z += width;
                    label.margin = m;
                }
                badge.enableAutoSizing = false;
                badge.textWrappingMode = TextWrappingModes.NoWrap;
                badge.overflowMode = TextOverflowModes.Overflow;
                badge.alignment = TextAlignmentOptions.MidlineRight;
                badge.richText = true;
                badge.raycastTarget = false;
            }
            if (badge == null) return;
            // The label's colour: grey on the disabled prefab, white otherwise.
            if (label != null) badge.color = label.color;
            badge.fontSize = Cfg.Hot.FontSize;
            if (badge.text != text) badge.text = text;
        }

        // The button's label: the first TMP in children that isn't ours (the game's CreateButton rule).
        private static TextMeshProUGUI Label(GameObject button)
        {
            var t = button.GetComponentInChildren<TextMeshProUGUI>(true);
            if (t != null && t.gameObject.name != BadgeName) return t;
            foreach (var x in button.GetComponentsInChildren<TextMeshProUGUI>(true))
                if (x.gameObject.name != BadgeName) return x;
            return null;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RecipeBook
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.recipebook";
        public const string Name = "Recipe Book";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Harmony Harmony;

        private static readonly List<string> On = new List<string>();
        private static readonly List<string> Off = new List<string>();
        private static readonly List<string> Failed = new List<string>();
        private static bool _startupReported;
        private static bool _list, _guard, _pad, _tooltip, _panel, _picks, _reroll;

        // Each feature is patched as its own group (spec §4, §5.2), only when its switch is on at launch. A group checks
        // the game members it needs first; a missing member or a patch exception unpatches that group's own targets, logs
        // one error and leaves the other groups working. No two groups share a target, so the rollback by owner is safe;
        // if a later change shares one, switch to per-method Unpatch(original, patchMethod). Patch bodies read their
        // switch live (Cfg.Hot), so turning a feature off works at once; turning one on needs a restart.
        private void Awake()
        {
            Log.Init(Logger);
            Cfg.Bind(Config);
            if (Application.version != TestedGameVersion)
                Log.Warn($"Built for {TestedGameVersion}, running on {Application.version}. Patches may fail.");

            Harmony = new Harmony(Guid);

            // S-017 station list (§6, §9): the filter and the badges share P1.
            _list = PatchGroup("list", Cfg.Search.Enabled.Value || Cfg.Badges.Enabled.Value,
                Needs(Vanilla.UpdateRecipes, Vanilla.RecipeButtons, Vanilla.RecipeButtonPrefab),
                Targets(Vanilla.UpdateRecipes), typeof(UpdateRecipesPatch));

            // The text field is built only when its key guard patched (§7).
            _guard = TypingGuard.Available = PatchGroup("typing-guard", _list && Cfg.Search.Enabled.Value && Cfg.Search.TextField.Value,
                Needs(Vanilla.GetKeyDown, Vanilla.GetKey, Vanilla.GetKeyWithHold, Vanilla.GetKeyDownKeyboardOnly, Vanilla.GetAuxiliaryReloadDown,
                    Vanilla.DisabledWritelockList),
                Targets(Vanilla.GetKeyDown, Vanilla.GetKey, Vanilla.GetKeyWithHold, Vanilla.GetKeyDownKeyboardOnly, Vanilla.GetAuxiliaryReloadDown),
                typeof(GuardGetKeyDown), typeof(GuardGetKey), typeof(GuardGetKeyWithHold), typeof(GuardGetKeyDownKeyboardOnly),
                typeof(GuardGetAuxiliaryReloadDown));
            if (_list && Cfg.Search.Enabled.Value && Cfg.Search.TextField.Value && !_guard)
                Log.Info("Filter: text search off this session (the key guard failed); the Show button still works.");

            // Controller route and hint (§8).
            _pad = PadRoute.Patched = PatchGroup("pad", _list && Cfg.Search.Enabled.Value && Cfg.Search.ControllerRoute.Value,
                Needs(Vanilla.NavMove, Vanilla.HandleRecipeInteraction, Vanilla.GetButtonInfoTab0, Vanilla.KeyCodeInfoToString,
                    Vanilla.SwitchToNextRecipe, Vanilla.ClearPreviousSelections),
                Targets(Vanilla.NavMove, Vanilla.HandleRecipeInteraction, Vanilla.GetButtonInfoTab0),
                typeof(NavMovePatch), typeof(RecipeInteractionPatch), typeof(ButtonInfoPatch));

            // "Used in" and "New recipes left" lines (§10, §11). Gated on the Lines switches only: UsedIn.Enabled and
            // Remaining.Enabled are read live by the bodies, so either can be turned on mid-session.
            _tooltip = PatchGroup("tooltip", Cfg.Lines.Tooltip.Value,
                Needs(Vanilla.ConstructDataString, Vanilla.UpdateTooltip),
                Targets(Vanilla.ConstructDataString, Vanilla.UpdateTooltip), typeof(ConstructDataStringPatch), typeof(UpdateTooltipPatch));
            _panel = PatchGroup("item-panel", Cfg.Lines.ItemPanel.Value,
                Needs(Vanilla.UpdateInfo, Vanilla.ItemDescription), Targets(Vanilla.UpdateInfo), typeof(UpdateInfoPatch));

            // S-010 balance extras, off by default (§12).
            _picks = PatchGroup("readable-picks", Cfg.Discovery.ReadablesAlwaysNew.Value,
                Needs(Vanilla.GiveRecipes), Targets(Vanilla.GiveRecipes), typeof(GiveRecipesPatch));
            _reroll = PatchGroup("book-reroll", Cfg.Discovery.BooksRerollKnown.Value,
                Needs(Vanilla.SetRandomRecipes, Vanilla.BookOnLoadingGame, Vanilla.BookUpdateTitle, Vanilla.BookRecipeIds, Vanilla.BookRecipesAdded),
                Targets(Vanilla.SetRandomRecipes, Vanilla.BookOnLoadingGame), typeof(SetRandomRecipesPatch), typeof(BookOnLoadingGamePatch));

#if RECIPEBOOK_TESTS
            Setups.Bind(Config);
#endif

            Config.SettingChanged += OnSettingChanged;
            SceneManager.sceneLoaded += OnSceneLoaded;
            SaveController.PlayerWillChangeLevel += OnPlayerWillChangeLevel;
            SaveController.PlayerWillLoadGame += OnPlayerWillLoadGame;
            SaveController.LoadingDone += OnLoadingDone;
        }

        private static string List(List<string> l) => l.Count == 0 ? "-" : string.Join(", ", l.ToArray());

        private static object[] Needs(params object[] members) => members;

        private static MethodBase[] Targets(params MethodBase[] targets) => targets;

        private static bool PatchGroup(string name, bool enabled, object[] needs, MethodBase[] targets, params Type[] types)
        {
            if (!enabled)
            {
                Off.Add(name);
                return false;
            }
            try
            {
                foreach (var n in needs)
                    if (n == null) throw new MissingMemberException("a game member this feature needs is missing (see the error above)");
                foreach (var t in types) Harmony.CreateClassProcessor(t).Patch();
                On.Add(name);
                return true;
            }
            catch (Exception e)
            {
                Rollback(targets);
                Log.Error($"Patching '{name}' failed; that feature is off this session: {e}");
                Failed.Add(name);
                return false;
            }
        }

        private static void Rollback(MethodBase[] targets)
        {
            foreach (var m in targets)
            {
                if (m == null) continue;
                try { Harmony.Unpatch(m, HarmonyPatchType.All, Guid); }
                catch (Exception e) { Log.Exception("unpatch " + m.DeclaringType?.Name + "." + m.Name, e); }
            }
        }

        // ---- Events (spec §4) ----

        private static void OnSettingChanged(object sender, SettingChangedEventArgs a)
        {
            Cfg.Hot.Refresh();
            Step("settings (lines)", ExtraLines.Clear);
            Step("settings (filter)", () => FilterBar.Active?.Reapply());
            if (_list) Step("settings (badges)", Badges.Reapply);
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (mode == LoadSceneMode.Additive && scene != SceneManager.GetActiveScene()) return;
            Step("scene (typing)", TypingGuard.ReleaseAll);
            Step("scene (pad)", PadRoute.Reset);
            Step("scene (books)", BookReroll.Clear);
            Step("scene (lines)", ExtraLines.Clear);
            if (!_startupReported && GlobalState.instance != null)
            {
                _startupReported = true;
                Step("startup", ReportStartup);
            }
        }

        private static void OnPlayerWillChangeLevel()
        {
            Step("level change (typing)", TypingGuard.ReleaseAll);
            Step("level change (books)", BookReroll.Clear);
        }

        private static void OnPlayerWillLoadGame()
        {
            Step("game load (typing)", TypingGuard.ReleaseAll);
            Step("game load (books)", BookReroll.Clear);
        }

        private static void OnLoadingDone() => Step("loading done (books)", BookReroll.Flush);

        // The startup line and the Compat lines (spec §4, §16), once, at the first gameplay scene: all plugins have
        // loaded by then, so other mods' patches are visible.
        private static void ReportStartup()
        {
            Log.Info($"{Name} {Version}: on [{List(On)}]; off [{List(Off)}]; failed [{List(Failed)}]");
            if (_list) Compat.Foreign(Vanilla.UpdateRecipes, "the recipe filter and badges", "may not apply");
            if (_guard)
            {
                Compat.Overlap(Vanilla.GetKeyDown, "the search field's key guard");
                Compat.Overlap(Vanilla.GetKey, "the search field's key guard");
                Compat.Overlap(Vanilla.GetKeyWithHold, "the search field's key guard");
                Compat.Overlap(Vanilla.GetKeyDownKeyboardOnly, "the search field's key guard");
                Compat.Overlap(Vanilla.GetAuxiliaryReloadDown, "the search field's key guard");
            }
            if (_pad)
            {
                Compat.Overlap(Vanilla.NavMove, "the controller route to the search bar");
                Compat.Foreign(Vanilla.HandleRecipeInteraction, "A on the search bar", "may open a recipe instead");
                Compat.Overlap(Vanilla.GetButtonInfoTab0, "the filter hint");
            }
            if (_tooltip)
            {
                Compat.Foreign(Vanilla.ConstructDataString, "the tooltip lines", "may not show");
                Compat.Overlap(Vanilla.UpdateTooltip, "the tooltip lines");
            }
            if (_panel) Compat.Overlap(Vanilla.UpdateInfo, "the item panel lines");
            if (_picks) Compat.Foreign(Vanilla.GiveRecipes, "unknown-first magazine picks", "may not apply");
            if (_reroll) Compat.Foreign(Vanilla.SetRandomRecipes, "the recipe paper re-roll", "may not apply");
            MailCheck.Run();
        }

        internal static void Step(string key, Action a)
        {
            try { a(); }
            catch (Exception e) { Log.Exception(key, e); }
        }

        private void Update()
        {
            if (_guard) Step("typing", TypingGuard.Tick);
            if (_list) Step("filter", FilterBar.TickActive);
            if (_reroll) Step("books", BookReroll.Tick);
        }

        private void OnDestroy()
        {
            Config.SettingChanged -= OnSettingChanged;
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SaveController.PlayerWillChangeLevel -= OnPlayerWillChangeLevel;
            SaveController.PlayerWillLoadGame -= OnPlayerWillLoadGame;
            SaveController.LoadingDone -= OnLoadingDone;
            TypingGuard.ReleaseAll();
        }
    }
}

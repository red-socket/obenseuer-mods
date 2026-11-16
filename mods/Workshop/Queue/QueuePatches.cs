using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Workshop
{
    // Machine Queue patches W3-W7 (spec §3, §5). All pass-through: none changes what the game does.
    internal static class QueuePatches
    {
        internal static bool Patched, VesselsPatched;

        // W3 postfix body: a real manual start records Last and turns an armed repeat count into a queue (spec §5.3).
        internal static void OnCraft(manufacturing m, Recipe recipe)
        {
            if (!Cfg.Queue.Enabled.Value || recipe == null || recipe.ID <= 0) return;
            var kind = Stations.KindOf(m);
            if (kind != Kind.Plain && kind != Kind.RequirePlayer) return;
            var e = MachineStore.Get(m, true);
            bool sameQueue = e.Queued && e.QId == recipe.ID;
            if (kind == Kind.Plain && e.ArmCount > 1 && ShownId(m) == recipe.ID)
            {
                int count = e.ArmCount;
                e.ArmCount = 1;
                e.LastCount = count;
                MachineStore.SetQueue(m, recipe.ID, recipe.Name, count == int.MaxValue ? int.MaxValue : count - 1);
                Log.Verbose($"Queue: {Stations.Name(m)} repeats {Stations.RecipeName(recipe)} {(count == int.MaxValue ? "until inputs run out" : count + " times")}.");
                RepeatButton.RefreshCraftLabel(m);
            }
            else if (e.Queued && !sameQueue)
            {
                MachineStore.ClearQueue(m);
                Log.Verbose($"Queue: a different craft on {Stations.Name(m)} ends its repeat.");
                e.LastCount = 1;
            }
            else if (!sameQueue) e.LastCount = 1;
            e.LastId = recipe.ID;
            e.LastName = recipe.Name;
            e.LastMult = 1;
            LookHint.Refresh(m);
        }

        private static int ShownId(manufacturing m)
        {
            try
            {
                var info = ManufacturingPanel.instance != null ? ManufacturingPanel.instance.manufacturingPanelUI.recipeListUI.recipeInfo : null;
                var r = info != null && Vanilla.PanelOpen(m) ? info.CurrentRecipe() : null;
                return r != null ? r.ID : int.MinValue;
            }
            catch { return int.MinValue; }
        }
    }

    // W3: manufacturing.Craft() (SRC/manufacturing.cs:357). The prefix notes the job count, the instant-craft frame and
    // the selected recipe; the postfix sees whether a craft really started. Never alters the outcome.
    [HarmonyPatch]
    internal static class CraftPatch
    {
        internal struct State
        {
            internal int Jobs, Frame;
            internal Recipe Recipe;
            internal bool Valid;
        }

        private static MethodBase TargetMethod() => Vanilla.Craft;

        private static void Prefix(manufacturing __instance, out State __state)
        {
            __state = default;
            try
            {
                if (Gates.InOurCall || !Cfg.Queue.Enabled.Value) return;
                var jobs = Vanilla.JobsOf(__instance);
                __state = new State { Jobs = jobs != null ? jobs.Count : 0, Frame = Vanilla.LastInstantCraftFrame(__instance), Recipe = Vanilla.SelectedRecipe(__instance), Valid = true };
            }
            catch (Exception e) { Log.Exception("craft watch", e); }
        }

        private static void Postfix(manufacturing __instance, State __state)
        {
            if (!__state.Valid) return;
            try
            {
                var jobs = Vanilla.JobsOf(__instance);
                int frame = Vanilla.LastInstantCraftFrame(__instance);
                bool started = (jobs != null && jobs.Count > __state.Jobs) || (frame != __state.Frame && frame == Time.frameCount);
                if (started) QueuePatches.OnCraft(__instance, __state.Recipe);
            }
            catch (Exception e) { Log.Exception("craft watch", e); }
        }
    }

    // W4: manufacturingProcess.StartProcess() (SRC/manufacturingProcess.cs:159). Records the vessel's Last with the
    // batch multiplier it started with.
    [HarmonyPatch]
    internal static class StartProcessPatch
    {
        internal struct State
        {
            internal Recipe Recipe;
            internal int Mult;
            internal bool Loading;
        }

        private static MethodBase TargetMethod() => Vanilla.StartProcess;

        private static void Prefix(manufacturingProcess __instance, out State __state)
        {
            __state = default;
            try
            {
                if (!Cfg.Queue.Enabled.Value) return;
                __state = new State { Loading = __instance.stage == manufacturingProcess.Stage.LoadingItems, Recipe = __instance.ActiveRecipe.Key, Mult = __instance.ActiveRecipe.Value };
            }
            catch (Exception e) { Log.Exception("vessel watch", e); }
        }

        private static void Postfix(manufacturingProcess __instance, State __state)
        {
            if (!__state.Loading || __state.Recipe == null || __state.Recipe.ID <= 0) return;
            try
            {
                // Started: the stage moved on, or an instant batch finished and cleared the recipe.
                bool started = __instance.stage != manufacturingProcess.Stage.LoadingItems || __instance.ActiveRecipe.Key == null;
                if (!started) return;
                var e = MachineStore.Get(__instance, true);
                e.LastId = __state.Recipe.ID;
                e.LastName = __state.Recipe.Name;
                e.LastMult = Math.Max(1, __state.Mult);
                e.LastCount = 1;
                LookHint.Refresh(__instance);
            }
            catch (Exception e) { Log.Exception("vessel watch", e); }
        }
    }

    // W5: GlobalState.OnSavingGame() prefix. Only the live GlobalState; GUIDs are final by now (after the Tertiary
    // renumber). MachineStore.OnSaving never throws.
    [HarmonyPatch]
    internal static class GlobalSavePatch
    {
        private static MethodBase TargetMethod() => Vanilla.GlobalStateOnSavingGame;

        private static void Prefix(GlobalState __instance)
        {
            try
            {
                if (__instance == GlobalState.instance) MachineStore.OnSaving();
            }
            catch (Exception e) { Log.Exception("queue save", e); }
        }
    }

    // W6: GlobalState.OnLoadingGame() postfix: read the record and bind stations that already loaded.
    [HarmonyPatch]
    internal static class GlobalLoadPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GlobalStateOnLoadingGame;

        private static void Postfix(GlobalState __instance, bool __result)
        {
            try
            {
                if (__result && __instance == GlobalState.instance) MachineStore.OnGlobalsLoaded();
            }
            catch (Exception e) { Log.Exception("queue load", e); }
        }
    }

    // W7: CraftingBase.OnLoadingGame() postfix: bind this station to its record. The job list is deserialized by now and
    // manufacturing.LoadDelayProcess has not yet let offline-finished jobs fire.
    [HarmonyPatch]
    internal static class StationLoadPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CraftingBaseOnLoadingGame;

        private static void Postfix(CraftingBase __instance)
        {
            try
            {
                if (__instance is manufacturing || __instance is manufacturingProcess) MachineStore.OnStationLoaded(__instance);
            }
            catch (Exception e) { Log.Exception("queue bind", e); }
        }
    }
}

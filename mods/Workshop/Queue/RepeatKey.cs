using System;

namespace Workshop
{
    // The repeat-last key (spec §5.5). With a station panel open it acts on that station (the player is at the bench, so
    // benches that need the player are allowed); with no menu open, on the machine the player looks at within reach.
    internal static class RepeatKey
    {
        private const string StopKey = "repeat-stop", ShortKey = "repeat-short";

        internal static void Tick()
        {
            LookHint.Tick();
            if (!QueuePatches.Patched || !Cfg.Queue.Enabled.Value) return;
            if (!Keys.Down(Cfg.Queue.RepeatKey) || !PlayGate.Open) return;
            try { Press(); }
            catch (Exception e) { Log.Exception("repeat key", e); }
        }

        private static void Press()
        {
            CraftingBase target;
            bool attended;
            if (Game.MenuVisible)
            {
                target = CraftingBase.currentCraftingBase;
                if (target == null || !Vanilla.PanelOpen(target)) return;
                attended = true;
            }
            else
            {
                var io = InteractObjects.instance;
                target = io != null ? io.interactable as CraftingBase : null;
                if (target == null || !io.IsWithinUseDistance(target)) return;
                attended = false;
            }
            if (Game.DragActive || Game.SliderOpen || Game.TradeActive) return;

            switch (Stations.KindOf(target))
            {
                case Kind.Plain:
                    Manufacturing((manufacturing)target, attended);
                    break;
                case Kind.RequirePlayer:
                    if (attended) Manufacturing((manufacturing)target, true);
                    else
                    {
                        if (MachineStore.Get(target, false)?.HasLast == true) LookHint.Flash(target, Strings.OpenTheBench, warn: true);
                    }
                    break;
                case Kind.Vessel:
                    Vessel((manufacturingProcess)target);
                    break;
                default:
                    return;   // dishwashers, the game's Autocraft, auto-run crushers: nothing, no hint
            }
        }

        private static void Manufacturing(manufacturing m, bool attended)
        {
            var e = MachineStore.Get(m, false);
            if (e == null || !e.HasLast) return;
            if (e.Queued)
            {
                if (Confirm.IsArmed(StopKey, m))
                {
                    Confirm.Reset(StopKey);
                    MachineStore.ClearQueue(m);
                    RepeatButton.RefreshCraftLabel(m);
                    LookHint.Flash(m, Strings.QueueCleared, warn: false);
                    return;
                }
                Confirm.Arm(StopKey, m, Cfg.Queue.ConfirmSeconds.Value);
                LookHint.Flash(m, Strings.StopArmed(e.QName, e.QLeft, Keys.Label(Cfg.Queue.RepeatKey)), warn: false);
                return;
            }
            // With the panel open a single start is a Craft click; one that arms a queue is checked like the pump's starts.
            bool armsQueue = e.LastCount > 1 && Stations.KindOf(m) == Kind.Plain;
            if (!Gates.CanCraft(m, e.LastId, e.LastName, attended, checkRoom: !attended || armsQueue, autoRun: false, out var copy, out var why))
            {
                Log.Verbose($"Queue: repeat on {Stations.Name(m)} refused: {why.Text}");
                LookHint.Flash(m, why.Text, warn: true);
                return;
            }
            if (!Gates.TryCraft(m, copy))
            {
                LookHint.Flash(m, Strings.Unavailable, warn: true);
                return;
            }
            if (armsQueue)
                MachineStore.SetQueue(m, e.LastId, e.LastName, e.LastCount == int.MaxValue ? int.MaxValue : e.LastCount - 1);
            RepeatButton.RefreshCraftLabel(m);
            Log.Verbose($"Queue: repeat started {Stations.RecipeName(copy)} on {Stations.Name(m)}.");
            LookHint.Flash(m, Strings.Started(copy.Name), warn: false);
        }

        private static void Vessel(manufacturingProcess p)
        {
            var e = MachineStore.Get(p, false);
            if (e == null || !e.HasLast) return;
            if (!Gates.CanStartVessel(p, e.LastId, e.LastName, out var copy, out int kNow, out var why))
            {
                Log.Verbose($"Queue: repeat on {Stations.Name(p)} refused: {why.Text}");
                LookHint.Flash(p, why.Text, warn: true);
                return;
            }
            if (kNow < e.LastMult && !Confirm.IsArmed(ShortKey, p))
            {
                Confirm.Arm(ShortKey, p, Cfg.Queue.ConfirmSeconds.Value);
                LookHint.Flash(p, Strings.ShortBatch(kNow, e.LastMult, Keys.Label(Cfg.Queue.RepeatKey)), warn: false);
                return;
            }
            Confirm.Reset(ShortKey);
            // The game's own SetActiveRecipe gives the batch the slots allow now, the value its Start button uses.
            Vanilla.SetActiveRecipe.Invoke(p, new object[] { copy });
            p.StartProcess();
            bool started = p.stage != manufacturingProcess.Stage.LoadingItems || p.ActiveRecipe.Key == null;
            Log.Verbose($"Queue: repeat {(started ? "started" : "did not start")} {Stations.RecipeName(copy)} ×{kNow} on {Stations.Name(p)}.");
            LookHint.Flash(p, started ? Strings.Started(copy.Name) : Strings.Unavailable, warn: !started);
        }
    }
}

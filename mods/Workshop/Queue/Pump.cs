using System;
using System.Collections.Generic;
using UnityEngine;

namespace Workshop
{
    // Starts queued jobs and auto-run crusher jobs (spec §5.4, §5.8), polled from Plugin.Update: no timer hooks, so a
    // start never runs inside the game's UpdateTimers or Craft. One start per machine per tick, MaxStartsPerTick in all.
    // Deferred while any menu other than the machine's own panel is open, and with a controller also while that panel is
    // open: Craft always calls ExitRecipeOpenedPanel (SRC/manufacturing.cs:407-411), which sets the navigation window to
    // the inventory at (0,0) (SRC/InventoryNavigationHandler.cs:1838-1843) and would move a controller cursor elsewhere.
    // Every pump start checks output room (gate 12), panel open or not: its output arrives later, maybe unwatched.
    internal static class Pump
    {
        private static float _next, _nextScan;
        private static readonly List<manufacturing> Crushers = new List<manufacturing>();
        private static bool _autoRunLogged, _autoRunWas;

        internal static void OnSceneLoaded()
        {
            Crushers.Clear();
            _nextScan = 0f;
        }

        internal static void Tick()
        {
            if (!QueuePatches.Patched || !Cfg.Queue.Enabled.Value) return;
            LogAutoRun();
            if (!PlayGate.Open) return;
            float now = Time.unscaledTime;
            bool periodic = now >= _next;
            if (periodic) _next = now + Cfg.Queue.PollSeconds.Value;
            if (!periodic && MachineStore.PollNow.Count == 0) return;

            int budget = Cfg.Queue.MaxStartsPerTick.Value;
            foreach (var cb in MachineStore.Machines())
            {
                if (budget <= 0) break;
                if (!periodic && !MachineStore.PollNow.Contains(cb)) continue;
                if (!(cb is manufacturing m)) continue;
                var e = MachineStore.Get(m, false);
                if (e == null || !e.Queued || !CanRunNow(m)) continue;
                try { if (StartQueued(m, e)) budget--; }
                catch (Exception ex) { Log.Exception("queue start", ex); }
            }
            MachineStore.PollNow.Clear();

            if (periodic && Cfg.Queue.AutoRun.Value)
            {
                if (now >= _nextScan) Scan(now);
                foreach (var m in Crushers)
                {
                    if (budget <= 0) break;
                    if (m == null || Stations.KindOf(m) != Kind.AutoRun || !CanRunNow(m)) continue;
                    var e = MachineStore.Get(m, false);
                    if (e != null && e.Queued) continue;
                    try { if (AutoRunOnce(m)) budget--; }
                    catch (Exception ex) { Log.Exception("auto-run", ex); }
                }
            }
        }

        private static bool CanRunNow(manufacturing m)
        {
            if (m == null || !m.isActiveAndEnabled) return false;
            if (Game.MenuVisible && (!Vanilla.PanelOpen(m) || Game.ControllerActive)) return false;
            return !Game.DragActive && !Game.SliderOpen;
        }

        private static bool StartQueued(manufacturing m, Entry e)
        {
            if (!Gates.CanCraft(m, e.QId, e.QName, attended: false, checkRoom: true, autoRun: false, out var copy, out var why))
            {
                if (why.Transient)
                {
                    Log.Verbose($"Queue: {Stations.Name(m)} waits: {why.Text}");
                    return false;
                }
                int left = e.QLeft;
                string name = e.QName;
                MachineStore.ClearQueue(m);
                RepeatButton.RefreshCraftLabel(m);
                Log.Verbose($"Queue: {Stations.Name(m)} stops: {why.Text}");
                if (m.gameObject.scene == UnityEngine.SceneManagement.SceneManager.GetActiveScene())
                    Notify.Show(Strings.QueueStopped(Stations.Name(m), why.Text, left));
                return false;
            }
            if (!Gates.TryCraft(m, copy)) return false;
            if (e.QLeft != int.MaxValue) e.QLeft--;
            if (e.QLeft <= 0) MachineStore.ClearQueue(m);
            Log.Verbose($"Queue: {Stations.Name(m)} started {Stations.RecipeName(copy)}{(e.Queued ? ", " + (e.QLeft == int.MaxValue ? "repeating" : e.QLeft + " left") : ", done")}.");
            RepeatButton.RefreshCraftLabel(m);
            LookHint.Refresh(m);
            return true;
        }

        // The first allowed recipe that passes every gate, output room included. Unattended rules always apply: the
        // crusher's own panel being open changes nothing here.
        private static bool AutoRunOnce(manufacturing m)
        {
            foreach (int id in Cfg.Ints(Cfg.Queue.AutoRunRecipes))
            {
                if (!Gates.CanCraft(m, id, null, attended: false, checkRoom: true, autoRun: true, out var copy, out _)) continue;
                if (!Gates.TryCraft(m, copy)) return false;
                Log.Verbose($"Queue: auto-run {Stations.Name(m)} started {Stations.RecipeName(copy)}.");
                return true;
            }
            return false;
        }

        private static void Scan(float now)
        {
            _nextScan = now + 10f;
            Crushers.Clear();
            try
            {
                foreach (var m in UnityEngine.Object.FindObjectsByType<manufacturing>(FindObjectsSortMode.None))
                    if (m != null && Stations.AutoRunEligible(m)) Crushers.Add(m);
            }
            catch (Exception e) { Log.Exception("auto-run scan", e); }
        }

        private static void LogAutoRun()
        {
            bool on = Cfg.Queue.AutoRun.Value;
            if (_autoRunLogged && on == _autoRunWas) return;
            _autoRunLogged = true;
            _autoRunWas = on;
            Log.Info(on ? $"Queue: auto-run is on for {Cfg.Queue.AutoRunMachines.Value}." : "Queue: auto-run is off.");
            _nextScan = 0f;
        }
    }
}

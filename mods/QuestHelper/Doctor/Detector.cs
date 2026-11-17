using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace QuestHelper
{
    // Runs every fix's Detect (read-only) and keeps the findings (spec §3.3). Never during a conversation, while loading,
    // or before the post-load tick. RunAll runs on the post-load tick and when the window opens; Tick recomputes at most
    // every 5 real seconds after a task change, so the pause entry's "(n)" and the journal lines stay current.
    internal static class Detector
    {
        private const float Interval = 5f;

        internal static readonly IFix[] All = { new RatProblem(), new CasinoCoup(), new ToivoLie(), new MiningPermit(), new ElianaTalk() };

        private static readonly Dictionary<string, Diagnosis> Findings = new Dictionary<string, Diagnosis>();
        private static readonly HashSet<string> Logged = new HashSet<string>();
        private static readonly HashSet<string> Notified = new HashSet<string>();
        private static float _next;

        internal static bool Ready;
        internal static bool Dirty;
        internal static bool Available;   // the doctor group is on

        internal static IEnumerable<IFix> Fixes
        {
            get
            {
                var off = Cfg.DisabledFixes();
                return All.Where(f => !off.Contains(f.Id));
            }
        }

        internal static Diagnosis Get(IFix f) => f != null && Findings.TryGetValue(f.Id, out var d) ? d : null;

        internal static int StuckCount => Fixes.Count(f => Get(f)?.State == DiagState.Stuck);

        internal static bool StuckFor(string taskId) =>
            !string.IsNullOrEmpty(taskId) && Fixes.Any(f => f.TaskId == taskId && Get(f)?.State == DiagState.Stuck);

        internal static IFix StuckFixFor(string taskId) =>
            string.IsNullOrEmpty(taskId) ? null : Fixes.FirstOrDefault(f => f.TaskId == taskId && Get(f)?.State == DiagState.Stuck);

        internal static void OnLoadingStarted()
        {
            Ready = false;
            Findings.Clear();
        }

        // The main menu: the findings belong to the character that was just left.
        internal static void OnMainMenu()
        {
            Ready = false;
            Dirty = false;
            Findings.Clear();
        }

        internal static Diagnosis Run(IFix f)
        {
            Diagnosis d;
            try { d = f.Detect() ?? Diagnosis.Of(DiagState.CannotCheck); }
            catch (Exception e)
            {
                Log.Exception("detect " + f.Id, e);
                d = Diagnosis.Of(DiagState.CannotCheck, Strings.CheckFailed);
            }
            Findings[f.Id] = d;
            string key = Character() + "|" + f.Id + "|" + d.State + "|" + d.Variant;
            if (Logged.Add(key))
            {
                if (d.State == DiagState.NotAffected) Log.Verbose($"Doctor: {f.Id} not affected.");
                else Log.Info($"Doctor: {f.Id} {d.State}{(d.Variant.Length > 0 ? "/" + d.Variant : "")}{(d.Why != null ? " (" + d.Why + ")" : "")}.");
            }
            return d;
        }

        internal static bool CanRun => Available && Ready && TaskState.Ready && !SaveController.Loading && !Game.InConversation;

        internal static void RunAll()
        {
            _next = Time.unscaledTime + Interval;
            if (!CanRun)
            {
                Dirty = Available;   // try again once the conversation or load is over
                return;
            }
            Dirty = false;
            foreach (var f in Fixes) Run(f);
            PauseEntry.Relabel();
        }

        // The post-load tick: also the once-per-session note per character and fix.
        internal static void AfterLoad()
        {
            Ready = true;
            RunAll();
            if (!Available || !Cfg.QuestDoctor.NotifyWhenStuck.Value) return;
            bool any = false;
            foreach (var f in Fixes)
                if (Get(f)?.State == DiagState.Stuck && Notified.Add(Character() + "|" + f.Id)) any = true;
            if (any) Toast.Show(Strings.StuckNotice, 10f);
        }

        internal static void Tick()
        {
            if (!Dirty || Time.unscaledTime < _next || !CanRun) return;
            RunAll();
        }

        private static string Character()
        {
            // The character folder of the newest save of this playthrough (SaveController.CharacterName is private).
            try
            {
                string raw = SaveFolder.Raw;
                return string.IsNullOrEmpty(raw) ? "" : System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(raw.TrimEnd('/', '\\')));
            }
            catch { return ""; }
        }
    }
}

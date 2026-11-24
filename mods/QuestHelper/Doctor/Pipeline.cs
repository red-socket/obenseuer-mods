using System;
using System.Collections.Generic;

namespace QuestHelper
{
    internal sealed class Outcome
    {
        internal bool Changed, Verified, Broke;
        internal string Message, BackupKind, BackupPath, BackupSave, Title;
    }

    // Gates, re-check, prepare, backup, apply, verify, record (spec §4.6), shared by the curated fixes and the generic
    // edits. Nothing changes unless every gate holds, the state is still the one the player saw, everything the edit
    // needs was found, and the backup succeeded. Quest Helper never saves: the result page offers the vanilla save menu.
    internal static class Pipeline
    {
        // Null when an edit may run, otherwise the reason shown to the player.
        internal static string Gates()
        {
            if (!HelperWindow.IsOpen || !Game.PauseOpen) return Strings.GateWindow;
            if (Game.InConversation || SaveController.Loading || Game.Dead || TaskController.instance == null) return Strings.GateBusy;
            if (SaveGuardBridge.SaveInProgress) return Strings.GateSaving;
            if (Game.SaveLocked) return Strings.GateSaveLock;
            if (SaveFolder.Current(out _) == null) return Strings.SaveFirst;
            return null;
        }

        internal static Outcome RunFix(IFix fix, Diagnosis shown)
        {
            var o = new Outcome { Title = fix.Title };
            string gate = Gates();
            if (gate != null) { o.Message = gate; Log.Warn($"Doctor: {fix.Id} refused: {gate}"); return o; }

            var d = Detector.Run(fix);
            if (d.State != DiagState.Stuck || shown == null || d.Variant != shown.Variant)
            {
                o.Message = Strings.NotThatState;
                Log.Warn($"Doctor: {fix.Id} refused: the state changed to {d.State}/{d.Variant}.");
                return o;
            }

            var plan = new Plan();
            bool ready;
            try { ready = fix.Prepare(d, plan); }
            catch (Exception e)
            {
                Log.Exception("prepare " + fix.Id, e);
                ready = false;
                plan.Missing = plan.Missing ?? e.Message;
            }
            if (!ready)
            {
                o.Message = Strings.CouldntFindPrep(plan.Missing ?? "what this fix needs");
                Log.Warn($"Doctor: {fix.Id} refused: missing {plan.Missing}.");
                return o;
            }

            string before = Snap(fix, plan);
            return Run(o, fix.Id, string.IsNullOrEmpty(plan.Variant) ? d.Variant : plan.Variant, before,
                log => fix.Apply(plan, log),
                log => fix.Verify(plan, log) && Detector.Run(fix).State != DiagState.Stuck,
                () => Snap(fix, plan));
        }

        // A generic edit (spec §4.5): still has to be valid now; apply and verify are given by the caller.
        internal static Outcome RunGeneric(string title, string op, Func<bool> stillValid, Action<List<string>> apply, Func<bool> verify, Func<string> snapshot)
        {
            var o = new Outcome { Title = title };
            string gate = Gates();
            if (gate != null) { o.Message = gate; Log.Warn($"Doctor: generic {op} refused: {gate}"); return o; }
            if (!stillValid()) { o.Message = Strings.NotThatState; return o; }
            return Run(o, "generic", "generic:" + op, snapshot(), apply, _ => verify(), snapshot);
        }

        private static Outcome Run(Outcome o, string id, string variant, string before, Action<List<string>> apply,
            Func<List<string>, bool> verify, Func<string> snapshot)
        {
            var b = Backup.Make(id);
            if (!b.Ok)
            {
                o.Message = b.Error ?? Strings.BackupFailed("unknown error");
                return o;
            }
            o.BackupKind = b.Kind;
            o.BackupPath = b.Path;
            o.BackupSave = b.Save;

            var log = new List<string>();
            Edits.Applying = true;
            try { apply(log); }
            catch (Exception e)
            {
                o.Broke = true;
                o.Changed = true;
                o.Message = Strings.StoppedPartway(b.Path);
                Log.Error($"Doctor: {id}/{variant} stopped partway: {e}\n  steps: {string.Join(" | ", log.ToArray())}\n  backup {b.Kind} {b.Path}");
                return o;
            }
            finally
            {
                Edits.Applying = false;
                TbcSection.Dirty = true;
                MapPin.MarkDirty();
                Detector.Dirty = true;
            }
            o.Changed = true;
            try { o.Verified = verify(log); }
            catch (Exception e) { Log.Exception("verify " + id, e); o.Verified = false; }
            string after;
            try { after = snapshot(); } catch (Exception e) { after = "(" + e.Message + ")"; }
            Records.AddDoctor(id, variant, b.Kind);
            Log.Info($"Doctor: {id}/{variant} applied; backup {b.Kind} {b.Path}; before {{{before}}}; after {{{after}}}; verify {(o.Verified ? "OK" : "FAILED")}");
            foreach (var line in log) Log.Verbose("Doctor:   " + line);
            Detector.RunAll();
            return o;
        }

        private static string Snap(IFix fix, Plan p)
        {
            try { return fix.Snapshot(p); }
            catch (Exception e) { return "(" + e.Message + ")"; }
        }
    }
}

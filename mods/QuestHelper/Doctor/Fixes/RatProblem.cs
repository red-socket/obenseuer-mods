using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace QuestHelper
{
    // QD-RAT: Rat problem (#6547), variants "counter" and "released" (spec §4.4).
    // Six task rats live under NPC/Rats/Rat Task/Hide in the Mines canteen. Catching or killing one fires Relay &33598 ->
    // EditTask &33599 (+1). A 2 s hold-use slaughters a rat through InvokeActions, which destroys it without counting
    // (CollectibleItem.cs:327 -> :408), so the counter can never reach 6. A caught, killed or slaughtered rat is destroyed
    // and stays destroyed after reload (V2), so the rats still under Hide are exactly the ones left to catch.
    // "released": Anna's talk was cut between 202:25/44 and 202:46, so Relay_rats never showed the task rats (V1).
    // Sources: #6547; DB 202; Canteen &33598/&33599/&33539/&33540.
    internal sealed class RatProblem : IFix
    {
        private const string TaskIdConst = "RatProblem74";
        private const string ObjId = "CatchRats12";
        private const string Scene = "Interior Mines Canteen";
        private const int Needed = 6;

        public string Id => "QD-RAT";
        public string Title => Strings.RatTitle;
        public string TaskId => TaskIdConst;

        private sealed class Found
        {
            internal Transform RatTask, Hide;
            internal GameObjectStateSave TaskSaver, DefaultSaver;
            internal int Remaining;
        }

        public Diagnosis Detect()
        {
            var tsi = TaskState.Find(TaskIdConst);
            var osi = TaskState.FindObj(tsi, ObjId);
            if (!TaskState.ObjActive(TaskIdConst, ObjId) || osi == null) return Diagnosis.Of(DiagState.NotAffected);
            if (osi.counterAmount != Needed)
                return Diagnosis.Of(DiagState.CannotCheck, Strings.RatCounterChanged(osi.counterAmount));
            if (osi.currentAmount >= Needed) return Diagnosis.Of(DiagState.NotAffected);
            if (Game.SceneName != Scene)
                return Diagnosis.Of(DiagState.CheckThere, Strings.RatCheckThere).Ev(Strings.RatCounted(osi.currentAmount, Needed));

            var f = Find(out string why);
            if (f == null) return Diagnosis.Of(DiagState.CannotCheck, why);

            var d = new Diagnosis();
            d.Ev(Strings.RatCounted(osi.currentAmount, Needed));
            if (!f.Hide.gameObject.activeSelf)
            {
                d.State = DiagState.Stuck;
                d.Variant = "released";
                d.Ev(Strings.RatNotReleased);
                d.Plan.Add(Strings.RatPlanRelease);
                return d;
            }
            d.Ev(Strings.RatLeft(f.Remaining));
            if (osi.currentAmount + f.Remaining < Needed)
            {
                int missing = Needed - osi.currentAmount - f.Remaining;
                d.State = DiagState.Stuck;
                d.Variant = "counter";
                d.Ev(Strings.RatLost(missing));
                d.Plan.Add(Strings.RatPlanCounter(missing, osi.currentAmount + missing, f.Remaining));
                return d;
            }
            d.State = DiagState.NotAffected;
            return d;
        }

        // NPC/Rats/Rat Task with its saver and its Hide child (V1). The saver's own target must be that Hide when the
        // private field can be read.
        private static Found Find(out string why)
        {
            why = null;
            Transform npc = null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
                if (root != null && root.name == "NPC") { npc = root.transform; break; }
            var ratTask = npc != null ? npc.Find("Rats/Rat Task") : null;
            var saver = ratTask != null ? ratTask.GetComponent<GameObjectStateSave>() : null;
            if (ratTask == null || saver == null) { why = Strings.CouldNotFind("NPC/Rats/Rat Task"); return null; }
            var hide = ratTask.Find("Hide");
            if (hide == null) { why = Strings.CouldNotFind("Rat Task/Hide"); return null; }
            if (Vanilla.StateSaveObject != null && Vanilla.StateSaveObject(saver) != hide.gameObject)
            {
                why = Strings.CouldNotFind("the rats' saved switch");
                return null;
            }
            var f = new Found { RatTask = ratTask, Hide = hide, TaskSaver = saver };
            var def = npc.Find("Rats/Rat Default");
            if (def != null) f.DefaultSaver = def.GetComponent<GameObjectStateSave>();
            foreach (Transform c in hide)
                if (c != null && c.name.StartsWith("rat_001", System.StringComparison.Ordinal) && c.GetComponent<CollectibleItem>() != null)
                    f.Remaining++;
            return f;
        }

        public bool Prepare(Diagnosis d, Plan p)
        {
            p.Tsi = TaskState.Find(TaskIdConst);
            p.Osi = TaskState.FindObj(p.Tsi, ObjId);
            if (p.Tsi == null || p.Osi == null) { p.Missing = Strings.RatTitle; return false; }
            var f = Find(out string why);
            if (f == null) { p.Missing = "NPC/Rats/Rat Task"; return false; }
            p.Variant = d.Variant;
            p.Before = p.Osi.currentAmount;
            p.Watch = f.Hide.gameObject;
            if (d.Variant == "released")
            {
                p.SaverOn = f.TaskSaver;
                p.SaverOff = f.DefaultSaver;   // may be null: the vanilla relay hides it, we skip what isn't there
                return true;
            }
            p.Amount = Needed - p.Osi.currentAmount - f.Remaining;
            return p.Amount > 0;
        }

        public void Apply(Plan p, List<string> log)
        {
            if (p.Variant == "released")
            {
                // Exactly Relay_rats: the task rats' saver on, the ambient rats' saver off (V1).
                Edits.SetSceneSaver(p.SaverOn, true, log);
                if (p.SaverOff != null) Edits.SetSceneSaver(p.SaverOff, false, log);
                return;
            }
            Edits.Counter(p.Tsi, p.Osi, p.Amount, log);
        }

        public bool Verify(Plan p, List<string> log)
        {
            if (p.Variant == "released") return p.Watch != null && p.Watch.activeSelf;
            if (p.Before + p.Amount >= Needed)
                return TaskState.ObjCompleted(TaskIdConst, ObjId) && TaskState.ObjActive(TaskIdConst, "CaughtRats16");
            return p.Osi.currentAmount == p.Before + p.Amount;
        }

        public string Snapshot(Plan p) =>
            $"{ObjId} {p.Osi?.status} {p.Osi?.currentAmount}/{Needed}, hide active {(p.Watch != null && p.Watch.activeSelf)}";
    }
}

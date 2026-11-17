using System.Collections.Generic;

namespace QuestHelper
{
    // QD-ELIANA: Eliana's first talk cut off (spec §4.4, D4, D6).
    // 145:2/73 set TalkedFirstTime before 145:6 (Eliana11 finished), 145:8 (ElianaGet12), 145:17/52 (B1 keys) and
    // 145:18/53 (Priest's debt). Afterwards her root goes to 25 -> 29, whose answers need ElianaTell56 Active, so a cut
    // talk leaves only "Not yet". Variant a: Eliana11 still active, so the first talk can simply replay. Variant b:
    // Eliana11 already finished, so the rest of what the talk gives is given here: ElianaGet12 (if missing), the B1 keys
    // (only if not held, the lost key item the quest needs) and Priest's debt.
    // Sources: DB 145:2-53, 25-29; Reddit report.
    internal sealed class ElianaTalk : IFix
    {
        private const string Task = "GettingPassport5";
        internal const string Eliana = "Tenement_Kolhola_Eliana_Passmore";
        private const string GetPath = "Assets/Content/Tasks/Getting a passport/Get the passport from Eliana.asset";
        private const string PriestPath = "Assets/Content/Tasks/Priest's debt/Go to check the priest's apartment B1.asset";
        internal const string KeysPath = "Assets/Content/TaskItems/Keys/Deekula apartment B1 keys.asset";

        public string Id => "QD-ELIANA";
        public string Title => Strings.ElianaTitle;
        public string TaskId => Task;

        public Diagnosis Detect()
        {
            if (!TaskState.TaskActive(Task)) return Diagnosis.Of(DiagState.NotAffected);
            var first = LuaRead.ActorBool(Eliana, "TalkedFirstTime");
            var priest = LuaRead.ActorBool(Eliana, "TalkedAboutPriest");
            if (first == null || priest == null) return Diagnosis.Of(DiagState.CannotCheck, Strings.CouldNotRead("Eliana's talk flags"));
            if (!first.Value || priest.Value || TaskState.TaskAny("Priest10")) return Diagnosis.Of(DiagState.NotAffected);

            var d = new Diagnosis();
            d.Ev(Strings.ElianaEvidence);
            if (TaskState.ObjActive(Task, "Eliana11"))
            {
                d.State = DiagState.Stuck;
                d.Variant = "a";
                d.Plan.Add(Strings.ElianaPlanA);
                return d;
            }
            if (TaskState.ObjCompleted(Task, "Eliana11"))
            {
                d.State = DiagState.Stuck;
                d.Variant = "b";
                if (!TaskState.ObjAny(Task, "ElianaGet12")) d.Plan.Add(Strings.ElianaPlanGet);
                d.Plan.Add(Strings.ElianaPlanKeys);
                d.Plan.Add(Strings.ElianaPlanPriest);
                return d;
            }
            d.State = DiagState.CannotFix;
            d.Why = Strings.ElianaCannotFix;
            return d;
        }

        public bool Prepare(Diagnosis d, Plan p)
        {
            p.Variant = d.Variant;
            if (d.Variant == "a") return true;
            p.Tsi = TaskState.Find(Task);
            if (p.Tsi == null) { p.Missing = "the \"Getting a passport\" task"; return false; }
            if (!TaskState.ObjAny(Task, "ElianaGet12"))
            {
                p.Obj1 = Edits.LoadObjective(GetPath, "ElianaGet12");
                if (p.Obj1 == null) { p.Missing = GetPath; return false; }
                if (!Edits.CanActivate(p.Obj1, p.Tsi.task, p.Tsi)) { p.Missing = Strings.Requirements(p.Obj1.title); return false; }
            }
            p.Item = Edits.LoadTaskItem(KeysPath);
            if (p.Item == null || TaskItemsManager.instance == null) { p.Missing = KeysPath; return false; }
            p.Obj2 = Edits.LoadObjective(PriestPath, "PriestApartment26");
            if (p.Obj2 == null || p.Obj2.task == null) { p.Missing = PriestPath; return false; }
            if (!Edits.CanActivate(p.Obj2, p.Obj2.task, null)) { p.Missing = Strings.Requirements(p.Obj2.task.title); return false; }
            return true;
        }

        public void Apply(Plan p, List<string> log)
        {
            if (p.Variant == "a")
            {
                // The first talk replays and gives everything the vanilla way.
                Edits.SetActorField(Eliana, "TalkedFirstTime", false, log);
                return;
            }
            if (p.Obj1 != null) Edits.ActivateNew(p.Obj1, p.Tsi.task, false, log);
            Edits.GiveTaskItemIfMissing(p.Item, log);
            Edits.ActivateNew(p.Obj2, p.Obj2.task, true, log);
        }

        public bool Verify(Plan p, List<string> log)
        {
            if (p.Variant == "a") return LuaRead.ActorBool(Eliana, "TalkedFirstTime") == false;
            return TaskState.ObjActive("Priest10", "PriestApartment26") && TaskState.ObjAny(Task, "ElianaGet12") &&
                   TaskItemsManager.instance != null && TaskItemsManager.instance.PlayerHasTaskItemId(p.Item.Id);
        }

        public string Snapshot(Plan p) =>
            $"Eliana.TalkedFirstTime={LuaRead.Show(LuaRead.ActorBool(Eliana, "TalkedFirstTime"))}, Eliana11 active={TaskState.ObjActive(Task, "Eliana11")}, " +
            $"ElianaGet12 any={TaskState.ObjAny(Task, "ElianaGet12")}, Priest10 any={TaskState.TaskAny("Priest10")}, " +
            $"B1 keys={(TaskItemsManager.instance != null && TaskItemsManager.instance.PlayerHasTaskItemId(KeysPath))}";
    }
}

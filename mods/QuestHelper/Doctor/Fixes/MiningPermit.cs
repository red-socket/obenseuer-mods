using System.Collections.Generic;

namespace QuestHelper
{
    // QD-PERMIT: Tanja's permit talk cut off (spec §4.4, D4, D5).
    // FillForm42 is finished when the form is handed in (207:26); the talk then branches on age (207:28) and ends either
    // at the passport check (207:41 -> 60, PassportTanja52) or at the tax (207:90 -> 96 or 120, TaxPay41). Cut off in
    // between (an arrest, say), Management25 stays active with nothing left to do. The route follows Tanja's own
    // branches (V4): Points >= 5 -> tax; age over 30 -> tax (28 -> 31 -> 90, or 30 -> 74); age 30 or under -> passport,
    // except where her talk has already passed the passport check: PassportTanja52 already has a status; the form was
    // handed in a second time (FillFormAgain33 finished at 207:98, then 99 -> 31 -> 90 with no passport question); or the
    // player holds the forged passport, which she accepts at 53 -> 55 -> 67 -> 90. Known limit (D5): a hand-drawn
    // passport she accepted after 69 -> 70 leaves no trace, so that player gets the passport step. The 200 RM tax and,
    // on the passport route, the forged passport are still required; nothing is handed out.
    // Sources: DB 207:26-121; Reddit report.
    internal sealed class MiningPermit : IFix
    {
        private const string Task = "Permission32";
        internal const string Tanja = "Mines_Tanja_Nypan";
        private const string TaxPath = "Assets/Content/Tasks/Permission to dig/Pay miners tax.asset";
        private const string PassportPath = "Assets/Content/Tasks/Permission to dig/Show Tanja a valid passport.asset";

        public string Id => "QD-PERMIT";
        public string Title => Strings.PermitTitle;
        public string TaskId => Task;

        public Diagnosis Detect()
        {
            if (!TaskState.ObjActive(Task, "Management25") || !TaskState.ObjCompleted(Task, "FillForm42")) return Diagnosis.Of(DiagState.NotAffected);
            if (TaskState.ObjActive(Task, "FillFormAgain33") || TaskState.ObjActive(Task, "PassportTanja52") || TaskState.ObjActive(Task, "TaxPay41"))
                return Diagnosis.Of(DiagState.NotAffected);
            if (TaskItemsManager.instance == null) return Diagnosis.Of(DiagState.CannotCheck, Strings.CouldNotRead("your task items"));
            if (TaskItemsManager.instance.PlayerHasTaskItemId("MiningPermit")) return Diagnosis.Of(DiagState.NotAffected);
            var talked = LuaRead.ActorBool(Tanja, "Event1");
            if (talked == null) return Diagnosis.Of(DiagState.CannotCheck, Strings.CouldNotRead("Tanja's Event1"));
            if (talked.Value) return Diagnosis.Of(DiagState.NotAffected);

            var d = new Diagnosis();
            d.Ev(Strings.PermitEvidence);
            if (TaskState.ObjCompleted(Task, "TaxPay41"))
            {
                d.State = DiagState.CannotFix;
                d.Why = Strings.PermitCannotFix;
                return d;
            }
            var points = LuaRead.ActorNum(Tanja, "Points");
            var age = LuaRead.ActorNum("Player", "Age");
            if (points == null || age == null) return Diagnosis.Of(DiagState.CannotCheck, Strings.CouldNotRead(points == null ? "Tanja's Points" : "your age"));
            d.Variant = points.Value >= 5 || age.Value > 30 || PassedPassportCheck() ? "tax" : "passport";
            d.State = DiagState.Stuck;
            d.Plan.Add(d.Variant == "passport" ? Strings.PermitPlanPassport : Strings.PermitPlanTax);
            return d;
        }

        // Tanja's talk already went past the passport question (see the header comment).
        private static bool PassedPassportCheck() =>
            TaskState.ObjAny(Task, "PassportTanja52") || TaskState.ObjCompleted(Task, "FillFormAgain33") ||
            (TaskItemsManager.instance != null && TaskItemsManager.instance.PlayerHasTaskItemId("ForgedPassport11"));

        public bool Prepare(Diagnosis d, Plan p)
        {
            p.Variant = d.Variant;
            p.Tsi = TaskState.Find(Task);
            if (p.Tsi == null) { p.Missing = "the \"Permission to dig\" task"; return false; }
            string path = d.Variant == "passport" ? PassportPath : TaxPath;
            string id = d.Variant == "passport" ? "PassportTanja52" : "TaxPay41";
            p.Obj1 = Edits.LoadObjective(path, id);
            if (p.Obj1 == null) { p.Missing = path; return false; }
            if (!Edits.CanActivate(p.Obj1, p.Tsi.task, p.Tsi)) { p.Missing = Strings.Requirements(p.Obj1.title); return false; }
            return true;
        }

        public void Apply(Plan p, List<string> log) => Edits.ActivateNew(p.Obj1, p.Tsi.task, false, log);

        public bool Verify(Plan p, List<string> log) => TaskState.ObjActive(Task, p.Obj1.Id);

        public string Snapshot(Plan p) =>
            $"Tanja.Points={LuaRead.Show(LuaRead.ActorNum(Tanja, "Points"))}, Age={LuaRead.Show(LuaRead.ActorNum("Player", "Age"))}, " +
            $"PassportTanja52 any={TaskState.ObjAny(Task, "PassportTanja52")}, FillFormAgain33 done={TaskState.ObjCompleted(Task, "FillFormAgain33")}, " +
            $"TaxPay41 any={TaskState.ObjAny(Task, "TaxPay41")}";
    }
}

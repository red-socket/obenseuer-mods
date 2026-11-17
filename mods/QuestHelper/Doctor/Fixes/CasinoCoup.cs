using System.Collections.Generic;

namespace QuestHelper
{
    // QD-CASINO: "The boss isn't expecting you" (#6071, spec §4.4, D17).
    // Heikki's 431:47 gives CasperTalk65 and CasperRid66. Casper's meeting (DB 435, "Mailanen Back") finishes CasperTalk65
    // at every ending: 435:25 (paid, task finished), 435:59 (proof route, CasperProof78), and 435:58 / 436:5, where he
    // sends the player away to get the money ("don't come back until you have the money!", reached from 57, 71 and 79;
    // the pocket check's "get out there and find the rest!"). Gérard (350:22) links 26, 28, 27, 23, 25 and the first
    // valid entry wins; 23 lets the player up only while CasperTalk65 is Active, so after 435:58 / 436:5 the office is
    // closed although Casper asked the player to come back, and the routes that need his office (paying him at 435:4,
    // the poisoned Socca Junior at 435:33) are gone. That is the stuck state; the fix re-opens the meeting.
    // Not stuck while another route is under way: SeamusRM10 Active (agreed to pay Seamus, 209:102) or CrazyBalcony23
    // Active (the Crazy Neighbor's balcony, 164:132); SeamusCasino99 / CrazyCasino99 / CasperReturn26 / CasperProof78
    // already exclude the later states. Entry 28 (Raimo knows the player) comes first and goes to security (V3), so
    // restoring the meeting would not help then (Cannot fix).
    // Sources: #6071; DB 350:22-28, 431:47, 435, 436; Casino chair &39488.
    internal sealed class CasinoCoup : IFix
    {
        private const string Task = "casinoCoup10";
        private const string Talk = "CasperTalk65";
        internal const string Raimo = "Bazaar_Casino_Raimo_Räyhä";

        public string Id => "QD-CASINO";
        public string Title => Strings.CasinoTitle;
        public string TaskId => Task;

        public Diagnosis Detect()
        {
            if (!TaskState.TaskActive(Task) || !TaskState.ObjActive(Task, "CasperRid66")) return Diagnosis.Of(DiagState.NotAffected);
            if (!TaskState.ObjCompleted(Task, Talk) || TaskState.ObjActive(Task, Talk)) return Diagnosis.Of(DiagState.NotAffected);
            // The fingers path (CasperProof78) is "To be continued", not stuck.
            if (TaskState.ObjAny(Task, "CasperProof78")) return Diagnosis.Of(DiagState.NotAffected);
            if (TaskState.ObjAny(Task, "CrazyCasino99") || TaskState.ObjAny(Task, "SeamusCasino99") || TaskState.ObjActive(Task, "RewardMailanen65"))
                return Diagnosis.Of(DiagState.NotAffected);
            if (TaskState.ObjAny("BankHunt77", "CasperReturn26")) return Diagnosis.Of(DiagState.NotAffected);
            // Another route is under way: the player chose it, and Casper's office isn't needed for it.
            if (TaskState.ObjActive(Task, "SeamusRM10") || TaskState.ObjActive(Task, "CrazyBalcony23")) return Diagnosis.Of(DiagState.NotAffected);
            var casper = LuaRead.VarIs("CasinoState", "Casper");
            if (casper == null) return Diagnosis.Of(DiagState.CannotCheck, Strings.CouldNotRead("CasinoState"));
            if (casper.Value) return Diagnosis.Of(DiagState.NotAffected);

            var d = new Diagnosis();
            d.Ev(Strings.CasinoEvidence);
            if (LuaRead.ActorBool(Raimo, "KnowsPlayer") == true)
            {
                d.State = DiagState.CannotFix;
                d.Why = Strings.CasinoCannotFix;
                return d;
            }
            d.State = DiagState.Stuck;
            d.Plan.Add(Strings.CasinoPlan);
            return d;
        }

        public bool Prepare(Diagnosis d, Plan p)
        {
            p.Tsi = TaskState.Find(Task);
            p.Osi = TaskState.FindObj(p.Tsi, Talk);
            if (p.Tsi == null || p.Osi == null) { p.Missing = "the \"Talk to Casper\" objective"; return false; }
            return true;
        }

        public void Apply(Plan p, List<string> log) => Edits.ReactivateObjective(p.Tsi, p.Osi, log);

        public bool Verify(Plan p, List<string> log)
        {
            var tc = TaskController.instance;
            return TaskState.ObjActive(Task, Talk) && !TaskState.ObjCompleted(Task, Talk) && tc != null &&
                   TaskState.Count(tc.activeObjectives, Task + "_" + Talk) == 1;
        }

        public string Snapshot(Plan p)
        {
            var tc = TaskController.instance;
            string key = Task + "_" + Talk;
            return $"{Talk} {p.Osi?.status}, active x{TaskState.Count(tc?.activeObjectives, key)}, completed x{TaskState.Count(tc?.completedObjectives, key)}";
        }
    }
}

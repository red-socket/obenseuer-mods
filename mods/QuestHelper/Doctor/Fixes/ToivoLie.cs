using System.Collections.Generic;

namespace QuestHelper
{
    // QD-TOIVO: "not MY tenement" locks Collapse hazard (spec §4.4).
    // Toivo 224:50 (Event1) sets Toivo.Event4 and skips 224:41, the only activator of CollapseHazard11/Nakahara43, which
    // Ken (223:4) needs. The vanilla way back: Tor 340:49 -> 48 sets Tor.Event1 when Toivo.Event4 is true, then Toivo
    // 224:128 -> 129-132 -> 40 -> 41. Tor's 340:49 is only reachable through BazaarPermit99/110, so it is gone once
    // SurfaceElevator99 is finished. The fix does exactly what 340:48 does.
    // Sources: DB 224:41/50/128-132, 223:4, 340:48-49.
    internal sealed class ToivoLie : IFix
    {
        internal const string Toivo = "Mines_Toivo_Malmijoki";
        internal const string Tor = "Bazaar_Industrial_Tor_Bergstahl";

        public string Id => "QD-TOIVO";
        public string Title => Strings.ToivoTitle;
        public string TaskId => "CollapseHazard11";

        public Diagnosis Detect()
        {
            var lied = LuaRead.ActorBool(Toivo, "Event4");
            if (lied == null) return Diagnosis.Of(DiagState.CannotCheck, Strings.CouldNotRead("Toivo's Event4"));
            if (!lied.Value || TaskState.ObjAny("CollapseHazard11", "Nakahara43")) return Diagnosis.Of(DiagState.NotAffected);
            var d = new Diagnosis();
            d.Ev(Strings.ToivoEvidence);
            if (LuaRead.ActorBool(Tor, "Event1") == true)
            {
                d.State = DiagState.OkForNow;
                d.Why = Strings.ToivoOkTalk;
                return d;
            }
            if (!TaskState.TaskFinished("SurfaceElevator99"))
            {
                d.State = DiagState.OkForNow;
                d.Why = Strings.ToivoOkTor;
                return d;
            }
            d.State = DiagState.Stuck;
            d.Plan.Add(Strings.ToivoPlan);
            return d;
        }

        public bool Prepare(Diagnosis d, Plan p) => true;

        public void Apply(Plan p, List<string> log) => Edits.SetActorField(Tor, "Event1", true, log);

        public bool Verify(Plan p, List<string> log) => LuaRead.ActorBool(Tor, "Event1") == true;

        public string Snapshot(Plan p) =>
            $"Toivo.Event4={LuaRead.Show(LuaRead.ActorBool(Toivo, "Event4"))}, Tor.Event1={LuaRead.Show(LuaRead.ActorBool(Tor, "Event1"))}";
    }
}

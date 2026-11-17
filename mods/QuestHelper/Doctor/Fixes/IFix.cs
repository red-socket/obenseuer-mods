using System.Collections.Generic;
using UnityEngine;

namespace QuestHelper
{
    // Finding states (spec §4.2.1).
    internal enum DiagState { NotAffected, Stuck, CheckThere, OkForNow, CannotFix, CannotCheck }

    internal sealed class Diagnosis
    {
        internal DiagState State;
        internal string Variant = "";
        internal readonly List<string> Evidence = new List<string>();
        internal readonly List<string> Plan = new List<string>();
        internal string Why;

        internal static Diagnosis Of(DiagState s, string why = null) => new Diagnosis { State = s, Why = why };

        internal Diagnosis Ev(string line)
        {
            Evidence.Add(line);
            return this;
        }
    }

    // Everything a fix resolved in Prepare (spec §4.3): assets, task records, scene objects. Apply only uses these, so a
    // miss always aborts before the backup and before any change.
    internal sealed class Plan
    {
        internal string Variant;
        internal TaskStatusInfo Tsi;
        internal ObjectiveStatusInfo Osi;
        internal int Amount, Before;
        internal GameObjectStateSave SaverOn, SaverOff;
        internal GameObject Watch;
        internal TaskObjective Obj1, Obj2;
        internal TaskItem Item;
        internal string Missing;
    }

    // One curated softlock fix (spec §4.3). Detect, Prepare and Verify are read-only; Apply changes state only through
    // Edits. Fixes are typed classes, not a predicate language (Design A).
    internal interface IFix
    {
        string Id { get; }
        string Title { get; }
        // The task whose journal rows get the "Quest Helper can fix" line and whose Tasks-page hint gets "(!)".
        string TaskId { get; }
        Diagnosis Detect();
        bool Prepare(Diagnosis d, Plan p);
        void Apply(Plan p, List<string> log);
        bool Verify(Plan p, List<string> log);
        // One line for the audit log: the values the fix reads and changes.
        string Snapshot(Plan p);
    }
}

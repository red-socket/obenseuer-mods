namespace MessageLog
{
    // One logged message (spec §5.1). The first block is what the side-car stores; the rest is rebuilt in memory.
    internal sealed class Entry
    {
        internal const byte FlagSilent = 1, FlagEstimated = 4;

        // ---- Persisted ----
        internal long Seq;              // strictly increasing in a lineage: the order key (game time can go backwards)
        internal char Kind;             // 'n' notification, 'd' NPC line, 'c' player choice, 'g' gap marker
        internal int Day;               // TimeAndDay.currentDay (the day counter)
        internal short Hm = -1;         // minutes since midnight; -1 until stamped
        internal sbyte Wd;              // weekday, Mon = 0
        internal byte Flags;
        internal int Count = 1;         // merged repeats (D12)
        internal string Head = "";      // raw notification type, or the speaker as shown; empty for 'c' and 'g'
        internal string Text = "";      // raw notification text, or stripped dialogue text

        // ---- Memory only ----
        internal float Real = float.NegativeInfinity;   // realtimeSinceStartup of the last merge; never merges across loads
        internal string Line;           // the pre-serialized side-car line (SideCar.Format), rebuilt on merge
        internal int Bytes;             // UTF-8 size of Line plus its newline

        // Display caches (LogModel), dropped when the PT-BR state flips. BaseGen: category and search key, built in the
        // rebuild. Gen: the display text, built in the rebuild without PT-BR, otherwise in LogModel's translation pass or
        // when the row is drawn.
        internal int Gen = -1, BaseGen = -1;
        internal Cat Cat;
        internal string ShownHead, ShownText, RowText, Folded;

        internal bool IsTalk => Kind == 'd' || Kind == 'c';
        internal bool Timed => Hm >= 0;
        internal int GameMinute => Day * 1440 + Hm;
    }

    // A save the log knew about (spec §7.2, D15): the raw Info.tnmt "Date" of that save and the last Seq it contained.
    internal sealed class Checkpoint
    {
        internal string Info;
        internal long Seq;
        internal int Day;
        internal short Hm;
    }
}

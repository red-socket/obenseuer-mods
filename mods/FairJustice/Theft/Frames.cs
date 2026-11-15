using System.Collections.Generic;

namespace FairJustice
{
    // One add to the player's slots in progress (P2/P3): what it adds, where, and each candidate receiver's stack as it
    // was before the add. P1 sets tagStolen; the add's postfix then marks the stacks that grew.
    internal sealed class AddFrame
    {
        internal Item item;
        internal int owner;
        internal SlotController target;
        internal bool tagStolen;
        internal MoveFrame ownMove;   // P2: the own move this add belongs to, when P2 took over its marking
        internal int n;
        internal SlotController[] slots = new SlotController[96];
        internal int[] ids = new int[96];
        internal int[] amounts = new int[96];

        internal void Reset(Item i, int o, SlotController t)
        {
            item = i;
            owner = o;
            target = t;
            tagStolen = false;
            ownMove = null;
            n = 0;
        }

        internal void Snapshot(SlotController s, SlotController skip = null)
        {
            if (s == null || ReferenceEquals(s, skip) || s.itemStack == null) return;
            if (n == slots.Length)
            {
                System.Array.Resize(ref slots, n * 2);
                System.Array.Resize(ref ids, n * 2);
                System.Array.Resize(ref amounts, n * 2);
            }
            slots[n] = s;
            ids[n] = s.itemStack.itemId;
            amounts[n] = s.itemStack.itemAmount;
            n++;
        }

        internal void Snapshot(SlotController[] arr, SlotController skip = null)
        {
            if (arr == null) return;
            foreach (var s in arr) Snapshot(s, skip);
        }

        // Stacks that received items (new id or more of it), of this owner, without permission. Comparing amounts
        // rather than the return value covers partial fills; the id test covers the empty liquid container swap.
        internal int TagReceivers()
        {
            int tagged = 0;
            for (int i = 0; i < n; i++)
            {
                var st = slots[i]?.itemStack;
                if (st == null || st.itemAmount <= 0) continue;
                if (st.itemId == ids[i] && st.itemAmount <= amounts[i]) continue;
                if (st.ownerId != owner || Owned.HasPermission(st.Meta)) continue;
                if (Owned.Tag(st)) tagged++;
            }
            return tagged;
        }

        internal void Clear()
        {
            item = null;
            target = null;
            ownMove = null;
            for (int i = 0; i < n; i++) slots[i] = null;
            n = 0;
        }
    }

    // One move whose source is the player's own slot (P4-P6).
    internal sealed class MoveFrame
    {
        internal int itemId;
        internal int owner;
        internal string kind;
        internal bool skipped;
    }

    // Main-thread stacks with pooled frames (spec §2.5). Moves are synchronous, so a frame left open between two
    // Update calls is always a leak; Plugin.Update clears it (a leaked MoveFrame would make outside theft free).
    internal static class AddFrames
    {
        private static readonly List<AddFrame> Active = new List<AddFrame>();
        private static readonly Stack<AddFrame> Pool = new Stack<AddFrame>();

        internal static int Count => Active.Count;
        internal static AddFrame Top => Active.Count > 0 ? Active[Active.Count - 1] : null;

        internal static AddFrame Push(Item item, int owner, SlotController target)
        {
            var f = Pool.Count > 0 ? Pool.Pop() : new AddFrame();
            f.Reset(item, owner, target);
            Active.Add(f);
            return f;
        }

        // Removes f and anything opened after it (those would be leaks); no-op when f is not open.
        internal static void Pop(AddFrame f)
        {
            int i = Active.LastIndexOf(f);
            if (i < 0) return;
            for (int k = Active.Count - 1; k >= i; k--)
            {
                var x = Active[k];
                Active.RemoveAt(k);
                x.Clear();
                Pool.Push(x);
            }
        }

        internal static bool IsOpen(AddFrame f) => f != null && Active.LastIndexOf(f) >= 0;

        internal static void Clear()
        {
            while (Active.Count > 0) Pop(Active[0]);
        }
    }

    internal static class MoveFrames
    {
        private static readonly List<MoveFrame> Active = new List<MoveFrame>();
        private static readonly Stack<MoveFrame> Pool = new Stack<MoveFrame>();

        internal static int Count => Active.Count;
        internal static MoveFrame Top => Active.Count > 0 ? Active[Active.Count - 1] : null;

        // The innermost own move, if it is moving this item of this owner. A nested add of something else during an
        // own move is not exempted.
        internal static MoveFrame Match(Item item, int owner)
        {
            var mf = Top;
            return mf != null && item != null && mf.itemId == item.ID && mf.owner == owner ? mf : null;
        }

        internal static MoveFrame Push(int itemId, int owner, string kind)
        {
            var f = Pool.Count > 0 ? Pool.Pop() : new MoveFrame();
            f.itemId = itemId;
            f.owner = owner;
            f.kind = kind;
            f.skipped = false;
            Active.Add(f);
            return f;
        }

        internal static void Pop(MoveFrame f)
        {
            int i = Active.LastIndexOf(f);
            if (i < 0) return;
            for (int k = Active.Count - 1; k >= i; k--)
            {
                Pool.Push(Active[k]);
                Active.RemoveAt(k);
            }
        }

        internal static bool IsOpen(MoveFrame f) => f != null && Active.LastIndexOf(f) >= 0;

        internal static void Clear()
        {
            while (Active.Count > 0) Pop(Active[0]);
        }
    }

    internal static class Frames
    {
        // Plugin.Update and LoadingDone.
        internal static void Check(string where)
        {
            if (AddFrames.Count == 0 && MoveFrames.Count == 0) return;
            Log.WarnOnce("frame leak", $"Fair Justice: {AddFrames.Count + MoveFrames.Count} item-move frame(s) were left open ({where}); cleared. " +
                                       "Another mod may have thrown inside an item move.");
            AddFrames.Clear();
            MoveFrames.Clear();
        }
    }
}

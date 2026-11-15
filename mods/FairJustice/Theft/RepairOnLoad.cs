using System;

namespace FairJustice
{
    // Optional repair (spec §3.1 D, off by default). No patch: SaveController.LoadingDone runs this after every load,
    // including each door. It adds the game's stolen mark to every carried stack the game itself counts as stolen
    // (Inventory.GetStolenItems plus the permission exclusion of Crime.cs:402-410) that lacks it. It only ever adds
    // marks, commits no crime (crime never reads the mark) and is idempotent.
    internal static class RepairOnLoad
    {
        internal static void Run()
        {
            try
            {
                Frames.Check("load");
                if (!Cfg.RepairOnLoad.Value) return;
                var inv = Inventory.instance;
                if (inv == null) return;
                int n = 0;
                foreach (var s in inv.AllSlots())
                {
                    var st = s?.itemStack;
                    if (st == null || st.itemAmount <= 0 || st.ownerId <= 1 || st.itemId < 0) continue;
                    var meta = st.Meta;
                    if (Owned.HasPermission(meta) || Owned.IsStolenTagged(meta)) continue;
                    if (Owned.Tag(st))
                    {
                        n++;
                        s.Update();
                    }
                }
                if (n > 0) Log.Info($"RepairOnLoad: tagged {n} stolen stacks.");
            }
            catch (Exception e) { Log.Exception("RepairOnLoad", e); }
        }
    }
}

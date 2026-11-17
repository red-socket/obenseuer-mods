using UnityEngine;

namespace GreenThumb
{
    // S-044a (spec §4, D1, D2). Once a fruit plant is grown, grow.GetCurrentStageProcess returns 1 - the plant's spoil
    // time (SRC/grow.cs:582-593) while GetCurrentStage calls that phase GrowingFruits (:595-610), so "Growing fruits"
    // counts the plant's remaining life down. Evaluate reads the fruit-cycle progress instead: 100% when any fruit is
    // ready (the panel's ready badge test, grow.VisibleItemsReadyToHarvest :1043-1055), otherwise the most advanced
    // fruit. It allocates nothing and never reads visibleGrownItemsAreGrowing, which is false after a load and while paused.
    internal static class FruitProgress
    {
        // The last evaluation made by G1, handed to G2 when G2 calls the patched GetCurrentStage on the same plant right
        // before it (nothing runs in between). Cleared by G2 before each use, so it never serves a stale value.
        private static grow _last;
        private static bool _lastRipe, _lastCanGrow;
        private static float _lastBest;

        internal static void Evaluate(grow g, out bool anyRipe, out bool anyCanGrow, out float best)
        {
            anyRipe = anyCanGrow = false;
            best = 0f;
            var items = g.visibleGrownItems;
            if (items == null) return;
            for (int i = 0; i < items.Length; i++)
            {
                var f = items[i];
                if (f == null) continue;
                int full = Vanilla.FullGrownAgeFinal(f), wait = Vanilla.RandomGrowStartTime(f);
                float p;
                if (f.gameObject.activeInHierarchy)
                {
                    if (f.readyToPick)
                    {
                        if (!f.Spoiled()) anyRipe = true;
                        continue;   // a spoiled leftover (hops) is neither ripe nor growing
                    }
                    // A regrown fruit keeps its wait time until ResetGrow (SRC/GrowItem.cs:151-173, :331-341).
                    int w = wait > 0 && f.currentWaitTime >= wait ? wait : 0;
                    int d = w + full;
                    if (d <= 0) continue;
                    p = (w + Mathf.Min(f.age, full)) / (float)d;
                }
                else if (!f.newGrowDisabled)
                {
                    // GrowingFruits already implies !growSpoil.StopGrow(), so a hidden fruit is waiting to appear.
                    int d = wait + full;
                    if (d <= 0) continue;
                    p = Mathf.Min(f.currentWaitTime, wait) / (float)d;
                }
                else continue;   // never grows again (Cucumber, Peas)
                anyCanGrow = true;
                p = Mathf.Clamp01(p);
                if (p > best) best = p;
            }
        }

        internal static void Remember(grow g, bool anyRipe, bool anyCanGrow, float best)
        {
            _last = g;
            _lastRipe = anyRipe;
            _lastCanGrow = anyCanGrow;
            _lastBest = best;
        }

        internal static void Forget() => _last = null;

        internal static bool Recall(grow g, out bool anyRipe, out bool anyCanGrow, out float best)
        {
            anyRipe = _lastRipe;
            anyCanGrow = _lastCanGrow;
            best = _lastBest;
            bool hit = !ReferenceEquals(_last, null) && ReferenceEquals(_last, g);
            _last = null;
            return hit;
        }
    }
}

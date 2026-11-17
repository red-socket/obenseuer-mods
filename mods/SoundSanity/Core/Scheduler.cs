using System.Collections.Generic;
using UnityEngine;

namespace SoundSanity
{
    // Realtime queue for routing sweeps (spec §3.3), ticked from Plugin.Update, which also runs while the game is paused.
    // No coroutines and no scaled time. Two kinds of work:
    //  - settle sweeps: after a delay, optionally counted from the moment the level is "ready" (no save is loading and the
    //    loading screen reports LevelLoadDone); entries that fall due in the same tick run as one sweep;
    //  - deferred root sweeps: a duplicate-only re-sweep of a root two frames after it was set up, for sources added in Start.
    internal static class Scheduler
    {
        private const float ReadyTimeout = 30f;

        private sealed class Settle
        {
            internal float Delay, Queued, Start = -1f;
            internal bool NeedsReady;
        }

        private static readonly List<Settle> Settles = new List<Settle>();
        private static readonly List<(GameObject root, int frame, string where)> Roots = new List<(GameObject, int, string)>();
        private static readonly HashSet<GameObject> QueuedRoots = new HashSet<GameObject>();

        internal static void QueueSettle(float delay, bool needsReady)
        {
            foreach (var s in Settles)
                if (s.NeedsReady == needsReady && s.Start < 0f && Mathf.Abs(s.Delay - delay) < 0.05f) return;
            Settles.Add(new Settle { Delay = delay, NeedsReady = needsReady, Queued = Time.realtimeSinceStartup });
        }

        internal static void QueueRoot(GameObject root, string where)
        {
            if (root == null || !QueuedRoots.Add(root)) return;
            Roots.Add((root, Time.frameCount + 2, where));
        }

        internal static bool Ready =>
            !SaveController.Loading && (LoadingScreen.instance == null || LoadingScreen.instance.LevelLoadDone);

        internal static void Tick()
        {
            if (Roots.Count > 0) TickRoots();
            if (Settles.Count == 0) return;
            float now = Time.realtimeSinceStartup;
            bool due = false, ready = false, readyKnown = false;
            for (int i = Settles.Count - 1; i >= 0; i--)
            {
                var s = Settles[i];
                if (s.Start < 0f)
                {
                    if (s.NeedsReady)
                    {
                        if (!readyKnown) { ready = Ready; readyKnown = true; }
                        if (ready || now - s.Queued > ReadyTimeout) s.Start = now;
                    }
                    else s.Start = s.Queued;
                }
                if (s.Start >= 0f && now >= s.Start + s.Delay)
                {
                    due = true;
                    Settles.RemoveAt(i);
                }
            }
            if (due) Sweeps.SweepSettle();
        }

        private static void TickRoots()
        {
            int frame = Time.frameCount;
            for (int i = Roots.Count - 1; i >= 0; i--)
            {
                var r = Roots[i];
                if (r.root != null && frame < r.frame) continue;
                Roots.RemoveAt(i);
                QueuedRoots.Remove(r.root);
                if (r.root != null) Sweeps.SweepRoot(r.root, duplicateOnly: true, r.where + " +2 frames");
            }
        }
    }
}

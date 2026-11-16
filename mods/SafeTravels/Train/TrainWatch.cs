using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafeTravels
{
    // What the train fixes need to know about the scene (spec §3.8-3.9, §4.5, §4.8): which trains are still loading
    // (P8 adds them, P9 removes them at the end of OnLoadingGameDelay), when the scene settled, which train the player
    // drives or drove last, and the coupler-graph component of that train (the "driven set").
    internal static class TrainWatch
    {
        private const float PendingTimeout = 30f;
        private const float FallbackSettleSeconds = 5f;
        private const int MaxDriven = 32;

        internal static bool Patched;

        internal static readonly Dictionary<TrackTrain, float> Pending = new Dictionary<TrackTrain, float>();

        private static float _firstFrameAt = float.PositiveInfinity;    // unscaled; +inf until the level runs
        private static float _pendingDoneAt = float.NegativeInfinity;   // unscaled
        private static float _loadingDoneAt = float.NegativeInfinity;   // unscaled, for the unpatched fallback
        private static bool _waitingFirstFrame;

        private static TrackTrain _prevCurrent;
        private static WeakReference _lastDriven;
        internal static float LastRideEnd = float.NegativeInfinity;      // Time.time

        private static readonly HashSet<TrackTrain> Driven = new HashSet<TrackTrain>();
        private static readonly Queue<TrackTrain> Bfs = new Queue<TrackTrain>();
        private static int _drivenFrame = -1;

        // The later of the end of the last pending load and the first running frame of the level.
        internal static float SettledAt => Mathf.Max(_firstFrameAt, _pendingDoneAt);

        internal static TrackTrain LastDriven
        {
            get
            {
                var t = _lastDriven?.Target as TrackTrain;
                return t != null ? t : null;
            }
        }

        internal static void OnSceneLoaded()
        {
            Pending.Clear();
            _lastDriven = null;
            _prevCurrent = null;
            LastRideEnd = float.NegativeInfinity;
            _firstFrameAt = float.PositiveInfinity;
            _pendingDoneAt = float.NegativeInfinity;
            _waitingFirstFrame = true;
            _drivenFrame = -1;
        }

        internal static void OnLoadingDone() => _loadingDoneAt = Time.unscaledTime;

        // P9: one train finished OnLoadingGameDelay.
        internal static void Finished(TrackTrain t)
        {
            if (Pending.Remove(t) && Pending.Count == 0)
            {
                _pendingDoneAt = Time.unscaledTime;
                Consist.Dirty = true;
            }
        }

        // Spec §3.9. Without the trainwatch patches: 5 s after LoadingDone with the level running.
        internal static bool Settled
        {
            get
            {
                if (SaveController.Loading || Changelevel.ChangeLevelActive || TrainController.instance == null) return false;
                if (Patched) return Pending.Count == 0;
                var tod = TimeOfDayAzure.instance;
                return tod != null && !tod.updateTimeDisabled && Time.unscaledTime - _loadingDoneAt >= FallbackSettleSeconds;
            }
        }

        // P3/P4: automatic coupling is refused while loading, while any train is still loading and for LoadGraceSeconds
        // after the scene settled.
        internal static bool CouplingAllowedNow(out string why)
        {
            why = null;
            if (SaveController.Loading || Changelevel.ChangeLevelActive) { why = "loading"; return false; }
            if (Pending.Count > 0) { why = "trains still loading"; return false; }
            if (Time.unscaledTime - SettledAt < Cfg.Coupling.LoadGraceSeconds.Value) { why = "load grace"; return false; }
            if (!Patched && !Settled) { why = "loading"; return false; }
            return true;
        }

        // Plugin.Update.
        internal static void Tick()
        {
            if (_waitingFirstFrame)
            {
                var tod = TimeOfDayAzure.instance;
                if (tod != null && !tod.updateTimeDisabled)
                {
                    _waitingFirstFrame = false;
                    _firstFrameAt = Time.unscaledTime;
                }
            }

            if (Pending.Count > 0) Prune();

            var tc = TrainController.instance;
            var cur = tc != null ? tc.CurrentTrain() : null;
            if (cur == null && _prevCurrent != null)
            {
                _lastDriven = new WeakReference(_prevCurrent);
                LastRideEnd = Time.time;
            }
            else if (cur != null && cur != _prevCurrent) _lastDriven = new WeakReference(cur);
            _prevCurrent = cur;
        }

        private static readonly List<TrackTrain> Drop = new List<TrackTrain>();

        private static void Prune()
        {
            Drop.Clear();
            float now = Time.realtimeSinceStartup;
            foreach (var kv in Pending)
                if (kv.Key == null || now - kv.Value > PendingTimeout) Drop.Add(kv.Key);
            if (Drop.Count == 0) return;
            int timedOut = 0;
            foreach (var t in Drop)
            {
                if (!ReferenceEquals(t, null) && t != null) timedOut++;
                Pending.Remove(t);
            }
            if (timedOut > 0) Log.Warn($"Minitrain: {timedOut} trains did not finish loading in {PendingTimeout:0} s; carrying on.");
            if (Pending.Count == 0)
            {
                _pendingDoneAt = Time.unscaledTime;
                Consist.Dirty = true;
            }
        }

        // The train the player drives, or the one they left within CoastSeconds, plus everything coupled to it (by
        // coupler links or by its wagon list, so a ghost-split consist still counts). Cached per frame.
        internal static HashSet<TrackTrain> DrivenSet()
        {
            if (_drivenFrame == Time.frameCount) return Driven;
            _drivenFrame = Time.frameCount;
            Driven.Clear();
            var tc = TrainController.instance;
            var start = tc != null ? tc.CurrentTrain() : null;
            if (start == null)
            {
                var last = LastDriven;
                if (last != null && Time.time - LastRideEnd <= Cfg.Coupling.CoastSeconds.Value) start = last;
            }
            if (start == null) return Driven;
            Component(start, Driven);
            if (start.train?.wagons != null)
                foreach (var w in start.train.wagons)
                    if (w != null && w.wagon != null && Driven.Count < MaxDriven) Driven.Add(w.wagon);
            return Driven;
        }

        // The coupler-graph component of t (coupledTo.parent links), capped at MaxDriven trains.
        internal static void Component(TrackTrain t, HashSet<TrackTrain> into)
        {
            Bfs.Clear();
            if (t == null || !into.Add(t)) return;
            Bfs.Enqueue(t);
            while (Bfs.Count > 0 && into.Count < MaxDriven)
            {
                var n = Bfs.Dequeue();
                Visit(n.frontCoupler, into);
                Visit(n.backCoupler, into);
            }
        }

        private static void Visit(TrackTrain.TrainCoupler c, HashSet<TrackTrain> into)
        {
            var other = c?.coupledTo?.parent;
            if (other != null && into.Count < MaxDriven && into.Add(other)) Bfs.Enqueue(other);
        }

        // P3: a or b belongs to the driven set.
        internal static bool Deliberate(TrackTrain a, TrackTrain b)
        {
            var set = DrivenSet();
            return set.Contains(a) || set.Contains(b);
        }
    }
}

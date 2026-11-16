using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafeTravels
{
    // Rerail (S-021 d, spec §4.8): the game's own TrainController.ResetTrainPosition on one locomotive, in place.
    // ResetTrackTrain keeps the current segment's GUID (it copies only the authored segment reference, then re-resolves
    // the GUID, SRC/TrackTrain.cs:1805-1847, Track_Segment.cs:62-70) but puts both axles back to the original offsets
    // and directions: the authored ones after a load, 0/Forward for a train bought this session. That would move the
    // train up to a segment length for free, possibly onto its own wagons. So for the duration of the call the original*
    // fields hold the axles' current values (ResetInPlace): the train is placed from its front axle where it stands, and
    // RepairPlacementIfInvalid still snaps an out-of-range axle to the nearest segment by projection. The target's own
    // two couplers are released first (UncoupleWagons), so wagons and cargo stay where they are instead of being pulled
    // along. No fuel change; no other train is touched.
    internal static class Rerail
    {
        private const float StillSpeed = 0.1f;

        internal static bool Available;

        private static float _lastUsed = float.NegativeInfinity;   // Time.realtimeSinceStartup
        private static int _pendingFrame = -1;
        private static readonly HashSet<TrackTrain> Component = new HashSet<TrackTrain>();

        internal static bool On => Available && Cfg.Rerail.Enabled.Value;

        internal static int CooldownLeft
        {
            get
            {
                float left = Cfg.Rerail.CooldownSeconds.Value - (Time.realtimeSinceStartup - _lastUsed);
                return left > 0f ? Mathf.CeilToInt(left) : 0;
            }
        }

        internal static void OnSceneLoaded() => _pendingFrame = -1;

        internal static void QueueFromPause() => _pendingFrame = Time.frameCount;

        internal static void Tick()
        {
            if (_pendingFrame >= 0 && Time.frameCount > _pendingFrame)
            {
                _pendingFrame = -1;
                if (On) Request(fromPause: true);
                return;
            }
            if (On && Keys.Down(Cfg.Rerail.Hotkey)) Request(fromPause: false);
        }

        private static GateResult Gate()
        {
            var core = PlayGate.Core(needTrains: true);
            if (core.Blocked) return core;
            var free = PlayGate.Free();
            if (free.Blocked) return free;
            string busy = PlayGate.Busy();
            if (busy != null) return GateResult.Note(Strings.Busy, busy);
            if (PlayGate.Riding) return GateResult.Note(Strings.OffTrain, "riding");
            if (InteractableChair.PlayerIsSitting) return GateResult.Note(Strings.Seated, "seated");
            if (!TrainWatch.Settled) return GateResult.Silent("trains loading");
            if (PlayGate.Jailed) return GateResult.Note(Strings.Jail, "jail");
            if (PlayGate.Police) return GateResult.Note(Strings.Police, "police");
            int cd = CooldownLeft;
            if (cd > 0) return GateResult.Note(Strings.RerailCooldown(cd), "cooldown");
            return GateResult.Clear;
        }

        private static void Request(bool fromPause)
        {
            try
            {
                var g = Gate();
                if (g.Blocked) { Refuse(g, fromPause); return; }

                var target = FindTarget(crosshair: !fromPause, out bool aimedAtWagon);
                if (target == null)
                {
                    Refuse(aimedAtWagon ? GateResult.Note(Strings.LookAtLoco, "wagon, no locomotive in range") : GateResult.Note(Strings.NoTrain, "no train"), fromPause);
                    return;
                }
                if (target.trainType != TrackTrain.Type.Locomotive && !Cfg.Rerail.AllowWagons.Value)
                {
                    Refuse(GateResult.Note(Strings.LookAtLoco, "wagon"), fromPause);
                    return;
                }
                if (Mathf.Abs(target.currentSpeed) > StillSpeed)
                {
                    Refuse(GateResult.Note(Strings.Moving, $"moving ({target.currentSpeed:0.00} m/s)"), fromPause);
                    return;
                }
                if (target.active) { Refuse(GateResult.Note(Strings.EngineOn, "engine on"), fromPause); return; }
                if (StandingOn(target)) { Refuse(GateResult.Note(Strings.OnTrain, "standing on it"), fromPause); return; }

                var before = target.transform.position;
                bool had = Coupled(target.frontCoupler) || Coupled(target.backCoupler);
                target.UncoupleWagons();
                ResetInPlace(target);
                Consist.Dirty = true;
                _lastUsed = Time.realtimeSinceStartup;
                Game.Notify(had ? Strings.RerailedUncoupled : Strings.Rerailed);
                Log.Info($"Rerail: {target.GUID} moved {Vector3.Distance(before, target.transform.position):0.0} m{(had ? ", wagons uncoupled" : "")} ({Game.SceneName})");
            }
            catch (Exception e) { Log.Exception("rerail", e); }
        }

        private static void Refuse(GateResult g, bool fromPause)
        {
            Log.Verbose($"Rerail: refused ({g.Reason})");
            if (g.Message == null) return;
            Game.Notify(g.Message);
            if (!fromPause) Game.Warning();
        }

        private static bool Coupled(TrackTrain.TrainCoupler c) => c != null && c.coupledTo != null;

        // ResetTrainPosition with the original* offsets set to the axles' current ones, restored afterwards so the
        // console's reset_train_position and anything else calling ResetTrackTrain later behave as in vanilla.
        private static void ResetInPlace(TrackTrain t)
        {
            var fa = t.frontAxle;
            var ba = t.backAxle;
            if (fa == null || ba == null)
            {
                TrainController.instance.ResetTrainPosition(t);   // ResetTrackTrain does nothing without both axles
                return;
            }
            float fp = Vanilla.TrainOrigFrontPos(t), bp = Vanilla.TrainOrigBackPos(t);
            var fd = Vanilla.TrainOrigFrontDir(t);
            var bd = Vanilla.TrainOrigBackDir(t);
            Vanilla.TrainOrigFrontPos(t) = fa.positionOnSegment;
            Vanilla.TrainOrigBackPos(t) = ba.positionOnSegment;
            Vanilla.TrainOrigFrontDir(t) = fa.directionOnSegment;
            Vanilla.TrainOrigBackDir(t) = ba.directionOnSegment;
            try { TrainController.instance.ResetTrainPosition(t); }
            finally
            {
                Vanilla.TrainOrigFrontPos(t) = fp;
                Vanilla.TrainOrigBackPos(t) = bp;
                Vanilla.TrainOrigFrontDir(t) = fd;
                Vanilla.TrainOrigBackDir(t) = bd;
            }
        }

        // The crosshair target (hotkey only) when it is a locomotive, or any train with AllowWagons; else the last
        // driven train in range, else the nearest locomotive in range. A wagon under the crosshair with AllowWagons off
        // falls through to those, and sets aimedAtWagon so a miss can say why.
        internal static TrackTrain FindTarget(bool crosshair, out bool aimedAtWagon)
        {
            aimedAtWagon = false;
            var tc = TrainController.instance;
            var pl = PlayerLocator.instance;
            if (tc == null || pl == null) return null;
            if (crosshair && InteractObjects.instance != null)
            {
                var go = InteractObjects.instance.currentGameObject;
                var aimed = go != null ? go.GetComponentInParent<TrackTrain>() : null;
                if (aimed != null)
                {
                    if (aimed.trainType == TrackTrain.Type.Locomotive || Cfg.Rerail.AllowWagons.Value) return aimed;
                    aimedAtWagon = true;
                }
            }
            var p = pl.transform.position;
            float max = Cfg.Rerail.MaxDistance.Value;
            var last = TrainWatch.LastDriven;
            if (last != null && last.gameObject.scene.isLoaded && tc.GetTrackTrain(last.GUID) == last
                && Vector3.Distance(last.transform.position, p) <= max)
                return last;
            TrackTrain best = null;
            float bestD = max;
            foreach (var t in tc.TrackTrains.Values)
            {
                if (t == null || t.trainType != TrackTrain.Type.Locomotive) continue;
                float d = Vector3.Distance(t.transform.position, p);
                if (d <= bestD) { bestD = d; best = t; }
            }
            return best;
        }

        // The ground under the player belongs to the target or anything coupled to it.
        private static bool StandingOn(TrackTrain target)
        {
            var occ = OpenSewerCharacterController.instance;
            var col = occ != null && occ.movement != null ? occ.movement.groundCollider : null;
            var under = col != null ? col.GetComponentInParent<TrackTrain>() : null;
            if (under == null) return false;
            Component.Clear();
            TrainWatch.Component(target, Component);
            return Component.Contains(under);
        }
    }
}

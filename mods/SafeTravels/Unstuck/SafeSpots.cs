using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SafeTravels
{
    // The safe-spot recorder (S-030, spec §4.2): a ring of the last 64 places where the player stood on solid, still
    // ground with full control, newest first. Memory only; cleared on every scene load, load and level change, and
    // whenever the player is in jail, under arrest or inside the toll area.
    internal static class SafeSpots
    {
        internal struct Sample
        {
            internal Vector3 Pos;
            internal float T;   // PlayClock.Now
        }

        private const int Capacity = 64;
        private const float GroundedFor = 0.5f;
        private const float MinSpacing = 1.5f;
        private const float SummarySeconds = 30f;

        private static readonly Sample[] Ring = new Sample[Capacity];
        private static int _head;    // next write
        private static int _count;
        private static float _nextSample;
        private static float _groundedSince = -1f;
        private static float _suspendUntil;
        private static readonly List<TrespassingArea> Areas = new List<TrespassingArea>();

        private static int _recorded, _clears;
        private static string _lastClear;
        private static float _nextSummary;

        internal static int Count => _count;

        // i = 0 is the newest sample.
        internal static Sample Get(int i) => Ring[(_head - 1 - i + Capacity * 2) % Capacity];

        // Drops sample i and every newer one, so repeated presses walk back along the trail.
        internal static void DropFrom(int i)
        {
            int n = Math.Min(i + 1, _count);
            _head = (_head - n + Capacity) % Capacity;
            _count -= n;
        }

        internal static void Clear(string reason)
        {
            if (_count > 0)
            {
                _clears++;
                _lastClear = reason;
            }
            _count = 0;
            _head = 0;
        }

        // Recording pauses for this long and the grounded timer restarts (after an unstuck).
        internal static void Suspend(float seconds)
        {
            _suspendUntil = PlayClock.Now + seconds;
            _groundedSince = -1f;
        }

        internal static void OnSceneLoaded()
        {
            Clear("scene loaded");
            _groundedSince = -1f;
            _nextSample = 0f;
            Areas.Clear();
            foreach (var a in Object.FindObjectsByType<TrespassingArea>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (a != null) Areas.Add(a);
        }

        // Plugin.Update: cheap checks every frame, a sample every SampleSeconds of play time (PlayClock).
        internal static void Tick()
        {
            if (!Unstuck.Available || !Cfg.Unstuck.Enabled.Value) return;
            var pl = PlayerLocator.instance;
            var occ = OpenSewerCharacterController.instance;
            if (pl == null || occ == null || occ.movement == null) return;
            float now = PlayClock.Now;
            Summary(now);

            var pos = pl.transform.position;
            if (TollSlot.InArea(pos)) Clear("toll area");
            if (PlayGate.Jailed || (Crime.instance != null && Crime.instance.ArrestInProgress)) Clear("jail/arrest");

            var m = occ.movement;
            if (!m.isGrounded) _groundedSince = -1f;
            else if (_groundedSince < 0f) _groundedSince = now;

            if (now < _nextSample) return;
            _nextSample = now + Cfg.Unstuck.SampleSeconds.Value;
            if (now < _suspendUntil || _groundedSince < 0f || now - _groundedSince < GroundedFor) return;
            if (!Safe(pl, occ, pos)) return;
            if (_count > 0 && Vector3.Distance(Get(0).Pos, pos) < MinSpacing) return;

            Ring[_head] = new Sample { Pos = pos, T = now };
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
            _recorded++;
        }

        private static bool Safe(PlayerLocator pl, OpenSewerCharacterController occ, Vector3 pos)
        {
            if (PlayGate.Core().Blocked || Game.Paused) return false;
            if (occ.ControlsDisabled()) return false;
            var gc = GameController.instance;
            if (gc == null || gc.keysDisabled) return false;
            var rb = pl.GetComponent<Rigidbody>();
            if (rb != null && rb.isKinematic) return false;
            if (pl.transform.parent != pl.root || InteractableChair.PlayerIsSitting || Vanilla.Climbing || PlayGate.Riding) return false;

            var m = occ.movement;
            if (!m.isGrounded || m.isOnPlatform || m.groundRigidbody != null) return false;
            var ground = m.groundCollider;
            if (ground != null && (ground.GetComponentInParent<TrackTrain>() != null || ground.GetComponentInParent<Elevator>() != null)) return false;

            var w = WaterController.instance;
            if (w != null && (w.playerIsSwimming || w.inShallowWater || w.inDeepWater || w.playerSubmersion >= 0.2f)) return false;

            // Position, not the area's `trespassing` flag: in 0.5.19 that flag stays true after the second exit
            // (OnTriggerExit only starts Detectioncooldown while detectCooldown is null, and nothing nulls it again,
            // SRC/TrespassingArea.cs:160-165), and allowSaving areas can load it as true.
            if (InEnforcedTrespass(pos)) return false;
            if (PlayGate.Jailed || PlayGate.Police) return false;
            if (TollSlot.InArea(pos)) return false;
            return true;
        }

        // p lies inside a cached, active trespassing area that is enforced now (AreaDisabled is false). Checked when a
        // sample is recorded, and again when a sample or an entrance is used, since a shop may have closed since.
        internal static bool InEnforcedTrespass(Vector3 p)
        {
            foreach (var a in Areas)
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                bool disabled;
                try { disabled = a.AreaDisabled; }
                catch (Exception) { continue; }
                if (disabled) continue;
                foreach (var c in a.GetComponents<Collider>())
                {
                    if (c == null || !c.enabled || !c.bounds.Contains(p)) continue;
                    // ClosestPoint needs a convex shape; a concave mesh only gets the bounds test.
                    if (c is MeshCollider mc && !mc.convex) return true;
                    if ((c.ClosestPoint(p) - p).sqrMagnitude <= 0.0001f) return true;
                }
            }
            return false;
        }

        private static void Summary(float now)
        {
            if (now < _nextSummary) return;
            _nextSummary = now + SummarySeconds;
            if (_recorded == 0 && _clears == 0) return;
            Log.Verbose($"Unstuck: {_recorded} spots recorded, {_clears} clears{(_lastClear != null ? " (last: " + _lastClear + ")" : "")}, {_count} kept.");
            _recorded = 0;
            _clears = 0;
            _lastClear = null;
        }
    }
}

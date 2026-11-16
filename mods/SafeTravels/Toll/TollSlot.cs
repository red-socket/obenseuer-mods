using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace SafeTravels
{
    // The Tenement toll bridge coin slot (U-006 / S-039, spec §4.1): identification on every scene load, the lock that
    // P1/P2 read, the optional price override, and the toll area and gate line that Unstuck uses. The scene wiring
    // (ASSETS/Scenes/Open Sewer Tenement.unity): coin_slot_001's onPurchase fires 'Bridge Pay Relay', whose single output
    // (delay 1 s) activates 'Bridge Open Trigger Pay', lights the lamp and plays PaidAudio. 'Trigger Close' deactivates
    // the trigger again when everyone has left. InteractableMoney has no GUID and is never saved.
    internal static class TollSlot
    {
        internal const string TollScene = "Open Sewer Tenement";
        private const string BridgeRoot = "_Tenement_Toll_Bridge";
        private const float StaleSeconds = 2f;
        private const float GateHalfLength = 15f;

        internal static bool Patched;

        internal static InteractableMoney Slot;
        internal static Relay PayRelay;
        internal static int PayIndex;
        internal static GameObject PayTrigger;
        internal static float LastPaid = float.NegativeInfinity;

        private static Transform _root;
        private static bool _boundsDone, _gateDone;
        private static Bounds? _bounds;
        private static bool _hasGate;
        private static Vector2 _gateCentre, _gateDir;
        private static int _vanillaPrice;
        private static bool _hasVanillaPrice;
        private static float _lastDelay, _lastDelayChange;

        // Reasons are constants, so Locked never allocates (P2 calls it every frame while the slot is aimed at).
        private const string WhyTime = "time", WhyPending = "pending", WhyArmed = "armed";

        internal static void OnSceneLoaded(Scene scene)
        {
            Slot = null;
            PayRelay = null;
            PayIndex = 0;
            PayTrigger = null;
            LastPaid = float.NegativeInfinity;
            _root = null;
            _boundsDone = _gateDone = false;
            _bounds = null;
            _hasGate = false;
            _hasVanillaPrice = false;
            _lastDelay = 0f;
            _lastDelayChange = 0f;
            if (scene.name != TollScene) return;
            Identify(scene);
            if (Slot != null) ApplyPrice();
        }

        private static void Identify(Scene scene)
        {
            var matches = new List<(InteractableMoney slot, Relay relay, int index, GameObject trigger)>();
            InteractableMoney byPath = null;
            foreach (var m in Object.FindObjectsByType<InteractableMoney>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (m == null || m.gameObject.scene != scene) continue;
                var t = m.transform;
                if (m.name == "coin_slot_001" && t.parent != null && t.parent.name == "Buy" && t.parent.parent != null && t.parent.parent.name == BridgeRoot)
                    byPath = m;
                if (m.onPurchase == null) continue;
                foreach (var o in m.onPurchase)
                {
                    var ev = o?.OnTrigger;
                    if (ev == null) continue;
                    for (int j = 0; j < ev.GetPersistentEventCount(); j++)
                    {
                        if (!(ev.GetPersistentTarget(j) is Relay r) || r == null || ev.GetPersistentMethodName(j) != "triggerOutputs") continue;
                        if (r.outputs == null) continue;
                        for (int k = 0; k < r.outputs.Length; k++)
                        {
                            var trig = ActivatedTrigger(r.outputs[k]?.OnTrigger);
                            if (trig != null) matches.Add((m, r, k, trig));
                        }
                    }
                }
            }

            if (matches.Count > 0)
            {
                var pick = matches[0];
                if (matches.Count > 1)
                {
                    foreach (var x in matches)
                        if (x.relay.gameObject.name == "Bridge Pay Relay") { pick = x; break; }
                    Log.Warn($"Toll: {matches.Count} coin slots open a gate trigger in {TollScene}; using {pick.slot.name} ({pick.relay.name}).");
                }
                Slot = pick.slot;
                PayRelay = pick.relay;
                PayIndex = pick.index;
                PayTrigger = pick.trigger;
                Log.Info($"Toll: found (price {Slot.price} {(Slot.RM ? "RM" : "OC")})");
                return;
            }
            if (byPath != null)
            {
                Slot = byPath;
                Log.WarnOnce("toll relay", "Toll: gate relay not found; the slot is locked for LockSeconds after paying only.");
                return;
            }
            Log.WarnOnce("toll slot", "Toll: coin slot not found; toll lock off.");
        }

        // The GameObject with a TriggerMultiple that this output's persistent calls SetActive, or null.
        private static GameObject ActivatedTrigger(UnityEvent ev)
        {
            if (ev == null) return null;
            for (int i = 0; i < ev.GetPersistentEventCount(); i++)
                if (ev.GetPersistentTarget(i) is GameObject go && go != null && ev.GetPersistentMethodName(i) == "SetActive"
                    && go.GetComponent<TriggerMultiple>() != null)
                    return go;
            return null;
        }

        // Spec §3.1: time lock, then a pending relay delay (ignored once stale), then the armed trigger. First match wins.
        internal static bool Locked(out string why)
        {
            why = null;
            if (Slot == null) return false;
            float lockFor = Cfg.Toll.LockSeconds.Value;
            if (lockFor > 0f && Time.time - LastPaid < lockFor) { why = WhyTime; return true; }
            if (PayRelay != null && PayRelay.outputs != null && PayIndex < PayRelay.outputs.Length)
            {
                var o = PayRelay.outputs[PayIndex];
                if (o != null && o.delayLeft > 0f && !PendingStale(o.delayLeft)) { why = WhyPending; return true; }
            }
            if (PayTrigger != null && PayTrigger.activeSelf) { why = WhyArmed; return true; }
            return false;
        }

        // delayLeft stays above 0 for good if the relay coroutine dies (SRC/Relay.cs:285-292), so a value that has not
        // moved for StaleSeconds of running game time no longer locks the slot. delayLeft is only read, never written.
        private static bool PendingStale(float delay) =>
            delay > 0f && Time.timeScale > 0f && delay == _lastDelay && Time.time - _lastDelayChange > StaleSeconds;

        // Plugin.Update: keeps the stale tracker for the pay relay's delay.
        internal static void Tick()
        {
            if (PayRelay == null || PayRelay.outputs == null || PayIndex >= PayRelay.outputs.Length) return;
            var o = PayRelay.outputs[PayIndex];
            if (o == null) return;
            if (o.delayLeft != _lastDelay)
            {
                _lastDelay = o.delayLeft;
                _lastDelayChange = Time.time;
            }
        }

        // Toll.Price: on scene load and live on SettingChanged. -1 puts the scene's own price back. RM is never changed.
        internal static void ApplyPrice()
        {
            if (Slot == null) return;
            int want = Cfg.Toll.Price.Value;
            if (want >= 0)
            {
                if (want == Slot.price && !_hasVanillaPrice) return;
                if (!_hasVanillaPrice)
                {
                    _vanillaPrice = Slot.price;
                    _hasVanillaPrice = true;
                }
                Slot.price = want;
                Log.Info($"Toll: price set to {want} (the game's is {_vanillaPrice}).");
            }
            else if (_hasVanillaPrice)
            {
                Slot.price = _vanillaPrice;
                _hasVanillaPrice = false;
                Log.Info($"Toll: price back to {Slot.price}.");
            }
        }

        // ---- For Unstuck ----

        private static Transform Root
        {
            get
            {
                if (_root != null || Slot == null) return _root;
                for (var t = Slot.transform; t != null; t = t.parent)
                    if (t.name == BridgeRoot) return _root = t;
                if (PayTrigger != null)
                    for (var t = Slot.transform.parent; t != null; t = t.parent)
                        if (PayTrigger.transform.IsChildOf(t)) return _root = t;
                return null;
            }
        }

        // The toll triggers' area (Trigger Close covers x 67.1..73.2, z 35.8..41.1), grown by 0.5 m on each side.
        internal static Bounds? AreaBounds
        {
            get
            {
                if (_boundsDone) return _bounds;
                if (Slot == null) return null;
                _boundsDone = true;
                try
                {
                    var root = Root;
                    if (root == null) { Log.Warn("Toll: the toll bridge was not found; Unstuck can't keep clear of the toll area."); return null; }
                    bool any = false;
                    var b = new Bounds();
                    foreach (var tm in root.GetComponentsInChildren<TriggerMultiple>(false))
                        foreach (var col in tm.GetComponents<Collider>())
                        {
                            if (col == null || !col.enabled || !col.isTrigger) continue;
                            if (!any) { b = col.bounds; any = true; }
                            else b.Encapsulate(col.bounds);
                        }
                    if (!any) { Log.Warn("Toll: no toll triggers found; Unstuck can't keep clear of the toll area."); return null; }
                    b.Expand(1f);
                    _bounds = b;
                    Log.Verbose($"Toll: area {b.min.x:0.0}..{b.max.x:0.0} x {b.min.z:0.0}..{b.max.z:0.0}");
                }
                catch (Exception e) { Log.Exception("toll area", e); }
                return _bounds;
            }
        }

        internal static bool InArea(Vector3 p)
        {
            var b = AreaBounds;
            return b.HasValue && b.Value.Contains(p);
        }

        private static bool Gate()
        {
            if (_gateDone) return _hasGate;
            if (Slot == null) return false;
            _gateDone = true;
            try
            {
                var root = Root;
                var doors = root != null ? root.GetComponentsInChildren<Door>(true) : null;
                if (doors == null || doors.Length != 2)
                {
                    Log.Warn($"Toll: expected the 2 gate halves, found {(doors == null ? 0 : doors.Length)}; Unstuck only keeps clear of the toll area.");
                    return false;
                }
                Vector3 p1 = doors[0].transform.position, p2 = doors[1].transform.position;
                var dir = new Vector2(p2.x - p1.x, p2.z - p1.z);
                if (dir.sqrMagnitude < 0.01f) { Log.Warn("Toll: the gate halves overlap; Unstuck only keeps clear of the toll area."); return false; }
                _gateDir = dir.normalized;
                _gateCentre = new Vector2((p1.x + p2.x) * 0.5f, (p1.z + p2.z) * 0.5f);
                _hasGate = true;
            }
            catch (Exception e) { Log.Exception("toll gate line", e); }
            return _hasGate;
        }

        // The XZ segment a→b crosses the gate line within GateHalfLength of its centre.
        internal static bool AcrossGate(Vector3 a, Vector3 b)
        {
            if (!Gate()) return false;
            Vector2 g1 = _gateCentre - _gateDir * GateHalfLength, g2 = _gateCentre + _gateDir * GateHalfLength;
            return Intersects(new Vector2(a.x, a.z), new Vector2(b.x, b.z), g1, g2);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool Intersects(Vector2 p, Vector2 p2, Vector2 q, Vector2 q2)
        {
            Vector2 r = p2 - p, s = q2 - q;
            float d = Cross(r, s);
            if (Mathf.Abs(d) < 1e-6f) return false;   // parallel: never crosses the line
            float t = Cross(q - p, s) / d, u = Cross(q - p, r) / d;
            return t >= 0f && t <= 1f && u >= 0f && u <= 1f;
        }
    }
}

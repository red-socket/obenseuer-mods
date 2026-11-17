using System;
using System.Collections.Generic;
using Outputs;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FairMarket
{
    // Which doors and stall traders keep opening hours in this scene (spec §7.2), read from the live components the
    // game itself uses. Built lazily on the first lookup after a scene load, never changed afterwards, read-only: no
    // relay is fired and no ForceCheck, CheckIsOpen, Lock or UnLock is called.
    //   - RelayWeekdays (active ones only, so the disabled barber and vet sources stay out): its onOpen/onClosed relays
    //     are walked to the doors they lock and the traders they hide.
    //   - DoorSchedule (active): straight to its Doors.
    //   - Trade.openingTimes, only when that Trade has its own onOpen/onClosed with outputs (Berry stall, Mr. Bag,
    //     Auction); other Trades' default 8-18 times drive nothing.
    // Relays are followed through their persistent UnityEvent calls (the same API as SRC/Outputs/output.cs:128-132),
    // into other Relays up to depth 3. output.outputCustomEvent isn't walked: the class isn't [Serializable], so scene
    // relays never carry one (only code-built buttons do, SRC/RecipeInfoUI.cs:305). The first source wins per target.
    internal static class HoursIndex
    {
        private const int MaxDepth = 3;

        private static readonly Dictionary<Object, Schedule> Map = new Dictionary<Object, Schedule>();
        private static bool _built;

        // Non-shop sources found by the test build's hours-dump, as (scene, RelayWeekdays GameObject path). Expected empty.
        private static readonly (string scene, string path)[] Deny = new (string, string)[0];

        internal static void Clear()
        {
            Map.Clear();
            _built = false;
        }

        internal static Schedule For(Object target)
        {
            if (target == null) return null;
            if (!_built) Build();
            return Map.TryGetValue(target, out var s) ? s : null;
        }

        internal static IEnumerable<KeyValuePair<Object, Schedule>> All
        {
            get
            {
                if (!_built) Build();
                return Map;
            }
        }

        private static void Build()
        {
            _built = true;
            Map.Clear();
            int sources = 0;
            string scene = SceneManager.GetActiveScene().name;
            try
            {
                foreach (var rw in Object.FindObjectsByType<RelayWeekdays>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (rw == null || !rw.isActiveAndEnabled || rw.openingTimes == null) continue;
                    string path = Path(rw.transform);
                    if (Array.Exists(Deny, d => d.scene == scene && d.path == path)) continue;
                    var s = new WeekdaysSchedule(rw.openingTimes) { Kind = "RelayWeekdays", Source = path };
                    var seen = new HashSet<Relay>();
                    Walk(rw.onOpen, s, 0, seen);
                    Walk(rw.onClosed, s, 0, seen);
                    sources++;
                }
                foreach (var ds in Object.FindObjectsByType<DoorSchedule>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (ds == null || !ds.isActiveAndEnabled || ds.OpenTimes == null || ds.Doors == null) continue;
                    var s = new DoorScheduleSchedule(ds.OpenTimes) { Kind = "DoorSchedule", Source = Path(ds.transform) };
                    foreach (var d in ds.Doors) Add(d, s);
                    sources++;
                }
                foreach (var tr in Object.FindObjectsByType<Trade>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (tr == null || tr.openingTimes == null || !(HasOutputs(tr.onOpen) || HasOutputs(tr.onClosed))) continue;
                    var s = new WeekdaysSchedule(tr.openingTimes) { Kind = "Trade", Source = Path(tr.transform) };
                    var seen = new HashSet<Relay>();
                    Walk(tr.onOpen, s, 0, seen);
                    Walk(tr.onClosed, s, 0, seen);
                    Add(tr, s);
                    sources++;
                }
            }
            catch (Exception e) { Log.Exception("hours index", e); }
            if (Log.VerboseOn)
            {
                int doors = 0, stalls = 0;
                foreach (var kv in Map) if (kv.Key is Trade) stalls++; else doors++;
                Log.Verbose($"Hours: {scene}: {doors} doors and {stalls} traders with hours, from {sources} sources.");
            }
        }

        private static bool HasOutputs(Relay r) => r != null && r.outputs != null && r.outputs.Length > 0;

        private static void Walk(Relay relay, Schedule s, int depth, HashSet<Relay> seen)
        {
            if (relay == null || depth > MaxDepth || !seen.Add(relay) || relay.outputs == null) return;
            foreach (var o in relay.outputs)
            {
                var ev = o?.OnTrigger;
                if (ev == null) continue;
                int n = ev.GetPersistentEventCount();
                for (int i = 0; i < n; i++)
                {
                    var target = ev.GetPersistentTarget(i);
                    string method = ev.GetPersistentMethodName(i);
                    if (target == null || method == null) continue;
                    switch (target)
                    {
                        case DoorChangelevel dc when method == "Lock" || method == "UnLock":
                            Add(dc, s);
                            break;
                        case Door d when method == "Lock" || method == "LockAndClose" || method == "Unlock" || method == "UnlockOpen":
                            Add(d, s);
                            break;
                        case FadeGameObject f when method == "ChangeHiddenStatus":
                            AddTrades(f.gameObject, s);
                            break;
                        case GameObjectStateSave g when method == "ChangeState":
                            AddTrades(g.gameObject, s);
                            break;
                        case GameObject go when method == "SetActive":
                            AddTrades(go, s);
                            break;
                        case Relay next:
                            Walk(next, s, depth + 1, seen);
                            break;
                    }
                }
            }
        }

        private static void AddTrades(GameObject root, Schedule s)
        {
            if (root == null) return;
            foreach (var t in root.GetComponentsInChildren<Trade>(true)) Add(t, s);
        }

        private static void Add(Object target, Schedule s)
        {
            if (target == null) return;
            if (Map.TryGetValue(target, out var first))
            {
                if (!ReferenceEquals(first, s))
                    Log.Verbose($"Hours: {target.name} has hours from {first.Kind} {first.Source}; {s.Kind} {s.Source} ignored.");
                return;
            }
            Map[target] = s;
        }

        internal static string Path(Transform t)
        {
            if (t == null) return "";
            var parts = new List<string>();
            for (var x = t; x != null; x = x.parent) parts.Add(x.name);
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }
    }
}

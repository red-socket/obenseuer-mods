using System;
using System.Collections.Generic;
using UnityEngine;

namespace TidyInventory
{
    // Two-step confirmation (spec §4.3, §4.4, §6): the first press arms a key for one target, a second press on the same
    // target within the window acts. Expiry runs on unscaled time (the inventory pauses nothing, but the slider and
    // sleep can change the time scale) and calls the entry's onExpire, which restores button labels.
    internal static class Confirm
    {
        private sealed class Entry
        {
            internal object Target;
            internal float Until;
            internal Action OnExpire;
        }

        private static readonly Dictionary<string, Entry> Armed = new Dictionary<string, Entry>();

        internal static void Arm(string key, object target, float seconds, Action onExpire = null)
        {
            Reset(key);
            Armed[key] = new Entry { Target = target, Until = Time.unscaledTime + seconds, OnExpire = onExpire };
        }

        internal static bool IsArmed(string key, object target) =>
            Armed.TryGetValue(key, out var e) && ReferenceEquals(e.Target, target) && Time.unscaledTime <= e.Until;

        internal static bool AnyArmed(string key) => Armed.ContainsKey(key);

        internal static object Target(string key) => Armed.TryGetValue(key, out var e) ? e.Target : null;

        // Disarms and restores the entry's labels. Safe to call when nothing is armed.
        internal static void Reset(string key)
        {
            if (!Armed.TryGetValue(key, out var e)) return;
            Armed.Remove(key);
            try { e.OnExpire?.Invoke(); }
            catch (Exception ex) { Log.Exception("confirm reset " + key, ex); }
        }

        internal static void ResetAll()
        {
            foreach (var k in new List<string>(Armed.Keys)) Reset(k);
        }

        // Plugin.Update: expires stale entries.
        internal static void Tick()
        {
            if (Armed.Count == 0) return;
            float now = Time.unscaledTime;
            List<string> stale = null;
            foreach (var kv in Armed)
                if (now > kv.Value.Until) (stale ?? (stale = new List<string>())).Add(kv.Key);
            if (stale != null)
                foreach (var k in stale) Reset(k);
        }
    }
}

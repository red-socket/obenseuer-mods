using System.Collections.Generic;
using UnityEngine;

namespace Workshop
{
    // Two-step confirmation (spec §5.5, §7.2): the first press arms a key for one target, a second press on the same
    // target within the window acts. Expiry runs on unscaled time (sleeping and waiting change the time scale).
    internal static class Confirm
    {
        private sealed class Entry
        {
            internal object Target;
            internal float Until;
        }

        private static readonly Dictionary<string, Entry> Armed = new Dictionary<string, Entry>();

        internal static void Arm(string key, object target, float seconds)
        {
            Armed[key] = new Entry { Target = target, Until = Time.unscaledTime + seconds };
        }

        internal static bool IsArmed(string key, object target) =>
            Armed.TryGetValue(key, out var e) && ReferenceEquals(e.Target, target) && Time.unscaledTime <= e.Until;

        // Disarms. Safe to call when nothing is armed.
        internal static void Reset(string key) => Armed.Remove(key);

        internal static void ResetAll() => Armed.Clear();

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

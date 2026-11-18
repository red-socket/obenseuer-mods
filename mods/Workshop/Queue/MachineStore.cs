using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using UnityEngine.SceneManagement;

namespace Workshop
{
    // What a machine remembers (spec §5.1). Last is the last recipe started by hand or by the repeat key; Q is a running
    // repeat. ArmCount is the repeat button's count and never saved.
    internal sealed class Entry
    {
        internal string Scene, Guid;
        internal char Kind = 'm';
        internal int LastId;
        internal string LastName;
        internal int LastCount = 1, LastMult = 1;
        internal int ArmCount = 1;
        internal int QId;
        internal string QName;
        internal int QLeft;   // int.MaxValue: "All"
        // Not saved. True for entries this session vouches for: kept in memory over a level change, or read back from the
        // record Workshop itself saved last when the entry was trusted at that save (live then, or already trusted while
        // pending). An entry read from an earlier session's save stays untrusted until it binds in this session. Trusted
        // repeats are kept at bind even with no job running (bug 2, 2026-11-18).
        internal bool Trusted;

        internal bool HasLast => LastId > 0;
        internal bool Queued => QLeft > 0;
        internal string Key => Scene + "|" + Guid;
    }

    // Machine memory and its save record (spec §5.1, §5.7). Live entries hang off the station in a ConditionalWeakTable,
    // with a strong list for enumeration. Pending entries are keyed "scene|GUID" and wait for their station to load.
    // Saved as one plain string in GlobalState under "workshop.machines" (deviation D1), written in a GlobalState.OnSavingGame
    // prefix, so the record is in Globals.tnmt with the rest of the level's save (and in Save Guard's transaction).
    internal static class MachineStore
    {
        internal const string Key = "workshop.machines";
        private const string Version = "v1";
        private const int MaxEntries = 512;

        private static readonly ConditionalWeakTable<CraftingBase, Entry> Live = new ConditionalWeakTable<CraftingBase, Entry>();
        private static readonly List<CraftingBase> Strong = new List<CraftingBase>();
        // Ordered oldest first; the dictionary indexes it.
        private static readonly List<Entry> PendingList = new List<Entry>();
        private static readonly Dictionary<string, Entry> Pending = new Dictionary<string, Entry>();
        // Stations whose OnLoadingGame ran during the current load; reset at LoadingDone.
        private static readonly List<CraftingBase> LoadedThisLoad = new List<CraftingBase>();
        private static bool _readOnly;
        // The record this session last wrote. Not cleared at the main menu: a save made in this session can't have been
        // touched by the game without Workshop.
        private static string _lastWritten;
        // Keys of the entries in _lastWritten that were trusted when it was written (written together with it). Other
        // entries of that record came unchecked from an earlier session's save and keep the strict bind rule.
        private static HashSet<string> _lastTrusted = new HashSet<string>();

        internal static readonly HashSet<CraftingBase> PollNow = new HashSet<CraftingBase>();

        internal static Entry Get(CraftingBase cb, bool create)
        {
            if (cb == null) return null;
            if (Live.TryGetValue(cb, out var e)) return e;
            if (!create) return null;
            e = new Entry { Kind = Stations.KindChar(cb) };
            Remember(cb, e);
            return e;
        }

        private static void Remember(CraftingBase cb, Entry e)
        {
            Live.Remove(cb);
            Live.Add(cb, e);
            if (!Strong.Exists(x => ReferenceEquals(x, cb))) Strong.Add(cb);
            UpdateKey(cb, e);
        }

        private static void UpdateKey(CraftingBase cb, Entry e)
        {
            var key = Stations.SaveKey(cb);
            if (key == null) return;
            int bar = key.IndexOf('|');
            e.Scene = key.Substring(0, bar);
            e.Guid = key.Substring(bar + 1);
        }

        // Live machines in loaded scenes (copy: callers may start crafts while walking it).
        internal static List<CraftingBase> Machines()
        {
            Strong.RemoveAll(cb => cb == null);
            return new List<CraftingBase>(Strong);
        }

        internal static void SetQueue(CraftingBase cb, int id, string name, int left)
        {
            var e = Get(cb, true);
            e.QId = id;
            e.QName = name;
            e.QLeft = left;
            PollNow.Add(cb);
            LookHint.Refresh(cb);
        }

        internal static void ClearQueue(CraftingBase cb)
        {
            var e = Get(cb, false);
            if (e == null || !e.Queued) return;
            e.QLeft = 0;
            e.QId = 0;
            e.QName = null;
            LookHint.Refresh(cb);
        }

        // ---- Scene events ----

        // A new scene: destroyed stations keep their entries as pending under their last known key (the level-change
        // save just wrote them), so they come back even when the globals are not read again. The main menu clears all.
        internal static void OnSceneLoaded()
        {
            if (GlobalState.instance == null)
            {
                ClearAll();
                return;
            }
            // Index loop with reference identity: destroyed Unity objects compare equal to each other.
            for (int i = Strong.Count - 1; i >= 0; i--)
            {
                var cb = Strong[i];
                if (cb != null) continue;
                Strong.RemoveAt(i);
                if (Live.TryGetValue(cb, out var e))
                {
                    Live.Remove(cb);
                    if (!string.IsNullOrEmpty(e.Guid) && (e.HasLast || e.Queued))
                    {
                        e.Trusted = true;
                        AddPending(e);
                    }
                }
            }
            PollNow.RemoveWhere(cb => cb == null);
        }

        private static void ClearAll()
        {
            foreach (var cb in Strong) Live.Remove(cb);
            Strong.Clear();
            PendingList.Clear();
            Pending.Clear();
            LoadedThisLoad.Clear();
            PollNow.Clear();
            _readOnly = false;
        }

        // LoadingDone: entries for a loaded scene whose station never loaded belong to furniture that is gone or moved.
        internal static void OnLoadingDone()
        {
            var loaded = new HashSet<string>();
            for (int i = 0; i < SceneManager.sceneCount; i++) loaded.Add(SceneManager.GetSceneAt(i).name);
            int dropped = PendingList.RemoveAll(e => loaded.Contains(e.Scene));
            if (dropped > 0)
            {
                Pending.Clear();
                foreach (var e in PendingList) Pending[e.Key] = e;
                Log.Verbose($"Queue: dropped {dropped} remembered machine(s) that are no longer here.");
            }
            LoadedThisLoad.Clear();
            foreach (var cb in Machines())
                if (Get(cb, false) is Entry e && e.Queued) PollNow.Add(cb);
        }

        // ---- W6, W7: load ----

        // W6: GlobalState.OnLoadingGame. The save's record replaces what is in memory, and stations that already loaded
        // in this load are bound again from it.
        internal static void OnGlobalsLoaded()
        {
            PendingList.Clear();
            Pending.Clear();
            _readOnly = false;
            var raw = GlobalState.instance?.GetState(Key)?.data;
            if (raw != null) Parse(raw as string);
            int bound = 0;
            foreach (var cb in new List<CraftingBase>(LoadedThisLoad))
            {
                if (cb == null) continue;
                if (Live.TryGetValue(cb, out _))
                {
                    Live.Remove(cb);
                    Strong.RemoveAll(x => ReferenceEquals(x, cb));
                }
                if (Bind(cb)) bound++;
            }
            Log.Verbose($"Queue: read {PendingList.Count + bound} remembered machine(s), {bound} bound at once.");
        }

        // W7: CraftingBase.OnLoadingGame.
        internal static void OnStationLoaded(CraftingBase cb)
        {
            if (cb == null) return;
            if (!LoadedThisLoad.Exists(x => ReferenceEquals(x, cb))) LoadedThisLoad.Add(cb);
            Bind(cb);
        }

        // Spec §5.7 validation: the kind matches, the last recipe is made here, and a repeat only survives with one of
        // its jobs running (this defuses entries made stale by a save without Workshop that renumbered the GUIDs). A
        // Trusted entry (see Entry.Trusted) can't be stale that way, so its repeat survives on a plain machine with no
        // job running too: a repeat waiting for room, power or (with instant crafting) its next poll.
        private static bool Bind(CraftingBase cb)
        {
            var key = Stations.SaveKey(cb);
            if (key == null || !Pending.TryGetValue(key, out var e)) return false;
            Pending.Remove(key);
            PendingList.Remove(e);
            if (e.Kind != Stations.KindChar(cb) || (e.HasLast && RecipeLookup.Find(cb, e.LastId, e.LastName) == null))
            {
                Log.Verbose($"Queue: dropped the record for {Stations.Name(cb)} ({key}): it no longer matches.");
                return false;
            }
            if (e.Queued)
            {
                bool ok = false;
                if (cb is manufacturing m && Stations.KindOf(m) == Kind.Plain)
                {
                    ok = e.Trusted;
                    var jobs = Vanilla.JobsOf(m);
                    if (jobs != null)
                        foreach (var j in jobs)
                            if (j?.recipe != null && j.recipe.ID == e.QId) { ok = true; break; }
                }
                if (!ok)
                {
                    e.QLeft = 0;
                    e.QId = 0;
                    e.QName = null;
                    Log.Verbose($"Queue: dropped the repeat on {Stations.Name(cb)}: none of its jobs is running.");
                }
            }
            if (!e.HasLast && !e.Queued) return false;
            Remember(cb, e);
            if (e.Queued) PollNow.Add(cb);
            return true;
        }

        // ---- W5: save ----

        // W5: GlobalState.OnSavingGame prefix. Never throws: on any error the previous record stays.
        internal static void OnSaving()
        {
            try
            {
                if (_readOnly || GlobalState.instance == null) return;
                var entries = new List<Entry>(PendingList);
                var liveKeys = new HashSet<string>();
                var live = new List<Entry>();
                foreach (var cb in Machines())
                {
                    if (!Live.TryGetValue(cb, out var e) || (!e.HasLast && !e.Queued)) continue;
                    UpdateKey(cb, e);
                    if (string.IsNullOrEmpty(e.Guid) || !liveKeys.Add(e.Key)) continue;
                    live.Add(e);
                }
                entries.RemoveAll(e => liveKeys.Contains(e.Key));
                // Oldest entries of other scenes go first when the record is full.
                int over = entries.Count + live.Count - MaxEntries;
                if (over > 0) entries.RemoveRange(0, Math.Min(over, entries.Count));
                entries.AddRange(live);
                if (entries.Count > MaxEntries) entries.RemoveRange(0, entries.Count - MaxEntries);
                // Trusted: every live entry (made this session, or bound after validation) and pending entries already
                // trusted. Pending entries from an untrusted read are not, however often the record is rewritten.
                var trusted = new HashSet<string>(liveKeys);
                foreach (var e in entries)
                    if (e.Trusted) trusted.Add(e.Key);
                string record = Format(entries);
                GlobalState.instance.SetState(Key, true, record);
                _lastWritten = record;
                _lastTrusted = trusted;
                Log.Verbose($"Queue: saved {entries.Count} remembered machine(s), {live.Count} in this scene.");
            }
            catch (Exception e) { Log.Exception("queue save", e); }
        }

        // ---- Format: v1;<scene>|<guid>|<m|p>|<lastId>|<lastName>|<lastCount>|<lastMult>|<qId>|<qName>|<qLeft>;... ----

        private static string Format(List<Entry> entries)
        {
            var sb = new StringBuilder(Version);
            foreach (var e in entries)
            {
                sb.Append(';').Append(Esc(e.Scene)).Append('|').Append(Esc(e.Guid)).Append('|').Append(e.Kind).Append('|')
                  .Append(I(e.LastId)).Append('|').Append(Esc(e.LastName)).Append('|').Append(I(e.LastCount)).Append('|')
                  .Append(I(e.LastMult)).Append('|').Append(I(e.QId)).Append('|').Append(Esc(e.QName)).Append('|').Append(I(e.QLeft));
            }
            return sb.ToString();
        }

        // An unknown version or a parse error makes the record read-only for the session: nothing is read and it is
        // never overwritten (a newer Workshop may have written it).
        private static void Parse(string raw)
        {
            try
            {
                if (raw == null) throw new FormatException("not a string");
                bool ours = string.Equals(raw, _lastWritten, StringComparison.Ordinal);
                var parts = raw.Split(';');
                if (parts.Length == 0 || parts[0] != Version) throw new FormatException("unknown version '" + (parts.Length > 0 ? parts[0] : "") + "'");
                var list = new List<Entry>();
                for (int i = 1; i < parts.Length; i++)
                {
                    if (parts[i].Length == 0) continue;
                    var f = parts[i].Split('|');
                    if (f.Length != 10 || f[2].Length != 1) throw new FormatException("bad entry " + i);
                    var e = new Entry
                    {
                        Scene = Unesc(f[0]), Guid = Unesc(f[1]), Kind = f[2][0], LastId = P(f[3]), LastName = Unesc(f[4]),
                        LastCount = Math.Max(1, P(f[5])), LastMult = Math.Max(1, P(f[6])), QId = P(f[7]), QName = Unesc(f[8]), QLeft = Math.Max(0, P(f[9])),
                    };
                    e.Trusted = ours && _lastTrusted.Contains(e.Key);
                    list.Add(e);
                }
                foreach (var x in list) AddPending(x);
            }
            catch (Exception ex)
            {
                _readOnly = true;
                PendingList.Clear();
                Pending.Clear();
                Log.WarnOnce("record version", $"Queue: the saved machine record could not be read ({ex.Message}); Workshop leaves it unchanged and remembers nothing this session.");
            }
        }

        private static void AddPending(Entry e)
        {
            if (Pending.TryGetValue(e.Key, out var old)) PendingList.Remove(old);
            Pending[e.Key] = e;
            PendingList.Add(e);
        }

        private static string Esc(string s) => Uri.EscapeDataString(s ?? "");
        private static string Unesc(string s) => Uri.UnescapeDataString(s ?? "");
        private static string I(int v) => v.ToString(CultureInfo.InvariantCulture);
        private static int P(string s) => int.Parse(s, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }
}

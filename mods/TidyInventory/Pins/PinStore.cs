using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using OS.Items;

namespace TidyInventory
{
    // One pin: the container family that the slot is reserved for, and the item ID the badge and ghost show.
    internal struct Pin
    {
        internal int Family;
        internal int ShownId;
    }

    // The pin map and its GlobalState record (spec §3.1, §3.3). The key is an index into Inventory.instance.Slots. Stored
    // as one plain string "v1|idx:id;idx:id" under "tidyinventory.pins" (a BCL type: no mod $type in the save); the
    // family is recomputed from the shown ID. A record with an unknown version prefix (written by a newer version) is
    // left alone: no pins and no writes this session.
    internal static class PinStore
    {
        internal const string Key = "tidyinventory.pins";
        private const string Version = "v1";

        internal static readonly SortedDictionary<int, Pin> Map = new SortedDictionary<int, Pin>();
        internal static bool ReadThisScene;
        internal static bool UnknownVersion;
        internal static bool Dirty;

        private static readonly Dictionary<int, int> ContainerCache = new Dictionary<int, int>();

        // Every hot path returns at once when this is false.
        internal static bool Active => PinPatches.Patched && Map.Count > 0 && Cfg.Pins.Enabled.Value;

        // The container family of an item: a filled liquid container belongs to its empty container (13200 -> 2), an
        // empty container and every other item is its own family (spec §3.1). Every in-place ID change in the game
        // (SlotController.ChangeItem, ItemStack.ChangeItem, RemoveLiquidAndReturnRemaining) stays within one family.
        internal static int Container(int id)
        {
            if (ContainerCache.TryGetValue(id, out var fam)) return fam;
            fam = id;
            try
            {
                var item = ItemDatabase.instance != null ? ItemDatabase.instance.FetchItemByID(id) : null;
                if (item != null && ItemMetaUtilities.GetMetaOfType<ItemEmptyLiquidContainerData>(item.Meta) == null)
                {
                    var l = ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(item.Meta);
                    if (l?.emptyContainerItem != null && l.emptyContainerItem.ID > 0) fam = l.emptyContainerItem.ID;
                }
            }
            catch (Exception e) { Log.Exception("container family", e); fam = id; }
            if (ItemDatabase.instance != null) ContainerCache[id] = fam;
            return fam;
        }

        internal static bool InFamily(Pin p, int itemId) => Container(itemId) == p.Family;

        internal static Pin Make(int id) => new Pin { Family = Container(id), ShownId = id };

        internal static void Clear(string why)
        {
            if (Map.Count > 0) Log.Verbose($"Pins: cleared ({why}).");
            Map.Clear();
            Dirty = false;
            PinOverlay.MarkDirty();
        }

        // ---- Format ----

        internal static string Format()
        {
            var sb = new StringBuilder(Version).Append('|');
            bool first = true;
            foreach (var kv in Map)
            {
                if (!first) sb.Append(';');
                first = false;
                sb.Append(kv.Key.ToString(CultureInfo.InvariantCulture)).Append(':').Append(kv.Value.ShownId.ToString(CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        // Parses into Map. Malformed entries are skipped; an unknown version sets UnknownVersion and leaves Map empty.
        private static void Parse(string s)
        {
            Map.Clear();
            if (string.IsNullOrEmpty(s)) return;
            int bar = s.IndexOf('|');
            string ver = bar < 0 ? s : s.Substring(0, bar);
            if (ver != Version)
            {
                UnknownVersion = true;
                Log.WarnOnce("pin-version", $"Pins: the save holds pins in a format this version doesn't know ({ver}); pins are off and left untouched this session.");
                return;
            }
            if (bar < 0) return;
            foreach (var part in s.Substring(bar + 1).Split(';'))
            {
                int colon = part.IndexOf(':');
                if (colon <= 0) continue;
                if (!int.TryParse(part.Substring(0, colon), NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) || idx < 0) continue;
                if (!int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id <= 1) continue;
                Map[idx] = Make(id);
            }
        }

        // ---- Read (spec §3.3) ----

        // P13 (GlobalState.OnLoadingGame postfix), LoadingDone and the Update fallback.
        internal static void ReadFrom(GlobalState gs, string why)
        {
            if (gs == null) return;
            UnknownVersion = false;
            object data = null;
            try { data = gs.GetState(Key)?.data; }
            catch (Exception e) { Log.Exception("pin read", e); }
            Parse(data as string);
            ReadThisScene = true;
            Dirty = false;
            PinOverlay.MarkDirty();
            Log.Verbose($"Pins: read {Map.Count} ({why}).");
        }

        internal static void FallbackRead()
        {
            if (ReadThisScene || !PinPatches.Patched || SaveController.Loading) return;
            var gs = GlobalState.instance;
            if (gs == null) return;
            ReadFrom(gs, "fallback");
        }

        internal static void OnLoadingDone()
        {
            if (!PinPatches.Patched) return;
            if (!ReadThisScene && GlobalState.instance != null) ReadFrom(GlobalState.instance, "loading done");
            PinOverlay.MarkDirty();
            Reservation.Verify();
            if (Map.Count > 0) Log.Info($"Pins: {Map.Count} pinned slots loaded.");
        }

        // ---- Write ----

        internal static bool CanWrite =>
            GlobalState.instance != null && !SaveController.Loading && ReadThisScene && !UnknownVersion;

        internal static void Write(string why)
        {
            if (!CanWrite) return;
            try
            {
                // Always the 3-argument form: the 2-argument one clears data.
                GlobalState.instance.SetState(Key, true, Format());
                Dirty = false;
                Log.Verbose($"Pins: saved {Map.Count} ({why}).");
            }
            catch (Exception e) { Log.Exception("pin write", e); }
        }

        // SaveController.SavingStarted. The game does not guard this event (SRC/SaveController.cs:519): never throws.
        // A door save runs with Loading already set (ChangeLevelWhenReady, :334, then ChangeLevelDelay → SaveGame), but
        // SavingStarted always fires in the old scene before anything is serialized, with this scene's pins read, so
        // the Loading guard of CanWrite does not apply here.
        internal static void OnSavingStarted()
        {
            try
            {
                if (!PinPatches.Patched || GlobalState.instance == null || !ReadThisScene || UnknownVersion) return;
                if (!Cfg.Pins.KeepReservationWhenEmpty.Value) UnpinEmpty("saving");
                GlobalState.instance.SetState(Key, true, Format());
                Dirty = false;
                Log.Verbose($"Pins: saved {Map.Count} (saving).");
            }
            catch (Exception e) { Log.Exception("pin save", e); }
        }

        // Plugin.Update. With KeepReservationWhenEmpty off, the pin goes with the last item, whether or not the inventory
        // is open. Not while a drag or the amount slider has taken the stack out of its slot for a moment.
        internal static void Tick()
        {
            if (!PinPatches.Patched || Map.Count == 0 || Cfg.Pins.KeepReservationWhenEmpty.Value || !CanWrite) return;
            if (!AnyEmpty() || Game.DragActive || Game.SliderOpen) return;
            if (UnpinEmpty("emptied")) Write("emptied");
        }

        private static bool AnyEmpty()
        {
            foreach (var i in Map.Keys)
            {
                var sc = Reservation.SlotAt(i);
                if (sc != null && !Game.NonEmpty(sc)) return true;
            }
            return false;
        }

        // With KeepReservationWhenEmpty off, the pin goes with the last item.
        internal static bool UnpinEmpty(string why)
        {
            var inv = Inventory.instance;
            if (inv == null || inv.Slots == null || Map.Count == 0) return false;
            var gone = Map.Keys.Where(i => i < inv.Slots.Length && !Game.NonEmpty(inv.Slots[i])).ToList();
            foreach (var i in gone)
            {
                Map.Remove(i);
                Log.Verbose($"Pins: slot {i + 1} is empty and unpinned (KeepReservationWhenEmpty is off; {why}).");
            }
            if (gone.Count > 0) PinOverlay.MarkDirty();
            return gone.Count > 0;
        }
    }
}

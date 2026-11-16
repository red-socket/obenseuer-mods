using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using OS.Items;
using UnityEngine;

namespace Workshop
{
    internal enum SourceKind { Inventory, Bag, Storage }

    // One stack Fetch may take from, in planner preference order.
    internal sealed class Source
    {
        internal SlotController Sc;
        internal SourceKind Kind;
        internal Storage Storage;   // the bag's or the nearby storage's, null for the main inventory
        internal int Index;         // slot index in the main inventory
    }

    // Where Fetch may take items from (spec §4.4), in order: the main inventory (never character slots, never Tidy
    // Inventory pins), the worn bag, then the player's own storage near the station, nearest first. The list is a
    // snapshot at most 3 seconds old for button labels, and always rebuilt for a click. Storage checks that never change
    // for a live object (components, parents, furniture) are cached per Storage, as in No Floor Spill's NearbyStorage.
    internal static class Sources
    {
        private sealed class StaticInfo
        {
            internal bool Ok;
            internal FurniturePlaceable Furniture;
        }

        private static readonly ConditionalWeakTable<Storage, StaticInfo> Static = new ConditionalWeakTable<Storage, StaticInfo>();
        private static readonly string[] BinTitles = { "Trash Bin", "Green Dumpster" };

        private static List<Source> _list;
        private static CraftingBase _for;
        private static float _builtAt = -100f;
        internal static int Version { get; private set; }

        internal static void Clear()
        {
            _list = null;
            _for = null;
            Version++;
        }

        internal static List<Source> Snapshot(CraftingBase station, bool fresh)
        {
            if (!fresh && _list != null && _for == station && Time.unscaledTime - _builtAt <= 3f) return _list;
            _list = Build(station);
            _for = station;
            _builtAt = Time.unscaledTime;
            Version++;
            return _list;
        }

        private static List<Source> Build(CraftingBase station)
        {
            var list = new List<Source>();
            if (station == null || SaveController.Loading) return list;

            // 1. Main inventory.
            try
            {
                var pins = Pins.Indices();
                var inv = Inventory.instance?.Slots;
                if (inv != null && pins != null)
                    for (int i = 0; i < inv.Length; i++)
                        if (!pins.Contains(i) && StackOk(inv[i]))
                            list.Add(new Source { Sc = inv[i], Kind = SourceKind.Inventory, Index = i });
            }
            catch (Exception e) { Log.Exception("inventory sources", e); }

            // 2. Worn bag.
            if (Cfg.Fetch.UseWornBag.Value)
                try
                {
                    var bag = BackpackStorage.instance != null ? BackpackStorage.instance.GetStorage() : null;
                    if (bag != null && bag.Slots != null)
                        foreach (var sc in bag.Slots)
                            if (StackOk(sc)) list.Add(new Source { Sc = sc, Kind = SourceKind.Bag, Storage = bag });
                }
                catch (Exception e) { Log.Exception("bag sources", e); }

            // 3. Nearby storage.
            if (Cfg.Fetch.UseNearbyStorage.Value)
                try { AddStorages(station, list); }
                catch (Exception e) { Log.Exception("storage sources", e); }
            return list;
        }

        private static void AddStorages(CraftingBase station, List<Source> list)
        {
            var excluded = SceneExclusions();
            var stationFp = station.GetComponentInParent<FurniturePlaceable>();
            var o = station.transform.position;
            float range = Cfg.Fetch.Range.Value, maxDy = Cfg.Fetch.MaxHeightDifference.Value;
            var found = new List<(Storage s, float d, Vector3 p)>();
            foreach (var s in UnityEngine.Object.FindObjectsByType<Storage>(FindObjectsSortMode.None))
            {
                try
                {
                    if (!Eligible(s, station, stationFp, excluded)) continue;
                    var p = s.transform.position;
                    float dx = p.x - o.x, dz = p.z - o.z;
                    float d = Mathf.Sqrt(dx * dx + dz * dz);
                    if (d > range || Mathf.Abs(p.y - o.y) > maxDy) continue;
                    found.Add((s, d, p));
                }
                catch (Exception e) { Log.Exception("storage check", e); }
            }
            // Nearest first; ties by x, then z (deterministic).
            found.Sort((a, b) =>
            {
                int c = a.d.CompareTo(b.d);
                if (c != 0) return c;
                c = a.p.x.CompareTo(b.p.x);
                return c != 0 ? c : a.p.z.CompareTo(b.p.z);
            });
            foreach (var (s, _, _) in found)
                foreach (var sc in s.Slots)
                    if (StackOk(sc)) list.Add(new Source { Sc = sc, Kind = SourceKind.Storage, Storage = s });
        }

        // Storages that are part of something else: crafting stations' separate output storages, toilet buckets and
        // their paper storage, and player-shop sales shelves. Rebuilt per snapshot.
        private static HashSet<Storage> SceneExclusions()
        {
            var set = new HashSet<Storage>();
            try
            {
                foreach (var cb in UnityEngine.Object.FindObjectsByType<CraftingBase>(FindObjectsSortMode.None))
                {
                    if (cb == null) continue;
                    if (cb.OutputStorage != null) set.Add(cb.OutputStorage);
                    if (cb.InputOutputStorage != null) set.Add(cb.InputOutputStorage);
                }
                foreach (var t in UnityEngine.Object.FindObjectsByType<Toilet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                {
                    if (t == null) continue;
                    if (t.Bucket != null) set.Add(t.Bucket);
                    if (t.connectedStorages != null)
                        foreach (var c in t.connectedStorages)
                            if (c != null) set.Add(c);
                }
                foreach (var shop in UnityEngine.Object.FindObjectsByType<PlayerShopManager>(FindObjectsSortMode.None))
                    if (shop?.SalesStorages != null)
                        foreach (var r in shop.SalesStorages)
                        {
                            var st = r?.storage;
                            if (st != null) set.Add(st);
                        }
            }
            catch (Exception e) { Log.Exception("storage exclusions", e); }
            return set;
        }

        // Spec §4.4 "eligible storage". Ownership: the player's own (StorageOwner.ID 0) always; no owner at all (a
        // vacant flat, SRC/TenementApartmentPrefabInfo.cs:283-305) only for a station nobody else owns and only in the
        // station's own area; any NPC owner never, even with PermissionToTake.
        internal static bool Eligible(Storage s, CraftingBase station, FurniturePlaceable stationFp, HashSet<Storage> excluded)
        {
            if (s == null || !s.isActiveAndEnabled || s.Slots == null || s.Slots.Length == 0) return false;
            if (s.gameObject.scene != station.gameObject.scene || s.global) return false;
            if (excluded != null && excluded.Contains(s)) return false;
            var info = StaticOf(s);
            if (!info.Ok) return false;
            bool sameArea = stationFp != null && info.Furniture != null && info.Furniture.manager != null && info.Furniture.manager == stationFp.manager;
            var owner = s.StorageOwner;
            if (owner != null)
            {
                if (owner.ID != 0) return false;
            }
            else
            {
                bool stationFree = station.owner == null || station.owner.ID == 0;
                if (!stationFree || !sameArea) return false;
            }
            if (s.IsInteractDisabled()) return false;
            if (s._Lock != null && s._Lock.Locked) return false;
            if ((s.spawnSettings != null && s.spawnSettings.respawnEnabled) || s.RemoveItemsRandomly) return false;
            if (Cfg.Fetch.OnlyPlacedFurniture.Value && info.Furniture == null) return false;
            if (Cfg.Fetch.SameAreaOnly.Value && stationFp != null && !sameArea) return false;
            return true;
        }

        private static StaticInfo StaticOf(Storage s)
        {
            if (Static.TryGetValue(s, out var info)) return info;
            var go = s.gameObject;
            var fp = s.GetComponentInParent<FurniturePlaceable>();
            bool bin = fp != null && fp.furniture != null && Array.IndexOf(BinTitles, fp.furniture.title) >= 0;
            bool ok = !bin
                // Same GameObject: crafting stations, put-item spots, traders, vending machines, batteries, janitor bins,
                // tenement resource chests (contractor storage).
                && go.GetComponent<CraftingBase>() == null
                && go.GetComponent<PutItemSelection>() == null
                && go.GetComponent<Trade>() == null
                && go.GetComponent<VendingMachine>() == null
                && go.GetComponent<BatteryPowerSource>() == null
                && go.GetComponent<StorageJanitorAction>() == null
                && go.GetComponent<TenementResourceStorage>() == null
                // Self or a parent: NPC character storage, loose items, minitrain wagons.
                && s.GetComponentInParent<NPCInfo>() == null
                && s.GetComponentInParent<NPCController>() == null
                && s.GetComponentInParent<CollectibleItem>() == null
                && s.GetComponentInParent<MinitrainWagonData>() == null;
            info = new StaticInfo { Ok = ok, Furniture = fp };
            Static.Add(s, info);
            return info;
        }

        // Spec §4.4 stack filter, for every source: a real item the player may use. Never stolen goods (any owner), never
        // an NPC's stack (owner 1 or more) unless AllowPermittedStacks and it is a bought stack with permission.
        internal static bool StackOk(SlotController sc)
        {
            try
            {
                var st = sc?.itemStack;
                if (st == null || st.itemId <= 1 || st.itemAmount < 1) return false;
                var data = Game.DataOf(sc);
                if (data != null && (data.itemDisabled || (Vanilla.ItemMoveDisabled != null && Vanilla.ItemMoveDisabled(data)))) return false;
                var meta = st.Meta;
                if (st.ownerId == 1) return false;
                if (st.ownerId >= 2)
                {
                    if (!Cfg.Fetch.AllowPermittedStacks.Value) return false;
                    var perm = ItemMetaUtilities.GetMetaOfType<ItemOwnerPermission>(meta);
                    if (perm == null || !perm.ownerPermission || ItemMetaUtilities.GetMetaOfType<ItemShopData>(meta) != null) return false;
                }
                var stolen = ItemMetaUtilities.GetMetaOfType<ItemStolenData>(meta);
                if (stolen != null && stolen.playerStolen) return false;
                var liquid = ItemMetaUtilities.GetMetaOfType<ItemLiquidData>(meta);
                if (liquid != null && liquid.emptyContainerItem != null && liquid.emptyContainerItem.ID != st.itemId
                    && liquid.GetLiquidAmount(st.itemAmount) <= 0f) return false;
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("stack check", e);
                return false;
            }
        }

        // The source is still where it was and still allowed (re-checked before each move).
        internal static bool StillOk(Source src, CraftingBase station, HashSet<int> pins)
        {
            if (!StackOk(src.Sc)) return false;
            switch (src.Kind)
            {
                case SourceKind.Inventory:
                    return pins != null && !pins.Contains(src.Index) && Inventory.instance != null
                        && src.Index < Inventory.instance.Slots.Length && Inventory.instance.Slots[src.Index] == src.Sc;
                case SourceKind.Bag:
                    return Cfg.Fetch.UseWornBag.Value && BackpackStorage.instance != null && BackpackStorage.instance.GetStorage() == src.Storage;
                default:
                    return Cfg.Fetch.UseNearbyStorage.Value && src.Storage != null
                        && Eligible(src.Storage, station, station.GetComponentInParent<FurniturePlaceable>(), null);
            }
        }
    }
}

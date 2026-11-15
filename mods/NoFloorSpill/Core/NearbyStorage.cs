using System;
using System.Collections.Generic;
using UnityEngine;

namespace NoFloorSpill
{
    // Player-owned storage near a stove or machine (spec §4). Only storage the player owns (StorageOwner.ID == 0) in
    // the same scene and within range is used; shops, residents, NPCs, tenement resource chests, self-refilling or
    // self-emptying bins, crafting stations, toilet buckets and worn bags never are. Items go in through Storage.AddItemAndReturnRemaining,
    // which never drops, keeps the owner, clones the meta into new stacks and applies the storage's own category filter.
    // The caller drops whatever is left, as the game would. Main thread only.
    internal static class NearbyStorage
    {
        private struct Candidate
        {
            internal Storage S;
            internal bool Holds;
            internal float Dist;
            internal Vector3 Pos;
        }

        // Component and parent checks, which never change for a live object (rules 6, 7) and FurniturePlaceable (rule 11).
        private struct StaticInfo
        {
            internal bool Ok, Placed;
        }

        private static Storage[] _all;
        private static int _allFrame = -1;
        // Instance IDs of every Toilet.Bucket and Toilet.connectedStorages in the loaded scenes, rebuilt with _all.
        private static readonly HashSet<int> ToiletStorages = new HashSet<int>();
        private static readonly Dictionary<int, StaticInfo> Static = new Dictionary<int, StaticInfo>();

        private static readonly List<Storage> Ordered = new List<Storage>();
        private static readonly List<Candidate> Scratch = new List<Candidate>();
        private static int _orderFrame = -1, _orderOrigin, _orderItem;
        private static Kind _orderKind;

        private static readonly HashSet<int> Notified = new HashSet<int>();
        private static int _notifiedFrame = -1;

        // PlayerWillChangeLevel and LoadingDone: instance IDs are not reused within a session, but the scene's objects go away.
        internal static void ClearCache()
        {
            Static.Clear();
            _all = null;
            _allFrame = -1;
            ToiletStorages.Clear();
            _orderFrame = -1;
            Ordered.Clear();
        }

        // Moves as much of rem as fits into nearby storage. rem is lowered after each storage, so an exception midway
        // never makes the caller drop more than what is really left. Returns false when storage was skipped because the
        // game is still loading: the caller must then drop the rest as the game would (never delete it).
        internal static bool Insert(GameObject origin, Item item, ref int rem, int owner, object[] meta, GameObject excludeMachine,
            string src, Kind kind, params Storage[] alsoExclude)
        {
            if (rem <= 0 || item == null || origin == null) return true;
            if (SaveController.Loading)
            {
                // Storage contents may not be restored yet; the caller drops as the game would.
                Log.InfoOnce("loading|" + kind, $"Storage: {(kind == Kind.Refuse ? "refuse" : "junk")} arrived while the game was still " +
                    "loading, so nearby storage was not used for it; it dropped as in the game.");
                return false;
            }

            // A copy: storage callbacks must never be able to change the list we walk.
            var list = Candidates(origin, item, kind, excludeMachine, alsoExclude).ToArray();
            int frame = Time.frameCount;
            if (frame != _notifiedFrame)
            {
                Notified.Clear();
                _notifiedFrame = frame;
            }
            foreach (var s in list)
            {
                if (rem <= 0) break;
                if (s == null) continue;
                int left = s.AddItemAndReturnRemaining(item, rem, owner, meta);
                if (left < 0 || left >= rem) continue;
                int moved = rem - left;
                rem = left;
                Tally.Add(kind, Outcome.Stored, src, item, moved, Tally.CleanName(s.gameObject.name));
                if (Notified.Add(s.GetInstanceID())) s.ContentsExternallyModified();
            }
            return true;
        }

        // Cached per (frame, origin, item, kind); the exclusions are fixed for a given origin and kind.
        private static List<Storage> Candidates(GameObject origin, Item item, Kind kind, GameObject excludeMachine, Storage[] alsoExclude)
        {
            int frame = Time.frameCount;
            int originId = origin.GetInstanceID();
            if (frame == _orderFrame && originId == _orderOrigin && item.ID == _orderItem && kind == _orderKind) return Ordered;

            _orderFrame = frame;
            _orderOrigin = originId;
            _orderItem = item.ID;
            _orderKind = kind;
            Ordered.Clear();
            Scratch.Clear();

            if (_allFrame != frame || _all == null)
            {
                _all = UnityEngine.Object.FindObjectsByType<Storage>(FindObjectsSortMode.None);
                _allFrame = frame;
                RefreshToiletStorages();
            }

            var order = Cfg.Order.Value;
            Vector3 o = origin.transform.position;
            float radius = Cfg.Radius.Value, maxDy = Cfg.MaxHeightDifference.Value;
            var scene = origin.scene;

            foreach (var s in _all)
            {
                try
                {
                    if (!Eligible(s, scene, excludeMachine, alsoExclude)) continue;
                    Vector3 p = s.transform.position;
                    float dx = p.x - o.x, dz = p.z - o.z;
                    if (dx * dx + dz * dz > radius * radius || Mathf.Abs(p.y - o.y) > maxDy) continue;
                    bool holds = Holds(s, item.ID);
                    if (order == TargetOrder.SameItemOnly && !holds) continue;
                    Scratch.Add(new Candidate { S = s, Holds = order == TargetOrder.SameItemFirst && holds, Dist = (p - o).magnitude, Pos = p });
                }
                catch (Exception e) { Log.Exception("storage check", e); }
            }

            // Storage already holding the item first (SameItemFirst), then by distance; ties by x, then z (deterministic).
            Scratch.Sort((a, b) =>
            {
                if (a.Holds != b.Holds) return a.Holds ? -1 : 1;
                int c = a.Dist.CompareTo(b.Dist);
                if (c != 0) return c;
                c = a.Pos.x.CompareTo(b.Pos.x);
                return c != 0 ? c : a.Pos.z.CompareTo(b.Pos.z);
            });
            foreach (var c in Scratch) Ordered.Add(c.S);
            Scratch.Clear();
            return Ordered;
        }

        private static bool Eligible(Storage s, UnityEngine.SceneManagement.Scene scene, GameObject excludeMachine, Storage[] alsoExclude)
        {
            // 1. Live, enabled, has slots.
            if (s == null || !s.isActiveAndEnabled || s.Slots == null || s.Slots.Length == 0) return false;
            var go = s.gameObject;
            // 2. Same scene as the stove or machine.
            if (go.scene != scene) return false;
            // 3. Not a global storage (backpack, rucksack, cooler bag, shared trader stock).
            if (s.global) return false;
            // 4. Owned by the player. Vacant flats have no owner (TenementApartmentPrefabInfo.ChangeOwner).
            if (s.StorageOwner == null || s.StorageOwner.ID != 0) return false;
            // 5. Not disabled for interaction.
            if (s.IsInteractDisabled()) return false;
            // 8. Not a bin that refills or empties itself.
            if ((s.spawnSettings != null && s.spawnSettings.respawnEnabled) || s.RemoveItemsRandomly) return false;
            // 9. Not the stove or machine itself, nor its own storages.
            if (go == excludeMachine) return false;
            if (alsoExclude != null)
                foreach (var x in alsoExclude)
                    if (x != null && x == s) return false;
            // 12. Not a toilet's bucket or toilet-paper storage. The game adds excrement to the bucket with Storage.AddItem,
            //     which drops it on the floor when the bucket is full (PlayerStatsHungerAndThirst.cs:189-197). The bucket
            //     is its own GameObject next to the Toilet, so a parent check would miss it.
            if (ToiletStorages.Contains(s.GetInstanceID())) return false;
            // 10. Not locked.
            if (Cfg.SkipLocked.Value && s._Lock != null && s._Lock.Locked) return false;
            // 6, 7, 11.
            var info = StaticOf(s);
            if (!info.Ok) return false;
            if (Cfg.OnlyPlacedFurniture.Value && !info.Placed) return false;
            return true;
        }

        private static void RefreshToiletStorages()
        {
            ToiletStorages.Clear();
            foreach (var t in UnityEngine.Object.FindObjectsByType<Toilet>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                try
                {
                    if (t == null) continue;
                    if (t.Bucket != null) ToiletStorages.Add(t.Bucket.GetInstanceID());
                    if (t.connectedStorages != null)
                        foreach (var c in t.connectedStorages)
                            if (c != null) ToiletStorages.Add(c.GetInstanceID());
                }
                catch (Exception e) { Log.Exception("toilet check", e); }
            }
        }

        private static StaticInfo StaticOf(Storage s)
        {
            int id = s.GetInstanceID();
            if (Static.TryGetValue(id, out var info)) return info;
            var go = s.gameObject;
            // 6. Same GameObject: crafting stations, put-item spots, traders, vending machines, batteries, janitor bins,
            //    tenement resource chests. interactDisabled is only set in Start, and VendingMachine never sets it.
            bool ok = go.GetComponent<CraftingBase>() == null
                && go.GetComponent<PutItemSelection>() == null
                && go.GetComponent<Trade>() == null
                && go.GetComponent<VendingMachine>() == null
                && go.GetComponent<BatteryPowerSource>() == null
                && go.GetComponent<StorageJanitorAction>() == null
                && go.GetComponent<TenementResourceStorage>() == null
                // 7. Self or a parent: NPC character storage (owner 0 too), loose items, minitrain wagons.
                && s.GetComponentInParent<NPCInfo>() == null
                && s.GetComponentInParent<NPCController>() == null
                && s.GetComponentInParent<CollectibleItem>() == null
                && s.GetComponentInParent<MinitrainWagonData>() == null;
            // 11. Furniture the player placed.
            info = new StaticInfo { Ok = ok, Placed = s.GetComponentInParent<FurniturePlaceable>() != null };
            Static[id] = info;
            return info;
        }

        private static bool Holds(Storage s, int itemId)
        {
            foreach (var slot in s.Slots)
                if (slot?.itemStack != null && slot.itemStack.itemId == itemId && slot.itemStack.itemAmount > 0) return true;
            return false;
        }
    }
}

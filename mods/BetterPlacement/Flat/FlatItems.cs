using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace BetterPlacement
{
    // S-083 flat-item collision bypass (#5848, spec §3.7), off by default. A rug or curtain preview may overlap specific
    // contacts that can't matter for play: placed furniture that rises clear of a rug (or sticks out past a curtain), and
    // floor seams and thresholds lower than 3 cm under a rug. The bypass only ever removes those contacts from what
    // InsideTrigger records; it never writes ObjectPreview.inside, so walls, doors, the player, NoBuild areas and anything
    // usable stay on vanilla's path. Every frame (C3) each ignored contact is checked again and handed back to vanilla's
    // list when the rule no longer holds (the option was turned off, the rug moved to a wall, the furniture was taken),
    // exactly as vanilla's own OnTriggerEnter would have recorded it; vanilla's exit and Refresh clear it later.
    //
    // Not done in 0.1.0: curtains over window frames, sills and radiators. Those are static level geometry, as tall as
    // walls, so a rule for them would be new validity logic. With VerboseLogging on, the colliders that block a
    // whitelisted piece are logged to design 0.2.0. Vanilla's own ignoreOtherItems flag is never touched
    // (unused and buggy, SRC/InsideTrigger.cs:51-54).
    internal static class FlatItems
    {
        internal static bool Patched;

        private struct Info
        {
            internal bool Door, Trash, Usable;
        }

        private static readonly Dictionary<int, List<(Collider col, Surface surf)>> Ignored = new Dictionary<int, List<(Collider col, Surface surf)>>();
        private static readonly Dictionary<int, InsideTrigger> Triggers = new Dictionary<int, InsideTrigger>();
        private static readonly Dictionary<int, Info> FpInfo = new Dictionary<int, Info>();
        private static readonly Dictionary<int, bool> DoorLike = new Dictionary<int, bool>();
        private static readonly HashSet<int> Logged = new HashSet<int>();
        private static readonly List<int> Gone = new List<int>();
        private static readonly List<ObjectPreview> Promoted = new List<ObjectPreview>();
        private static InsideTrigger[] _previewTriggers = new InsideTrigger[0];
        private static int _previewId;

        private static readonly HashSet<int> CatSet = new HashSet<int>();
        private static readonly HashSet<string> ExtraSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<int, bool> ExtraHit = new Dictionary<int, bool>();
        private static string _catRaw, _extraRaw;

        internal static void Clear()
        {
            Ignored.Clear();
            Triggers.Clear();
            FpInfo.Clear();
            DoorLike.Clear();
            Logged.Clear();
            _previewTriggers = new InsideTrigger[0];
            _previewId = 0;
        }

        private static void SyncPreview(Transform preview)
        {
            int id = preview.GetInstanceID();
            if (id == _previewId) return;
            _previewId = id;
            Ignored.Clear();
            Triggers.Clear();
            Logged.Clear();
            _previewTriggers = preview.GetComponentsInChildren<InsideTrigger>(true);
        }

        // The selected piece is on the whitelist: furniture (not an item stack) whose building-menu category is in
        // Categories, or whose name (base or any skin's, as the building menu shows it) or id is in ExtraFurniture.
        // Both lists are parsed once per value; entries compare without case or spaces, so "License Plates" (the menu's
        // [Description] text) and "LicensePlates" (the enum name) both work. The per-call check allocates nothing.
        private static bool Whitelisted(BuildingSystem bs)
        {
            var cf = bs.currentFurniture;
            var f = cf?.furniture;
            if (f == null || cf.itemStack != null) return false;
            SyncLists();
            if (CatSet.Contains((int)f.category)) return true;
            if (ExtraSet.Count == 0) return false;
            int id = f.GetInstanceID();
            if (!ExtraHit.TryGetValue(id, out bool hit))
            {
                hit = InExtra(f.title) || InExtra(f.Id);
                if (!hit && f.skins != null)
                    foreach (var s in f.skins)
                        if (s != null && InExtra(s.title)) { hit = true; break; }
                ExtraHit[id] = hit;
            }
            return hit;
        }

        private static bool InExtra(string name) => !string.IsNullOrEmpty(name) && ExtraSet.Contains(Norm(name));

        private static string Norm(string s) => s.Replace(" ", "").Trim();

        private static void SyncLists()
        {
            string cats = Cfg.FlatItems.Categories.Value ?? "", extra = Cfg.FlatItems.ExtraFurniture.Value ?? "";
            if (cats != _catRaw)
            {
                _catRaw = cats;
                CatSet.Clear();
                foreach (var entry in Cfg.List(Cfg.FlatItems.Categories))
                {
                    string want = Norm(entry);
                    bool found = false;
                    foreach (Furniture.Category c in Enum.GetValues(typeof(Furniture.Category)))
                    {
                        if (!string.Equals(Norm(c.ToString()), want, StringComparison.OrdinalIgnoreCase) &&
                            !string.Equals(Norm(Description(c)), want, StringComparison.OrdinalIgnoreCase)) continue;
                        CatSet.Add((int)c);
                        found = true;
                    }
                    if (!found)
                        Log.WarnOnce("category|" + entry, $"FlatItems.Categories: \"{entry}\" is not a building menu category; it is ignored.");
                }
            }
            if (extra != _extraRaw)
            {
                _extraRaw = extra;
                ExtraSet.Clear();
                ExtraHit.Clear();
                foreach (var entry in Cfg.List(Cfg.FlatItems.ExtraFurniture)) ExtraSet.Add(Norm(entry));
            }
        }

        // The building menu's name for a category (BuildingSystemSlot.GetCategoryName, SRC/BuildingSystemSlot.cs:142-145).
        private static string Description(Furniture.Category c)
        {
            try
            {
                var field = typeof(Furniture.Category).GetField(c.ToString());
                var attr = field != null ? Attribute.GetCustomAttribute(field, typeof(DescriptionAttribute)) as DescriptionAttribute : null;
                return attr?.Description ?? "";
            }
            catch (Exception) { return ""; }
        }

        // Checks 1-5 of C1 (spec §3.7) for a contact of the current preview.
        private static bool Applies(InsideTrigger it, Collider other, out BuildingSystem bs)
        {
            bs = null;
            if (!Patched || !Cfg.FlatItems.Enabled.Value) return false;
            if (other == null || other.isTrigger) return false;
            bs = BuildingSystem.instance;
            if (bs == null || bs.currentPreview == null || it == null || !it.transform.IsChildOf(bs.currentPreview)) return false;
            if (Vanilla.AttachPoint(bs)) return false;
            return Whitelisted(bs);
        }

        // Used by the alignment overlap pre-check: would C1 ignore this contact right now?
        internal static bool WouldIgnore(InsideTrigger it, Collider other)
        {
            try { return Applies(it, other, out var bs) && Rule(bs, it, other, Game.SurfaceOf(Vanilla.Hit(bs).normal)); }
            catch (Exception) { return false; }
        }

        // C1 body: true skips vanilla's OnTriggerEnter for this contact.
        internal static bool TryIgnore(InsideTrigger it, Collider other)
        {
            if (!Applies(it, other, out var bs)) return false;
            SyncPreview(bs.currentPreview);
            var surf = Game.SurfaceOf(Vanilla.Hit(bs).normal);
            if (!Rule(bs, it, other, surf)) return false;
            int id = it.GetInstanceID();
            if (!Ignored.TryGetValue(id, out var list)) Ignored[id] = list = new List<(Collider col, Surface surf)>();
            bool present = false;
            foreach (var e in list)
                if (e.col == other) { present = true; break; }
            if (!present) list.Add((other, surf));
            Triggers[id] = it;
            if (Log.VerboseOn && Logged.Add(other.GetInstanceID()))
                Log.Verbose($"Flat items: {bs.currentFurniture.furniture.title} may overlap {Game.Path(other)} ({surf}).");
            return true;
        }

        // C2 body: the contact ended; vanilla's Remove and Refresh still run.
        internal static void OnExit(InsideTrigger it, Collider other)
        {
            if (it == null || other == null) return;
            if (!Ignored.TryGetValue(it.GetInstanceID(), out var list)) return;
            for (int i = list.Count - 1; i >= 0; i--)
                if (list[i].col == other) list.RemoveAt(i);
        }

        // C3 body: re-validates every ignored contact and promotes the ones that no longer pass.
        internal static void Revalidate(BuildingSystem bs)
        {
            var preview = bs.currentPreview;
            if (preview == null) return;
            SyncPreview(preview);
            if (Log.VerboseOn) LogBlocking(bs);
            if (Ignored.Count == 0) return;

            bool on = Cfg.FlatItems.Enabled.Value && !Vanilla.AttachPoint(bs) && Whitelisted(bs);
            var surf = Game.SurfaceOf(Vanilla.Hit(bs).normal);
            Gone.Clear();
            Promoted.Clear();
            foreach (var kv in Ignored)
            {
                Triggers.TryGetValue(kv.Key, out var it);
                if (it == null) { Gone.Add(kv.Key); continue; }
                var list = kv.Value;
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var (col, s) = list[i];
                    if (col == null || !col.enabled || !col.gameObject.activeInHierarchy)
                    {
                        list.RemoveAt(i);
                        continue;
                    }
                    if (on && s == surf && Rule(bs, it, col, surf)) continue;
                    // Promote: what vanilla's OnTriggerEnter would have done (SRC/InsideTrigger.cs:42-49).
                    var collision = Vanilla.Collision(it);
                    if (collision != null && !collision.Contains(col)) collision.Add(col);
                    if (it.objectPreview != null)
                    {
                        it.objectPreview.inside = true;
                        if (!Promoted.Contains(it.objectPreview)) Promoted.Add(it.objectPreview);
                    }
                    list.RemoveAt(i);
                    Log.Verbose("flat promote", col, k => $"Flat items: {Game.Path(k)} blocks again.");
                }
            }
            foreach (int id in Gone)
            {
                Ignored.Remove(id);
                Triggers.Remove(id);
            }
            // isPlaceable gets stricter this frame rather than at the next physics step.
            foreach (var op in Promoted)
                if (op != null) op.ChangeColor();
            Promoted.Clear();
        }

        // Data for #5309/#5848: while a whitelisted piece reads "Is inside an object!", log what it is inside.
        private static void LogBlocking(BuildingSystem bs)
        {
            if (!Whitelisted(bs)) return;
            foreach (var it in _previewTriggers)
            {
                if (it == null || it.objectPreview == null || !it.objectPreview.inside) continue;
                var list = Vanilla.Collision(it);
                if (list == null || list.Count == 0) continue;
                Log.Verbose("flat blocking", () =>
                {
                    var paths = new List<string>();
                    foreach (var c in list)
                        if (c != null) paths.Add(Game.Path(c));
                    return $"Flat items: {bs.currentFurniture.furniture.title} is inside {string.Join("; ", paths.ToArray())}.";
                });
                return;
            }
        }

        // The rule (spec §3.7, with deviation D3 in design.md): whether a contact of a flat piece on this surface may be
        // ignored.
        private static bool Rule(BuildingSystem bs, InsideTrigger it, Collider other, Surface surf)
        {
            if (surf != Surface.Floor && surf != Surface.Wall) return false;
            int layer = other.gameObject.layer;
            // Never: the player (10), usable level objects (23), NPCs (15), layer 20, and anything door-like.
            if (layer == 10 || layer == 23 || layer == 15 || layer == 20) return false;
            if (IsDoorLike(other)) return false;

            var n = Vanilla.Hit(bs).normal;
            float p = PlaneOf(it, n);
            var fp = Neighbours.OwnerOf(other);
            if (fp == null)
            {
                // (b) Level geometry: floors only, layer 0, nothing usable, and the whole collider within LowColliderHeight
                // of the rug plane (thresholds, seams, floor tiles). Walls, door frames and merged floor-and-wall meshes
                // are always taller.
                if (surf != Surface.Floor || layer != 0) return false;
                if (other.GetComponentInParent<IInteractable>() != null) return false;
                return Game.MaxAlong(other.bounds, n) <= p + Cfg.FlatItems.LowColliderHeight.Value;
            }

            // (a) Placed furniture of this flat or area, not the preview or the piece being moved, with no door or bin in it.
            if (!fp.gameObject.activeInHierarchy || fp.transform.IsChildOf(bs.currentPreview)) return false;
            if (ReferenceEquals(fp, Vanilla.MovingPlacedItem(bs))) return false;
            var mgr = BuildingSystem.activeManager;
            if (mgr == null || !(fp.manager == mgr || fp.transform.IsChildOf(mgr.transform))) return false;
            var info = InfoOf(fp);
            if (info.Door || info.Trash) return false;
            if (surf == Surface.Floor)
            {
                if (layer != 0 && layer != 12 && layer != 17) return false;
                // Loose items placed through building mode (a bottle, a crate of milk) keep blocking: they are things
                // you pick up, not furniture to stand on a rug.
                if (Footprints.IsLoose(fp)) return false;
            }
            else
            {
                // Curtains: never over anything usable (the whole piece), and never layer 17.
                if (layer != 0 && layer != 12) return false;
                if (info.Usable) return false;
            }
            // It must rise clear of the rug (stick out past the curtain), so the interaction ray still reaches it and flat
            // things (other rugs, placemats, coins) keep blocking and stay takeable.
            return Game.MaxAlong(other.bounds, n) >= p + Cfg.FlatItems.MinStickOut.Value;
        }

        // The rug or curtain plane: the InsideTrigger's own collider centre along the surface normal.
        private static float PlaneOf(InsideTrigger it, Vector3 n)
        {
            var c = it.GetComponent<Collider>();
            return Vector3.Dot(c != null ? c.bounds.center : it.transform.position, n);
        }

        private static bool IsDoorLike(Collider c)
        {
            int id = c.GetInstanceID();
            if (DoorLike.TryGetValue(id, out bool d)) return d;
            d = c.GetComponentInParent<Door>() != null || c.GetComponentInParent<Changelevel>() != null ||
                c.GetComponentInParent<FakeDoor>() != null || c.GetComponentInParent<DoorLockBypass>() != null ||
                c.GetComponentInParent<TarpDoor>() != null || c.GetComponentInParent<BuildingAreaWallsEffect>() != null;
            DoorLike[id] = d;
            return d;
        }

        private static Info InfoOf(FurniturePlaceable fp)
        {
            int id = fp.GetInstanceID();
            if (FpInfo.TryGetValue(id, out var info)) return info;
            try
            {
                info = new Info
                {
                    Door = fp.GetComponentInChildren<Door>(true) != null || fp.GetComponentInChildren<Changelevel>(true) != null ||
                           fp.GetComponentInChildren<FakeDoor>(true) != null || fp.GetComponentInChildren<DoorLockBypass>(true) != null ||
                           fp.GetComponentInChildren<TarpDoor>(true) != null,
                    Trash = fp.GetComponentInChildren<FurnitureTrash>(true) != null,
                    Usable = fp.GetComponentInChildren<IInteractable>(true) != null,
                };
            }
            catch (Exception) { info = new Info { Door = true, Trash = true, Usable = true }; }
            FpInfo[id] = info;
            return info;
        }
    }

    // C1: InsideTrigger.OnTriggerEnter(Collider) (SRC/InsideTrigger.cs:27). Skips vanilla only for a contact the rule
    // allows; on any error vanilla runs.
    [HarmonyPatch]
    internal static class FlatEnterPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TriggerEnter;

        private static bool Prefix(InsideTrigger __instance, Collider other)
        {
            try { return !FlatItems.TryIgnore(__instance, other); }
            catch (Exception e)
            {
                Log.Exception("flat items enter", e);
                return true;
            }
        }
    }

    // C2: InsideTrigger.OnTriggerExit(Collider) (SRC/InsideTrigger.cs:57). Never skips.
    [HarmonyPatch]
    internal static class FlatExitPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TriggerExit;

        private static void Prefix(InsideTrigger __instance, Collider other)
        {
            try { FlatItems.OnExit(__instance, other); }
            catch (Exception e) { Log.Exception("flat items exit", e); }
        }
    }

    // C3: BuildingSystem.ShowPreview(RaycastHit) (SRC/BuildingSystem.cs:1192), postfix after alignment.
    [HarmonyPatch]
    internal static class FlatPreviewPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ShowPreview;

        [HarmonyPriority(Priority.Normal)]
        private static void Postfix(BuildingSystem __instance)
        {
            try { FlatItems.Revalidate(__instance); }
            catch (Exception e) { Log.Exception("flat items check", e); }
        }
    }
}

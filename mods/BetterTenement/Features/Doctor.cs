using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BetterTenement
{
    // S-071 Tenement Doctor (spec §3.4.2): lists the preplaced junk that keeps an apartment under its cleared gates
    // (Primary >= 90, Secondary >= 80, TenementApartment.cs:177-192). Read-only unless Doctor.AllowForceClear is on.
    internal static class Doctor
    {
        internal static bool PanelPatched;

        internal const int GatePrimary = 90, GateSecondary = 80;

        internal sealed class Entry
        {
            internal string Name, Kind;
            internal int Group;
            internal GameObject Go;
            internal FurniturePlaceable Fp;
            internal bool Stale;
            internal float Distance = -1f;
            internal bool? Content;   // HoldsItems cache, per scan
        }

        internal sealed class Apt
        {
            internal TenementApartment A;
            internal FurnitureManager Fm;   // null outside the tenement building
            internal readonly List<Entry> Entries = new List<Entry>();
            internal int P, S, KP, KS;
            internal bool Available;
        }

        internal static readonly List<Apt> Apts = new List<Apt>();
        internal static int Selected;
        internal static bool AnyLive => Apts.Any(a => a.Fm != null);

        internal static Apt Current => Selected >= 0 && Selected < Apts.Count ? Apts[Selected] : null;

        // Smallest k with 100 - (int)((float)(cur - k) / pre * 100f) >= gate: vanilla's own float and int math.
        internal static int ClearMore(int cur, int pre, int gate)
        {
            if (pre <= 0) return 0;
            for (int k = 0; k <= cur; k++)
                if (100 - (int)((float)(cur - k) / (float)pre * 100f) >= gate) return k;
            return cur;
        }

        // D1 text. "Group 1/2" are placeholder names for vanilla's primary and secondary lists (open item O3).
        internal static string GroupLines(TenementApartment a)
        {
            int p = a.FurnitureClearPercentPrimary(), s = a.FurnitureClearPercentSecondary();
            int kp = ClearMore(a.currentFurnitureAmountPrimary, a.preplacedFurnitureAmountPrimary, GatePrimary);
            int ks = ClearMore(a.currentFurnitureAmountSecondary, a.preplacedFurnitureAmountSecondary, GateSecondary);
            return $"\nGroup 1: {p}% (needs {GatePrimary}) · {(p >= GatePrimary ? "met" : $"clear {kp} more")}" +
                   $"\nGroup 2: {s}% (needs {GateSecondary}) · {(s >= GateSecondary ? "met" : $"clear {ks} more")}";
        }

        internal static void Scan()
        {
            int keepNumber = Current != null ? Current.A.apartmentNumber : -1;
            Apts.Clear();
            var tc = TenementController.instance;
            if (tc == null || tc.apartments == null) return;
            var cam = Game.PlayerPosition();
            foreach (var a in tc.apartments.Where(x => x != null).OrderBy(x => x.apartmentNumber))
            {
                var apt = new Apt { A = a };
                var pref = a.tenementApartmentPrefabs;
                if (pref != null && pref.furnitureManager != null) apt.Fm = pref.furnitureManager;
                apt.P = a.FurnitureClearPercentPrimary();
                apt.S = a.FurnitureClearPercentSecondary();
                apt.KP = ClearMore(a.currentFurnitureAmountPrimary, a.preplacedFurnitureAmountPrimary, GatePrimary);
                apt.KS = ClearMore(a.currentFurnitureAmountSecondary, a.preplacedFurnitureAmountSecondary, GateSecondary);
                apt.Available = a.IsApartmentAvailable();
                if (apt.Fm != null)
                {
                    AddEntries(apt, apt.Fm.preplacedFurniturePrimary, 1, cam);
                    AddEntries(apt, apt.Fm.preplacedFurnitureSecondary, 2, cam);
                }
                Apts.Add(apt);
            }
            Selected = Apts.FindIndex(x => x.A.apartmentNumber == keepNumber);
        }

        private static void AddEntries(Apt apt, List<BuildingSystem.FurnitureInfo> list, int group, Vector3? cam)
        {
            if (list == null) return;
            foreach (var info in list)
            {
                if (info == null) continue;
                var e = new Entry { Group = group, Go = info.gameObject };
                e.Stale = e.Go == null;
                if (e.Stale)
                {
                    e.Kind = "stale entry";
                    e.Name = info.furniture != null ? info.furniture.title : "(object gone)";
                }
                else
                {
                    e.Fp = e.Go.GetComponent<FurniturePlaceable>();
                    if (e.Fp == null) e.Fp = e.Go.GetComponentInParent<FurniturePlaceable>();
                    e.Kind = KindOf(e.Go, e.Fp);
                    e.Name = info.furniture != null ? info.furniture.title
                        : e.Fp != null && e.Fp.item != null ? e.Fp.item.title
                        : e.Go.name;
                    if (cam.HasValue) e.Distance = Vector3.Distance(cam.Value, e.Go.transform.position);
                }
                if (string.IsNullOrEmpty(e.Name)) e.Name = "(unnamed)";
                apt.Entries.Add(e);
            }
        }

        private static string KindOf(GameObject go, FurniturePlaceable fp)
        {
            if (fp == null) return "object";
            string kind = fp.decal ? "decal"
                : go.GetComponentInChildren<FurnitureTrash>(true) != null ? "trash"
                : fp.item != null ? "collectible"
                : fp.furniture != null ? "furniture"
                : "other";
            return fp.cantTake ? kind + ", can't take" : kind;
        }

        // Default selection: the apartment whose furniture manager is active (the player is in build mode there),
        // otherwise the nearest live one.
        internal static void SelectDefault()
        {
            if (Selected >= 0 && Selected < Apts.Count) return;
            Selected = Apts.FindIndex(x => x.Fm != null && BuildingSystem.activeManager == x.Fm);
            if (Selected >= 0) return;
            var cam = Game.PlayerPosition();
            float best = float.MaxValue;
            for (int i = 0; i < Apts.Count; i++)
            {
                if (Apts[i].Fm == null || !cam.HasValue) continue;
                float d = Vector3.Distance(cam.Value, Apts[i].Fm.transform.position);
                if (d < best) { best = d; Selected = i; }
            }
            if (Selected < 0 && Apts.Count > 0) Selected = 0;
        }

        // ---- actions ----

        // Why neither Take nor Delete can run on this row, apart from the object's own cantTake flag (null = none).
        // Cheap checks only: CanTake is asked at action time, because it can show build-mode warnings.
        private static string BaseRefusal(Apt apt, Entry e)
        {
            if (!Cfg.AllowForceClear.Value) return "force-clear is off";
            if (BuildingSystem.instance == null || BuildingSystemMenu.instance == null || DestructibleList.instance == null) return "not available in this area";
            if (apt.Fm == null) return "not in this area";
            if (apt.A.resident != null) return "occupied flat";
            if (apt.A.onlyFurniture) return "furniture left by a tenant";
            if (e.Stale || e.Go == null) return "stale entry: use Recount";
            // FurniturePlaceable.Take also takes child and attached objects, and Destroyed() unregisters them at once
            // while Object.Destroy waits for the end of the frame: an entry that is no longer registered is gone.
            if (!apt.Fm.ContainsFurnitureGameObject(e.Go)) return "already taken";
            if (e.Fp == null) return "not a furniture object";
            if (e.Fp.manager == null) return "not managed by build mode";
            if (e.Fp.GetComponent<Mailbox>() != null) return "mailbox";
            if (string.IsNullOrEmpty(e.Fp.GUID)) return "no save id";
            return null;
        }

        // Why Take cannot run on this row (null = it can).
        internal static string TakeRefusal(Apt apt, Entry e)
        {
            string r = BaseRefusal(apt, e);
            if (r != null) return r;
            return e.Fp.cantTake ? "cannot be taken" : null;
        }

        // Delete is offered only in TakeOrDelete mode and only for the one thing Take cannot do: an object the game
        // marks as not takeable, in a vacant flat, that holds nothing. The vanilla take path keeps objects with
        // saveable content (BuildingSystem.HasSaveableContent, BuildingSystem.cs:1722, used by AddFurniture); Delete
        // would destroy that content with the object, so such rows are refused. fresh = re-check the contents now.
        internal static bool CanDelete(Apt apt, Entry e, bool fresh = false)
        {
            if (!Cfg.AllowForceClear.Value || Cfg.ClearMode.Value != ForceClearMode.TakeOrDelete) return false;
            if (BaseRefusal(apt, e) != null || !e.Fp.cantTake) return false;
            return !HoldsItems(e, fresh);
        }

        internal static bool HoldsItems(Entry e, bool fresh)
        {
            if (!fresh && e.Content.HasValue) return e.Content.Value;
            bool held = true;   // fail safe: unknown counts as holding something
            try
            {
                held = e.Go != null && (BuildingSystem.instance != null && BuildingSystem.instance.HasSaveableContent(e.Go)
                                        || e.Go.GetComponentsInChildren<Storage>(true).Any(st => st != null && !st.IsEmpty())
                                        || e.Go.GetComponentsInChildren<LiquidStorage>(true).Any(l => l != null && !l.IsEmpty()));
            }
            catch (Exception ex) { Log.Exception("Doctor content check", ex); }
            e.Content = held;
            return held;
        }

        // Exactly what taking by hand does (BuildingSystem.cs:1034-1042), as TenementUpgradeCleanupArea.ReturnFurniture does.
        internal static bool Take(Apt apt, Entry e, out string msg)
        {
            msg = TakeRefusal(apt, e);
            if (msg != null) return false;
            var fp = e.Fp;
            if (!fp.CanTake(false)) { msg = "Inventory full"; return false; }
            string guid = fp.GUID;
            fp.UseItem();
            fp.UpdateMeta();
            fp.Take(fp.gameObject, new List<FurniturePlaceable>(), out _);
            apt.Fm.RemovePreplacedFurniture(null);
            msg = $"Took {e.Name}.";
            Log.Verbose($"Doctor: took \"{e.Name}\" in apartment {apt.A.apartmentNumber} (GUID {guid}).");
            return true;
        }

        // Permanent: the object and every GUID in its hierarchy are recorded as destroyed (DestructibleList), so the
        // preplaced object never loads again. The window asks for confirmation first.
        internal static bool Delete(Apt apt, Entry e, out string msg)
        {
            msg = null;
            if (!CanDelete(apt, e, fresh: true))
            {
                msg = e.Content == true ? $"{e.Name} holds items; empty it first." : "Delete is not allowed for this row.";
                return false;
            }
            var go = e.Go;
            var guids = new HashSet<string> { e.Fp.GUID };
            foreach (var mb in go.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null) continue;
                var f = mb.GetType().GetField("GUID", BindingFlags.Public | BindingFlags.Instance);
                if (f == null || f.FieldType != typeof(string)) continue;
                if (f.GetValue(mb) is string g && !string.IsNullOrEmpty(g)) guids.Add(g);
            }
            foreach (var g in guids) DestructibleList.instance.DestroyMapItem(g);
            e.Fp.Destroyed();
            Object.Destroy(go);
            apt.Fm.RemovePreplacedFurniture(go);
            msg = $"Deleted {e.Name}.";
            Log.Verbose($"Doctor: deleted \"{e.Name}\" in apartment {apt.A.apartmentNumber} (GUIDs {string.Join(", ", guids)}).");
            return true;
        }

        internal static int TakeableCount(Apt apt) => apt.Entries.Count(e => TakeRefusal(apt, e) == null);

        // Takes every takeable entry of the snapshot. Objects taken along with an earlier entry (its child or attached
        // furniture) are skipped: TakeRefusal sees them unregistered, and the set below also covers them in case a
        // child is not unregistered by the parent's Take.
        internal static int ClearAll(Apt apt, out string msg)
        {
            int done = 0, failed = 0;
            var gone = new HashSet<GameObject>();
            foreach (var e in apt.Entries.ToList())
            {
                if (e.Go == null || gone.Contains(e.Go) || (e.Fp != null && gone.Contains(e.Fp.gameObject))) continue;
                if (TakeRefusal(apt, e) != null) continue;
                try
                {
                    CollectTakenWith(e.Fp, gone);
                    if (Take(apt, e, out _)) done++;
                    else failed++;
                }
                catch (Exception ex) { failed++; Log.Exception("Doctor clear all", ex); }
            }
            msg = $"Took {done} leftovers" + (failed > 0 ? $"; {failed} could not be taken (inventory full?)" : "") + ".";
            return done;
        }

        private static void CollectTakenWith(FurniturePlaceable fp, HashSet<GameObject> into)
        {
            if (fp == null || Vanilla.ChildFurnitures == null || Vanilla.AttachedFurnitures == null) return;
            var stack = new Stack<FurniturePlaceable>();
            stack.Push(fp);
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                foreach (var list in new[] { Vanilla.ChildFurnitures(cur), Vanilla.AttachedFurnitures(cur) })
                {
                    if (list == null) continue;
                    foreach (var other in list)
                    {
                        if (other == null || other == cur || !into.Add(other.gameObject)) continue;
                        stack.Push(other);
                    }
                }
            }
        }

        // Drops entries whose object is gone and refreshes the counters (FurnitureManager.cs:466-486). In memory only.
        internal static void Recount(Apt apt)
        {
            if (apt == null || apt.Fm == null) return;
            int before = apt.Fm.preplacedFurniturePrimary.Count + apt.Fm.preplacedFurnitureSecondary.Count;
            apt.Fm.RemovePreplacedFurniture(null);
            int after = apt.Fm.preplacedFurniturePrimary.Count + apt.Fm.preplacedFurnitureSecondary.Count;
            Log.Verbose($"Doctor: recount in apartment {apt.A.apartmentNumber}: {before} -> {after} entries.");
        }
    }

    // D1: ApartmentInfoUI.ApartmentIsNotAvailable(TenementApartment) (private, SRC/ApartmentInfoUI.cs:251).
    [HarmonyPatch(typeof(ApartmentInfoUI), "ApartmentIsNotAvailable", new[] { typeof(TenementApartment) })]
    internal static class ApartmentInfoPatch
    {
        private static void Postfix(ApartmentInfoUI __instance, TenementApartment apartment)
        {
            try
            {
                if (!Doctor.PanelPatched || !Cfg.DoctorPanelDetails.Value || apartment == null || !apartment.examined || Vanilla.NotAvailableInfoText == null) return;
                var t = Vanilla.NotAvailableInfoText(__instance);
                if (t == null || t.text == null || !t.text.Contains("Cleared:")) return;
                t.text += Doctor.GroupLines(apartment);
            }
            catch (Exception e) { Log.Exception("apartment panel details", e); }
        }
    }
}

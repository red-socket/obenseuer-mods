using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace BetterPlacement
{
    // S-083 remembered angle (spec §3.5). Each furniture type ("f:" + Furniture.Id) and item ("i:" + itemId) remembers the
    // yaw it was last placed at; selecting it again in the building menu starts there. Wallpaper and the scraper have no
    // key. M1 stores on a real placement; M2 restores when the selection changes to a different key, by writing
    // currentrot before the preview is built (ShowPreview re-applies currentrot every frame, SRC/BuildingSystem.cs:1433,
    // so this is the point that counts, not InstantiatePreview).
    //
    // Session scope (default) keeps a dictionary for the game session (it survives doors; vanilla resets currentrot per
    // scene) and writes nothing. Save scope mirrors it into one GlobalState string, "betterplacement.angles" =
    // "v1|" + lines of key TAB degrees: plain BCL data, no mod $type, saved inside the game's own save (and Save Guard's
    // transaction) at the next save or door. Without the mod the key is orphaned and ignored.
    internal static class AngleMemory
    {
        internal const string StateKey = "betterplacement.angles";
        private const int MaxEntries = 512;

        internal static bool Patched;

        private static readonly Dictionary<string, float> Angles = new Dictionary<string, float>();
        private static readonly Dictionary<string, long> Used = new Dictionary<string, long>();
        private static long _tick;
        private static string _lastKey;
        private static GlobalState _bound;

        internal static void OnSceneLoaded() => _lastKey = null;

        // The remembered-angle key of a selection, or null for wallpaper, the scraper and anything unknown.
        internal static string Key(BuildingSystem.FurnitureInfo fi, ItemStack stack)
        {
            try
            {
                if (stack != null) return ItemKey(stack);
                if (fi == null) return null;
                if (fi.itemStack != null) return ItemKey(fi.itemStack);
                var f = fi.furniture;
                if (f == null) return null;
                string id = f.Id;
                return string.IsNullOrEmpty(id) ? null : "f:" + id;
            }
            catch (Exception) { return null; }
        }

        private static string ItemKey(ItemStack s)
        {
            if (s.itemId <= 0) return null;
            var item = s.itemReference?.Item;
            if (item != null && (ItemMetaUtilities.GetMetaOfType<ItemWallpaperData>(item.Meta) != null ||
                                 ItemMetaUtilities.GetMetaOfType<ItemScraperData>(item.Meta) != null)) return null;
            return "i:" + s.itemId.ToString(CultureInfo.InvariantCulture);
        }

        // M1 body: a placement is about to happen (A3 let it through and the preview is valid).
        internal static void OnPlace(BuildingSystem bs)
        {
            if (!Cfg.Rotation.RememberAngle.Value || bs.IsMovingPlacedItem || Vanilla.AttachPoint(bs)) return;
            var preview = bs.currentPreview;
            if (preview == null || !preview.gameObject.activeInHierarchy || preview.childCount == 0) return;
            var child = preview.GetChild(0);
            if (!child.gameObject.activeSelf) return;
            var op = child.GetComponent<ObjectPreview>();
            if (op == null || !op.isPlaceable) return;
            string key = Key(bs.currentFurniture, null);
            if (key == null) return;
            float deg = (float)Math.Round(Mathf.Repeat(Vanilla.CurrentRot(bs).y, 360f), 1);
            Sync();
            bool changed = !Angles.TryGetValue(key, out float old) || Mathf.Abs(old - deg) > 0.05f;
            Set(key, deg);
            if (changed)
            {
                Log.Verbose("angle store", (deg, key), a => $"Angles: remembered {a.deg:0.#}° for {a.key}.");
                if (Cfg.Rotation.RememberScope.Value == RememberScope.Save) Write();
            }
        }

        // M2 prefix body: a new selection. Returns nothing; writes currentrot when there is something to restore.
        internal static void OnSelect(BuildingSystem bs, BuildingSystem.FurnitureInfo buildObject, ItemStack itemStack, bool wholeItemStack)
        {
            if (!Cfg.Rotation.RememberAngle.Value || wholeItemStack) return;
            if (buildObject == null && itemStack == null) return;
            string key = Key(buildObject, itemStack);
            if (key == null || key == _lastKey) return;
            Sync();
            if (!Angles.TryGetValue(key, out float deg)) return;
            Used[key] = ++_tick;
            Vanilla.CurrentRot(bs) = new Vector3(0f, deg, 0f);
            Log.Verbose("angle restore", (deg, key), a => $"Angles: {a.key} starts at {a.deg:0.#}°.");
        }

        internal static void AfterSelect(BuildingSystem bs) => _lastKey = Key(bs.currentFurniture, null);

        private static void Set(string key, float deg)
        {
            Angles[key] = deg;
            Used[key] = ++_tick;
            if (Angles.Count <= MaxEntries) return;
            string oldest = null;
            long best = long.MaxValue;
            foreach (var kv in Used)
                if (kv.Value < best) { best = kv.Value; oldest = kv.Key; }
            if (oldest == null) return;
            Angles.Remove(oldest);
            Used.Remove(oldest);
        }

        // Save scope: re-read the save's string whenever GlobalState is a new instance (a level load). Session scope never
        // reads or writes it.
        private static void Sync()
        {
            if (Cfg.Rotation.RememberScope.Value != RememberScope.Save) return;
            var gs = GlobalState.instance;
            if (gs == null || ReferenceEquals(gs, _bound)) return;
            _bound = gs;
            Angles.Clear();
            Used.Clear();
            string raw = null;
            try { raw = gs.GetState(StateKey)?.data as string; }
            catch (Exception e) { Log.Exception("angles read", e); }
            if (string.IsNullOrEmpty(raw)) return;
            if (!Parse(raw))
            {
                Angles.Clear();
                Used.Clear();
                Log.WarnOnce("angles parse", "Angles: the remembered angles in this save could not be read; starting with none.");
            }
        }

        private static bool Parse(string raw)
        {
            if (!raw.StartsWith("v1|", StringComparison.Ordinal)) return false;
            foreach (var line in raw.Substring(3).Split('\n'))
            {
                if (line.Length == 0) continue;
                int tab = line.IndexOf('\t');
                if (tab <= 0) return false;
                if (!float.TryParse(line.Substring(tab + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out float deg)) return false;
                Set(line.Substring(0, tab), deg);
            }
            return true;
        }

        private static void Write()
        {
            try
            {
                var gs = GlobalState.instance;
                if (gs == null) return;
                var sb = new StringBuilder("v1|");
                bool first = true;
                foreach (var kv in Angles)
                {
                    if (!first) sb.Append('\n');
                    first = false;
                    sb.Append(kv.Key).Append('\t').Append(kv.Value.ToString("0.#", CultureInfo.InvariantCulture));
                }
                gs.SetState(StateKey, true, sb.ToString());
                _bound = gs;
            }
            catch (Exception e) { Log.Exception("angles write", e); }
        }
    }

    // M1: BuildingSystem.Place() (SRC/BuildingSystem.cs:1485). A late prefix that never skips: after the settle guard
    // (A3, first), it sees through __runOriginal whether the placement goes ahead.
    [HarmonyPatch]
    internal static class PlaceRememberPatch
    {
        private static MethodBase TargetMethod() => Vanilla.Place;

        [HarmonyPriority(Priority.Low)]
        private static void Prefix(BuildingSystem __instance, bool __runOriginal)
        {
            if (!__runOriginal) return;
            try { AngleMemory.OnPlace(__instance); }
            catch (Exception e) { Log.Exception("angle store", e); }
        }
    }

    // M2: BuildingSystem.ChangeCurrentObject(FurnitureInfo, ItemStack, int, bool) (SRC/BuildingSystem.cs:778). The
    // prefix restores before InstantiatePreview reads currentrot; the postfix records the new selection's key, so the
    // re-selections after Place, after ChangeRotationMode's UpdateMenu and after ChangeAlternativePrefab (:1448) keep the
    // angle the player has now. Item Move sets currentrot first and passes wholeItemStack (:1073-1074); furniture Move sets
    // currentrot after selecting (:1094); either way the moved object's own angle wins.
    [HarmonyPatch]
    internal static class ChangeObjectPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ChangeCurrentObject;

        private static void Prefix(BuildingSystem __instance, BuildingSystem.FurnitureInfo buildObject, ItemStack itemStack, bool wholeItemStack)
        {
            try { AngleMemory.OnSelect(__instance, buildObject, itemStack, wholeItemStack); }
            catch (Exception e) { Log.Exception("angle restore", e); }
        }

        private static void Postfix(BuildingSystem __instance)
        {
            try { AngleMemory.AfterSelect(__instance); }
            catch (Exception e) { Log.Exception("angle restore", e); }
        }
    }
}

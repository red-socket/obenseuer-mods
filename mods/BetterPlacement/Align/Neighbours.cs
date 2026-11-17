using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterPlacement
{
    // Nearby placed pieces (spec §4.4). One sphere query with the building system's own layer mask, ignoring triggers
    // (the preview's colliders are all triggers), re-run only when the preview moved more than 0.10 m, after 0.10 s or
    // when the preview changed. Colliders map to their FurniturePlaceable through a cache that also remembers "none".
    // Kept: active pieces of the current flat or area (activeManager, so never the next flat's furniture through a shared
    // wall), not part of the preview, not the item being moved, not a trash bin, and loose items only with LooseItems.
    internal static class Neighbours
    {
        private const float RequeryDistance = 0.10f;
        private const float RequerySeconds = 0.10f;
        private const int MaxBuffer = 4096;

        // Grows when a query fills it (vanilla's own pattern, BuildingSystemMenu.TryAutoOpenNearbyDoors): layersHit includes
        // layer 0, so walls, pipes and props share the buffer with furniture, and a full buffer drops pieces arbitrarily.
        private static Collider[] Buf = new Collider[256];
        private static readonly Dictionary<int, FurniturePlaceable> Owners = new Dictionary<int, FurniturePlaceable>();
        private static readonly Dictionary<int, bool> Trash = new Dictionary<int, bool>();
        private static readonly List<FurniturePlaceable> Found = new List<FurniturePlaceable>();
        private static readonly HashSet<int> Seen = new HashSet<int>();

        private static Vector3 _lastCentre;
        private static float _lastTime = -100f;
        private static int _lastPreview;

        internal static void Clear()
        {
            Owners.Clear();
            Trash.Clear();
            Found.Clear();
            _lastPreview = 0;
        }

        // The placed piece a collider belongs to, or null for level geometry. Cached by collider id.
        internal static FurniturePlaceable OwnerOf(Collider c)
        {
            if (c == null) return null;
            int id = c.GetInstanceID();
            // A cached "none" stays none; a cached piece that has since been destroyed is looked up again.
            if (Owners.TryGetValue(id, out var fp) && (ReferenceEquals(fp, null) || fp != null)) return fp;
            fp = c.GetComponentInParent<FurniturePlaceable>();
            Owners[id] = fp;
            return fp;
        }

        // Pieces near the preview; the caller filters for surface and distance.
        internal static List<FurniturePlaceable> Near(BuildingSystem bs, Transform preview, Vector3 centre, float radius)
        {
            int pid = preview.GetInstanceID();
            float now = Time.unscaledTime;
            if (pid == _lastPreview && (centre - _lastCentre).sqrMagnitude < RequeryDistance * RequeryDistance && now - _lastTime < RequerySeconds)
            {
                Prune(bs, preview);
                return Found;
            }
            _lastPreview = pid;
            _lastCentre = centre;
            _lastTime = now;
            Found.Clear();
            Seen.Clear();
            int n = Physics.OverlapSphereNonAlloc(centre, radius, Buf, bs.layersHit, QueryTriggerInteraction.Ignore);
            while (n == Buf.Length && Buf.Length < MaxBuffer)
            {
                Buf = new Collider[Buf.Length * 2];
                n = Physics.OverlapSphereNonAlloc(centre, radius, Buf, bs.layersHit, QueryTriggerInteraction.Ignore);
            }
            if (n == Buf.Length) Log.WarnOnce("neighbour buffer", $"Align: more than {MaxBuffer} colliders near the piece; some nearby furniture may be skipped.");
            for (int i = 0; i < n; i++)
            {
                var fp = OwnerOf(Buf[i]);
                Buf[i] = null;
                if (fp == null || !Seen.Add(fp.GetInstanceID())) continue;
                if (Keep(bs, preview, fp)) Found.Add(fp);
            }
            return Found;
        }

        private static void Prune(BuildingSystem bs, Transform preview)
        {
            for (int i = Found.Count - 1; i >= 0; i--)
                if (!Keep(bs, preview, Found[i])) Found.RemoveAt(i);
        }

        private static bool Keep(BuildingSystem bs, Transform preview, FurniturePlaceable fp)
        {
            if (fp == null || !fp.gameObject.activeInHierarchy) return false;
            if (fp.transform.IsChildOf(preview)) return false;
            if (ReferenceEquals(fp, Vanilla.MovingPlacedItem(bs))) return false;
            var mgr = BuildingSystem.activeManager;
            if (mgr == null || !(fp.manager == mgr || fp.transform.IsChildOf(mgr.transform))) return false;
            if (IsTrash(fp)) return false;
            if (Footprints.IsLoose(fp) && !Cfg.Align.LooseItems.Value) return false;
            return true;
        }

        private static bool IsTrash(FurniturePlaceable fp)
        {
            int id = fp.GetInstanceID();
            if (Trash.TryGetValue(id, out bool t)) return t;
            try { t = fp.GetComponentInChildren<FurnitureTrash>(true) != null; }
            catch (Exception) { t = true; }
            Trash[id] = t;
            return t;
        }
    }
}

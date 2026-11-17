using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterPlacement
{
    // An oriented box in a frame F, stored as centre and half-size in F's rotation-only space (world units).
    internal struct Box
    {
        internal Vector3 C, H;
        internal bool Valid;
    }

    // Projection of a box onto a world axis: centre and half-length along it, and how parallel the box is to it.
    internal struct Span
    {
        internal float C, H, Par;
        internal float Lo => C - H;
        internal float Hi => C + H;
    }

    // Visual and collision footprints (spec §4.3). The frame is child(0) for furniture (its world rotation also covers a
    // pre-placed root's yaw, matching vanilla's own split at SRC/BuildingSystem.cs:1087-1091 and :1593-1600), the root
    // for loose items, and "rotate" (child 0) for the preview. Boxes are built once per object and cached by instance
    // id; the caches are cleared on every scene load and when the building menu closes.
    //
    // Visual box (centre and edge lines): the union of enabled mesh and skinned renderers, skipping renderers owned by
    // another FurniturePlaceable (nested pre-placed objects) and renderers whose materials are all the game's
    // "invisible" helpers; without a renderer, the collision box. Collision box (flush lines): the solid colliders on
    // layers 0, 12, 17 and 23 for neighbours; for the preview, the InsideTrigger colliders the game checks for overlap
    // (an item preview has none: its visual box).
    internal static class Footprints
    {
        private const int SolidMask = (1 << 0) | (1 << 12) | (1 << 17) | (1 << 23);

        private static readonly Dictionary<int, (Box vis, Box col)> Cache = new Dictionary<int, (Box vis, Box col)>();
        private static readonly Dictionary<int, bool> Loose = new Dictionary<int, bool>();
        private static readonly List<Renderer> Renderers = new List<Renderer>();
        private static readonly List<Collider> Colliders = new List<Collider>();
        private static readonly Vector3[] Corners = new Vector3[8];

        private static int _previewId;
        private static Box _previewVis, _previewCol;
        private static bool _previewHasInside;

        internal static void Clear()
        {
            Cache.Clear();
            Loose.Clear();
            _previewId = 0;
        }

        // Vanilla's split between a loose item and furniture (SRC/BuildingSystem.cs:1056-1057), cached per piece.
        internal static bool IsLoose(FurniturePlaceable fp)
        {
            if (fp == null) return false;
            int id = fp.GetInstanceID();
            if (Loose.TryGetValue(id, out bool loose)) return loose;
            loose = fp.furniture == null && fp.GetComponentInChildren<CollectibleItem>() != null;
            Loose[id] = loose;
            return loose;
        }

        internal static Transform FrameOf(FurniturePlaceable fp) =>
            IsLoose(fp) || fp.transform.childCount == 0 ? fp.transform : fp.transform.GetChild(0);

        internal static (Box vis, Box col) Of(FurniturePlaceable fp, Transform frame)
        {
            int id = fp.GetInstanceID();
            if (Cache.TryGetValue(id, out var b)) return b;
            var col = Solid(fp.transform, frame, fp);
            var vis = Visual(fp.transform, frame, fp);
            if (!vis.Valid) vis = col;
            b = (vis, col);
            Cache[id] = b;
            return b;
        }

        // The preview's boxes in the frame of "rotate", computed on the first call for each preview instance.
        internal static void Preview(Transform preview, Transform rotate, out Box vis, out Box col, out bool hasInside)
        {
            int id = preview.GetInstanceID();
            if (id != _previewId)
            {
                _previewId = id;
                _previewCol = InsideBox(preview, rotate, out _previewHasInside);
                _previewVis = Visual(preview, rotate, null);
                if (!_previewVis.Valid) _previewVis = _previewCol;
                if (!_previewCol.Valid) _previewCol = _previewVis;
            }
            vis = _previewVis;
            col = _previewCol;
            hasInside = _previewHasInside;
        }

        // Exact projection of an oriented box onto u.
        internal static Span Project(Box b, Transform frame, Vector3 u)
        {
            var q = frame.rotation;
            var ax = q * Vector3.right;
            var ay = q * Vector3.up;
            var az = q * Vector3.forward;
            float dx = Mathf.Abs(Vector3.Dot(ax, u)), dy = Mathf.Abs(Vector3.Dot(ay, u)), dz = Mathf.Abs(Vector3.Dot(az, u));
            return new Span
            {
                C = Vector3.Dot(frame.position + q * b.C, u),
                H = dx * b.H.x + dy * b.H.y + dz * b.H.z,
                Par = Mathf.Max(dx, Mathf.Max(dy, dz)),
            };
        }

        private static Box Visual(Transform root, Transform frame, FurniturePlaceable owner)
        {
            var acc = new Acc(frame);
            Renderers.Clear();
            root.GetComponentsInChildren(false, Renderers);
            foreach (var r in Renderers)
            {
                try
                {
                    if (r == null || !r.enabled) continue;
                    if (!(r is MeshRenderer) && !(r is SkinnedMeshRenderer)) continue;
                    if (owner != null && r.GetComponentInParent<FurniturePlaceable>() != owner) continue;
                    if (AllInvisible(r)) continue;
                    if (r is SkinnedMeshRenderer smr)
                    {
                        var t = smr.rootBone != null ? smr.rootBone : smr.transform;
                        acc.Add(t, smr.localBounds);
                    }
                    else
                    {
                        var mf = r.GetComponent<MeshFilter>();
                        if (mf == null || mf.sharedMesh == null) continue;
                        acc.Add(r.transform, mf.sharedMesh.bounds);
                    }
                }
                catch (Exception) { }
            }
            Renderers.Clear();
            return acc.Result();
        }

        private static bool AllInvisible(Renderer r)
        {
            var mats = r.sharedMaterials;
            if (mats == null || mats.Length == 0) return false;
            foreach (var m in mats)
                if (m == null || m.name.IndexOf("invisible", StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        private static Box Solid(Transform root, Transform frame, FurniturePlaceable owner)
        {
            var acc = new Acc(frame);
            Colliders.Clear();
            root.GetComponentsInChildren(false, Colliders);
            foreach (var c in Colliders)
            {
                try
                {
                    if (c == null || !c.enabled || c.isTrigger) continue;
                    if ((SolidMask & (1 << c.gameObject.layer)) == 0) continue;
                    if (owner != null && c.GetComponentInParent<FurniturePlaceable>() != owner) continue;
                    AddCollider(ref acc, c);
                }
                catch (Exception) { }
            }
            Colliders.Clear();
            return acc.Result();
        }

        // The preview's InsideTrigger colliders: on an InsideTrigger's GameObject, or on a body that carries one.
        private static Box InsideBox(Transform preview, Transform frame, out bool hasInside)
        {
            var acc = new Acc(frame);
            hasInside = false;
            Colliders.Clear();
            preview.GetComponentsInChildren(true, Colliders);
            foreach (var c in Colliders)
            {
                try
                {
                    if (c == null || InsideOf(c) == null) continue;
                    hasInside = true;
                    AddCollider(ref acc, c);
                }
                catch (Exception) { }
            }
            Colliders.Clear();
            return acc.Result();
        }

        internal static InsideTrigger InsideOf(Collider c)
        {
            var it = c.GetComponent<InsideTrigger>();
            if (it != null) return it;
            var rb = c.attachedRigidbody;
            return rb != null ? rb.GetComponent<InsideTrigger>() : null;
        }

        private static void AddCollider(ref Acc acc, Collider c)
        {
            switch (c)
            {
                case BoxCollider b:
                    acc.Add(c.transform, new Bounds(b.center, b.size));
                    break;
                case MeshCollider m:
                    if (m.sharedMesh != null) acc.Add(c.transform, m.sharedMesh.bounds);
                    break;
                case CapsuleCollider cap:
                {
                    var size = new Vector3(cap.radius * 2f, cap.radius * 2f, cap.radius * 2f);
                    size[Mathf.Clamp(cap.direction, 0, 2)] = Mathf.Max(cap.height, cap.radius * 2f);
                    acc.Add(c.transform, new Bounds(cap.center, size));
                    break;
                }
                case SphereCollider s:
                    acc.Add(c.transform, new Bounds(s.center, Vector3.one * s.radius * 2f));
                    break;
            }
        }

        // Accumulates local boxes, each turned into its 8 corners in the frame's rotation-only space.
        private struct Acc
        {
            private readonly Vector3 _origin;
            private readonly Quaternion _inv;
            private Vector3 _min, _max;
            private bool _any;

            internal Acc(Transform frame)
            {
                _origin = frame.position;
                _inv = Quaternion.Inverse(frame.rotation);
                _min = Vector3.positiveInfinity;
                _max = Vector3.negativeInfinity;
                _any = false;
            }

            internal void Add(Transform t, Bounds local)
            {
                var c = local.center;
                var e = local.extents;
                int k = 0;
                for (int x = -1; x <= 1; x += 2)
                for (int y = -1; y <= 1; y += 2)
                for (int z = -1; z <= 1; z += 2)
                    Corners[k++] = t.TransformPoint(c + new Vector3(x * e.x, y * e.y, z * e.z));
                foreach (var w in Corners)
                {
                    var p = _inv * (w - _origin);
                    _min = Vector3.Min(_min, p);
                    _max = Vector3.Max(_max, p);
                }
                _any = true;
            }

            internal Box Result() =>
                _any ? new Box { C = (_min + _max) * 0.5f, H = (_max - _min) * 0.5f, Valid = true } : default;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterPlacement
{
    // Each line kind is its own value, so a switch between, say, the two edges of one neighbour is a signature change.
    internal enum SnapKind : byte { None, FlushPlus, FlushMinus, EdgeMin, EdgeMax, Centre }

    // The alignment algorithm (spec §4), run from the ShowPreview postfix (A2). Per frame: guards, the surface frame,
    // footprints, nearby pieces, MatchRotation on engagement, then per axis the nearest centre, edge or flush line within
    // reach (with hysteresis), then up to five candidate moves, each checked for support, tunnelling and (advisory)
    // overlap. The first that passes moves currentpos and the preview; if none passes, the preview stays where vanilla put
    // it. Snapping never decides validity: vanilla's ChangeColor still does, and the settle guard waits for it.
    internal static class Solver
    {
        // Correctness invariants, kept out of config (spec §8).
        private const float ProbeLift = 0.05f, ProbeDepth = 0.10f, ProbeTolerance = 0.002f, ProbeAngle = 1f;
        private const float LevelTolerance = 0.25f;
        private const float WallFront = 0.15f, WallBehind = -0.02f;
        private const float MatchNormalAngle = 2f;
        private const int MaxAttempts = 5;
        private const float SwitchMargin = 0.02f, TieMargin = 0.001f;
        private const float GuideLift = 0.004f, GuideOverhang = 0.05f;
        private const int OverlapMask = (1 << 0) | (1 << 10) | (1 << 12) | (1 << 17) | (1 << 23);
        // Item previews: the solid layers of placed pieces, and how far the visual box is shrunk on every side so an
        // item standing on a table or leaning on a shelf back doesn't count as inside it.
        private const int ItemMask = (1 << 0) | (1 << 12) | (1 << 17) | (1 << 23);
        private const float ItemShrink = 0.008f;

        private sealed class Nb
        {
            internal FurniturePlaceable Fp;
            internal int Id;
            internal Transform F;
            internal Box Vis, Col;
            internal readonly Span[] V = new Span[2];
            internal readonly Span[] C = new Span[2];
            internal float Gap;
        }

        private struct Cand
        {
            internal bool Valid;
            internal int Nb;
            internal SnapKind Kind;
            internal float Delta, Line, Gap;
        }

        private struct Line
        {
            internal int Nb;
            internal SnapKind Kind;
            internal bool Same(Cand c) => c.Valid && c.Nb == Nb && c.Kind == Kind && Kind != SnapKind.None;
        }

        private struct Sig : IEquatable<Sig>
        {
            internal int N1, N2;
            internal SnapKind K1, K2;
            internal bool Empty => K1 == SnapKind.None && K2 == SnapKind.None;
            public bool Equals(Sig o) => N1 == o.N1 && N2 == o.N2 && K1 == o.K1 && K2 == o.K2;
        }

        private static readonly List<Nb> Pool = new List<Nb>();
        private static readonly List<Nb> Nbs = new List<Nb>();
        private static readonly List<Cand>[] Cands = { new List<Cand>(), new List<Cand>() };
        private static readonly Line[] Held = new Line[2];
        private static readonly RaycastHit[] RayBuf = new RaycastHit[16];
        private static readonly Collider[] BoxBuf = new Collider[64];
        private static readonly List<(Collider col, InsideTrigger it)> InsideCols = new List<(Collider col, InsideTrigger it)>();
        private static readonly Comparison<Nb> ByGap = (a, b) => a.Gap.CompareTo(b.Gap);

        private static Sig _sig;
        private static int _previewId;
        // MatchRotation has had its one chance in this engagement (spec §4.5): it matched, it looked at the nearest
        // neighbour and found it too far off, or the player rotated the piece. Cleared only when alignment switches off
        // (EndEngagement), on a new preview and on Reset; a transient Release (sloped hit, preview hidden) keeps it, so a
        // change of nearest neighbour never turns the piece again.
        private static bool _matchDone;
        private static bool _crafting;
        // Vanilla's UpdateConnectionIndicator call inside this ShowPreview is skipped (D1 patch) and run once after the
        // snap instead, so crafting furniture costs one scene-wide scan per frame, as in vanilla, and the lines start
        // at the snapped spot.
        private static bool _deferConnection;
        internal static bool DeferringConnection => _deferConnection;
        internal static bool ConnectionPatched;

        // Per-frame measurements.
        private static Vector3 _n, _u1, _u2;
        private static Span[] _pv = new Span[2], _pc = new Span[2];
        private static Span _pn;

        internal static void Reset()
        {
            Release();
            _previewId = 0;
            _matchDone = false;
            Nbs.Clear();
        }

        // Alignment switched off (AlignInput): the next engagement may match rotation again.
        internal static void EndEngagement() => _matchDone = false;

        // A2 prefix: decide whether vanilla's connection-indicator call in this ShowPreview waits for the snap.
        internal static void BeginFrame(BuildingSystem bs)
        {
            _deferConnection = false;
            AlignInput.Tick();
            var preview = bs.currentPreview;
            _deferConnection = ConnectionPatched && Vanilla.UpdateConnection != null && _crafting && Cfg.Align.Enabled.Value &&
                               AlignInput.Active && preview != null && preview.GetInstanceID() == _previewId;
        }

        // A2 postfix, after Run (also after a failed one): the deferred connection-indicator update, from the final spot.
        internal static void EndFrame(BuildingSystem bs)
        {
            if (!_deferConnection) return;
            _deferConnection = false;
            Vanilla.UpdateConnection(bs);
        }

        // Alignment does not apply this frame: hide the guides, forget the held lines and, if a snap was applied, record
        // the release as a change for the settle guard.
        internal static void Release()
        {
            Guides.Hide();
            Held[0] = default;
            Held[1] = default;
            if (_sig.Empty) return;
            _sig = default;
            Settle.Changed();
            Log.Verbose("align release", () => "Align: released.");
        }

        internal static void Run(BuildingSystem bs, RaycastHit hit2)
        {
            AlignInput.Tick();
            var preview = bs.currentPreview;
            if (!Cfg.Align.Enabled.Value || !AlignInput.Active || Time.timeScale <= 0f || preview == null ||
                !preview.gameObject.activeInHierarchy || Vanilla.AttachPoint(bs)) { Release(); return; }
            var n = Vanilla.Hit(bs).normal;
            var surf = Game.SurfaceOf(n);
            if (surf == Surface.None || surf == Surface.Sloped || preview.childCount == 0) { Release(); return; }
            var rotate = preview.GetChild(0);
            if (!rotate.gameObject.activeSelf || BuildingSystem.activeManager == null) { Release(); return; }
            bool looseItem = bs.currentFurniture != null && bs.currentFurniture.itemStack != null;
            if ((looseItem && !Cfg.Align.LooseItems.Value) || (surf == Surface.Wall && !Cfg.Align.WallItems.Value)) { Release(); return; }
            var op = rotate.GetComponent<ObjectPreview>();
            // A floorAndWall piece on a wall hangs from its alternativeOrigin (:1417-1424): wall axes, no MatchRotation.
            bool hanging = surf == Surface.Wall && op != null && op.alternativeOrigin != null && Vanilla.PreviewType(op) == PlaceType.floorAndWall;

            int pid = preview.GetInstanceID();
            if (pid != _previewId)
            {
                _previewId = pid;
                Held[0] = default;
                Held[1] = default;
                _matchDone = false;
                _sig = default;
                _crafting = HasCrafting(bs);
                CacheInside(preview);
            }

            Footprints.Preview(preview, rotate, out var pVis, out var pCol, out bool hasInside);
            if (!pVis.Valid) { Release(); return; }
            _n = n;

            float camDist = 0f;
            try { if (PlayerCamera.instance != null) camDist = Vector3.Distance(PlayerCamera.instance.transform.position, hit2.point); }
            catch (Exception) { }
            float T = Cfg.Align.SnapDistance.Value * (Cfg.Align.ScaleWithDistance.Value ? Mathf.Clamp(camDist / 3f, 1f, 2f) : 1f);
            float Tr = T * Cfg.Align.ReleaseFactor.Value;

            if (!Axes(surf, hanging, rotate)) { Release(); return; }
            var centreW = rotate.position + rotate.rotation * pVis.C;
            var found = Neighbours.Near(bs, preview, centreW, Cfg.Align.SearchRadius.Value + pVis.H.magnitude);
            Measure(bs, surf, rotate, pVis, pCol, found, hit2.point);

            // MatchRotation (§4.5): at most once per engagement, before the position solve. The first qualifying
            // neighbour gets the one chance; rotating the piece by hand before that gives it up.
            bool matched = false;
            if (Cfg.Align.MatchRotation.Value && !hanging && !_matchDone)
            {
                if (RotationInput()) _matchDone = true;
                else
                {
                    Nb best = null;
                    foreach (var nb in Nbs)
                    {
                        if (nb.Gap > Cfg.Align.MatchDistance.Value) break;
                        if (Vector3.Angle(nb.F.up, n) <= MatchNormalAngle) { best = nb; break; }
                    }
                    if (best != null)
                    {
                        _matchDone = true;
                        if (Match(bs, rotate, best))
                        {
                            matched = true;
                            Axes(surf, hanging, rotate);
                            Measure(bs, surf, rotate, pVis, pCol, found, hit2.point);
                        }
                    }
                }
            }

            // Per-axis candidates (§4.6).
            var c1 = Choose(0, T, Tr, out var s1);
            var c2 = Choose(1, T, Tr, out var s2);

            // Candidate moves (§4.7).
            Cand a1 = default, a2 = default;
            Vector3 delta = Vector3.zero;
            bool ok = false;
            for (int attempt = 0; attempt < MaxAttempts && !ok; attempt++)
            {
                Cand x = default, y = default;
                switch (attempt)
                {
                    case 0: if (!c1.Valid || !c2.Valid) continue; x = c1; y = c2; break;
                    case 1: if (!c1.Valid) continue; x = c1; break;
                    case 2: if (!c2.Valid) continue; y = c2; break;
                    case 3: if (!s1.Valid) continue; x = s1; break;
                    case 4: if (!s2.Valid) continue; y = s2; break;
                }
                var d = (x.Valid ? x.Delta * _u1 : Vector3.zero) + (y.Valid ? y.Delta * _u2 : Vector3.zero);
                if (!Check(bs, preview, rotate, pVis, hit2, d, hasInside)) continue;
                a1 = x;
                a2 = y;
                delta = d;
                ok = true;
            }

            // Apply (§4.8).
            if (ok && delta.sqrMagnitude > 0f)
            {
                ref Vector3 cp = ref Vanilla.CurrentPos(bs);
                cp += delta;
                preview.position = cp;
                // Deferred: EndFrame redraws once. Otherwise (first frame of a preview, or the D1 patch is off) redraw now.
                if (_crafting && !_deferConnection && Vanilla.UpdateConnection != null) Vanilla.UpdateConnection(bs);
            }
            Held[0] = new Line { Nb = a1.Valid ? a1.Nb : 0, Kind = a1.Valid ? a1.Kind : SnapKind.None };
            Held[1] = new Line { Nb = a2.Valid ? a2.Nb : 0, Kind = a2.Valid ? a2.Kind : SnapKind.None };
            var sig = new Sig { N1 = Held[0].Nb, K1 = Held[0].Kind, N2 = Held[1].Nb, K2 = Held[1].Kind };
            if (!sig.Equals(_sig))
            {
                var was = _sig;
                _sig = sig;
                Settle.Changed();
                if (Log.VerboseOn) LogChange(was, sig, a1, a2);
            }
            if (matched) Settle.Changed();

            DrawGuides(hit2.point + delta, delta, a1, a2);
        }

        // ---- Frame and measurements ----

        // Floor and ceiling: the preview's own right/forward on the surface. Wall: along the wall and up the wall; depth is
        // never changed.
        private static bool Axes(Surface surf, bool hanging, Transform rotate)
        {
            if (surf == Surface.Wall || hanging)
            {
                _u1 = Vector3.Cross(_n, Vector3.up);
                _u2 = Vector3.ProjectOnPlane(Vector3.up, _n);
            }
            else
            {
                _u1 = Vector3.ProjectOnPlane(rotate.right, _n);
                _u2 = Vector3.ProjectOnPlane(rotate.forward, _n);
            }
            if (_u1.sqrMagnitude < 1e-6f || _u2.sqrMagnitude < 1e-6f) return false;
            _u1.Normalize();
            _u2.Normalize();
            return true;
        }

        // Preview spans (world) and the kept neighbours with their spans and in-plane gaps, nearest first.
        private static void Measure(BuildingSystem bs, Surface surf, Transform rotate, Box pVis, Box pCol, List<FurniturePlaceable> found, Vector3 point)
        {
            _pv[0] = Footprints.Project(pVis, rotate, _u1);
            _pv[1] = Footprints.Project(pVis, rotate, _u2);
            _pc[0] = Footprints.Project(pCol, rotate, _u1);
            _pc[1] = Footprints.Project(pCol, rotate, _u2);
            _pn = Footprints.Project(pVis, rotate, _n);

            Nbs.Clear();
            float sN = Vector3.Dot(Vanilla.CurrentPos(bs), _n);
            float lo = Mathf.Min(sN, _pn.Lo) - LevelTolerance, hi = Mathf.Max(sN, _pn.Hi) + LevelTolerance;
            float wall = Vector3.Dot(point, _n);
            float radius = Cfg.Align.SearchRadius.Value;
            int k = 0;
            foreach (var fp in found)
            {
                if (fp == null) continue;
                var F = Footprints.FrameOf(fp);
                var (vis, col) = Footprints.Of(fp, F);
                if (!vis.Valid) continue;
                if (!col.Valid) col = vis;
                var sn = Footprints.Project(vis, F, _n);
                if (surf == Surface.Wall)
                {
                    if (sn.Lo - wall > WallFront || sn.Hi - wall < WallBehind) continue;
                }
                else if (sn.Hi < lo || sn.Lo > hi) continue;

                if (k >= Pool.Count) Pool.Add(new Nb());
                var nb = Pool[k];
                nb.Fp = fp;
                nb.Id = fp.GetInstanceID();
                nb.F = F;
                nb.Vis = vis;
                nb.Col = col;
                float g2 = 0f;
                for (int a = 0; a < 2; a++)
                {
                    var u = a == 0 ? _u1 : _u2;
                    nb.V[a] = Footprints.Project(vis, F, u);
                    nb.C[a] = Footprints.Project(col, F, u);
                    float g = Mathf.Max(0f, Mathf.Abs(nb.V[a].C - _pv[a].C) - (nb.V[a].H + _pv[a].H));
                    g2 += g * g;
                }
                nb.Gap = Mathf.Sqrt(g2);
                if (nb.Gap > radius) continue;
                Nbs.Add(nb);
                k++;
            }
            Nbs.Sort(ByGap);
            int max = Cfg.Align.MaxNeighbours.Value;
            if (Nbs.Count > max) Nbs.RemoveRange(max, Nbs.Count - max);
        }

        // ---- MatchRotation (§4.5) ----

        private static bool RotationInput()
        {
            try
            {
                var kb = InputManager.instance != null ? InputManager.instance.keyBindings : null;
                if (kb != null && Vanilla.BindingKey != null)
                {
                    var k = Vanilla.BindingKey(kb, "Rotate Object");
                    if (k != KeyCode.None && Input.GetKey(k)) return true;
                }
                if (TriggerFilter.Active) return TriggerFilter.LastNonZeroFrame == Time.frameCount;
                return InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller &&
                       Mathf.Abs(Input.GetAxis("RightTrigger") - Input.GetAxis("LeftTrigger")) > 0.1f;
            }
            catch (Exception) { return true; }
        }

        // Turns the preview to the neighbour's yaw when it is within RotationTolerance, modulo 90°. Writes currentrot, so the
        // result persists after release (vanilla accumulates rotation on it), and the child yaw, since vanilla's :1433 has
        // already run this frame.
        private static bool Match(BuildingSystem bs, Transform rotate, Nb nb)
        {
            float yN = PlacementYaw(nb.F.rotation);
            ref Vector3 rot = ref Vanilla.CurrentRot(bs);
            float d = Mathf.DeltaAngle(rot.y, yN);
            d -= 90f * Mathf.Round(d / 90f);
            if (Mathf.Abs(d) > Cfg.Align.RotationTolerance.Value || Mathf.Abs(d) < 0.01f) return false;
            rot.y += d;
            rotate.localEulerAngles = rot;
            Log.Verbose("align match", (d, nb.Fp), a => $"Align: turned {a.d:+0.#;-0.#}° to match {Game.Title(a.Fp)}.");
            return true;
        }

        // BuildingSystem.GetSurfaceRotation and GetItemPlacementRotation, same code as SRC/BuildingSystem.cs:1180-1190.
        private static Quaternion SurfaceRotation(Vector3 normal) =>
            Quaternion.LookRotation(Vector3.Angle(normal, Vector3.down) > 85f && Vector3.Angle(normal, Vector3.up) > 85f ? Vector3.up : Vector3.forward, normal);

        private static float PlacementYaw(Quaternion world)
        {
            var v = Quaternion.Inverse(SurfaceRotation(world * Vector3.up)) * (world * Vector3.forward);
            return Mathf.Atan2(v.x, v.z) * Mathf.Rad2Deg;
        }

        // ---- Per-axis solve (§4.6) ----

        private static Cand Choose(int a, float T, float Tr, out Cand second)
        {
            var list = Cands[a];
            list.Clear();
            int b = 1 - a;
            float cosTol = Mathf.Cos(Cfg.Align.AxisTolerance.Value * Mathf.Deg2Rad);
            bool centres = Cfg.Align.Centres.Value, edges = Cfg.Align.Edges.Value, flush = Cfg.Align.Flush.Value;
            float gap = Cfg.Align.FlushGap.Value;
            var P = _pv[a];
            var Q = _pc[a];
            var Qb = _pc[b];
            foreach (var nb in Nbs)
            {
                var V = nb.V[a];
                var C = nb.C[a];
                if (centres) Add(list, Tr, nb, SnapKind.Centre, V.C - P.C, V.C);
                if (edges && V.Par >= cosTol && P.Par >= cosTol)
                {
                    Add(list, Tr, nb, SnapKind.EdgeMin, V.Lo - P.Lo, V.Lo);
                    Add(list, Tr, nb, SnapKind.EdgeMax, V.Hi - P.Hi, V.Hi);
                }
                if (flush && C.Par >= cosTol && Q.Par >= cosTol)
                {
                    var Cb = nb.C[b];
                    if (Cb.Lo < Qb.Hi && Qb.Lo < Cb.Hi)
                    {
                        Add(list, Tr, nb, SnapKind.FlushPlus, C.Hi + gap - Q.Lo, C.Hi);
                        Add(list, Tr, nb, SnapKind.FlushMinus, C.Lo - gap - Q.Hi, C.Lo);
                    }
                }
            }

            var best = Best(list, T, default);
            Cand chosen = best;
            foreach (var c in list)
            {
                if (!Held[a].Same(c)) continue;
                // Keep last frame's line until it is released or another is more than 2 cm nearer.
                if (!best.Valid || Mathf.Abs(best.Delta) >= Mathf.Abs(c.Delta) - SwitchMargin) chosen = c;
                break;
            }
            second = chosen.Valid ? Best(list, T, chosen) : default;
            return chosen;
        }

        private static int Rank(SnapKind k) =>
            k == SnapKind.FlushPlus || k == SnapKind.FlushMinus ? 0 : k == SnapKind.EdgeMin || k == SnapKind.EdgeMax ? 1 : 2;

        private static string KindName(SnapKind k) => Rank(k) == 0 ? "flush" : Rank(k) == 1 ? "edge" : "centre";

        private static void Add(List<Cand> list, float Tr, Nb nb, SnapKind kind, float delta, float line)
        {
            if (Mathf.Abs(delta) > Tr) return;
            list.Add(new Cand { Valid = true, Nb = nb.Id, Kind = kind, Delta = delta, Line = line, Gap = nb.Gap });
        }

        // The nearest line within T, skipping the excluded one (and any line within 1 mm of it). Ties within 1 mm go to
        // flush, then edge, then centre, then the nearer neighbour.
        private static Cand Best(List<Cand> list, float T, Cand exclude)
        {
            Cand best = default;
            foreach (var c in list)
            {
                float d = Mathf.Abs(c.Delta);
                if (d > T) continue;
                if (exclude.Valid && Mathf.Abs(c.Line - exclude.Line) <= TieMargin) continue;
                if (!best.Valid) { best = c; continue; }
                float bd = Mathf.Abs(best.Delta);
                if (d < bd - TieMargin) { best = c; continue; }
                if (d > bd + TieMargin) continue;
                int r = Rank(c.Kind), br = Rank(best.Kind);
                if (r < br || (r == br && c.Gap < best.Gap)) best = c;
            }
            return best;
        }

        // ---- Candidate checks (§4.7) ----

        private static bool Check(BuildingSystem bs, Transform preview, Transform rotate, Box pVis, RaycastHit hit2, Vector3 d, bool hasInside)
        {
            if (d.sqrMagnitude < 1e-10f) return true;
            int mask = bs.layersHit;
            var n = _n;

            // 1. Support: the same surface, at the same height and angle, under the moved crosshair point. Keeps vanilla's
            // crosshair-based inputs true at the snapped spot (surface class, IsPlaceableSurface, FurnitureSurfaceArea and
            // the currentPlaceable parent link, :1590).
            if (!Physics.Raycast(hit2.point + d + n * ProbeLift, -n, out var h, ProbeDepth, mask, QueryTriggerInteraction.Ignore))
                return Reject("support", "no surface under the snapped spot", null);
            bool same = h.collider == hit2.collider;
            if (!same)
            {
                var a = Neighbours.OwnerOf(h.collider);
                same = a != null && a == Neighbours.OwnerOf(hit2.collider);
            }
            if (!same) return Reject("support", "a different surface", h.collider);
            if (Mathf.Abs(h.distance - ProbeLift) > ProbeTolerance) return Reject("support", "a step in the surface", h.collider);
            if (Vector3.Angle(h.normal, n) > ProbeAngle) return Reject("support", "a tilted surface", h.collider);
            if (h.collider.GetComponent<BuildingAreaWallsEffect>() != null) return Reject("support", "the area's edge", h.collider);

            // 2. Tunnelling: no level geometry (a wall, an area plane, a partition) between the two points.
            float len = d.magnitude;
            int k = Physics.RaycastNonAlloc(hit2.point + n * ProbeLift, d / len, RayBuf, len, mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < k; i++)
            {
                var c = RayBuf[i].collider;
                RayBuf[i] = default;
                if (c != null && Neighbours.OwnerOf(c) == null) return Reject("tunnel", "level geometry in the way", c);
            }

            // 3. Advisory overlap with the preview's InsideTrigger colliders at the moved pose. Too strict only skips a
            // snap; too loose is caught by vanilla's own check and the settle guard. An item preview has no InsideTrigger
            // (SRC/BuildingSystem.cs:818-832) and vanilla never checks items for overlap, so for it the moved visual box
            // must stay clear of every placed piece: a snap never merges two bottles.
            if (hasInside)
            {
                if (!Overlap(preview, d, out var hitCol)) return Reject("overlap", "it would overlap", hitCol);
            }
            else if (!ItemOverlap(preview, rotate, pVis, d, out var hitCol)) return Reject("overlap", "it would overlap", hitCol);
            return true;
        }

        private static bool ItemOverlap(Transform preview, Transform rotate, Box pVis, Vector3 d, out Collider blocking)
        {
            blocking = null;
            var q = rotate.rotation;
            var centre = rotate.position + q * pVis.C + d;
            var ext = Vector3.Max(pVis.H - new Vector3(ItemShrink, ItemShrink, ItemShrink), new Vector3(0.0005f, 0.0005f, 0.0005f));
            int k = Physics.OverlapBoxNonAlloc(centre, ext, BoxBuf, q, ItemMask, QueryTriggerInteraction.Ignore);
            bool clear = true;
            for (int i = 0; i < k; i++)
            {
                var other = BoxBuf[i];
                BoxBuf[i] = null;
                if (!clear || other == null || other.transform.IsChildOf(preview)) continue;
                // Placed pieces only; level geometry is the support and tunnel checks' business.
                if (Neighbours.OwnerOf(other) == null) continue;
                blocking = other;
                clear = false;
            }
            return clear;
        }

        private static bool Overlap(Transform preview, Vector3 d, out Collider blocking)
        {
            blocking = null;
            foreach (var (ic, it) in InsideCols)
            {
                if (ic == null || !ic.enabled || !ic.gameObject.activeInHierarchy) continue;
                var b = ic.bounds;
                var centre = b.center + d;
                var ext = Vector3.Max(b.extents, new Vector3(0.0005f, 0.0005f, 0.0005f));
                var pos = ic.transform.position + d;
                var rot = ic.transform.rotation;
                int k = Physics.OverlapBoxNonAlloc(centre, ext, BoxBuf, Quaternion.identity, OverlapMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < k; i++)
                {
                    var other = BoxBuf[i];
                    BoxBuf[i] = null;
                    if (other == null || other.transform.IsChildOf(preview)) continue;
                    if (FlatItems.WouldIgnore(it, other)) continue;
                    try
                    {
                        if (Physics.ComputePenetration(ic, pos, rot, other, other.transform.position, other.transform.rotation, out _, out float dist) && dist > 0.0005f)
                        {
                            blocking = other;
                            return false;
                        }
                    }
                    catch (Exception) { }
                }
                k = Physics.OverlapBoxNonAlloc(centre, ext, BoxBuf, Quaternion.identity, ~0, QueryTriggerInteraction.Collide);
                for (int i = 0; i < k; i++)
                {
                    var other = BoxBuf[i];
                    BoxBuf[i] = null;
                    if (other == null || !other.isTrigger || other.transform.IsChildOf(preview)) continue;
                    if (other.CompareTag("NoBuild"))
                    {
                        blocking = other;
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool Reject(string topic, string why, Collider c)
        {
            if (Log.VerboseOn)
                Log.Verbose("align reject " + topic, (topic, why, c), a => $"Align: snap skipped ({a.topic}): {a.why}{(a.c != null ? ": " + Game.Path(a.c) : "")}.");
            return false;
        }

        // ---- Per-preview caches ----

        private static void CacheInside(Transform preview)
        {
            InsideCols.Clear();
            var cols = preview.GetComponentsInChildren<Collider>(true);
            foreach (var c in cols)
            {
                var it = Footprints.InsideOf(c);
                if (it != null) InsideCols.Add((c, it));
            }
        }

        // Vanilla only draws connection lines for furniture whose prefab has a CraftingUpgrade or CraftingBase (:736-738).
        private static bool HasCrafting(BuildingSystem bs)
        {
            try
            {
                var prefab = bs.currentFurniture?.furniture?.prefab;
                if (prefab == null) return false;
                return prefab.GetComponentInChildren<CraftingUpgrade>() != null || prefab.GetComponentInChildren<CraftingBase>() != null;
            }
            catch (Exception) { return false; }
        }

        // ---- Guides (§4.9) and logging ----

        private static void DrawGuides(Vector3 surfacePoint, Vector3 delta, Cand a1, Cand a2)
        {
            Guides.Begin();
            var basePoint = surfacePoint + _n * GuideLift;
            for (int a = 0; a < 2; a++)
            {
                var c = a == 0 ? a1 : a2;
                if (!c.Valid) continue;
                var u = a == 0 ? _u1 : _u2;
                var v = a == 0 ? _u2 : _u1;
                int b = 1 - a;
                var nb = Find(c.Nb);
                if (nb == null) continue;
                float shift = Vector3.Dot(delta, v);
                float lo = Mathf.Min(nb.V[b].Lo, _pv[b].Lo + shift) - GuideOverhang;
                float hi = Mathf.Max(nb.V[b].Hi, _pv[b].Hi + shift) + GuideOverhang;
                var onLine = basePoint + u * (c.Line - Vector3.Dot(basePoint, u));
                float at = Vector3.Dot(onLine, v);
                var colour = Cfg.Colour(c.Kind == SnapKind.Centre ? Cfg.Align.CentreColour : Cfg.Align.EdgeColour);
                Guides.Draw(onLine + v * (lo - at), onLine + v * (hi - at), colour);
            }
            Guides.End();
        }

        private static Nb Find(int id)
        {
            foreach (var nb in Nbs)
                if (nb.Id == id) return nb;
            return null;
        }

        private static void LogChange(Sig was, Sig now, Cand a1, Cand a2)
        {
            if (now.Empty)
            {
                Log.Verbose("align release", () => "Align: released.");
                return;
            }
            string Part(Cand c, string axis) =>
                c.Valid ? $"{axis}: {Game.Title(Find(c.Nb)?.Fp)} {KindName(c.Kind)} ({c.Delta:+0.000;-0.000} m)" : null;
            var parts = new List<string>();
            var p1 = Part(a1, "axis 1");
            var p2 = Part(a2, "axis 2");
            if (p1 != null) parts.Add(p1);
            if (p2 != null) parts.Add(p2);
            string verb = was.Empty ? "snapped" : "switched";
            Log.Verbose("align change", () => $"Align: {verb}, {string.Join("; ", parts.ToArray())}.");
        }
    }
}

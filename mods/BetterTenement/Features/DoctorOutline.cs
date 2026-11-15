using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTenement
{
    // Wireframe boxes around the selected apartment's leftovers, drawn with GL lines after the main camera renders
    // (built-in pipeline), through walls. Materials of the objects themselves are never swapped: build mode captures
    // "original" materials on hover and could keep a highlight permanently.
    internal static class DoctorOutline
    {
        internal static readonly List<Bounds> Boxes = new List<Bounds>();
        internal static bool Keep;   // "Keep outlines" after the window closes, until the scene changes

        private static Material _mat;
        private static bool _shaderMissing;
        private static int _builtAlive = -1;   // live entries when the boxes were built
        private static readonly Vector3[] Corners = new Vector3[8];   // scratch for Box: no allocation per frame

        internal static void Clear()
        {
            Boxes.Clear();
            _builtAlive = -1;
        }

        // For kept outlines with the window closed: rebuild only when an outlined object has gone (taken by hand).
        internal static void RefreshIfChanged(Doctor.Apt apt)
        {
            if (apt == null) { Clear(); return; }
            int alive = 0;
            foreach (var e in apt.Entries)
                if (!e.Stale && e.Go != null) alive++;
            if (alive != _builtAlive) Rebuild(apt);
        }

        // Encapsulated renderer bounds of each live entry (MeshRenderer and SkinnedMeshRenderer children).
        internal static void Rebuild(Doctor.Apt apt)
        {
            Boxes.Clear();
            _builtAlive = -1;
            if (apt == null || !Cfg.DoctorOutlines.Value) return;
            _builtAlive = 0;
            foreach (var e in apt.Entries)
                if (!e.Stale && e.Go != null) _builtAlive++;
            int max = Mathf.Max(1, Cfg.MaxOutlines.Value);
            foreach (var e in apt.Entries)
            {
                if (Boxes.Count >= max) break;
                if (e.Stale || e.Go == null) continue;
                bool any = false;
                var b = new Bounds();
                foreach (var r in e.Go.GetComponentsInChildren<Renderer>())
                {
                    if (r == null || !r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
                    if (!any) { b = r.bounds; any = true; }
                    else b.Encapsulate(r.bounds);
                }
                if (!any) b = new Bounds(e.Go.transform.position, Vector3.one * 0.3f);
                Boxes.Add(b);
            }
        }

        private static bool EnsureMaterial()
        {
            if (_mat != null) return true;
            if (_shaderMissing) return false;
            var shader = Shader.Find("Hidden/Internal-Colored");
            if (shader == null)
            {
                _shaderMissing = true;
                Log.WarnOnce("outline-shader", "Hidden/Internal-Colored is not in this build; the Doctor lists leftovers without outlines.");
                return false;
            }
            _mat = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _mat.SetInt("_ZWrite", 0);
            _mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
            return true;
        }

        // Camera.onPostRender handler (subscribed in Plugin.Awake, removed in OnDestroy).
        internal static void OnPostRender(Camera cam)
        {
            if (Boxes.Count == 0 || !Cfg.DoctorOutlines.Value || cam == null || cam != Camera.main) return;
            try
            {
                if (!EnsureMaterial()) return;
                GL.PushMatrix();
                _mat.SetPass(0);
                GL.Begin(GL.LINES);
                GL.Color(Cfg.Outline());
                for (int i = 0; i < Boxes.Count; i++) Box(Boxes[i]);
                GL.End();
                GL.PopMatrix();
            }
            catch (Exception e) { Log.Exception("Doctor outlines", e); }
        }

        private static void Box(Bounds b)
        {
            Vector3 mn = b.min, mx = b.max;
            var c = Corners;
            c[0] = new Vector3(mn.x, mn.y, mn.z); c[1] = new Vector3(mx.x, mn.y, mn.z);
            c[2] = new Vector3(mx.x, mn.y, mx.z); c[3] = new Vector3(mn.x, mn.y, mx.z);
            c[4] = new Vector3(mn.x, mx.y, mn.z); c[5] = new Vector3(mx.x, mx.y, mn.z);
            c[6] = new Vector3(mx.x, mx.y, mx.z); c[7] = new Vector3(mn.x, mx.y, mx.z);
            for (int i = 0; i < 4; i++)
            {
                Line(c[i], c[(i + 1) % 4]);
                Line(c[i + 4], c[(i + 1) % 4 + 4]);
                Line(c[i], c[i + 4]);
            }
        }

        private static void Line(Vector3 a, Vector3 b)
        {
            GL.Vertex(a);
            GL.Vertex(b);
        }
    }
}

using System;
using UnityEngine;

namespace BetterPlacement
{
    // Guide lines (spec §4.9): four pooled LineRenderers under one DontDestroyOnLoad root on layer 2 (Ignore Raycast,
    // outside the building system's layersHit), with no colliders, so they never touch placement. The material uses
    // "Sprites/Default", the shader vanilla's own connection lines use (SRC/CraftingConnectionIndicator.cs:43-57).
    // Renderers are disabled when unused, never destroyed. Plugin.Update's watchdog hides them when no frame drew them
    // (raycast misses, the Take/Move slot, a closed menu).
    internal static class Guides
    {
        private const int Pool = 4;

        private static GameObject _root;
        private static readonly LineRenderer[] Lines = new LineRenderer[Pool];
        private static Material _mat;
        private static bool _shaderMissing;
        private static int _used;

        internal static int LastShownFrame = -100;

        internal static void Begin() => _used = 0;

        // One segment from a to b.
        internal static void Draw(Vector3 a, Vector3 b, Color colour)
        {
            if (!Cfg.Align.ShowGuides.Value || _shaderMissing || _used >= Pool) return;
            if (!Ensure()) return;
            var lr = Lines[_used++];
            lr.widthMultiplier = Cfg.Align.GuideWidth.Value;
            lr.startColor = colour;
            lr.endColor = colour;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
            if (!lr.enabled) lr.enabled = true;
            LastShownFrame = Time.frameCount;
        }

        // Disables the pooled lines this frame's drawing didn't use.
        internal static void End()
        {
            for (int i = _used; i < Pool; i++)
                if (Lines[i] != null && Lines[i].enabled) Lines[i].enabled = false;
        }

        internal static void Hide()
        {
            _used = 0;
            End();
        }

        internal static void Watchdog()
        {
            if (LastShownFrame < Time.frameCount - 1) Hide();
        }

        private static bool Ensure()
        {
            try
            {
                if (_mat == null)
                {
                    var shader = Shader.Find("Sprites/Default");
                    if (shader == null)
                    {
                        _shaderMissing = true;
                        Log.WarnOnce("guide shader", "Align: the Sprites/Default shader is missing, so no guide lines are drawn. Snapping still works.");
                        return false;
                    }
                    _mat = new Material(shader) { name = "BetterPlacement Guide" };
                    foreach (var lr in Lines)
                        if (lr != null) lr.sharedMaterial = _mat;
                }
                if (_root == null)
                {
                    _root = new GameObject("BetterPlacement Guides") { layer = 2 };
                    UnityEngine.Object.DontDestroyOnLoad(_root);
                    for (int i = 0; i < Pool; i++) Lines[i] = null;
                }
                for (int i = 0; i < Pool; i++)
                {
                    if (Lines[i] != null) continue;
                    var go = new GameObject("Guide " + i) { layer = 2 };
                    go.transform.SetParent(_root.transform, false);
                    var lr = go.AddComponent<LineRenderer>();
                    lr.useWorldSpace = true;
                    lr.positionCount = 2;
                    lr.alignment = LineAlignment.View;
                    lr.numCapVertices = 0;
                    lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    lr.receiveShadows = false;
                    lr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                    lr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                    lr.sharedMaterial = _mat;
                    lr.enabled = false;
                    Lines[i] = lr;
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("guides", e);
                return false;
            }
        }
    }
}

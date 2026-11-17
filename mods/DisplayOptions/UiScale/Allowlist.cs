using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace DisplayOptions
{
    internal enum UiKind { Never, GameUi, Loading, Shop, StartSel, CardGame, Dialogue, Pause }

    // Which canvases UI scale touches (spec §7.2, D11). Only root, screen-space overlay canvases that scale with screen
    // size, identified by a game component, never by name. Everything else is Never: credits and stats (Credits.cs mixes
    // Screen and canvas units), the developer console, Version, intro screens, ConstantPixelSize fades and blockers,
    // world-space and nested canvases, and other mods' canvases. Cached per instance ID; the cache is cleared on scene load.
    internal static class Allowlist
    {
        private static readonly Dictionary<int, UiKind> Cache = new Dictionary<int, UiKind>();

        internal static void Clear() => Cache.Clear();

        internal static UiKind Classify(CanvasScaler s)
        {
            if (s == null) return UiKind.Never;
            int id = s.GetInstanceID();
            if (Cache.TryGetValue(id, out var k)) return k;
            k = Compute(s);
            Cache[id] = k;
            return k;
        }

        private static UiKind Compute(CanvasScaler s)
        {
            if (s.uiScaleMode != CanvasScaler.ScaleMode.ScaleWithScreenSize) return UiKind.Never;
            // In the OnEnable prefix the scaler's own m_Canvas is not assigned yet, so look the canvas up here.
            var canvas = s.GetComponent<Canvas>();
            if (canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay) return UiKind.Never;
            var parent = s.transform.parent;
            if (parent != null && parent.GetComponentInParent<Canvas>(true) != null) return UiKind.Never;   // nested
            if (Has(s, Vanilla.CreditsT, up: true, down: false)) return UiKind.Never;

            if (Has(s, Vanilla.GameUIInstanceT, up: false, down: false)) return UiKind.GameUi;
            if (Has(s, Vanilla.PauseMenuT, up: false, down: false)) return UiKind.Pause;
            if (Has(s, Vanilla.LoadingScreenT, up: false, down: false)) return UiKind.Loading;
            // The Furniture and Train shop scripts sit on the canvas's parent (Interior Bazaar Electronics.unity: Furniture
            // Shop UI/.../Furniture Shop UI → Furniture Shop UI Menu Main); look both ways.
            if (Has(s, Vanilla.ShopBaseUIT, up: true, down: true)) return UiKind.Shop;
            if (Has(s, Vanilla.StartSelectionsUIT, up: true, down: true)) return UiKind.StartSel;
            if (Has(s, Vanilla.CardGameUIT, up: false, down: true)) return UiKind.CardGame;
            if (Has(s, Vanilla.DialogueSystemControllerT, up: true, down: false)) return UiKind.Dialogue;
            return UiKind.Never;
        }

        private static bool Has(Component c, System.Type t, bool up, bool down)
        {
            if (t == null) return false;
            if (c.GetComponent(t) != null) return true;
            if (up && c.GetComponentInParent(t, true) != null) return true;
            if (down && c.GetComponentInChildren(t, true) != null) return true;
            return false;
        }
    }
}

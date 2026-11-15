using System;
using System.Collections.Generic;
using UnityEngine;

namespace BetterTenement
{
    // Landlord Ledger side panel (spec §3.5.3): shown beside the Tenement page, in the free screen margin next to the
    // game's menu, below the money HUD. No input capture: the wheel scrolls it, and the right stick scrolls it from
    // Plugin.Update (the Tenement tab has no vanilla right-stick action, InventoryNavigationHandler.cs:1168-1183).
    internal static class LedgerPanel
    {
        private static bool _visible;
        private static bool _flipped;   // F3: collapse (or, when the margin is too narrow, show as an overlay)
        private static float _nextCheck;
        private static Vector2 _scroll;
        private static List<LedgerData.Line> _measuredFor;
        private static float _measuredWidth, _contentHeight;

        internal static bool Visible => _visible;

        internal static void OnSceneLoaded()
        {
            _visible = false;
            _measuredFor = null;
        }

        private static bool ShouldShow()
        {
            if (!Cfg.LedgerShowWithTenementTab.Value || TenementController.instance == null || !Game.MenuVisible) return false;
            var pages = UIPageButtons.instance;
            if (pages == null || pages.currentPage != MenuPageNames.Tenement) return false;
            var ui = TenementController.instance.tenementPanelUI;
            return ui == null || ui.gameObject.activeInHierarchy;
        }

        internal static void Update()
        {
            if (Time.unscaledTime >= _nextCheck)
            {
                _nextCheck = Time.unscaledTime + 0.25f;
                bool was = _visible;
                _visible = ShouldShow();
                if (_visible && !was) LedgerData.Invalidate();
            }
            if (!_visible || Game.ConsoleOpen) return;
            if (Cfg.LedgerHotkey.Value.IsDown()) _flipped = !_flipped;
            try
            {
                float v = Input.GetAxis("RightStickVertical");
                if (Mathf.Abs(v) > 0.2f) _scroll.y = Mathf.Max(0f, _scroll.y + v * 900f * Time.unscaledDeltaTime);
            }
            catch (Exception) { /* axis missing: wheel only */ }
        }

        internal static void OnGUI(Vector2 screen)
        {
            if (!_visible) return;
            float scale = Mathf.Max(0.01f, Cfg.UiScale.Value);
            // The game's menu is about 1100 px wide at 1080 p and centred; the margin beside it is free.
            float gamePx = Screen.height / 1080f;
            float margin = (Screen.width - 1100f * gamePx) / 2f / (gamePx * scale);
            float top = 200f / scale, bottom = 40f / scale;
            bool narrow = margin - 24f < 240f;
            bool full = narrow ? _flipped : !_flipped;
            string key = Cfg.LedgerHotkey.Value.ToString();   // the full shortcut, modifiers included

            if (!full)
            {
                var tab = new GUIContent($"Ledger ({key})");
                float tw = Skin.Box.CalcSize(tab).x + 4f, th = 40f;
                float tx = Cfg.Side.Value == LedgerSide.Right ? screen.x - tw - 16f : 16f;
                GUI.Box(new Rect(tx, top, tw, th), tab, Skin.Box);
                return;
            }

            float w = Cfg.LedgerWidth.Value;
            float x;
            if (narrow)
            {
                w = Mathf.Min(w, screen.x - 48f);
                x = Cfg.Side.Value == LedgerSide.Right ? screen.x - w - 24f : 24f;
            }
            else
            {
                w = Mathf.Min(w, margin - 24f);
                x = Cfg.Side.Value == LedgerSide.Right ? screen.x - margin + 12f : margin - 12f - w;
            }

            var lines = LedgerData.Lines();
            float inner = w - 32f - 20f;
            if (!ReferenceEquals(lines, _measuredFor) || Math.Abs(_measuredWidth - inner) > 0.5f)
            {
                _measuredFor = lines;
                _measuredWidth = inner;
                _contentHeight = 0f;
                foreach (var l in lines) _contentHeight += StyleOf(l).CalcHeight(new GUIContent(l.Text), inner) + 2f;
            }
            float maxH = screen.y - top - bottom;
            float h = Mathf.Min(maxH, _contentHeight + 72f);
            GUILayout.BeginArea(new Rect(x, top, w, h), Skin.Box);
            GUILayout.Label($"Landlord Ledger <size=12>({key})</size>", Skin.TitleRich);
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var l in lines) GUILayout.Label(l.Text, StyleOf(l));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private static GUIStyle StyleOf(LedgerData.Line l) => l.Heading ? Skin.Bold : l.Small ? Skin.Small : Skin.Label;
    }
}

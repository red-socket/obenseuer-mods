using System;
using System.Collections.Generic;
using System.Linq;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UI.Button;

namespace QuestHelper
{
    // One transient map pin for the followed task (spec §5.3). The pin is a stray clone of Map.LandmarkPrefab placed
    // under LandmarksArea but kept out of Map's landmarks/selectedLandmarks lists and never handed to MapController, so
    // nothing about it is ever saved (V6). Its Button, EventTriggers and any SavableScript are removed, which makes it
    // inert; LandmarkUI stays for the hover title. Sprite: the journal's own active-task status icon, tinted amber (D16).
    internal static class MapPin
    {
        private const float Debounce = 0.5f;
        private static readonly Color Tint = new Color(1f, 0.671f, 0f, 1f);   // #FFAB00

        internal static bool Available;
        private static GameObject _pin;
        private static bool _ready, _failed, _dirty;
        private static float _dueAt;
        private static string _lastTarget;

        internal static bool On => Available && !_failed && Cfg.MapPin.Enabled.Value;

        internal static void MarkDirty()
        {
            if (_dirty) return;
            _dirty = true;
            _dueAt = Time.unscaledTime + Debounce;
        }

        internal static void OnSceneLoaded()
        {
            _pin = null;   // died with the scene
            _ready = false;
            _lastTarget = null;
        }

        // The post-load tick: the pin may be created from now on.
        internal static void Refresh()
        {
            _ready = true;
            _dirty = false;
            try { Apply(); }
            catch (Exception e)
            {
                Log.Exception("map pin", e);
                Hide();
            }
        }

        internal static void RefreshIfDirty()
        {
            if (!_dirty || !_ready || Time.unscaledTime < _dueAt) return;
            Refresh();
        }

        private static void Hide()
        {
            if (_pin != null) _pin.SetActive(false);
        }

        private static void Apply()
        {
            if (!On || SaveController.Loading) { Hide(); return; }
            var t = FindTarget(out Vector3 pos, out string title, out string details);
            if (!t) { Hide(); Note("none"); return; }
            if (!Ensure()) return;
            var map = Vanilla.MapOf(MapController.instance);
            var rt = map != null ? (Vanilla.MapSize(map) ?? map.GetComponent<RectTransform>()) : null;
            if (rt == null || info_map.instance == null) { Hide(); return; }
            var rel = info_map.instance.GetRelativePosition(pos);
            _pin.transform.localPosition = new Vector3(rel.x * rt.rect.width, rel.z * rt.rect.height, 0f);
            _pin.transform.localScale = Vector3.one;
            _pin.transform.SetAsLastSibling();
            var gl = _pin.GetComponent<GetLandMark>();
            if (gl != null)
            {
                var tip = Vanilla.LandmarkTip(gl);
                if (tip != null) tip.UpdateToolTipInfo(title, details, false);
                var ui = Vanilla.LandmarkUi(gl);
                if (ui != null) ui.title = title;
            }
            _pin.SetActive(true);
            Note(title + " @ " + pos);
        }

        private static void Note(string target)
        {
            if (target == _lastTarget) return;
            _lastTarget = target;
            Log.Verbose("Pin: " + target);
        }

        // The first Active, non-hidden objective (required ones first) with a place on the current map (spec §5.3).
        private static bool FindTarget(out Vector3 pos, out string title, out string details)
        {
            pos = default;
            title = details = null;
            var tsi = Tracking.Followed();
            if (tsi == null || tsi.objectives == null) return false;
            string curMap = Game.CurrentMapId;
            if (string.IsNullOrEmpty(curMap)) return false;
            int tier = Cfg.Tier;
            foreach (var osi in tsi.objectives.Where(o => o?.objective != null && o.status == ObjectiveStatus.Active)
                         .OrderBy(o => o.optional ? 1 : 0))
            {
                string key = TaskState.Key(tsi, osi);
                title = Strings.PinTitle(TaskState.Title(osi));
                var row = tier >= 2 ? Curated.Get(key) : null;
                if (row != null)
                {
                    string giver = Game.GiverFromConversation(row.Conv);
                    if (!string.IsNullOrEmpty(row.Scene) && row.Scene == Game.SceneName && LiveNpc(row.Conv, out pos))
                    {
                        details = TaskState.Title(tsi) + "\n" + giver;
                        return true;
                    }
                    var lm = Curated.LandmarkAsset(row.Landmark);
                    if (lm != null && lm.position != Vector3.zero && Curated.MapOf(row.Landmark) == curMap)
                    {
                        pos = lm.position;
                        details = TaskState.Title(tsi) + "\n" + (string.IsNullOrEmpty(giver) ? lm.title : giver + ", " + lm.title);
                        return true;
                    }
                }
                if (Cfg.MapPin.PinWhereReceived.Value)
                {
                    var rec = Records.Get(key);
                    if (rec != null && rec.Pos.HasValue && rec.Map == curMap)
                    {
                        pos = rec.Pos.Value;
                        details = TaskState.Title(tsi) + "\n" + (string.IsNullOrEmpty(rec.Giver) ? Strings.ReceivedHere : rec.Giver);
                        return true;
                    }
                }
            }
            return false;
        }

        // The live ConversationTrigger for this conversation in the current scene (no hand-copied coordinates).
        private static bool LiveNpc(string conv, out Vector3 pos)
        {
            pos = default;
            if (string.IsNullOrEmpty(conv)) return false;
            foreach (var ct in Object.FindObjectsByType<ConversationTrigger>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (ct != null && ct.conversation == conv && ct.gameObject.activeInHierarchy)
                {
                    pos = ct.transform.position;
                    return true;
                }
            return false;
        }

        // Created once per scene: needs a map image (info_map) and the map's landmark prefab.
        private static bool Ensure()
        {
            if (_pin != null) return true;
            if (info_map.instance == null || info_map.instance.mapImage == null) return false;
            var map = Vanilla.MapOf(MapController.instance);
            var prefab = map != null ? Vanilla.LandmarkPrefab(map) : null;
            var area = map != null ? Vanilla.LandmarksArea(map) : null;
            var tp = TaskPanel.instance;
            var taskPrefab = tp != null ? Vanilla.TaskPrefab(tp) : null;
            var panel = taskPrefab != null ? taskPrefab.GetComponent<TaskUIPanel>() : null;
            var sprite = panel != null ? Vanilla.StatusActiveUnselected(panel) : null;
            if (prefab == null || area == null || sprite == null)
            {
                Log.WarnOnce("pin parts", "Pin: the map's landmark template or the journal icon was not found; no pin in this area.");
                return false;
            }

            var pin = Object.Instantiate(prefab, area.transform, false);
            pin.name = "QuestHelper Pin";
            foreach (var b in pin.GetComponentsInChildren<UiButton>(true)) Object.DestroyImmediate(b);
            foreach (var e in pin.GetComponentsInChildren<EventTrigger>(true)) Object.DestroyImmediate(e);
            foreach (var s in pin.GetComponentsInChildren<SavableScript>(true)) Object.DestroyImmediate(s);
            if (pin.GetComponentsInChildren<SavableScript>(true).Length > 0)
            {
                Object.Destroy(pin);
                _failed = true;
                Log.WarnOnce("pin savable", "Pin: the map pin template carries saved data; map pins are off this session.");
                return false;
            }
            var gl = pin.GetComponent<GetLandMark>();
            var img = gl != null ? Vanilla.LandmarkImage(gl) : null;
            if (img != null)
            {
                img.sprite = sprite;
                img.color = Tint;
                img.raycastTarget = true;   // hover shows the title
            }
            var rt = pin.GetComponent<RectTransform>();
            int size = Cfg.MapPin.PinSize.Value;
            if (rt != null) rt.sizeDelta = new Vector2(size, size);
            pin.SetActive(false);
            _pin = pin;
            return true;
        }

        internal static void OnSizeChanged()
        {
            if (_pin == null) return;
            var rt = _pin.GetComponent<RectTransform>();
            int size = Cfg.MapPin.PinSize.Value;
            if (rt != null) rt.sizeDelta = new Vector2(size, size);
        }
    }
}

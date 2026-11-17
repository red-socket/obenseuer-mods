using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;
using UiButton = UnityEngine.UI.Button;

namespace QuestHelper
{
    // The "To be continued" section of the task panel (spec §7). UI only: rows are only re-ordered or parked, never
    // created or destroyed, and TaskPanel's own lists (taskList, indices) are never touched, so TaskUIPanel.taskIndex
    // stays valid. Rows are matched by TaskUIPanel.task.Id. With no TBC task and no header the panel is left exactly as
    // the game built it. Collapsed rows are parked in an inactive holder under TaskPanel, which drops them from both the
    // layout and the controller's task grid; any row that stops being TBC is always moved back.
    internal static class TbcSection
    {
        internal static bool Dirty;
        internal static bool Available;
        private static GameObject _header, _holder;
        private static int _clampFrame = -1;

        internal static bool HeaderExists => _header != null;

        internal static void OnSceneLoaded()
        {
            _header = null;
            _holder = null;
            Dirty = true;
        }

        internal static void Toggle()
        {
            Cfg.ToBeContinued.Collapsed.Value = !Cfg.ToBeContinued.Collapsed.Value;
            Dirty = true;
            RelayoutIfDirty();
            RefreshHint();
        }

        internal static void RefreshHint()
        {
            try
            {
                var h = InventoryNavigationHandler.Instance;
                var d = InventoryNavigationData.Instance;
                if (h != null && d != null && InputManager.instance != null && h.isActiveAndEnabled)
                    h.ChangeControlsInfo(InputManager.instance.inputType, d.CurrentTab, d.CurrentWindow);
            }
            catch (Exception e) { Log.Exception("tbc hint", e); }
        }

        // LateUpdate.
        internal static void RelayoutIfDirty()
        {
            if (Time.frameCount == _clampFrame + 1) Clamp();
            if (!Dirty || !Available) return;
            var tp = TaskPanel.instance;
            if (tp == null) return;
            Dirty = false;
            try { Relayout(tp); }
            catch (Exception e) { Log.Exception("tbc relayout", e); }
        }

        private static void Relayout(TaskPanel tp)
        {
            var list = Vanilla.TaskListPanel(tp);
            if (list == null) return;
            var rows = new List<TaskUIPanel>();
            foreach (Transform c in list.transform)
            {
                var p = c != null ? c.GetComponent<TaskUIPanel>() : null;
                if (p != null && p.task != null) rows.Add(p);
            }
            if (_holder != null)
                foreach (Transform c in _holder.transform)
                {
                    var p = c != null ? c.GetComponent<TaskUIPanel>() : null;
                    if (p != null && p.task != null) rows.Add(p);
                }

            // RefreshPanels re-selects the first inventory slot for controller users (SRC/InventoryNavigationHandler.cs:511),
            // so it only runs when this relayout actually moved, added or removed something.
            string before = Layout(list);

            bool on = Cfg.ToBeContinued.Enabled.Value;
            var tbc = on ? rows.FindAll(r => TaskState.IsTbc(r.task)) : new List<TaskUIPanel>();
            var activeTitle = Vanilla.ActiveTaskTitle(tp);

            if (tbc.Count == 0)
            {
                if (_header == null && (_holder == null || _holder.transform.childCount == 0)) return;   // vanilla untouched
                foreach (var r in rows)
                    if (r.transform.parent != list.transform) r.transform.SetParent(list.transform, false);
                Order(tp, list, rows, new List<TaskUIPanel>(), false);
                bool hadHeader = _header != null;
                if (hadHeader) Object.Destroy(_header);   // deferred to the end of the frame, so Layout still lists it
                _header = null;
                if (activeTitle != null) activeTitle.SetActive(rows.Exists(r => r.task.status == CurrentTaskStatus.Active && !r.task.renovation));
                Log.Verbose("TBC: section removed.");
                if (hadHeader || Layout(list) != before) Changed(list);
                return;
            }

            if (_header == null && !CreateHeader(tp, list)) return;
            bool collapsed = Cfg.ToBeContinued.Collapsed.Value;
            if (_holder == null)
            {
                _holder = new GameObject("QuestHelper TBC Holder");
                _holder.SetActive(false);
                _holder.transform.SetParent(tp.transform, false);
            }
            foreach (var r in rows)
            {
                bool park = collapsed && tbc.Contains(r);
                var want = park ? _holder.transform : list.transform;
                if (r.transform.parent != want) r.transform.SetParent(want, false);
            }
            Order(tp, list, rows, tbc, collapsed);
            SetHeaderText(collapsed, tbc.Count);
            if (activeTitle != null)
                activeTitle.SetActive(rows.Exists(r => r.task.status == CurrentTaskStatus.Active && !r.task.renovation && !tbc.Contains(r)));
            Log.Verbose($"TBC: {tbc.Count} task(s), {(collapsed ? "collapsed" : "expanded")}.");
            if (Layout(list) != before) Changed(list);
        }

        // The list's children in order (active state included) and the parked rows: equal strings mean nothing moved.
        private static string Layout(GameObject list)
        {
            var sb = new System.Text.StringBuilder();
            foreach (Transform c in list.transform) sb.Append(c.GetInstanceID()).Append(c.gameObject.activeSelf ? '+' : '-').Append(',');
            sb.Append('|');
            if (_holder != null) foreach (Transform c in _holder.transform) sb.Append(c.GetInstanceID()).Append(',');
            return sb.ToString();
        }

        // [Active tasks title, active rows, renovation title, renovation rows, TBC header, TBC rows (expanded),
        //  Finished title, finished rows]; relative order inside each group is kept.
        private static void Order(TaskPanel tp, GameObject list, List<TaskUIPanel> rows, List<TaskUIPanel> tbc, bool collapsed)
        {
            var parent = list.transform;
            var seq = new List<Transform>();
            void Add(GameObject go) { if (go != null && go.transform.parent == parent) seq.Add(go.transform); }
            List<TaskUIPanel> Sorted(Predicate<TaskUIPanel> p)
            {
                var l = rows.FindAll(r => r.transform.parent == parent && p(r));
                l.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
                return l;
            }
            Add(Vanilla.ActiveTaskTitle(tp));
            foreach (var r in Sorted(r => r.task.status == CurrentTaskStatus.Active && !r.task.renovation && !tbc.Contains(r))) seq.Add(r.transform);
            Add(Vanilla.RenovationTaskTitle(tp));
            foreach (var r in Sorted(r => r.task.status == CurrentTaskStatus.Active && r.task.renovation)) seq.Add(r.transform);
            if (_header != null) Add(_header);
            if (!collapsed) foreach (var r in Sorted(r => tbc.Contains(r))) seq.Add(r.transform);
            Add(Vanilla.FinishedTaskTitle(tp));
            foreach (var r in Sorted(r => r.task.status != CurrentTaskStatus.Active)) seq.Add(r.transform);
            for (int i = 0; i < seq.Count; i++) seq[i].SetSiblingIndex(i);
        }

        private static bool CreateHeader(TaskPanel tp, GameObject list)
        {
            var prefab = Vanilla.FinishedTaskTitlePrefab(tp);
            if (prefab == null) return false;
            var go = Object.Instantiate(prefab, list.transform, false);
            go.name = "QuestHelper TBC";
            go.transform.localScale = Vector3.one;
            foreach (var s in go.GetComponentsInChildren<SavableScript>(true)) Object.DestroyImmediate(s);
            foreach (var p in go.GetComponentsInChildren<TaskUIPanel>(true)) Object.DestroyImmediate(p);
            PauseClone.StripLocalization(go);
            var img = go.GetComponent<Image>();
            if (img == null)
            {
                img = go.AddComponent<Image>();
                img.color = new Color(0f, 0f, 0f, 0f);
            }
            img.raycastTarget = true;
            var btn = go.GetComponent<UiButton>() ?? go.AddComponent<UiButton>();
            btn.transition = Selectable.Transition.None;
            btn.onClick = new UiButton.ButtonClickedEvent();
            btn.onClick.AddListener(Toggle);
            if (go.GetComponent<TbcHeaderHover>() == null) go.AddComponent<TbcHeaderHover>();
            go.SetActive(true);
            _header = go;
            return true;
        }

        private static void SetHeaderText(bool collapsed, int n)
        {
            var t = _header != null ? _header.GetComponentInChildren<TMP_Text>(true) : null;
            string s = Strings.TbcHeader(collapsed, n);
            if (t != null && t.text != s) t.text = s;
        }

        // Rows moved: rebuild the controller's task grid and keep its saved cursor inside it, now and next frame
        // (RefreshPanels rebuilds a frame later).
        private static void Changed(GameObject list)
        {
            Clamp();
            try
            {
                var h = InventoryNavigationHandler.Instance;
                if (h != null && h.isActiveAndEnabled)
                {
                    h.RefreshPanels();
                    _clampFrame = Time.frameCount;
                    Log.Verbose("TBC: controller grid refreshed.");
                }
            }
            catch (Exception e) { Log.Exception("tbc nav refresh", e); }
            RefreshHint();
        }

        private static void Clamp()
        {
            try
            {
                var tp = TaskPanel.instance;
                var d = InventoryNavigationData.Instance;
                var list = tp != null ? Vanilla.TaskListPanel(tp) : null;
                if (d == null || list == null) return;
                int rows = 0;
                foreach (Transform c in list.transform) if (c.GetComponent<TaskUIPanel>() != null) rows++;
                var cur = d.PrevTaskCoordinates;
                float y = rows == 0 ? 0f : Mathf.Clamp(cur.y, 0f, rows - 1);
                if (y != cur.y) d.PrevTaskCoordinates = new Vector2(cur.x, y);
            }
            catch (Exception e) { Log.Exception("tbc clamp", e); }
        }

        // P2: the "cannot be completed" marker (D14): relabelled on Active objectives, hidden on the others.
        internal static void Label(ObjectiveUI ui, ObjectiveStatusInfo osi)
        {
            if (!Available || !Cfg.ToBeContinued.Enabled.Value || osi?.objective == null || !osi.objective.cannotBeCompleted) return;
            var marker = Vanilla.CannotBeCompleted(ui);
            if (marker == null) return;
            bool active = osi.status == ObjectiveStatus.Active;
            if (!active && Cfg.ToBeContinued.HideLabelWhenNotActive.Value) { marker.SetActive(false); return; }
            if (active && Cfg.ToBeContinued.ClearerLabel.Value)
            {
                var t = marker.GetComponentInChildren<TMP_Text>(true);
                if (t != null) t.text = Strings.TbcLabel;
            }
        }

        // P1: a note when a story reaches its current end, once per objective per session.
        private static readonly HashSet<string> Noticed = new HashSet<string>();

        internal static void Notice(TaskStatusInfo tsi, ObjectiveStatusInfo osi)
        {
            if (!Available || !Cfg.ToBeContinued.Enabled.Value || !Cfg.ToBeContinued.Notice.Value) return;
            if (!Noticed.Add(TaskState.Key(tsi, osi))) return;
            Toast.Show(Strings.TbcNotice(TaskState.Title(tsi)));
        }
    }

    // Hover tooltip on the TBC header, through the game's own ToolTip.
    internal sealed class TbcHeaderHover : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public void OnPointerEnter(PointerEventData eventData)
        {
            try { if (ToolTip.instance != null) ToolTip.instance.Activate(Strings.TbcTooltipTitle, Strings.TbcTooltip); }
            catch (Exception e) { Log.Exception("tbc tooltip", e); }
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            try { if (ToolTip.instance != null) ToolTip.instance.Deactivate(); }
            catch (Exception e) { Log.Exception("tbc tooltip", e); }
        }

        private void OnDisable() => OnPointerExit(null);
    }
}

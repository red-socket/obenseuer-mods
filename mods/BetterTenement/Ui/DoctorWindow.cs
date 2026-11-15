using System;
using UnityEngine;

namespace BetterTenement
{
    // Tenement Doctor window (spec §3.4.2), IMGUI in the house skin. Opens over gameplay only (not over a game menu,
    // the pause menu or the console). While open, player controls are paused through the game's own
    // ControlsDisabled list (so the cursor shows) and the EventSystem is disabled (Save Guard's InputBlock).
    internal static class DoctorWindow
    {
        internal static bool Open;

        private static int _row;
        private static Vector2 _scroll;
        private static string _message;
        private static bool _confirmClearAll;
        private static Doctor.Entry _confirmDelete;   // row waiting for "Yes, delete"
        private static float _nextScan;
        private static GameObject _controlsKey;
        private static bool _controlsTaken;
        private static int _openedFrame;
        private static float _prevVertical;

        private static Action _deferred;
        private static void Defer(Action a) => _deferred += a;

        internal static void OnSceneLoaded()
        {
            // The old scene's GameController is gone together with its disabled lists.
            Open = false;
            _controlsTaken = false;
            _confirmClearAll = false;
            _confirmDelete = null;
            InputBlock.Release();
            DoctorOutline.Keep = false;
            DoctorOutline.Clear();
            Doctor.Apts.Clear();
        }

        internal static void Toggle()
        {
            if (Open) { Close(); return; }
            if (!Cfg.Doctor.Value || Game.ConsoleOpen || Game.MenuVisible || Game.Paused || TenementController.instance == null) return;
            Doctor.Scan();
            Doctor.SelectDefault();
            _row = 0;
            _scroll = Vector2.zero;
            _message = null;
            CancelConfirm();
            _openedFrame = Time.frameCount;
            Open = true;
            TakeControls();
            RefreshOutlines();
        }

        private static bool Confirming => _confirmClearAll || _confirmDelete != null;

        private static void CancelConfirm()
        {
            _confirmClearAll = false;
            _confirmDelete = null;
        }

        private static void Close()
        {
            Open = false;
            CancelConfirm();
            ReleaseControls();
            InputBlock.Release();
            if (!DoctorOutline.Keep) DoctorOutline.Clear();
        }

        private static void TakeControls()
        {
            try
            {
                var gc = GameController.instance;
                if (gc == null) return;
                if (_controlsKey == null)
                {
                    _controlsKey = new GameObject("BetterTenement.Doctor") { hideFlags = HideFlags.HideAndDontSave };
                    UnityEngine.Object.DontDestroyOnLoad(_controlsKey);
                }
                gc.ControlsDisabled(_controlsKey, ShowCursor: true);
                _controlsTaken = true;
            }
            catch (Exception e) { Log.Exception("Doctor controls", e); }
        }

        private static void ReleaseControls()
        {
            if (!_controlsTaken) return;
            _controlsTaken = false;
            try
            {
                var gc = GameController.instance;
                if (gc != null && _controlsKey != null) gc.ControlsEnabled(_controlsKey);
            }
            catch (Exception e) { Log.Exception("Doctor controls", e); }
        }

        internal static void OnDestroy()
        {
            if (Open) Close();
        }

        private static void RefreshOutlines()
        {
            if (Open || DoctorOutline.Keep) DoctorOutline.Rebuild(Doctor.Current);
            else DoctorOutline.Clear();
        }

        internal static void Update()
        {
            if (!Cfg.Doctor.Value)
            {
                // Switched off: close, and drop kept outlines (the window that could untick them no longer opens).
                if (Open) Close();
                if (DoctorOutline.Keep || DoctorOutline.Boxes.Count > 0)
                {
                    DoctorOutline.Keep = false;
                    DoctorOutline.Clear();
                }
                return;
            }
            if (!Game.ConsoleOpen && Cfg.DoctorHotkey.Value.IsDown()) Toggle();
            if (Open && TenementController.instance == null) Close();

            if (Open && Time.unscaledTime >= _nextScan)
            {
                _nextScan = Time.unscaledTime + 0.5f;
                if (!Confirming) Doctor.Scan();
                if (Doctor.Selected < 0) Doctor.SelectDefault();
                RefreshOutlines();
            }
            else if (!Open && DoctorOutline.Keep && Time.unscaledTime >= _nextScan)
            {
                // Kept outlines with the window closed: no rescan, only drop boxes of objects taken since.
                _nextScan = Time.unscaledTime + 1f;
                DoctorOutline.RefreshIfChanged(Doctor.Current);
            }
            if (Open && Game.ControllerMode && Time.frameCount > _openedFrame) Controller();
        }

        // D-pad or left stick: row; LB/RB: apartment; A: act on the row; B or Menu: close.
        private static void Controller()
        {
            var im = InputManager.instance;
            if (im == null) return;
            float v = 0f;
            try { v = Input.GetAxisRaw("DPadVertical"); if (Mathf.Abs(v) < 0.5f) v = Input.GetAxisRaw("LeftStickInventoryVertical"); }
            catch (Exception) { }
            int dir = v > 0.5f ? -1 : v < -0.5f ? 1 : 0;
            int prev = _prevVertical > 0.5f ? -1 : _prevVertical < -0.5f ? 1 : 0;
            _prevVertical = v;
            if (dir != 0 && dir != prev) MoveRow(dir);

            var kb = im.keyBindings;
            if (Input.GetKeyDown(kb.ControllerLB)) SwitchApartment(-1);
            else if (Input.GetKeyDown(kb.ControllerRB)) SwitchApartment(1);
            else if (Input.GetKeyDown(kb.ControllerA)) ActOnRow();
            else if (Input.GetKeyDown(kb.ControllerB) || Input.GetKeyDown(kb.ControllerMenu))
            {
                // GameController (execution order 10400) runs after this Update in the same frame: closing here would
                // re-enable its keys while B ("Menu") or Start ("Pause Menu") is still down, opening the game menu or
                // the pause menu. Deferred to OnGUI, after every Update, like the Esc path.
                if (Confirming) CancelConfirm();
                else Defer(Close);
            }
        }

        private static void MoveRow(int d)
        {
            var apt = Doctor.Current;
            int n = apt != null ? apt.Entries.Count : 0;
            if (n == 0) { _row = 0; return; }
            CancelConfirm();
            _row = Mathf.Clamp(_row + d, 0, n - 1);
            _scroll.y = Mathf.Max(0f, _row * 30f - 120f);
        }

        private static void SwitchApartment(int d)
        {
            if (Doctor.Apts.Count == 0) return;
            int i = Doctor.Selected < 0 ? 0 : Doctor.Selected;
            Doctor.Selected = (i + d + Doctor.Apts.Count) % Doctor.Apts.Count;
            _row = 0;
            _scroll = Vector2.zero;
            _message = null;
            CancelConfirm();
            RefreshOutlines();
        }

        private static Doctor.Entry SelectedEntry()
        {
            var apt = Doctor.Current;
            return apt != null && _row >= 0 && _row < apt.Entries.Count ? apt.Entries[_row] : null;
        }

        // What Enter / A does on the selected row: "pick up", "delete" (asks first), "confirm", or null (nothing).
        private static string RowAction()
        {
            if (Confirming) return "confirm";
            var apt = Doctor.Current;
            var e = SelectedEntry();
            if (!Cfg.AllowForceClear.Value || apt == null || apt.Fm == null || e == null) return null;
            if (Doctor.TakeRefusal(apt, e) == null) return "pick up";
            return Doctor.CanDelete(apt, e) ? "delete" : null;
        }

        private static void ActOnRow()
        {
            if (_confirmClearAll) { RunClearAll(); return; }
            if (_confirmDelete != null) { RunDelete(); return; }
            var apt = Doctor.Current;
            var e = SelectedEntry();
            if (e == null) return;
            if (!Cfg.AllowForceClear.Value) { _message = "Picking up is off (Doctor.AllowForceClear in the config)."; return; }
            try
            {
                if (Doctor.TakeRefusal(apt, e) == null)
                {
                    Doctor.Take(apt, e, out string msg);
                    _message = msg;
                }
                else if (Doctor.CanDelete(apt, e, fresh: true))
                {
                    // Never in one step: the row waits for "Yes, delete".
                    _confirmDelete = e;
                    _message = null;
                    return;
                }
                else _message = "Can't pick up: " + Doctor.TakeRefusal(apt, e) + (e.Content == true ? " (it holds items)" : "") + ".";
            }
            catch (Exception ex) { _message = "That did not work (see the log)."; Log.Exception("Doctor action", ex); }
            Doctor.Scan();
            RefreshOutlines();
        }

        private static void RunDelete()
        {
            var e = _confirmDelete;
            _confirmDelete = null;
            var apt = Doctor.Current;
            if (apt == null || e == null) return;
            try
            {
                Doctor.Delete(apt, e, out string msg);
                _message = msg;
            }
            catch (Exception ex) { _message = "That did not work (see the log)."; Log.Exception("Doctor delete", ex); }
            Doctor.Scan();
            RefreshOutlines();
        }

        private static void RunClearAll()
        {
            _confirmClearAll = false;
            var apt = Doctor.Current;
            if (apt == null) return;
            try { Doctor.ClearAll(apt, out _message); }
            catch (Exception ex) { _message = "That did not work (see the log)."; Log.Exception("Doctor clear all", ex); }
            Doctor.Scan();
            RefreshOutlines();
        }

        // ---- drawing ----

        internal static void OnGUI(Vector2 screen)
        {
            if (!Open) return;
            HandleKeys();
            float w = Mathf.Min(820f, screen.x - 48f), h = Mathf.Min(680f, screen.y - 48f);
            var rect = new Rect(Mathf.Max(24f, screen.x * 0.08f), (screen.y - h) / 2f, w, h);
            GUILayout.BeginArea(rect, Skin.Box);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Tenement Doctor", Skin.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", Skin.Button, GUILayout.Width(90))) Defer(Close);
            GUILayout.EndHorizontal();

            if (!Doctor.AnyLive) DrawSavedCounters();
            else DrawApartment();

            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(Skin.Color(_message, Skin.Amber), Skin.Label);
            string act = RowAction();
            string back = Confirming ? "cancel" : "close";
            GUILayout.Label(Game.ControllerMode
                ? "D-pad: row · LB/RB: apartment" + (act != null ? $" · A: {act}" : "") + $" · B: {back}"
                : "Arrows: row and apartment" + (act != null ? $" · Enter: {act}" : "") + $" · Esc: {back}", Skin.Small);
            GUILayout.EndArea();

            var run = _deferred;
            _deferred = null;
            if (run != null)
            {
                try { run(); }
                catch (Exception e) { Log.Exception("Doctor window action", e); }
            }
            InputBlock.Sync(Open);
        }

        private static void DrawSavedCounters()
        {
            GUILayout.Label("Go to the tenement building to list leftovers.", Skin.Label);
            GUILayout.Space(6);
            _scroll = GUILayout.BeginScrollView(_scroll);
            foreach (var apt in Doctor.Apts)
            {
                var a = apt.A;
                string state = !a.examined ? "not examined" : $"Group 1 {apt.P}% / Group 2 {apt.S}%" + (apt.Available ? " · ready" : "");
                GUILayout.Label($"Apt {a.apartmentNumber}: {state}", Skin.Label);
            }
            GUILayout.EndScrollView();
        }

        private static void DrawApartment()
        {
            var apt = Doctor.Current;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("<", Skin.Button, GUILayout.Width(40))) Defer(() => SwitchApartment(-1));
            GUILayout.Label(apt != null ? $"<b>Apartment {apt.A.apartmentNumber}</b>" + (apt.Fm == null ? " (not in this area)" : "") : "-", Skin.Label, GUILayout.ExpandWidth(false));
            if (GUILayout.Button(">", Skin.Button, GUILayout.Width(40))) Defer(() => SwitchApartment(1));
            GUILayout.FlexibleSpace();
            bool keep = GUILayout.Toggle(DoctorOutline.Keep, " Keep outlines", Skin.Toggle);
            if (keep != DoctorOutline.Keep) Defer(() => { DoctorOutline.Keep = keep; RefreshOutlines(); });
            GUILayout.EndHorizontal();
            if (apt == null) return;

            var a = apt.A;
            string examined = a.examined ? "examined" : $"not examined ({a.currentExamineCounter}/{a.examineCounterMax})";
            string avail = apt.Available ? Skin.Color("available for renovation", Skin.Amber) : "not available";
            GUILayout.Label($"{examined} · {avail}" + (a.resident != null ? " · occupied" : a.onlyFurniture ? " · furniture left" : ""), Skin.Label);
            GUILayout.Label($"Group 1: {apt.P}% (needs {Doctor.GatePrimary})" + (apt.P >= Doctor.GatePrimary ? " · met" : $" · clear {apt.KP} more") +
                            $"    Group 2: {apt.S}% (needs {Doctor.GateSecondary})" + (apt.S >= Doctor.GateSecondary ? " · met" : $" · clear {apt.KS} more"), Skin.Label);

            GUILayout.Space(4);
            _scroll = GUILayout.BeginScrollView(_scroll, GUILayout.ExpandHeight(true));
            if (apt.Fm != null && apt.Entries.Count == 0) GUILayout.Label("No leftovers listed.", Skin.Small);
            if (_row >= apt.Entries.Count) _row = Math.Max(0, apt.Entries.Count - 1);
            for (int i = 0; i < apt.Entries.Count; i++)
            {
                var e = apt.Entries[i];
                string dist = e.Distance >= 0f ? $" · {e.Distance:0.0} m" : "";
                string text = $"G{e.Group} · {e.Name} · {e.Kind}{dist}";
                int index = i;
                if (GUILayout.Button(text, i == _row ? Skin.RowSelectedStyle : Skin.RowStyle)) Defer(() => { if (_row != index) CancelConfirm(); _row = index; });
            }
            GUILayout.EndScrollView();

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Recount", Skin.Button)) Defer(() => { Doctor.Recount(apt); Doctor.Scan(); RefreshOutlines(); _message = "Recounted."; });
            if (Cfg.AllowForceClear.Value && apt.Fm != null)
            {
                var sel = _row >= 0 && _row < apt.Entries.Count ? apt.Entries[_row] : null;
                if (sel != null)
                {
                    string refusal = Doctor.TakeRefusal(apt, sel);
                    if (refusal == null)
                    {
                        if (GUILayout.Button("Pick up", Skin.Button)) Defer(ActOnRow);
                    }
                    else if (Doctor.CanDelete(apt, sel))
                    {
                        if (_confirmDelete == null && GUILayout.Button("Delete…", Skin.Button)) Defer(ActOnRow);
                    }
                    else GUILayout.Label(Skin.Color("Can't pick up: " + refusal + (sel.Content == true ? " (it holds items)" : ""), Skin.Dim), Skin.Small);
                }
                int n = Doctor.TakeableCount(apt);
                if (n > 0 && !Confirming && GUILayout.Button($"Clear all ({n})", Skin.Button)) Defer(() => _confirmClearAll = true);
            }
            GUILayout.EndHorizontal();
            if (_confirmDelete != null)
            {
                GUILayout.Label(Skin.Color($"Delete {_confirmDelete.Name} permanently? It is removed from the world for good and does not go to " +
                                           "your inventory. This cannot be undone.", Skin.Amber), Skin.Label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Yes, delete", Skin.Button)) Defer(RunDelete);
                if (GUILayout.Button("Cancel", Skin.Button)) Defer(CancelConfirm);
                GUILayout.EndHorizontal();
            }
            if (_confirmClearAll)
            {
                GUILayout.Label(Skin.Color($"Pick up all {Doctor.TakeableCount(apt)} takeable leftovers in apartment {a.apartmentNumber}? " +
                                           "Items go to your inventory. This is permanent.", Skin.Amber), Skin.Label);
                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Yes, clear all", Skin.Button)) Defer(RunClearAll);
                if (GUILayout.Button("Cancel", Skin.Button)) Defer(() => _confirmClearAll = false);
                GUILayout.EndHorizontal();
            }
        }

        private static void HandleKeys()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            switch (e.keyCode)
            {
                case KeyCode.Escape:
                    if (Confirming) Defer(CancelConfirm);
                    else Defer(Close);
                    break;
                case KeyCode.UpArrow: Defer(() => MoveRow(-1)); break;
                case KeyCode.DownArrow: Defer(() => MoveRow(1)); break;
                case KeyCode.LeftArrow: Defer(() => SwitchApartment(-1)); break;
                case KeyCode.RightArrow: Defer(() => SwitchApartment(1)); break;
                case KeyCode.Return:
                case KeyCode.KeypadEnter: Defer(ActOnRow); break;
                default: return;
            }
            e.Use();
        }
    }
}

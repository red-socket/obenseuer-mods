using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SaveGuard
{
    // Main-menu window (§7.1), damaged-load prompt (§6.3), in-game status box, failure notice (§4.4) and the
    // main-menu banner (§6.4). All IMGUI; input to the game's UI is blocked while one of them has focus (§7.2).
    internal static class GuardWindow
    {
        private const string MainMenuScene = "Main Menu";

        // ---- notices (banner + window list) ----

        // Warning notices are drawn in amber (the house style has no red text).
        private sealed class Notice { internal string Id, Text; internal bool Warning, Dismissed; }
        private static readonly List<Notice> Notices = new List<Notice>();
        private static int _noticesVersion;

        internal static void AddNotice(string id, string text, bool warning)
        {
            _noticesVersion++;
            var n = Notices.FirstOrDefault(x => x.Id == id);
            if (n != null)
            {
                if (n.Text != text) n.Dismissed = false;
                n.Text = text; n.Warning = warning;
                return;
            }
            Notices.Add(new Notice { Id = id, Text = text, Warning = warning });
        }

        private static void Dismiss(Notice n)
        {
            n.Dismissed = true;
            _noticesVersion++;
        }

        // ---- state ----

        private static bool _mainOpen, _statusOpen;
        private static string _statusText;
        private static string _failureText;
        private static bool _cursorSaved, _savedVisible;
        private static CursorLockMode _savedLock;

        private sealed class Prompt
        {
            internal int OpenedFrame;
            internal LoadMenu Menu;
            internal string SlotDir, Character, Slot, DateText;
            internal CheckResult Result;
            internal RestorePoint Best;
            internal bool InMainMenu;
            internal string Message;
        }
        private static Prompt _prompt;

        internal static bool AnyOpen => _mainOpen || _prompt != null || _failureText != null;

        // IMGUI runs OnGUI once per event and expects the same controls in the Layout pass and the event pass that
        // follows it. Clicks therefore only record what to do; it runs after everything is drawn.
        private static Action _deferred;
        private static void Defer(Action a) => _deferred += a;

        private static bool InMainMenuScene()
        {
            try { return SceneManager.GetActiveScene().name == MainMenuScene; }
            catch { return false; }
        }

        // ---- entry points ----

        internal static void OnHotkey()
        {
            if (InMainMenuScene())
            {
                if (_mainOpen) CloseMain();
                else if (_prompt == null) OpenMain(null, null);
                return;
            }
            _statusOpen = !_statusOpen;
            if (_statusOpen) _statusText = BuildStatus();
        }

        internal static void OnSceneLoaded(Scene scene)
        {
            InputBlock.Release();
            _mainOpen = false;
            _statusOpen = false;
            _prompt = null;
            if (scene.name == MainMenuScene)
            {
                ScanMainMenu();
                Toasts.OnMainMenu();
            }
        }

        internal static void ShowFailureNotice(string text)
        {
            _failureText = text;
            try
            {
                if (!_cursorSaved) { _savedVisible = Cursor.visible; _savedLock = Cursor.lockState; _cursorSaved = true; }
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }
            catch (Exception e) { Log.Exception("cursor", e); }
        }

        // The game hides and locks the cursor again when the pause menu closes (PauseMenu.cs:276) or the input mode
        // changes (GameController.UpdateCursorVisibility), so the notice re-applies its cursor on every OnGUI call
        // while it is shown. OnGUI runs after every Update, so the game's change is undone in the same frame.
        private static void KeepCursorFree()
        {
            if (!Cursor.visible) Cursor.visible = true;
            if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
        }

        // The game may have changed what it wants while the notice was open (the pause menu opened or closed with the
        // Pause Menu key), so in gameplay the cursor follows the game's current state rather than the snapshot.
        private static void CloseFailure()
        {
            _failureText = null;
            if (!_cursorSaved) return;
            _cursorSaved = false;
            try
            {
                var gc = GameController.instance;
                if (!InMainMenuScene() && gc != null && !Vanilla.LoadInProgress())
                {
                    bool visible = gc.cursorVisible || (PauseMenu.instance != null && PauseMenu.instance.PauseIsActive());
                    gc.ChangeCursorStatus(visible ? CursorLockMode.None : CursorLockMode.Locked, visible);
                    return;
                }
            }
            catch (Exception e) { Log.Exception("cursor", e); }
            try { Cursor.visible = _savedVisible; Cursor.lockState = _savedLock; } catch { }
        }

        internal static void ShowDamagedPrompt(LoadMenu menu, string slotDir, CheckResult r)
        {
            var p = new Prompt
            {
                OpenedFrame = Time.frameCount,
                Menu = menu, SlotDir = slotDir, Result = r,
                Slot = Path.GetFileName(slotDir),
                Character = Path.GetFileName(Path.GetDirectoryName(slotDir)),
                InMainMenu = InMainMenuScene() && Vanilla.IsMainMenu(menu),
            };
            var h = SlotCheck.ReadHeader(slotDir);
            p.DateText = h != null ? SlotCheck.FormatDate(h.Date, h.DateFromMtime) : "no date";
            if (p.InMainMenu) p.Best = NewestGoodPoint(p.Character, p.Slot);
            _prompt = p;
        }

        private static RestorePoint NewestGoodPoint(string character, string slot)
        {
            try
            {
                foreach (var pt in BackupStore.ListPoints(character))
                {
                    if (!string.Equals(pt.Slot, slot, StringComparison.OrdinalIgnoreCase) || pt.Kind == "failed" || !pt.Complete) continue;
                    if (SlotCheck.Full(pt.Dir, false).Ok) return pt;
                }
            }
            catch (Exception e) { Log.Exception("restore point search", e); }
            return null;
        }

        private static string BuildStatus()
        {
            try
            {
                string ch = Vanilla.CurrentCharacterFolder();
                if (string.IsNullOrEmpty(ch)) return $"Save Guard: no character loaded. Restore from the main menu ({KeyName()}).";
                var (count, last) = BackupStore.Summary(ch);
                string when = count == 0 ? "none yet" : "last " + last.ToLocalTime().ToString("HH:mm");
                return $"Save Guard: {count} backups for {Paths.DisplayName(ch)}, {when}. Restore from the main menu ({KeyName()}).";
            }
            catch (Exception e) { Log.Exception("status", e); return "Save Guard: status unavailable."; }
        }

        internal static string KeyName()
        {
            try { return Cfg.ToggleWindow.Value.ToString(); }
            catch { return "the Save Guard hotkey"; }
        }

        // ---- main-menu scan (§6.4) ----

        internal static void ScanMainMenu()
        {
            try
            {
                Notices.RemoveAll(n => n.Id.StartsWith("scan:", StringComparison.Ordinal) && !n.Dismissed);
                _noticesVersion++;
                if (!Directory.Exists(Paths.Saves)) return;
                foreach (var charDir in Directory.GetDirectories(Paths.Saves))
                {
                    string ch = Path.GetFileName(charDir);
                    foreach (var slot in Paths.SlotNames)
                    {
                        string dir = Path.Combine(charDir, slot);
                        if (!Directory.Exists(dir)) continue;
                        var r = SlotCheck.Structural(dir);
                        if (r.Status != SlotStatus.Broken && r.Status != SlotStatus.Damaged) continue;
                        string what = r.Status == SlotStatus.Broken ? "is damaged" : "may be partly damaged";
                        string id = $"scan:{ch}/{slot}:{r.Reason}";
                        if (Notices.Any(n => n.Id == id)) continue;   // dismissed earlier this session
                        AddNotice(id, $"{Paths.SlotDisplay(slot)} of {Paths.DisplayName(ch)} {what} ({r.Reason}). Press {KeyName()} to restore.", r.Status == SlotStatus.Broken);
                        Log.Warn($"Slot {ch}/{slot}: {r}");
                    }
                }
            }
            catch (Exception e) { Log.Exception("main-menu scan", e); }
        }

        // ---- main window model ----

        private sealed class SlotRow { internal string Name; internal SlotHeader Header; internal CheckResult Check; }

        private static List<string> _chars = new List<string>();
        private static int _charIndex;
        private static List<SlotRow> _slots = new List<SlotRow>();
        private static List<RestorePoint> _points = new List<RestorePoint>();
        private static string _selSlot;
        private static RestorePoint _selPoint;
        private static CheckResult _selPointCheck;
        private static bool _showAll;
        private static string _target;
        private static bool _confirming;
        private static string _message;
        private static bool _messageIsError;
        private static Vector2 _pointsScroll, _noticeScroll, _mainScroll, _promptScroll;
        private static bool _charGone;

        private static string CurrentChar => _charIndex >= 0 && _charIndex < _chars.Count ? _chars[_charIndex] : null;

        private static void OpenMain(string character, string slot)
        {
            _mainOpen = true;
            _message = null;
            _confirming = false;
            try
            {
                _chars = BackupStore.ListCharacters();
                int i = character != null ? _chars.FindIndex(c => string.Equals(c, character, StringComparison.OrdinalIgnoreCase)) : -1;
                _charIndex = i >= 0 ? i : Math.Min(_charIndex, Math.Max(0, _chars.Count - 1));
                LoadCharacter(slot);
            }
            catch (Exception e) { Log.Exception("open window", e); _message = "Could not read the save folders: " + e.Message; _messageIsError = true; }
        }

        private static void CloseMain()
        {
            _mainOpen = false;
            _confirming = false;
        }

        private static void LoadCharacter(string slot)
        {
            _slots.Clear();
            _points.Clear();
            _selPoint = null;
            _selPointCheck = null;
            _confirming = false;
            string ch = CurrentChar;
            _charGone = false;
            if (ch == null) return;
            _charGone = !Directory.Exists(Path.Combine(Paths.Saves, ch));
            foreach (var name in Paths.SlotNames)
            {
                string dir = Paths.SlotDir(ch, name);
                _slots.Add(new SlotRow { Name = name, Header = SlotCheck.ReadHeader(dir), Check = SlotCheck.Structural(dir) });
            }
            _points = BackupStore.ListPoints(ch);
            _selSlot = slot ?? _slots.FirstOrDefault(s => s.Check.Status == SlotStatus.Broken || s.Check.Status == SlotStatus.Damaged)?.Name
                            ?? _slots.FirstOrDefault(s => s.Check.Status != SlotStatus.Empty)?.Name ?? "Autosave";
            SelectPoint(VisiblePoints().FirstOrDefault());
        }

        private static IEnumerable<RestorePoint> VisiblePoints() =>
            _showAll ? _points : _points.Where(p => string.Equals(p.Slot, _selSlot, StringComparison.OrdinalIgnoreCase));

        private static void SelectPoint(RestorePoint p)
        {
            _selPoint = p;
            _selPointCheck = null;
            _confirming = false;
            _target = p?.Slot;
            if (p == null) return;
            try { _selPointCheck = SlotCheck.Full(p.Dir, false); }
            catch (Exception e) { _selPointCheck = new CheckResult(SlotStatus.Broken, e.Message); }
        }

        private static List<string> TargetOptions(RestorePoint p)
        {
            var list = new List<string> { p.Slot };
            for (int i = 1; i <= 10; i++)
            {
                string s = "Slot" + i;
                if (!string.Equals(s, p.Slot, StringComparison.OrdinalIgnoreCase)) list.Add(s);
            }
            return list;
        }

        // §7.1 conditions; returns null when Restore may run, otherwise the hint.
        private static string RestoreBlocker()
        {
            if (_selPoint == null) return "Select a restore point.";
            if (!InMainMenuScene()) return "Restore works only in the main menu.";
            var mm = MainMenu.Instance;
            if (mm != null && mm.currentMenu != MainMenu.CurrentWindow.Main && mm.currentMenu != MainMenu.CurrentWindow.Load)
                return "Close the game's open menu or warning first.";
            if (Vanilla.LoadInProgress()) return "The game is loading a save.";
            if (RestoreService.InProgress || PendingOperation())
                return "Save Guard has unfinished work. Restart the game first.";
            if (_selPointCheck == null) return "This restore point could not be checked.";
            if (!_selPointCheck.Ok && !(_selPoint.Kind == "failed" && _selPointCheck.Status == SlotStatus.Damaged))
                return "This restore point fails the check: " + _selPointCheck.Reason;
            return null;
        }

        // Journal checks touch the disk; OnGUI runs several times per frame, so check once per second.
        private static float _pendingCheckedAt = -10f;
        private static bool _pending;
        private static bool PendingOperation()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _pendingCheckedAt >= 1f)
            {
                _pendingCheckedAt = now;
                _pending = SaveTxn.Unresolved() || KvFile.Read(Paths.RestoreJournal) != null;
            }
            return _pending;
        }

        private static void DoRestore(RestorePoint p, string target, bool allowIncomplete)
        {
            string result = RestoreService.Restore(p, target, allowIncomplete);
            _confirming = false;
            if (result == null) { _message = $"{Paths.SlotDisplay(target)} restored."; _messageIsError = false; }
            else if (result.StartsWith("!", StringComparison.Ordinal)) { _message = result.Substring(1); _messageIsError = false; }
            else { _message = result; _messageIsError = true; }
            string message = _message;
            bool isError = _messageIsError;
            OpenMain(CurrentChar, target);   // re-reads slots and restore points
            _message = message;
            _messageIsError = isError;
        }

        // ---- drawing ----

        internal static void OnGUI()
        {
            Vector2 screen = Skin.Begin();
            HandleEscape();
            bool menuScene = InMainMenuScene();
            if (_mainOpen && !menuScene) _mainOpen = false;

            if (menuScene && !_mainOpen && _prompt == null) DrawBanner(screen);
            if (_statusOpen && !menuScene) DrawStatus();
            if (_mainOpen) DrawMain(screen);
            if (_prompt != null) DrawPrompt(screen);
            if (_failureText != null)
            {
                KeepCursorFree();
                DrawFailure(screen);
            }
            Toasts.OnGUI(screen);

            var run = _deferred;
            _deferred = null;
            if (run != null)
            {
                try { run(); }
                catch (Exception e) { Log.Exception("window action", e); }
            }
            InputBlock.Sync(AnyOpen);
        }

        private static void HandleEscape()
        {
            var e = Event.current;
            if (e == null || e.type != EventType.KeyDown) return;
            if (_failureText != null && (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter))
            {
                Defer(CloseFailure);
                e.Use();
                return;
            }
            if (e.keyCode != KeyCode.Escape) return;
            // In game, Escape is also the game's Pause Menu key (handled in the same frame by GameController.Update),
            // so the failure notice there closes only with OK or Enter.
            if (_failureText != null) { if (!InMainMenuScene()) return; Defer(CloseFailure); }
            else if (_prompt != null) Defer(() => _prompt = null);
            else if (_mainOpen && _confirming) Defer(() => _confirming = false);
            else if (_mainOpen) Defer(CloseMain);
            else return;
            e.Use();
        }

        // Text and height are rebuilt only when the notices, the hotkey or the width change: OnGUI runs several
        // times per frame for as long as the banner shows.
        private static string _bannerKey;
        private static GUIContent _bannerContent;
        private static float _bannerHeight;

        private static void DrawBanner(Vector2 screen)
        {
            float w = Mathf.Min(760f, screen.x - 48f);
            string key = _noticesVersion + "|" + KeyName() + "|" + w;
            if (key != _bannerKey)
            {
                _bannerKey = key;
                var visible = Notices.Where(n => !n.Dismissed).ToList();
                if (visible.Count == 0) _bannerContent = null;
                else
                {
                    var lines = visible.Take(3).Select(n => n.Warning ? Skin.Color("Save Guard: " + n.Text, Skin.Amber) : "Save Guard: " + n.Text).ToList();
                    if (visible.Count > 3) lines.Add($"+{visible.Count - 3} more (press {KeyName()})");
                    _bannerContent = new GUIContent(string.Join("\n", lines));
                    _bannerHeight = Skin.BannerBox.CalcHeight(_bannerContent, w);
                }
            }
            if (_bannerContent == null) return;
            GUI.Box(new Rect(24f, 24f, w, _bannerHeight), _bannerContent, Skin.BannerBox);
        }

        private static void DrawStatus()
        {
            var content = new GUIContent(_statusText ?? "");
            float w = 560f;   // fits the narrowest virtual screen (UI.Scale 2.5: 768 wide)
            float h = Skin.Box.CalcHeight(content, w);
            GUI.Box(new Rect(24f, 24f, w, h), content, Skin.Box);
        }

        private static void DrawFailure(Vector2 screen)
        {
            float w = Mathf.Min(760f, screen.x - 48f);
            var rect = new Rect((screen.x - w) / 2f, screen.y * 0.3f, w, 10f);
            var content = new GUIContent(_failureText);
            rect.height = Skin.Label.CalcHeight(content, w - 32f) + 110f;
            GUILayout.BeginArea(rect, Skin.Box);
            GUILayout.Label("Save Guard", Skin.Title);
            GUILayout.Label(_failureText, Skin.Label);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("OK", Skin.Button, GUILayout.Width(120))) Defer(CloseFailure);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private static bool ControllerMode()
        {
            try { return InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller; }
            catch { return false; }
        }

        // With a controller, vanilla calls LoadGameSlot as soon as a slot is chosen (LoadMenu.cs:481-485) and the
        // prompt blocks the game's EventSystem, so the prompt reads the pad itself: A = first button (restore when
        // offered, otherwise Load anyway), X = Load anyway, B or Menu = Cancel. Called from Plugin.Update.
        // The pad buttons are read directly, whatever the game's input mode: the game switches to controller mode
        // only inside its own key checks (KeyBindings.CheckKeyController), possibly later in the same frame, so a
        // first press from keyboard mode would otherwise be lost.
        internal static void ControllerUpdate()
        {
            var p = _prompt;
            if (p == null || Time.frameCount <= p.OpenedFrame) return;
            var im = InputManager.instance;
            if (im == null || im.keyBindings == null) return;
            var kb = im.keyBindings;
            bool canRestore = p.InMainMenu && p.Best != null;
            if (Input.GetKeyDown(kb.ControllerB) || Input.GetKeyDown(kb.ControllerMenu)) PromptAction(p, false, false, false);
            else if (Input.GetKeyDown(kb.ControllerA)) PromptAction(p, canRestore, false, !canRestore);
            else if (canRestore && Input.GetKeyDown(kb.ControllerX)) PromptAction(p, false, false, true);
        }

        private static void DrawPrompt(Vector2 screen)
        {
            var p = _prompt;
            float w = Mathf.Min(820f, screen.x - 48f);
            float h = Mathf.Min(360f, screen.y - 48f);
            var rect = new Rect((screen.x - w) / 2f, Mathf.Min(screen.y * 0.28f, screen.y - h - 24f), w, h);
            GUILayout.BeginArea(rect, Skin.Box);
            _promptScroll = GUILayout.BeginScrollView(_promptScroll);
            bool broken = p.Result.Status == SlotStatus.Broken;
            GUILayout.Label(broken ? "This save looks damaged." : "This save may be partly damaged.", Skin.Bold);
            GUILayout.Label($"{Paths.SlotDisplay(p.Slot)} ({p.DateText}): {p.Result.Reason}. " +
                            (broken ? "The game would fail to load it." : "The game would load it, but some areas or conversations may reset."), Skin.Label);
            if (!p.InMainMenu) GUILayout.Label($"Restore from the main menu ({KeyName()}).", Skin.Label);
            else if (p.Best == null) GUILayout.Label("No restore point for this slot passes the check. Show all restore points to pick another.", Skin.Small);
            if (!string.IsNullOrEmpty(p.Message)) GUILayout.Label(Skin.Color(p.Message, Skin.Amber), Skin.Label);
            bool canRestore = p.InMainMenu && p.Best != null;
            if (ControllerMode())
                GUILayout.Label(canRestore ? "Controller: A restore, X load anyway, B cancel." : "Controller: A load anyway, B cancel.", Skin.Small);
            GUILayout.Space(10);

            bool restore = false, showAll = false, loadAnyway = false, cancel = false;
            if (canRestore)
                restore = GUILayout.Button($"Restore newest good copy ({SlotCheck.FormatDate(p.Best.SaveDate, p.Best.SaveDateFromMtime)}, {p.Best.KindLabel()})", Skin.Button);
            GUILayout.BeginHorizontal();
            if (!broken) loadAnyway = GUILayout.Button("Load anyway", Skin.Button);
            if (p.InMainMenu) showAll = GUILayout.Button("Show all restore points", Skin.Button);
            if (broken) loadAnyway = GUILayout.Button("Load anyway", Skin.Button);
            cancel = GUILayout.Button("Cancel", Skin.Button);
            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();

            if (restore || showAll || loadAnyway || cancel) Defer(() => PromptAction(p, restore, showAll, loadAnyway));
        }

        private static void PromptAction(Prompt p, bool restore, bool showAll, bool loadAnyway)
        {
            if (restore)
            {
                string result = RestoreService.Restore(p.Best, p.Slot, false);
                if (result == null || result.StartsWith("!", StringComparison.Ordinal))
                {
                    _prompt = null;
                    if (result != null) Toasts.Show(result.Substring(1), 10f);
                }
                else p.Message = result;
            }
            else if (showAll)
            {
                _prompt = null;
                OpenMain(p.Character, p.Slot);
            }
            else if (loadAnyway)
            {
                _prompt = null;
                // No InputBlock.Release here: from the pad this runs in Plugin.Update, and re-enabling the menu
                // navigation handler mid-frame would let its own Update see the same A press and click the slot
                // button again (LoadMenu.cs:481-485), opening a second prompt. The Sync at the end of OnGUI
                // releases the block after every Update of this frame has run.
                LoadMenuPatch.BypassOnce = true;
                try { p.Menu.LoadGameSlot(); }
                catch (Exception e) { LoadMenuPatch.BypassOnce = false; Log.Exception("Load anyway", e); }
            }
            else _prompt = null;
        }

        private static void DrawMain(Vector2 screen)
        {
            float w = Mathf.Min(1180f, screen.x - 48f), h = Mathf.Min(720f, screen.y - 48f);
            var rect = new Rect(screen.x - w - 24f, Mathf.Max(24f, (screen.y - h) / 2f), w, h);
            GUILayout.BeginArea(rect, Skin.Box);

            GUILayout.BeginHorizontal();
            GUILayout.Label($"Save Guard {Plugin.Version}", Skin.Title);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Close", Skin.Button, GUILayout.Width(90))) Defer(CloseMain);
            GUILayout.EndHorizontal();

            if (_chars.Count == 0)
            {
                GUILayout.Label("No characters and no backups yet.", Skin.Label);
                DrawFooter();
                GUILayout.EndArea();
                return;
            }

            // Everything below the title scrolls, so a large UI.Scale never clips the restore buttons.
            _mainScroll = GUILayout.BeginScrollView(_mainScroll);
            GUILayout.BeginHorizontal();
            GUILayout.Label("Character", Skin.Label, GUILayout.Width(100));
            if (GUILayout.Button("<", Skin.Button, GUILayout.Width(36))) Defer(() => { _charIndex = (_charIndex + _chars.Count - 1) % _chars.Count; LoadCharacter(null); });
            GUILayout.Label(Paths.DisplayName(CurrentChar) + (_charGone ? Skin.Color("  (no longer in Saves)", Skin.Dim) : ""), Skin.Bold, GUILayout.Width(360));
            if (GUILayout.Button(">", Skin.Button, GUILayout.Width(36))) Defer(() => { _charIndex = (_charIndex + 1) % _chars.Count; LoadCharacter(null); });
            GUILayout.FlexibleSpace();
            GUILayout.EndHorizontal();
            GUILayout.Space(6);

            GUILayout.BeginHorizontal();
            DrawSlots();
            GUILayout.Space(16);
            DrawPoints();
            GUILayout.EndHorizontal();

            DrawNotices();
            GUILayout.EndScrollView();
            DrawFooter();
            GUILayout.EndArea();
        }

        private static void DrawSlots()
        {
            GUILayout.BeginVertical(GUILayout.Width(400));
            GUILayout.Label("Slots", Skin.Bold);
            foreach (var s in _slots)
            {
                string date = s.Header != null && s.Header.Date != default ? s.Header.Date.ToString("dd.MM HH:mm") + (s.Header.DateFromMtime ? "*" : "") : "";
                string status;
                switch (s.Check.Status)
                {
                    case SlotStatus.Ok: status = Skin.Color("OK", Skin.Text2); break;
                    case SlotStatus.Empty: status = Skin.Color("Empty", Skin.Dim); break;
                    case SlotStatus.Damaged: status = Skin.Color("DAMAGED", Skin.Amber); break;
                    default: status = Skin.Color("<b>BROKEN</b>", Skin.Amber); break;
                }
                bool sel = string.Equals(s.Name, _selSlot, StringComparison.OrdinalIgnoreCase);
                string text = $"{Paths.SlotDisplay(s.Name)}   {(date.Length > 0 ? date + "   " : "")}{status}";
                string name = s.Name;
                if (GUILayout.Button(text, sel ? Skin.RowSelectedStyle : Skin.RowStyle))
                    Defer(() => { _selSlot = name; SelectPoint(VisiblePoints().FirstOrDefault()); });
            }
            var selRow = _slots.FirstOrDefault(s => s.Name == _selSlot);
            if (selRow != null && selRow.Check.Status != SlotStatus.Ok && selRow.Check.Status != SlotStatus.Empty)
                GUILayout.Label(selRow.Check.Reason, Skin.Small);
            GUILayout.EndVertical();
        }

        private static void DrawPoints()
        {
            GUILayout.BeginVertical();
            GUILayout.Label(_showAll ? "Restore points, all slots (newest first)" : $"Restore points for {Paths.SlotDisplay(_selSlot)} (newest first)", Skin.Bold);
            var visible = VisiblePoints().ToList();
            _pointsScroll = GUILayout.BeginScrollView(_pointsScroll, GUILayout.Height(250));
            if (visible.Count == 0) GUILayout.Label("None yet.", Skin.Small);
            foreach (var p in visible)
            {
                string date = SlotCheck.FormatDate(p.SaveDate != default ? p.SaveDate : p.CreatedUtc.ToLocalTime(), p.SaveDateFromMtime);
                string slot = _showAll ? Paths.SlotDisplay(p.Slot) + "  " : "";
                string level = TrimLevel(p.LevelName);
                string size = p.Bytes > 0 ? $"{p.Bytes / 1048576.0:0.0} MB" : "";
                string kind = p.Kind == "failed" ? Skin.Color(p.KindLabel(), Skin.Amber) : p.KindLabel();
                string text = $"{date}  {slot}{kind}   {p.SaveVersion}   {level}   {size}";
                var pt = p;
                if (GUILayout.Button(text, p == _selPoint ? Skin.RowSelectedStyle : Skin.RowStyle)) Defer(() => SelectPoint(pt));
            }
            GUILayout.EndScrollView();
            bool all = GUILayout.Toggle(_showAll, " show restore points of all slots", Skin.Toggle);
            if (all != _showAll) Defer(() => { _showAll = all; SelectPoint(VisiblePoints().FirstOrDefault()); });

            if (_selPoint != null)
            {
                var options = TargetOptions(_selPoint);
                int idx = Math.Max(0, options.FindIndex(o => string.Equals(o, _target, StringComparison.OrdinalIgnoreCase)));
                GUILayout.BeginHorizontal();
                GUILayout.Label("Restore into:", Skin.Label, GUILayout.Width(110));
                if (GUILayout.Button("<", Skin.Button, GUILayout.Width(36))) { idx = (idx + options.Count - 1) % options.Count; Defer(() => _confirming = false); }
                GUILayout.Label(Paths.SlotDisplay(options[idx]) + (idx == 0 ? " (original slot)" : ""), Skin.Label, GUILayout.Width(220));
                if (GUILayout.Button(">", Skin.Button, GUILayout.Width(36))) { idx = (idx + 1) % options.Count; Defer(() => _confirming = false); }
                _target = options[idx];
                GUILayout.FlexibleSpace();
                string blocker = RestoreBlocker();
                GUI.enabled = blocker == null && !_confirming;
                if (GUILayout.Button("Restore", Skin.Button, GUILayout.Width(110))) Defer(() => _confirming = true);
                GUI.enabled = true;
                GUILayout.EndHorizontal();
                if (blocker != null) GUILayout.Label(blocker, Skin.Small);
                if (_confirming && blocker == null) DrawConfirm();
            }
            if (!string.IsNullOrEmpty(_message)) GUILayout.Label(_messageIsError ? Skin.Color(_message, Skin.Amber) : Skin.Color(_message, Skin.Text2), Skin.Label);
            GUILayout.EndVertical();
        }

        private static void DrawConfirm()
        {
            var p = _selPoint;
            var row = _slots.FirstOrDefault(s => string.Equals(s.Name, _target, StringComparison.OrdinalIgnoreCase));
            string from = SlotCheck.FormatDate(p.SaveDate != default ? p.SaveDate : p.CreatedUtc.ToLocalTime(), p.SaveDateFromMtime);
            string slot = Paths.SlotDisplay(_target);
            string text;
            if (row == null || row.Check.Status == SlotStatus.Empty)
                text = $"Restore the point from {from} into {slot} (empty)?";
            else
            {
                string date = row.Header != null ? SlotCheck.FormatDate(row.Header.Date, row.Header.DateFromMtime) : "no date";
                string state = row.Check.Status == SlotStatus.Ok ? "" : row.Check.Status.ToString().ToUpperInvariant() + ", ";
                text = $"Replace {slot} ({state}{date}) with the restore point from {from}? The current {slot} goes to Save Guard's recovered list, so you can undo this.";
                var replaced = _points.Where(x => x.Kind == "replaced").OrderBy(x => x.CreatedUtc).ToList();
                int limit = Cfg.ReplacedToKeepPerCharacter.Value;
                if (replaced.Count >= limit)
                    text += $" Save Guard keeps {limit} replaced slots per character, so it will delete the oldest one ({Paths.SlotDisplay(replaced[0].Slot)}, " +
                            $"replaced {replaced[0].CreatedUtc.ToLocalTime():dd.MM.yyyy HH:mm}). Setting: ReplacedToKeepPerCharacter.";
            }
            if (_target.StartsWith("Autosave", StringComparison.OrdinalIgnoreCase))
                text += " The next door overwrites autosaves. To keep this state, restore it into a manual slot instead.";
            bool incomplete = p.Kind == "failed" || !p.Complete;
            if (incomplete) text += " This copy is incomplete. Some areas may reset.";
            GUILayout.Label(Skin.Color(text, Skin.Amber), Skin.Label);
            GUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            string target = _target;
            if (GUILayout.Button("Replace", Skin.Button, GUILayout.Width(110))) Defer(() => DoRestore(p, target, incomplete));
            if (GUILayout.Button("Cancel", Skin.Button, GUILayout.Width(110))) Defer(() => _confirming = false);
            GUILayout.EndHorizontal();
        }

        private static void DrawNotices()
        {
            var visible = Notices.Where(n => !n.Dismissed).ToList();
            if (visible.Count == 0) return;
            GUILayout.Space(6);
            GUILayout.Label("Notices", Skin.Bold);
            _noticeScroll = GUILayout.BeginScrollView(_noticeScroll, GUILayout.MaxHeight(110));
            foreach (var n in visible)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label(n.Warning ? Skin.Color(n.Text, Skin.Amber) : n.Text, Skin.Label);
                var notice = n;
                if (GUILayout.Button("Dismiss", Skin.Button, GUILayout.Width(90))) Defer(() => Dismiss(notice));
                GUILayout.EndHorizontal();
            }
            GUILayout.EndScrollView();
        }

        private static void DrawFooter()
        {
            GUILayout.FlexibleSpace();
            GUILayout.Label("Folder: " + Paths.BackupBase.Replace('\\', '/'), Skin.Small);
        }

        private static string TrimLevel(string level)
        {
            if (string.IsNullOrEmpty(level)) return "";
            foreach (var prefix in new[] { "Interior ", "Open Sewer " })
                if (level.StartsWith(prefix, StringComparison.Ordinal)) return level.Substring(prefix.Length);
            return level;
        }
    }
}

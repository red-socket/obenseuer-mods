using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using OS.Items;
using UnityEngine;

namespace FairMarket
{
    // The Invoices window (spec §9.4): IMGUI over the open pause menu, listing InvoiceController.invoices, the debts the
    // game has on record (a letter may still be in the post). While it is open the EventSystem is off (InputBlock), W1
    // skips the pause menu's own input and W2 routes Esc, pad B and pad Menu to Back. The window reads the pad and
    // keyboard itself on unscaled time: up/down move between rows (0.35 s first repeat, then 0.12 s), left/right between
    // Pay and Dismiss, A / X / Enter press the focused button, B steps back (disarm first, then close). The mouse works as
    // usual. The model is rebuilt every 0.5 s in Update from a copy of the list, never during OnGUI; the game's list is
    // never changed except through vanilla PayInvoice and (opt-in) CancelInvoice.
    internal static class InvoiceWindow
    {
        private const float FirstRepeat = 0.35f, Repeat = 0.12f, RefreshSeconds = 0.5f;
        private const string ToggleKey = "toggle";

        // Read by our other mods through reflection (one window at a time).
        internal static bool IsOpen;
        internal static int OpenedFrame = -10, ClosedFrame = -10;
        internal static bool Available;   // the list-window group is on

        private sealed class Row
        {
            internal int Id, Amount, Days;
            internal string Debtor;
            internal Money.Currency Currency;
            internal bool Scam, Expired, CanPay, CanDismiss;
            internal string Key => "row:" + Id;
        }

        private static readonly List<Row> Live = new List<Row>(), Old = new List<Row>();
        private static readonly List<string> Order = new List<string>();
        private static string _header = "", _status;
        private static bool _statusIsPrompt;   // _status asks for the second press; cleared once nothing is armed
        private static bool _showOld;
        private static string _focusKey;
        private static int _col;
        private static Vector2 _scroll;
        private static bool _scrollToFocus;
        private static Rect _focusRect, _viewRect;
        private static float _nextRefresh;
        private static int _lastBackFrame = -1;
        private static int _vDir, _hDir;
        private static float _vNext, _hNext;
        private static Action _deferred;
        private static GUIStyle _cell, _cellWarn, _cellDim;

        // Column widths for this frame (Layout). With room, the full row is debtor, amount, due, a "Scam, no penalty"
        // column and the buttons. On a narrow virtual screen (a 16:10 or 4:3 display with a large UiScale) the tag column
        // goes, "scam" moves into the due column and the debtor column takes what is left, so the buttons stay visible.
        private const float AmountW = 120f, DueW = 140f, TagW = 150f, DebtorMinW = 200f, CompactAmountW = 105f, CompactDueW = 190f;
        private const float CellGap = 8f, ScrollbarW = 22f;
        private static float _debtorW, _amountW, _dueW, _payW, _dismissW;
        private static bool _compact;

        // ---- Other windows (spec §9.4: one at a time) ----
        // Fair Market refuses to open over these, and closes its own window when one of them opens over it: Vital Signs'
        // optional key works over the pause menu and doesn't know about this window. Both windows read the pad and keys
        // directly, so leaving both open would let a press meant for the other one arm and confirm Pay here.

        private static readonly (string type, string field)[] OtherWindows =
        {
            ("QuestHelper.HelperWindow", "IsOpen"), ("VitalSigns.VitalWindow", "IsOpen"), ("BetterTenement.DoctorWindow", "Open"), ("MessageLog.LogWindow", "IsOpen"),
        };
        private static FieldInfo[] _otherFields;

        private static string OtherWindowOpen()
        {
            try
            {
                if (_otherFields == null)
                {
                    _otherFields = new FieldInfo[OtherWindows.Length];
                    for (int i = 0; i < OtherWindows.Length; i++)
                    {
                        var t = AccessTools.TypeByName(OtherWindows[i].type);
                        _otherFields[i] = t != null ? AccessTools.Field(t, OtherWindows[i].field) : null;
                    }
                }
                for (int i = 0; i < _otherFields.Length; i++)
                    if (_otherFields[i] != null && _otherFields[i].GetValue(null) is bool b && b) return OtherWindows[i].type;
            }
            catch { }
            return null;
        }

        internal static string OpenBlocked()
        {
            if (!Available || !Cfg.Hot.ListOn) return "off";
            string gate = PlayGate.Blocked();
            if (gate != null) return gate;
            if (Game.InConversation) return "conversation";
            if (Game.ConsoleOpen) return "developer console open";
            if (Game.Typing) return "typing";
            if (InvoiceController.instance == null || Money.instance == null || TimeOfDayAzure.instance == null) return "no invoices here";
            string other = OtherWindowOpen();
            if (other != null) return other + " is open";
            if (!Game.PauseOpen) return "pause menu closed";
            return null;
        }

        // The checks that don't need the pause menu, made before a key pauses the game for the window.
        internal static string OpenBlockedBeforePause()
        {
            if (!Available || !Cfg.Hot.ListOn) return "off";
            if (InvoiceController.instance == null || Money.instance == null) return "no invoices here";
            return null;
        }

        internal static void Open()
        {
            string blocked = OpenBlocked();
            if (blocked != null)
            {
                Log.Verbose($"Invoices: window not opened ({blocked}).");
                return;
            }
            IsOpen = true;
            OpenedFrame = Time.frameCount;
            InputBlock.Sync(true);
            Confirm.ResetAll();
            _scroll = Vector2.zero;
            _focusKey = null;
            _col = 0;
            SetStatus(null);
            _showOld = Cfg.Hot.ShowExpiredScams;
            Rebuild();
            Log.Verbose("Invoices: window opened.");
        }

        internal static void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            ClosedFrame = Time.frameCount;
            InputBlock.Release();
            Confirm.ResetAll();
            _deferred = null;
        }

        // W2 and pad B (deduplicated per frame, since both can fire for one press): an armed button is disarmed first,
        // then the window closes.
        internal static void Back()
        {
            if (!IsOpen || Time.frameCount == _lastBackFrame) return;
            _lastBackFrame = Time.frameCount;
            if (Game.ConsoleOpen) { Close(); return; }
            if (Confirm.AnyArmed)
            {
                Confirm.ResetAll();
                SetStatus(null);
                return;
            }
            Close();
        }

        // ---- Model ----

        private static void Rebuild()
        {
            _nextRefresh = Time.unscaledTime + RefreshSeconds;
            Live.Clear();
            Old.Clear();
            Order.Clear();
            try
            {
                var ic = InvoiceController.instance;
                var money = Money.instance;
                if (ic == null || ic.invoices == null || money == null) { Close(); return; }
                ItemInvoiceData[] recs = ic.invoices.ToArray();
                var real = new List<Row>();
                var scams = new List<Row>();
                int owedOc = 0, owedRm = 0;
                foreach (var d in recs)
                {
                    if (d == null) continue;
                    var r = new Row
                    {
                        Id = d.invoiceID, Amount = d.amount, Days = d.DaysRemaining(), Debtor = Game.Clean(d.debtor),
                        Currency = d.currency, Scam = d.nocrime, Expired = d.IsExpired(), CanPay = ic.CanPayInvoice(d.invoiceID),
                    };
                    r.CanDismiss = Cfg.Hot.AllowDismissZero && d.amount == 0;
                    if (r.Debtor.Length == 0) r.Debtor = "?";
                    if (r.Scam) (r.Expired ? Old : scams).Add(r);
                    else
                    {
                        real.Add(r);
                        if (r.Currency == Money.Currency.OC) owedOc += r.Amount; else owedRm += r.Amount;
                    }
                }
                Live.AddRange(real.OrderBy(r => r.Days).ThenBy(r => r.Id));
                Live.AddRange(scams.OrderBy(r => r.Days).ThenBy(r => r.Id));
                var oldSorted = Old.OrderBy(r => r.Days).ThenBy(r => r.Id).ToList();
                Old.Clear();
                Old.AddRange(oldSorted);

                foreach (var r in Live) Order.Add(r.Key);
                if (Old.Count > 0)
                {
                    Order.Add(ToggleKey);
                    if (_showOld) foreach (var r in Old) Order.Add(r.Key);
                }

                string have = Strings.And(Fmt.MoneyText(money.OCMoneyAmount, Money.Currency.OC), Fmt.MoneyText(money.RMoneyAmount, Money.Currency.RM));
                var owed = new List<string>();
                if (owedOc > 0) owed.Add(Fmt.MoneyText(owedOc, Money.Currency.OC));
                if (owedRm > 0) owed.Add(Fmt.MoneyText(owedRm, Money.Currency.RM));
                _header = owed.Count == 0 ? Strings.OweNothing(have) : Strings.Owe(string.Join(" and ", owed.ToArray()), have);
            }
            catch (Exception e)
            {
                Log.Exception("invoice list", e);
                _header = Strings.ListFailed;
            }
            if (_focusKey == null || !Order.Contains(_focusKey)) _focusKey = Order.Count > 0 ? Order[0] : null;
            var f = FocusedRow();
            if (f == null || !f.CanDismiss) _col = 0;
        }

        private static Row Find(string key)
        {
            foreach (var r in Live) if (r.Key == key) return r;
            foreach (var r in Old) if (r.Key == key) return r;
            return null;
        }

        private static Row FocusedRow() => _focusKey == null || _focusKey == ToggleKey ? null : Find(_focusKey);

        private static string Due(Row r)
        {
            if (r.Scam && r.Expired) return Strings.DueExpired;
            if (r.Expired) return Strings.DueOverdue;
            if (r.Days <= 0) return Strings.DueToday;
            if (r.Days == 1) return Strings.DueTomorrow;
            return Strings.DueIn(r.Days);
        }

        // ---- Actions ----

        private static void Activate(string key, int col)
        {
            if (key == null) return;
            if (key == ToggleKey)
            {
                Confirm.ResetAll();
                _showOld = !_showOld;
                SetStatus(null);
                Rebuild();
                return;
            }
            var row = Find(key);
            if (row == null) return;
            if (col == 1 && row.CanDismiss) Dismiss(row);
            else Pay(row);
            if (IsOpen) Rebuild();
        }

        // Exactly the invoice machine's effects (SRC/InvoicePaymentMachine.cs:38-53): vanilla PayInvoice takes the money,
        // runs the debt's onPaid (the lawyer's ClearDebt, a loan's downpayment), removes the record and gives the
        // fake-invoice achievement for a scam. Fair Market adds a notification and nothing else.
        private static void Pay(Row row)
        {
            var ic = InvoiceController.instance;
            var money = Money.instance;
            if (ic == null || money == null) return;
            Confirm.Reset("dismiss");
            var rec = ic.GetInvoice(row.Id);
            if (rec == null)
            {
                Confirm.ResetAll();
                SetStatus(Strings.AlreadyGone);
                return;
            }
            if (!ic.CanPayInvoice(row.Id))
            {
                Confirm.ResetAll();
                SetStatus(rec.IsExpired() ? Strings.Expired + "." : Strings.NotEnoughMoney + ".");
                return;
            }
            string amount = Fmt.MoneyText(rec.amount, rec.currency);
            string debtor = Game.Clean(rec.debtor);
            if (!Confirm.IsArmed("pay", row.Id))
            {
                Confirm.Arm("pay", row.Id, Cfg.InvoiceList.ConfirmSeconds.Value);
                SetStatus(Strings.PayPrompt(amount, debtor) + (rec.nocrime ? Strings.ScamPromptSuffix : ""), true);
                return;
            }
            Confirm.Reset("pay");
            if (!PlayGate.Open || !ic.CanPayInvoice(row.Id))
            {
                SetStatus(Strings.NotNow);
                return;
            }
            int amt = rec.amount;
            var cur = rec.currency;
            if (!ic.PayInvoice(row.Id))
            {
                if (cur == Money.Currency.RM) money.NotEnoughRM();
                else money.NotEnoughOC();
                SetStatus(Strings.NotEnoughMoney + ".");
                return;
            }
            try { Notifications.instance?.CreateNotification(Strings.NotificationTitle, Strings.NotificationText(debtor, amt, cur), false); }
            catch (Exception e) { Log.Exception("invoice notification", e); }
            SetStatus(Strings.Paid);
            Log.Verbose($"Invoices: paid #{row.Id}, {amt} {cur}.");
        }

        // Opt-in (AllowDismissZero): vanilla CancelInvoice on a 0 record. Permanent, as paying it would be.
        private static void Dismiss(Row row)
        {
            var ic = InvoiceController.instance;
            if (ic == null) return;
            Confirm.Reset("pay");
            var rec = ic.GetInvoice(row.Id);
            if (rec == null || rec.amount != 0 || !Cfg.Hot.AllowDismissZero)
            {
                Confirm.ResetAll();
                SetStatus(Strings.AlreadyGone);
                return;
            }
            if (!Confirm.IsArmed("dismiss", row.Id))
            {
                Confirm.Arm("dismiss", row.Id, Cfg.InvoiceList.ConfirmSeconds.Value);
                SetStatus(Strings.DismissPrompt(Game.Clean(rec.debtor)), true);
                return;
            }
            Confirm.Reset("dismiss");
            if (!PlayGate.Open) { SetStatus(Strings.NotNow); return; }
            ic.CancelInvoice(row.Id);
            SetStatus(Strings.Dismissed);
            Log.Verbose($"Invoices: dismissed #{row.Id}, 0 {rec.currency}.");
        }

        // ---- Frame ----

        internal static void Tick()
        {
            if (!IsOpen) return;
            // The F1 console reads raw keys too (SRC/DeveloperConsole.cs:356): close, so nothing typed there reaches the
            // window, and the console's input field gets the EventSystem back.
            if (!Game.PauseOpen || SaveController.Loading || Game.Dead || Game.ConsoleOpen || !Cfg.Hot.ListOn) { Close(); return; }
            string other = OtherWindowOpen();
            if (other != null)
            {
                Log.Verbose($"Invoices: window closed ({other} opened over it).");
                Close();
                return;
            }
            // Confirm.Tick (earlier in Plugin.Update) drops an expired arm; the "Press again" line goes with it.
            if (_statusIsPrompt && !Confirm.AnyArmed) SetStatus(null);
            InputBlock.Sync(true);
            if (Time.frameCount > OpenedFrame + 1) ReadInput();
            if (IsOpen && Time.unscaledTime >= _nextRefresh) Rebuild();
        }

        private static void ReadInput()
        {
            if (Input.GetKeyDown(KeyCode.Joystick1Button1)) { Back(); return; }
            if (Input.GetKeyDown(KeyCode.Joystick1Button0) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter) || Input.GetKeyDown(KeyCode.X))
            {
                Activate(_focusKey, _col);
                return;
            }
            int v = Axis(true);
            if (Held(v, ref _vDir, ref _vNext)) MoveFocus(-v);
            int h = Axis(false);
            if (Held(h, ref _hDir, ref _hNext))
            {
                var f = FocusedRow();
                int col = f != null && f.CanDismiss ? Mathf.Clamp(_col + h, 0, 1) : 0;
                if (col != _col) { _col = col; Disarm(); }
            }
        }

        private static void SetStatus(string text, bool prompt = false)
        {
            _status = text;
            _statusIsPrompt = prompt && text != null;
        }

        private static void Disarm()
        {
            if (!Confirm.AnyArmed) return;
            Confirm.ResetAll();
            SetStatus(null);
        }

        // +1 up/right, -1 down/left, 0 none. Keyboard: the game's movement keys and the arrows; pad: D-pad and stick.
        private static int Axis(bool vertical)
        {
            float a = 0f;
            try
            {
                a = vertical ? UINavigationCore.VerticalInput() : UINavigationCore.HorizonalInput();
                // The D-pad, read as the game's own UI does (UINavigationBase): VerticalInput misses it under the pause menu.
                if (Mathf.Abs(a) < 0.5f) a = Input.GetAxisRaw(vertical ? "DPadVertical" : "DPadHorizontal");
                if (Mathf.Abs(a) < 0.5f) a = Input.GetAxisRaw(vertical ? "LeftStickInventoryVertical" : "LeftStickInventoryHorizontal");
            }
            catch (Exception) { a = 0f; }
            if (vertical)
            {
                if (Input.GetKey(KeyCode.UpArrow)) a = 1f;
                else if (Input.GetKey(KeyCode.DownArrow)) a = -1f;
            }
            else
            {
                if (Input.GetKey(KeyCode.RightArrow)) a = 1f;
                else if (Input.GetKey(KeyCode.LeftArrow)) a = -1f;
            }
            return a > 0.5f ? 1 : a < -0.5f ? -1 : 0;
        }

        private static bool Held(int dir, ref int last, ref float next)
        {
            float now = Time.unscaledTime;
            if (dir == 0) { last = 0; return false; }
            if (dir != last) { last = dir; next = now + FirstRepeat; return true; }
            if (now < next) return false;
            next = now + Repeat;
            return true;
        }

        private static void MoveFocus(int delta)
        {
            if (Order.Count == 0) return;
            int cur = Order.IndexOf(_focusKey);
            int next = cur < 0 ? 0 : Mathf.Clamp(cur + delta, 0, Order.Count - 1);
            if (Order[next] == _focusKey) return;
            _focusKey = Order[next];
            var f = FocusedRow();
            if (f == null || !f.CanDismiss) _col = 0;
            _scrollToFocus = true;
            Disarm();
        }

        private static void Focus(string key, int col)
        {
            if (key != _focusKey || col != _col) Disarm();
            _focusKey = key;
            _col = col;
        }

        // ---- Drawing ----

        internal static void Draw(Vector2 screen)
        {
            if (!IsOpen) return;
            Styles();
            float w = Mathf.Min(1000f, screen.x - 80f), h = Mathf.Max(300f, screen.y - 140f);
            var r = new Rect((screen.x - w) / 2f, 70f, w, h);
            GUI.Box(r, GUIContent.none, Skin.Box);
            GUILayout.BeginArea(new Rect(r.x + 16f, r.y + 12f, r.width - 32f, r.height - 24f));

            GUILayout.BeginHorizontal();
            GUILayout.Label(Strings.WindowTitle, Skin.Title, GUILayout.ExpandWidth(false));
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(Strings.Close, Skin.Button, GUILayout.ExpandWidth(false))) Defer(Close);
            GUILayout.EndHorizontal();
            GUILayout.Label(Skin.Esc(_header), Skin.Label);
            GUILayout.Label(Strings.RecordNote, Skin.DimLabel);
            GUILayout.Space(8f);

            Layout(r.width - 32f);
            // No horizontal scroll bar: Layout keeps every row inside the view, and the focus code scrolls vertically only.
            _scroll = GUILayout.BeginScrollView(_scroll, false, false, GUIStyle.none, GUI.skin.verticalScrollbar);
            bool repaint = Event.current.type == EventType.Repaint;
            if (Live.Count == 0 && Old.Count == 0) GUILayout.Label(Strings.Empty, Skin.DimLabel);
            foreach (var row in Live) DrawRow(row, repaint);
            if (Old.Count > 0)
            {
                bool focused = _focusKey == ToggleKey;
                string label = _showOld ? "[-] " + Strings.HideOld(Old.Count) : "[+] " + Strings.ShowOld(Old.Count);
                if (GUILayout.Button(label, focused ? Skin.RowFocused : Skin.RowStyle)) Defer(() => { Focus(ToggleKey, 0); Activate(ToggleKey, 0); });
                if (focused && repaint) _focusRect = GUILayoutUtility.GetLastRect();
                GUILayout.Space(2f);
                if (_showOld) foreach (var row in Old) DrawRow(row, repaint);
            }
            GUILayout.EndScrollView();
            if (repaint) _viewRect = GUILayoutUtility.GetLastRect();

            GUILayout.Space(6f);
            GUILayout.Label(Strings.CrimeNote, Skin.Small);
            if (!string.IsNullOrEmpty(_status)) GUILayout.Label(Skin.Esc(_status), Skin.Warn);
            GUILayout.Label(FooterHint(), Skin.Small);
            GUILayout.EndArea();

            if (repaint && _scrollToFocus && _focusRect.height > 0f && _viewRect.height > 0f)
            {
                _scrollToFocus = false;
                if (_focusRect.yMin < _scroll.y) _scroll.y = Mathf.Max(0f, _focusRect.yMin - 8f);
                else if (_focusRect.yMax > _scroll.y + _viewRect.height) _scroll.y = _focusRect.yMax - _viewRect.height + 8f;
            }

            var run = _deferred;
            _deferred = null;
            if (run != null)
            {
                try { run(); }
                catch (Exception e) { Log.Exception("window click", e); }
            }
        }

        private static void Defer(Action a) => _deferred += a;

        private static void DrawRow(Row row, bool repaint)
        {
            bool focused = row.Key == _focusKey;
            GUILayout.BeginHorizontal(focused ? Skin.RowFocused : Skin.RowStyle);
            GUILayout.Label(Skin.Esc(row.Debtor), _cell, GUILayout.Width(_debtorW));
            GUILayout.Label(Fmt.MoneyText(row.Amount, row.Currency), _cell, GUILayout.Width(_amountW));
            bool urgent = !row.Scam && !row.Expired && row.Days <= 0;
            var dueStyle = urgent || (row.Expired && !row.Scam) ? _cellWarn : _compact && row.Scam ? _cellDim : _cell;
            string due = _compact && row.Scam && !row.Expired ? Strings.ScamShort(Due(row)) : Due(row);
            GUILayout.Label(due, dueStyle, GUILayout.Width(_dueW));
            if (!_compact) GUILayout.Label(row.Scam ? Strings.ScamTag : "", _cellDim, GUILayout.Width(TagW));

            string payLabel = Confirm.IsArmed("pay", row.Id) ? Strings.ConfirmButton(Confirm.SecondsLeft("pay"))
                : row.CanPay ? Strings.Pay : row.Expired ? Strings.Expired : Strings.NotEnoughMoney;
            var payStyle = !row.CanPay ? Skin.ButtonOff : focused && _col == 0 ? Skin.ButtonFocused : Skin.Button;
            if (GUILayout.Button(payLabel, payStyle, GUILayout.Width(_payW)))
            {
                string key = row.Key;
                Defer(() => { Focus(key, 0); Activate(key, 0); });
            }
            if (row.CanDismiss)
            {
                string disLabel = Confirm.IsArmed("dismiss", row.Id) ? Strings.ConfirmButton(Confirm.SecondsLeft("dismiss")) : Strings.Dismiss;
                var disStyle = focused && _col == 1 ? Skin.ButtonFocused : Skin.Button;
                if (GUILayout.Button(disLabel, disStyle, GUILayout.Width(_dismissW)))
                {
                    string key = row.Key;
                    Defer(() => { Focus(key, 1); Activate(key, 1); });
                }
            }
            GUILayout.EndHorizontal();
            if (focused && repaint) _focusRect = GUILayoutUtility.GetLastRect();
            GUILayout.Space(2f);
        }

        // Fixed button widths (the widest label each button can show, so the columns line up and never jump), then the
        // text columns from what is left of the row.
        private static void Layout(float areaWidth)
        {
            _payW = Mathf.Max(Width(Skin.Button, Strings.Pay), Width(Skin.Button, Strings.NotEnoughMoney), Width(Skin.Button, Strings.Expired),
                Width(Skin.Button, Strings.ConfirmButton(88)));
            bool anyDismiss = false;
            foreach (var row in Live) anyDismiss |= row.CanDismiss;
            if (_showOld) foreach (var row in Old) anyDismiss |= row.CanDismiss;
            _dismissW = anyDismiss ? Mathf.Max(Width(Skin.Button, Strings.Dismiss), Width(Skin.Button, Strings.ConfirmButton(88))) : 0f;
            float rest = areaWidth - ScrollbarW - Skin.RowStyle.padding.horizontal - _payW - _dismissW - CellGap * (anyDismiss ? 6f : 5f);
            _compact = rest < DebtorMinW + AmountW + DueW + TagW;
            if (!_compact)
            {
                _amountW = AmountW;
                _dueW = DueW;
                _debtorW = rest - AmountW - DueW - TagW;
                return;
            }
            _amountW = CompactAmountW;
            _dueW = CompactDueW;
            _debtorW = Mathf.Max(40f, rest + CellGap - CompactAmountW - CompactDueW);
        }

        private static float Width(GUIStyle style, string text) => style.CalcSize(new GUIContent(text)).x;

        private static void Styles()
        {
            if (_cell != null) return;
            _cell = new GUIStyle(Skin.Label) { wordWrap = false, clipping = TextClipping.Clip, alignment = TextAnchor.MiddleLeft };
            _cellWarn = new GUIStyle(_cell);
            _cellWarn.normal.textColor = Skin.Amber;
            _cellDim = new GUIStyle(_cell);
            _cellDim.normal.textColor = Skin.Dim;
        }

        // What A / X / Enter does on the focused row or button: show or hide the old scam letters, dismiss, or pay.
        private static string FocusVerb()
        {
            if (_focusKey == null) return null;
            if (_focusKey == ToggleKey) return _showOld ? Strings.HideHint : Strings.ShowHint;
            var f = FocusedRow();
            if (f == null) return null;
            return _col == 1 && f.CanDismiss ? Strings.DismissHint : Strings.PayHint;
        }

        // The game's own hint style ("A)SELECT   B)BACK").
        private static string FooterHint()
        {
            try
            {
                bool pad = Game.Controller;
                string back = pad ? "B)" + Strings.BackHint : "ESC)" + Strings.BackHint;
                string verb = FocusVerb();
                if (verb == null) return back;
                return (pad ? "A)" : "X/ENTER)") + verb + "   " + back;
            }
            catch { return ""; }
        }
    }
}

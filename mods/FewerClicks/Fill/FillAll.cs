using System;
using System.Collections.Generic;
using System.Linq;
using OS.Items;
using UnityEngine;

namespace FewerClicks
{
    // S-096 "Fill all" (spec §6): fills every fillable container in the main inventory from the open liquid source, one
    // after another, by doing what the player does by hand: select the container (ItemInfo.Activate, as a click does),
    // press Fill (LiquidStoragePanelUI.PressFill), wait for the panel's own filling to stop. No speed-up: liquid rules,
    // source amount, quality and spoilage mixing, the frame-paced tick, ChangeItem for empties and one theft per
    // witnessed container all stay vanilla. LiquidStorage.QuickMove is never used: it prefers emptying.
    //
    // A polled state machine (one step per frame from Plugin.LateUpdate) rather than a coroutine: Unity's StopCoroutine
    // does not run finally blocks, and every exit here goes through Finish or Abort. The selection is only changed while
    // the panel is not filling, so the mod never triggers vanilla's mid-fill reselect quirk.
    internal static class FillAll
    {
        private enum State { Idle, Select, Press, Wait, Settle }

        // Frames to wait after a fill that took no liquid: the game's own late re-select of a container it turned back
        // into its empty variant (Slot.UpdateInfoDataLate, two frames) and the end-of-frame destroy of the old item icon
        // land before the next selection.
        private const int SettleFrames = 3;

        internal static bool Patched;

        private static State _state = State.Idle;
        private static LiquidStoragePanelUI _panel;
        private static LiquidStorage _source;
        private static ItemData _orig;
        private static List<SlotController> _queue = new List<SlotController>();
        private static int _next, _total, _filled, _settle;
        private static bool _retried, _pressedAgain;
        private static SlotController _sc;
        private static ItemData _cur;
        private static float _crime0, _startLitres, _lastLitres, _stall, _litres;
        private static bool _seen;
        private static readonly Dictionary<string, int> Skipped = new Dictionary<string, int>();
        private static float _nextRefresh;

        internal static bool Running => _state != State.Idle;

        // ---- Start / stop ----

        // Button and hotkey: starts a run, or stops the one in progress.
        internal static void Toggle()
        {
            try
            {
                if (Running) Cancel("stopped");
                else Start();
            }
            catch (Exception e)
            {
                Log.Exception("fill all", e);
                SafeReset();
            }
        }

        private static void Start()
        {
            var panel = FillAllButton.Panel;
            if (!Cfg.FillAll.Enabled.Value || panel == null || !panel.gameObject.activeInHierarchy || LiquidStorage.currentStorage == null) { Skip("no liquid window open"); return; }
            var output = LiquidStorage.CurrentOutputStorage;
            var input = LiquidStorage.CurrentInputStorage;
            if (output == null || output.fillingItem || (input != null && input.unfillingItem)) { Skip("already filling or emptying"); return; }
            if (Game.TradeActive || Game.ConsoleOpen || Game.WriteLock || Game.DragActive()) { Skip("trade, console, typing or drag"); return; }
            if (!(bool)ItemInfo.instance) { Skip("no item panel"); return; }

            var queue = Candidates();
            if (queue.Count == 0) { Skip("nothing to fill"); return; }

            _panel = panel;
            _source = LiquidStorage.currentStorage;
            _orig = ItemInfo.instance.ItemData;
            _queue = queue;
            _next = 0;
            _total = queue.Count;
            _filled = 0;
            _litres = 0f;
            _seen = false;
            _retried = false;
            _pressedAgain = false;
            Skipped.Clear();
            _state = State.Select;
            Log.Verbose($"Fill all: start at \"{_source.title}\" (owner {OwnerId(output)}), {_total} containers.");
            FillAllButton.Refresh();
        }

        // Button or hotkey while running, scene change, level change.
        internal static void Cancel(string reason)
        {
            if (!Running) return;
            try
            {
                if (_state == State.Wait && PanelActive() && LiquidStorage.currentStorage != null && LiquidStorage.CurrentOutputStorage.fillingItem)
                    _panel.StopFilling();   // the vanilla stop: commits any accrued theft exactly as a manual stop does
            }
            catch (Exception e) { Log.Exception("fill all stop", e); }
            Finish(reason);
        }

        // ---- Per-frame ----

        internal static void LateUpdate()
        {
            if (!Patched) return;
            var panel = FillAllButton.Panel;
            bool open = panel != null && panel.gameObject.activeInHierarchy;

            if (open && Cfg.FillAll.Enabled.Value && Keys.Down(Cfg.FillAll.Hotkey)) Toggle();

            if (Running)
            {
                try { Step(); }
                catch (Exception e)
                {
                    Log.Exception("fill all step", e);
                    Abort("error");
                }
            }

            if (open && Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                FillAllButton.Refresh();
            }
        }

        private static void Step()
        {
            switch (_state)
            {
                case State.Select: SelectNext(); break;
                case State.Press: Press(); break;
                case State.Wait: Wait(); break;
                case State.Settle: Settle(); break;
            }
        }

        private static void SelectNext()
        {
            if (!PanelActive() || LiquidStorage.currentStorage != _source) { Abort("window closed"); return; }
            if (_next >= _queue.Count) { Finish("done"); return; }
            var sc = _queue[_next];
            var output = LiquidStorage.CurrentOutputStorage;
            var r = sc?.itemStack != null ? output.ItemCanBeFilled(ref sc.itemStack) : LiquidStorage.Result.None;
            switch (r)
            {
                case LiquidStorage.Result.OK:
                    break;
                case LiquidStorage.Result.Empty:
                    Finish("source empty");
                    return;
                case LiquidStorage.Result.Disabled:
                    Finish("not possible");
                    return;
                case LiquidStorage.Result.Filling:
                    if (!_retried) { _retried = true; return; }   // one more frame, then skip
                    Next("busy");
                    return;
                case LiquidStorage.Result.DifferentLiquid:
                    Next("different liquid");
                    return;
                case LiquidStorage.Result.FullItem:
                    Next("already full");
                    return;
                default:
                    Next("not a container");
                    return;
            }
            var data = sc.slot != null ? sc.slot.GetItemItemData() : null;
            if (data == null && sc.slot != null)
            {
                // The icon was destroyed when the game swapped the item (SlotController.ChangeItem) and not redrawn yet:
                // redraw the slot, as the item panel does on every selection (ItemInfoPanel.UpdateInfo).
                sc.UpdateNow();
                data = sc.slot.GetItemItemData();
            }
            if (data == null) { Next("no item"); return; }
            _sc = sc;
            _cur = data;
            ItemInfo.instance.Activate(data);   // what clicking the item does
            _state = State.Press;
        }

        private static void Press()
        {
            if (!PanelActive() || LiquidStorage.currentStorage != _source) { Abort("window closed"); return; }
            if (SelectionChanged()) { Abort("selection changed"); return; }
            _crime0 = Vanilla.CrimeCounter != null ? Vanilla.CrimeCounter(_panel) : 0f;
            _startLitres = _lastLitres = Litres(_sc);
            _stall = 0f;
            _panel.PressFill();   // the Fill button
            if (!LiquidStorage.CurrentOutputStorage.fillingItem)
            {
                // FillItem refused without a message (the liquid's own data does not mix with this container's).
                Next("won't mix");
                return;
            }
            _state = State.Wait;
        }

        private static void Wait()
        {
            if (!PanelActive() || LiquidStorage.currentStorage != _source) { Abort("window closed"); return; }
            if (SelectionChanged()) { Abort("selection changed"); return; }

            // Before the done check: the panel commits the theft (and zeroes the counter) in the frame after the last
            // witnessed litre, and this runs after every Update, so the rise is always seen first.
            if (Cfg.FillAll.StopWhenSeen.Value && Vanilla.CrimeCounter != null && Vanilla.CrimeCounter(_panel) > _crime0) _seen = true;

            float now = Litres(_sc);
            if (!LiquidStorage.CurrentOutputStorage.fillingItem)
            {
                if (now - _startLitres <= 0.0001f)
                {
                    // The panel stopped before any liquid went in. Seen in game with an Empty Wine Bottle at a sink: the
                    // container was turned back into its empty variant (Slot.CheckItemMetas on a 0 L liquid container)
                    // and the game re-selected it two frames later. Not a fill. Wait for the game to settle, press
                    // Fill once more as a player would, then skip it ("wouldn't fill").
                    if (_seen) { Finish("seen"); return; }
                    if (!_pressedAgain)
                    {
                        _pressedAgain = true;
                        Log.Verbose($"Fill all: {ItemName(_sc)} took no liquid; pressing Fill once more.");
                    }
                    else
                    {
                        Count("wouldn't fill");
                        Log.Verbose($"Fill all: skipped {ItemName(_sc)} (wouldn't fill).");
                        _next++;
                        _pressedAgain = false;
                    }
                    _retried = false;
                    _settle = SettleFrames;
                    _state = State.Settle;
                    return;
                }
                _filled++;
                _litres += now - _startLitres;
                Log.Verbose($"Fill all: {ItemName(_sc)} +{now - _startLitres:0.##} L.");
                if (_seen) { Finish("seen"); return; }
                _next++;
                _retried = false;
                _pressedAgain = false;
                _state = State.Select;
                return;
            }

            // Watchdog: 5 s of game time without a litre change while filling. Paused time does not count.
            if (Mathf.Abs(now - _lastLitres) > 0.0001f)
            {
                _lastLitres = now;
                _stall = 0f;
            }
            else
            {
                _stall += Time.deltaTime;
                if (_stall >= 5f)
                {
                    try { _panel.StopFilling(); }
                    catch (Exception e) { Log.Exception("fill all stop", e); }
                    Abort("stalled");
                }
            }
        }

        // The player picked another item. A new icon in the same slot is the game re-selecting this container after it
        // swapped the item (SlotController.ChangeItem -> Slot.UpdateInfoDataLate), not the player: follow it.
        private static bool SelectionChanged()
        {
            var sel = ItemInfo.instance != null ? ItemInfo.instance.ItemData : null;
            if (sel == _cur) return false;
            if (sel != null && _sc != null && ReferenceEquals(sel.slotController, _sc))
            {
                _cur = sel;
                return false;
            }
            return true;
        }

        // Counts frames, not game time: they only let the game's own delayed updates run first.
        private static void Settle()
        {
            if (!PanelActive() || LiquidStorage.currentStorage != _source) { Abort("window closed"); return; }
            if (--_settle <= 0) _state = State.Select;
        }

        private static void Next(string skipReason)
        {
            Count(skipReason);
            Log.Verbose($"Fill all: skipped {ItemName(_queue[_next])} ({skipReason}).");
            _next++;
            _retried = false;
            _pressedAgain = false;
            _state = State.Select;
        }

        // ---- Endings ----

        // The run ends with the panel idle: restore what was selected before, then report.
        private static void Finish(string reason)
        {
            try
            {
                if (PanelActive() && (bool)ItemInfo.instance && !LiquidStorage.CurrentOutputStorage.fillingItem)
                {
                    if (_orig != null && _orig.amount > 0) ItemInfo.instance.Activate(_orig);
                    else ItemInfo.instance.Deactivate();
                }
            }
            catch (Exception e) { Log.Exception("fill all restore", e); }
            End(reason);
        }

        // The player took over or the panel is gone: leave the selection alone (vanilla OnDisable already cleared the
        // filling flags when the panel closed).
        private static void Abort(string reason) => End(reason);

        private static void End(string reason)
        {
            int skipped = Skipped.Values.Sum();
            Log.Verbose($"Fill all: end ({reason}) at \"{(_source != null ? _source.title : "?")}\": filled {_filled} of {_total} " +
                        $"({_litres:0.##} L), skipped {skipped}.");
            try { Feedback(reason, skipped); }
            catch (Exception e) { Log.Exception("fill all feedback", e); }
            SafeReset();
            try { FillAllButton.Refresh(); }
            catch (Exception e) { Log.Exception("fill all button", e); }
        }

        private static void SafeReset()
        {
            _state = State.Idle;
            _panel = null;
            _source = null;
            _orig = null;
            _sc = null;
            _cur = null;
            _queue = new List<SlotController>();
            _seen = false;
            _pressedAgain = false;
            _settle = 0;
        }

        private static void Feedback(string reason, int skipped)
        {
            switch (reason)
            {
                case "scene":
                case "level change":
                    return;
                case "source empty":
                    Game.Notify($"Filled {_filled} of {_total}: the source ran dry");
                    return;
                case "seen":
                    Game.Notify("Stopped: someone saw you");
                    return;
                case "done":
                    if (skipped > 0)
                    {
                        var top = Skipped.OrderByDescending(p => p.Value).First().Key;
                        Game.Notify($"Filled {_filled}, skipped {skipped} ({top})");
                    }
                    else if (Cfg.FillAll.SummaryNotification.Value)
                        Game.Notify($"Filled {_filled} containers ({_litres:0.#} L)");
                    return;
                default:
                    Game.Notify("Stopped");
                    return;
            }
        }

        // ---- Candidates ----

        internal static bool AnyCandidate()
        {
            try
            {
                var inv = Inventory.instance;
                if (inv == null || inv.Slots == null || LiquidStorage.currentStorage == null) return false;
                var output = LiquidStorage.CurrentOutputStorage;
                foreach (var sc in inv.Slots)
                    if (Group(sc) >= 0 && output.ItemCanBeFilled(ref sc.itemStack) == LiquidStorage.Result.OK) return true;
                return false;
            }
            catch (Exception e)
            {
                Log.Exception("fill all candidates", e);
                return false;
            }
        }

        // Main inventory only: the backpack window cannot be open with a liquid window (BackpackStorage.cs:121-125), and
        // hands and equipment are never filled. Order: partly filled with the source's liquid, then liquid containers
        // at 0 L, then empty containers; slot order within each group.
        private static List<SlotController> Candidates()
        {
            var result = new List<SlotController>();
            var inv = Inventory.instance;
            if (inv == null || inv.Slots == null) return result;
            var output = LiquidStorage.CurrentOutputStorage;
            int sourceLiquid = output != null && output.Liquid != null ? output.Liquid.ID : -1;
            var groups = new List<(int group, int index, SlotController sc)>();
            for (int i = 0; i < inv.Slots.Length; i++)
            {
                var sc = inv.Slots[i];
                int g = Group(sc, sourceLiquid);
                if (g < 0 || output.ItemCanBeFilled(ref sc.itemStack) != LiquidStorage.Result.OK) continue;
                groups.Add((g, i, sc));
            }
            foreach (var x in groups.OrderBy(x => x.group).ThenBy(x => x.index)) result.Add(x.sc);
            return result;
        }

        // -1 when not a candidate.
        private static int Group(SlotController sc, int sourceLiquid = -1)
        {
            var st = sc?.itemStack;
            if (st == null || st.itemId == -1 || st.itemAmount <= 0) return -1;
            if (sc.slot == null || sc.slot.GetItemItemData() == null) return -1;
            var liquid = st.GetMetaOfType<ItemLiquidData>();
            if (liquid != null)
            {
                float l = liquid.GetLiquidAmount(st.itemAmount);
                if (l > 0f && liquid.liquidItem != null && liquid.liquidItem.ID == sourceLiquid) return 0;
                return 1;
            }
            if (st.GetMetaOfType<ItemEmptyLiquidContainerData>() != null) return Cfg.FillAll.IncludeEmptyContainers.Value ? 2 : -1;
            return -1;
        }

        // ---- Helpers ----

        private static bool PanelActive() => _panel != null && _panel.gameObject.activeInHierarchy;

        private static float Litres(SlotController sc)
        {
            var st = sc?.itemStack;
            var liquid = st?.GetMetaOfType<ItemLiquidData>();   // re-read: ChangeItem replaces it
            return liquid != null ? liquid.GetLiquidAmount(st.itemAmount) : 0f;
        }

        private static string ItemName(SlotController sc)
        {
            try { return sc?.itemStack?.itemReference?.Item != null ? $"{sc.itemStack.itemReference.Item.Title} ({sc.itemStack.itemId})" : "?"; }
            catch (Exception) { return "?"; }
        }

        private static int OwnerId(LiquidStorage s)
        {
            try { return s.StorageOwner != null ? s.StorageOwner.ID : -1; }
            catch (Exception) { return -1; }
        }

        private static void Count(string key)
        {
            Skipped.TryGetValue(key, out int n);
            Skipped[key] = n + 1;
        }

        private static void Skip(string why) => Log.Verbose("Fill all: not started (" + why + ").");
    }
}

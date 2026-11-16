using System;

namespace TidyInventory
{
    // Optional single-stack disposal (spec §4.4, off by default): with a bin open, hover a stack in it and press the key
    // twice. Mouse only; controller players use Empty. Moving to another stack disarms. Independent of the Empty button
    // switch (Bins.Enabled); uses the [Bins] bin and protection settings. A refusal always says why (not a one-time tip).
    internal static class HoverDispose
    {
        private static int _armedId = -1;

        internal static void Tick()
        {
            if (!Cfg.BinDispose.Enabled.Value) return;
            var armed = Confirm.Target("dispose") as SlotController;
            var d = ItemData.currentHoverItemData;
            var sc = d != null && !d.itemDisabled ? d.slotController : null;
            if (armed != null && armed != sc) Confirm.Reset("dispose");

            if (!Keys.Down(Cfg.BinDispose.Key)) return;
            var bin = Storage.active;
            if (sc == null || bin == null || bin.Slots == null || sc.slot == null || sc.slot.playerSlot) return;
            if (Array.IndexOf(bin.Slots, sc) < 0 || !Bins.IsBin(bin) || !Game.StorageVisible) return;
            if (Game.TradeActive || Game.DragActive || Game.SliderOpen || !Game.NonEmpty(sc)) return;

            if (Bins.Protected(sc, out var why))
            {
                Confirm.Reset("dispose");
                Hints.Notify(Strings.DisposeProtected(why, Cfg.Bins.ProtectValueAbove.Value));
                Log.Verbose($"Bins: kept a hovered stack ({why}).");
                return;
            }
            int id = sc.itemStack.itemId;
            if (!Confirm.IsArmed("dispose", sc) || _armedId != id)
            {
                Confirm.Arm("dispose", sc, Cfg.Bins.ConfirmSeconds.Value, () => _armedId = -1);
                _armedId = id;
                Hints.Notify(Strings.DisposeArmed(Keys.Label(Cfg.BinDispose.Key)));
                return;
            }
            Confirm.Reset("dispose");
            int n = sc.itemStack.itemAmount;
            string title = sc.itemStack.itemReference?.Item?.Title ?? id.ToString();
            sc.RemoveItem();
            Game.ClearHoverFor(x => x == sc);
            Hints.Notify(Strings.ThrewAway(n));
            Log.Info($"Bins: threw away {n} × {title} from \"{bin.title}\".");
        }
    }
}

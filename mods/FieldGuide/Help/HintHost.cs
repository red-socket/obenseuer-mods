using System.Collections.Generic;
using UnityEngine;

namespace FieldGuide
{
    // Our entries in the game's hint panel (spec §5.2). Every entry carries its own hidden override, so the Hints key
    // (K / View) flips only our entry and never the game's global hints switch (SRC/HintOverlay.cs:228-244, D4). With
    // Mode = UntilHidden, a kind the player hides once goes into [Popups] Hidden (D2, D3) and is added collapsed from then
    // on: still one key press away. Entries live only in memory; their owners die with the scene and HintOverlay prunes
    // them (:141), so OnScene just forgets them.
    //
    // The backpack round trip (D25). BackpackStorage.OpenBackpack exits the open station and CloseBackpack enters it
    // again (SRC/BackpackStorage.cs:100-104, :164). On a pad, View is both the Hints key (KeyBindings.cs:338) and, in the
    // inventory tab with a backpack worn, the backpack button (InventoryNavigationHandler.cs:1247); HintOverlay
    // (execution order 0) reads the press before InventoryNavigationHandler (10500), so one press flips our entry and
    // then opens the backpack. So a station exit and a toggle are judged one frame later, in Tick:
    // - an exit after which the backpack is open is a round trip: the entry comes back on re-entry as it was before
    //   the press that caused it (the toggle is not recorded as the player hiding the guide);
    // - any other exit is the player leaving; under UntilHidden, a station guide that was showing when a pad player
    //   wearing a backpack leaves counts as seen, since View can't hide it there;
    // - a toggle that hid our entry with no round trip in the same frame is the player hiding it (D2).
    internal static class HintHost
    {
        private sealed class Track
        {
            internal HelpKind Kind;
            internal bool Visible;   // what the panel shows (Tick); false while another hint covers ours
            internal bool Hidden;    // our override: true = collapsed
            internal bool Station;   // a CraftingBase entry, not a walk-in sauna
        }

        // An entry closed by CraftingBase.Exit, judged in Tick (or taken back by Open on re-entry).
        private sealed class Left
        {
            internal HelpKind Kind;
            internal bool Hidden;
            internal int Frame;
            internal bool RoundTrip;
        }

        // A Hints toggle that changed one of our entries.
        private struct Flip
        {
            internal Object Owner;
            internal HelpKind Kind;
            internal bool HiddenBefore, HiddenAfter;
            internal int Frame;
        }

        private static readonly Dictionary<Object, Track> Tracked = new Dictionary<Object, Track>();
        private static readonly Dictionary<Object, Left> Exited = new Dictionary<Object, Left>();
        private static readonly List<Flip> Flips = new List<Flip>();
        private static readonly List<Object> Dead = new List<Object>();

        internal static bool IsTracked(Object owner) => owner != null && Tracked.ContainsKey(owner);

        // On a controller with a backpack worn, View at a station opens the backpack (see above).
        internal static bool PadWithBackpack()
        {
            var im = InputManager.instance;
            var bp = BackpackStorage.instance;
            return im != null && im.inputType == InputManager.InputType.Controller && bp != null && bp.Equipped;
        }

        private static bool BackpackOpen()
        {
            var bp = BackpackStorage.instance;
            return bp != null && bp.BackpackIsOpen();
        }

        internal static void Open(Object owner, HelpKind kind, HintContent content, bool station)
        {
            if (owner == null || content == null || Tracked.ContainsKey(owner)) return; // idempotent (re-entry, several colliders)
            bool collapsed;
            string why;
            if (Exited.TryGetValue(owner, out var left))
            {
                // Back from the backpack, or the Exit inside a re-Enter (SRC/CraftingBase.cs:1771-1774): as it was.
                Exited.Remove(owner);
                collapsed = left.Hidden;
                why = "restored";
            }
            else
            {
                var mode = Cfg.Hot.Mode;
                collapsed = mode == PopupMode.Collapsed
                    || (mode == PopupMode.UntilHidden && Cfg.Hot.Hidden.Contains(kind))
                    || !HintOverlay.HintsEnabled;
                why = collapsed ? "added collapsed" : "showing";
            }
            HintOverlay.Show(owner, content, 0, collapsed);
            // Never null: with an override, the key toggles only this entry. SetHiddenOverride fires no HintsToggled.
            HintOverlay.SetHiddenOverride(owner, collapsed);
            Tracked[owner] = new Track { Kind = kind, Visible = !collapsed, Hidden = collapsed, Station = station };
            if (Log.VerboseOn) Log.Verbose($"Help: {why} {kind} at {Where(owner)}.");
        }

        internal static void Close(Object owner)
        {
            if (owner == null || !Tracked.Remove(owner)) return;
            HintOverlay.Hide(owner);
            if (Log.VerboseOn) Log.Verbose($"Help: hid the guide at {Where(owner)}.");
        }

        // H2: a station closed. Judged next frame (round trip or leaving).
        internal static void OnStationExit(Object owner)
        {
            if (owner == null || !Tracked.TryGetValue(owner, out var t)) return;
            Close(owner);
            if (t.Station) Exited[owner] = new Left { Kind = t.Kind, Hidden = t.Hidden, Frame = Time.frameCount };
        }

        // Moving between two walk-in sauna zones (the Bazaar Sauna has several): one sauna entry at a time.
        internal static void CloseKind(HelpKind kind, Object except)
        {
            Dead.Clear();
            foreach (var kv in Tracked)
                if (kv.Value.Kind == kind && !ReferenceEquals(kv.Key, except)) Dead.Add(kv.Key);
            foreach (var o in Dead) Close(o);
            Dead.Clear();
        }

        // HintOverlay.HintsToggled: the player pressed the Hints key on the current entry. IsShowing is true only for the
        // current entry, so `Visible` is refreshed for every tracked entry; an entry whose visibility changed had its
        // override flipped. Whether the press hid the guide for good is decided in Tick.
        internal static void OnToggled()
        {
            Dead.Clear();
            foreach (var kv in Tracked)
            {
                if (kv.Key == null) { Dead.Add(kv.Key); continue; }
                bool now = HintOverlay.IsShowing(kv.Key);
                var t = kv.Value;
                if (t.Visible != now)
                {
                    Flips.Add(new Flip { Owner = kv.Key, Kind = t.Kind, HiddenBefore = t.Hidden, HiddenAfter = !now, Frame = Time.frameCount });
                    t.Hidden = !now;
                }
                t.Visible = now;
            }
            foreach (var o in Dead) Tracked.Remove(o);
            Dead.Clear();
        }

        // Plugin.Update. Keeps `Visible` equal to what the panel shows, so an entry that another hint (a card game's
        // rules) covers is not counted as hidden by the player when that other hint is toggled; then judges last
        // frame's toggles and station exits.
        internal static void Tick()
        {
            foreach (var kv in Tracked)
                if (kv.Key != null) kv.Value.Visible = HintOverlay.IsShowing(kv.Key);
            if (Flips.Count == 0 && Exited.Count == 0) return;

            int frame = Time.frameCount;
            bool packOpen = BackpackOpen();
            for (int i = Flips.Count - 1; i >= 0; i--)
            {
                var f = Flips[i];
                if (f.Frame >= frame) continue;
                Flips.RemoveAt(i);
                if (f.Owner != null && Exited.TryGetValue(f.Owner, out var left) && left.Frame == f.Frame && packOpen)
                {
                    // The same press opened the backpack: not the player's hide (D25). Back as it was before the press.
                    left.Hidden = f.HiddenBefore;
                    Log.Verbose($"Help: View opened the backpack at {f.Kind}; not counted as hiding it.");
                    continue;
                }
                if (!f.HiddenBefore && f.HiddenAfter)
                {
                    Log.Verbose($"Help: player hid {f.Kind}.");
                    if (Cfg.Hot.Mode == PopupMode.UntilHidden) Cfg.AddHidden(f.Kind);
                }
                else if (f.HiddenBefore && !f.HiddenAfter) Log.Verbose($"Help: player showed {f.Kind}.");
            }

            if (Exited.Count == 0) return;
            Dead.Clear();
            foreach (var kv in Exited)
            {
                var l = kv.Value;
                if (l.Frame >= frame) continue;
                if (packOpen)
                {
                    l.RoundTrip = true; // kept until the station is entered again
                    continue;
                }
                // The player left (or closed the backpack without coming back).
                Dead.Add(kv.Key);
                if (!l.Hidden && Cfg.Hot.Mode == PopupMode.UntilHidden && PadWithBackpack())
                {
                    Log.Verbose($"Help: {l.Kind} was read on a pad with a backpack on; counted as seen.");
                    Cfg.AddHidden(l.Kind);
                }
            }
            foreach (var o in Dead) Exited.Remove(o);
            Dead.Clear();
        }

        internal static bool Busy => Tracked.Count > 0 || Exited.Count > 0 || Flips.Count > 0;

        internal static void OnScene()
        {
            Tracked.Clear();
            Exited.Clear();
            Flips.Clear();
        }

        private static string Where(Object owner)
        {
            try
            {
                var c = owner as Component;
                return c != null ? c.gameObject.scene.name + ": " + Classify.Path(c.transform) : owner.name;
            }
            catch { return "?"; }
        }
    }
}

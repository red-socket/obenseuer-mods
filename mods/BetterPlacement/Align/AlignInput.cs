using System;
using UnityEngine;

namespace BetterPlacement
{
    // Whether alignment is on this frame (spec §5.2). Hold: while the key or pad button is held. Toggle: a clean tap of
    // the key or a press of the pad button flips it. Always: on unless the key or button is held. Keys only count while
    // the building menu is open, but the tap and D-pad edges are followed every frame so a press made before the menu
    // opened never fires later. Toggle resets when the menu closes, on every scene load and when the window loses focus.
    // Tick runs once per frame, from Plugin.Update or the align postfix, whichever comes first.
    internal static class AlignInput
    {
        internal static bool Active { get; private set; }

        private static bool _toggled;
        private static int _frame = -1;

        internal static void Tick()
        {
            if (_frame == Time.frameCount) return;
            _frame = Time.frameCount;
            try
            {
                var key = Cfg.Align.Key;
                var pad = Cfg.Align.PadButton.Value;
                bool tap = Keys.TapUp(key);
                bool padDown = Keys.PadDown(pad);
                bool on = false;
                if (AlignPatches.Patched && Cfg.Align.Enabled.Value && Game.BuildOpen)
                {
                    bool held = Keys.Held(key) || Keys.PadHeld(pad);
                    switch (Cfg.Align.Mode.Value)
                    {
                        case AlignMode.Toggle:
                            if (tap || padDown)
                            {
                                _toggled = !_toggled;
                                Hint.Refresh();
                            }
                            on = _toggled;
                            break;
                        case AlignMode.Always:
                            on = !held;
                            break;
                        default:
                            on = held;
                            break;
                    }
                }
                if (on != Active)
                {
                    Active = on;
                    if (!on) Solver.EndEngagement();
                    Log.Verbose("align input", on, o => o ? "Align: on." : "Align: off.");
                }
            }
            catch (Exception e)
            {
                Log.Exception("align input", e);
                Active = false;
                Solver.EndEngagement();
            }
        }

        internal static void ResetToggle()
        {
            if (!_toggled) return;
            _toggled = false;
            Hint.Refresh();
        }
    }
}

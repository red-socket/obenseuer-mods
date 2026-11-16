using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FewerClicks
{
    // S-057 Quick Eat & Drink, Auto Eat and the slot keys (spec §2 P4). All three are polled inside
    // GameController.InteractControls, where vanilla reads the 1-4 keys: every early return of GameController.Update
    // (shop and crafting, typing, keysDisabled for sleep, wait, work and cutscenes, pause, dialogue, building, the
    // doctor and tenement panels) has already passed, and PlayGate adds the rest.
    internal static class QuickEat
    {
        internal static bool Patched;

        private const float Debounce = 0.5f;
        private const int BackOffMinutes = 60;

        private static float _lastManual = -10f;
        private static bool _minuteFlag;
        private static int _backOff;
        private static bool _backOffNotified;
        private static bool _subscribed;

        internal static void Subscribe()
        {
            if (_subscribed) return;
            TimeOfDayAzure.MinutePassed += OnMinutePassed;
            _subscribed = true;
        }

        internal static void Unsubscribe()
        {
            if (!_subscribed) return;
            TimeOfDayAzure.MinutePassed -= OnMinutePassed;
            _subscribed = false;
        }

        // Only flags; the work happens in Tick, inside the gate. Minutes that pass while asleep or fast-forwarding leave
        // one flag, so Auto Eat runs once after waking, never during.
        private static void OnMinutePassed(int minute)
        {
            _minuteFlag = true;
            if (_backOff > 0) _backOff--;
        }

        internal static void Tick()
        {
            bool manual = Cfg.QuickEat.Enabled.Value && (Keys.Down(Cfg.QuickEat.Hotkey) || Keys.PadEdge(Cfg.QuickEat.PadButton.Value));
            bool auto = Cfg.AutoEat.Enabled.Value && _minuteFlag;
            bool slots = Cfg.SlotKeys.Enabled.Value && SlotKeys.AnyDown();
            if (!manual && !auto && !slots) return;

            string blocked = PlayGate.Blocked();
            if (blocked != null)
            {
                if (manual || slots) Log.Verbose($"Quick Eat: key ignored ({blocked}).");
                return;   // the minute flag stays set: Auto Eat runs once play resumes
            }

            if (manual && Time.unscaledTime - _lastManual >= Debounce)
            {
                _lastManual = Time.unscaledTime;
                Run(manual: true);
            }
            if (auto)
            {
                _minuteFlag = false;
                var h = PlayerStats.instance.hungerAndThirst;
                float t = Cfg.AutoEat.Threshold.Value;
                if (_backOff <= 0 && h != null && (h.Hunger >= t || h.Thirst >= t)) Run(manual: false);
            }
            if (slots) SlotKeys.Poll();
        }

        private static void Run(bool manual)
        {
            float threshold = manual ? Cfg.QuickEat.Threshold.Value : Cfg.AutoEat.Threshold.Value;
            ConsumeCall.Result r;
            try { r = ConsumeCall.Run(threshold, Cfg.QuickEat.SafeFoodOnly.Value, Mathf.Clamp(Cfg.QuickEat.MaxItemsPerNeed.Value, 1, 5)); }
            catch (Exception e)
            {
                Log.Exception(manual ? "quick eat" : "auto eat", e);
                r = new ConsumeCall.Result { Outcome = ConsumeCall.Outcome.NotReady };
            }
            string what = r.Used != null ? Join(r.Used) : "";
            if (r.Outcome == ConsumeCall.Outcome.Ate && what.Length == 0) what = "Ate something";
            Log.Verbose($"{(manual ? "Quick Eat" : "Auto Eat")}: {r.Outcome}{(what.Length > 0 ? " (" + what + ")" : "")}.");

            if (manual)
            {
                if (r.Outcome != ConsumeCall.Outcome.Ate) Game.Warning();
                if (!Cfg.QuickEat.Notify.Value) return;
                switch (r.Outcome)
                {
                    case ConsumeCall.Outcome.Ate: Game.Notify(what); break;
                    case ConsumeCall.Outcome.NotHungry: Game.Notify("Not hungry or thirsty"); break;
                    case ConsumeCall.Outcome.NothingSafe: Game.Notify("Nothing safe to eat or drink"); break;
                    default:
                        Game.Notify(ItemInfo.instance == null ? "Open your inventory once, then try again" : "Can't eat right now");
                        break;
                }
                return;
            }

            if (r.Outcome == ConsumeCall.Outcome.Ate)
            {
                _backOffNotified = false;
                if (Cfg.AutoEat.Notify.Value) Game.Notify(what);
            }
            else if (r.Outcome == ConsumeCall.Outcome.NothingSafe || r.Outcome == ConsumeCall.Outcome.NotReady)
            {
                _backOff = BackOffMinutes;
                if (Cfg.AutoEat.Notify.Value && !_backOffNotified && r.Outcome == ConsumeCall.Outcome.NothingSafe)
                    Game.Notify("Hungry, but nothing safe to eat");
                _backOffNotified = true;
            }
        }

        // "Ate Bread, drank Water", at most 3 names, then "and N more".
        private static string Join(List<string> used)
        {
            if (used.Count == 0) return "";
            var shown = used.Count > 3 ? used.GetRange(0, 3) : used;
            string s = string.Join(", ", shown.ToArray());
            if (used.Count > 3) s += $" and {used.Count - 3} more";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }
    }

    // P4: GameController.InteractControls() (private, SRC/GameController.cs:252). Void prefix: vanilla always runs.
    // It is also called in building RemoveOnly mode (:120), which PlayGate blocks.
    [HarmonyPatch]
    internal static class InteractControlsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.InteractControls;

        private static void Prefix()
        {
            if (!QuickEat.Patched) return;
            try { QuickEat.Tick(); }
            catch (Exception e) { Log.Exception("quick keys", e); }
        }
    }
}

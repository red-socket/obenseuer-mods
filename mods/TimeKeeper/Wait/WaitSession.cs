using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace TimeKeeper
{
    // U-007 / S-079: pass time awake on the game's own sleep clock (spec §6). Idle → Picking (our picker is the vanilla
    // clock, opened with the parameters of a pass-out) → Running (a vanilla WaitingController.Wait of Type.Waiting) →
    // Idle. Everything here lives in memory only: nothing is saved, and the game state we touch (hasAlarmClock, the
    // slider maximum, the button text) is restored or reset by the next scene load.
    //
    // The mod never calls SetSpeedFactor, Sleep, PassOut or WaitingController.StartSleep, never touches
    // onWakeupOverride (logic_wait, Prison.PassTime), stats or needs, and never writes tiredness.lastAlarmTime. The
    // one game field it writes outside the picker is alarmEnabled, switched off when a pass-out takes over a wait
    // (TickRunning). P7 (CostPatch) only changes how the stats are fed while the wait runs, not what they do.
    internal static class WaitSession
    {
        internal enum Phase { Idle, Picking, Running }

        private enum StopReason { None, Cancelled, Tired, Drunk, Hurt, Stalled }

        internal const string WaitLabel = "Waiting";
        private const string ButtonLabel = "Wait";
        private const float StallSeconds = 15f;

        internal static bool Patched;
        internal static Phase State = Phase.Idle;

        // Picker phase: what we changed on the game's clock.
        private static bool _prevHasAlarm;
        private static Slider _slider;
        private static float _prevSliderMax = -1f, _ourSliderMax = -1f;
        private static TMP_Text _buttonText;
        private static string _buttonOriginal;

        // Running phase.
        private static int _lastSeconds;            // the last duration started, for this game session only
        private static int _target, _lastSeen, _lastSeenLate;
        private static float _startedUnscaled, _stallSince;
        private static int _startedFrame = -1;
        private static StopReason _stopReason;
        private static bool _wakeHandled;

        // ---- Per frame ----

        // Plugin.Update. The D-pad edge is read every frame in Idle and Picking so its arm state follows the stick.
        internal static void Tick()
        {
            switch (State)
            {
                case Phase.Idle: TickIdle(); break;
                case Phase.Picking: TickPicking(); break;
                case Phase.Running: TickRunning(); break;
            }
        }

        private static void TickIdle()
        {
            bool key = Keys.Down(Cfg.Wait.Hotkey);
            bool pad = Keys.PadEdge(Cfg.Wait.PadButton.Value, leaveToList: true);
            if (!Cfg.Wait.Enabled.Value || (!key && !pad)) return;
            Open(fromPad: !key);
        }

        private static void TickPicking()
        {
            bool pad = Keys.PadEdge(Cfg.Wait.PadButton.Value, leaveToList: false);
            var wc = WaitingController.instance;
            var ui = WaitingUI.instance;
            if (wc == null || ui == null) { End("scene", touchGame: false); return; }
            if (wc.waitingType != WaitingController.Type.None)
            {
                Log.Info($"Wait: a {wc.waitingType} took over the waiting clock.");
                End("taken over by " + wc.waitingType);
                return;
            }
            if (!ui.IsActive()) { End("closed"); return; }

            // Close our picker before the game's own pass-out reaches it (PassOut → Sleep with the stored parameters,
            // SRC/WaitingController.cs:219); the pass-out then runs on its normal path.
            if (PlayerStats.instance != null && (WaitGate.TooTired() || WaitGate.TooDrunkNow()))
            {
                string note = WaitGate.TooTired()
                    ? WaitGate.RefuseText(PlayerStatsTiredness.Reason.Tiredness)
                    : WaitGate.TooDrunk;
                ui.CloseTimeSetUI();
                Refuse(note);
                End("needs");
                return;
            }

            bool key = Keys.Down(Cfg.Wait.Hotkey);
            if ((key || pad) && !Game.Paused) ui.StartSleep();   // P3 → Start
        }

        private static void TickRunning()
        {
            var wc = WaitingController.instance;
            if (wc == null) { End("scene", touchGame: false); return; }
            if (wc.waitingType != WaitingController.Type.Waiting)
            {
                // Backstop: OnVanillaWakeup normally ends the session first (arrest included). A pass-out that started
                // during our wait (the stops at tiredness 90 and alcohol 95 normally come first; a very slow frame can
                // jump past them) goes through PassOut → StartSleep without Wakeup, so it keeps our wait's alarm
                // (alarmEnabled and the countdown, SRC/WaitingController.cs:219-223) and would end when the wait would
                // have. Outside a wait a pass-out has no alarm (it lasts until rested and sober, SRC/PlayerStatsTiredness
                // .cs:68-75, with its pass-out events and arrest roll), so the alarm we left behind is switched off.
                if (!_wakeHandled)
                {
                    if (wc.waitingType == WaitingController.Type.Passout && wc.alarmEnabled)
                    {
                        wc.alarmEnabled = false;
                        Log.Info("Wait: a pass-out started during a wait; it runs its full course, as without the mod.");
                    }
                    End("interrupted: " + wc.waitingType);
                }
                return;
            }
            _lastSeen = wc.sleepingTimeSeconds;
        }

        // Plugin.LateUpdate. GameController.Update has run with keys disabled by now, so the key that stops a wait cannot
        // also open the pause menu or the inventory once controls come back (its GetKeyDown edge is over next frame).
        internal static void LateTick()
        {
            if (State != Phase.Running) return;
            var wc = WaitingController.instance;
            if (wc == null || wc.waitingType != WaitingController.Type.Waiting) return;

            // Read the pad every frame so the D-pad edge re-arms; the key that started the wait is ignored this frame.
            bool padEdge = Keys.PadEdge(Cfg.Wait.PadButton.Value, leaveToList: false);
            bool padStop = Keys.ControllerMode && (Keys.PadDown(Cfg.Wait.CancelPadButton.Value) || padEdge);
            bool keyStop = Keys.Down(Cfg.Wait.CancelKey) || Keys.Down(Cfg.Wait.Hotkey);
            if (Time.frameCount == _startedFrame) return;

            // Vanilla ignores Wakeup during a sleep event (SRC/WaitingController.cs:374).
            var sec = SleepEventController.instance;
            if (sec != null && sec.eventInProgress) return;

            if (PlayerStats.instance != null && WaitGate.TooTired()) { Stop(StopReason.Tired); return; }
            if (PlayerStats.instance != null && WaitGate.TooDrunkNow()) { Stop(StopReason.Drunk); return; }
            if (Cfg.Wait.StopWhenHurt.Value && Danger.Patched && Danger.LastHurt > _startedUnscaled) { Stop(StopReason.Hurt); return; }
            if (keyStop || padStop) { Stop(StopReason.Cancelled); return; }

            // Stall guard: the wait shows a black screen and blocks every key but ours, so a wait whose time stopped
            // passing would otherwise trap the player. CheckEvent counts once per game minute (PlayerStats.DeltaSeconds),
            // so the limit is at least three game minutes of real time at the current speed (day-length mods).
            if (wc.sleepingTimeSeconds != _lastSeenLate)
            {
                _lastSeenLate = wc.sleepingTimeSeconds;
                _stallSince = Time.unscaledTime;
            }
            else if (Time.timeScale > 0f && Time.unscaledTime - _stallSince > StallLimit())
            {
                Log.WarnOnce("stall", "Wait: time stopped passing during a wait, so Time Keeper ended it.");
                Stop(StopReason.Stalled);
            }
        }

        // ---- Open (spec §6.2) ----

        private static void Open(bool fromPad)
        {
            var gate = WaitGate.CheckOpen(fromPad);
            if (gate.Blocked)
            {
                if (gate.Message != null) Refuse(gate.Message);
                else Log.Verbose($"Wait: not opened ({gate.Reason}).");
                return;
            }

            var wc = WaitingController.instance;
            var ui = WaitingUI.instance;
            try
            {
                _prevHasAlarm = wc.hasAlarmClock;
                _slider = Vanilla.SetAlarmSlider(ui);
                if (_slider != null)
                {
                    _prevSliderMax = _slider.maxValue;
                    _ourSliderMax = Mathf.Clamp(Cfg.Wait.MaxHours.Value * 3600f, _slider.minValue, _prevSliderMax);
                    _slider.maxValue = _ourSliderMax;
                }

                // Before ShowTimeSetUI, so P4 and P5 already shape SetTimeOpen's first label.
                State = Phase.Picking;

                // The parameters of the game's own pass-out (SRC/WaitingController.cs:223): if the pass-out reaches our
                // open picker through the stored parameters (:219), it plays out as a pass-out, never as a safe sleep.
                // Our callback also replaces a bed's stale one (ShowTimeSetUI only stores a non-null callback, :235).
                wc.ShowTimeSetUI(15f, true, null, true, 0.33f, OnVanillaWakeup, hasAlarmClock: true);

                var row = Vanilla.SetAlarm(ui);
                if (!ui.IsActive() || row == null || !row.activeInHierarchy)
                {
                    Log.WarnOnce("not opened", "Wait: the game's clock did not open; nothing was changed.");
                    Abort();
                    return;
                }

                ui.ChangeSleepTimeAndSlider(Rounded(_lastSeconds > 0 ? _lastSeconds : Cfg.Wait.DefaultMinutes.Value * 60));
                Relabel(ui);
                Log.Verbose($"Wait: opened (default {Hm(Rounded(_lastSeconds > 0 ? _lastSeconds : Cfg.Wait.DefaultMinutes.Value * 60))}, " +
                            $"max {Cfg.Wait.MaxHours.Value}h, step {Cfg.Wait.StepMinutes.Value}m).");
            }
            catch (Exception e)
            {
                Log.Exception("wait open", e);
                Abort();
            }
        }

        // The start button reads "Wait" while our picker is open. By path first (Game Logic prefab), then by the
        // button whose persistent onClick is WaitingUI.StartSleep, in case another mod rearranged the clock.
        private static void Relabel(WaitingUI ui)
        {
            _buttonText = null;
            _buttonOriginal = null;
            var root = Vanilla.SleepButtons != null ? Vanilla.SleepButtons(ui) : null;
            if (root == null)
            {
                Log.InfoOnce("no label", "Wait: the clock's start button was not found; it keeps its \"Sleep\" label.");
                return;
            }
            TMP_Text text = null;
            var t = root.transform.Find("Sleep button/Button Text");
            if (t != null) text = t.GetComponent<TMP_Text>();
            if (text == null)
                foreach (var b in root.GetComponentsInChildren<UnityEngine.UI.Button>(true))
                {
                    for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                        if (b.onClick.GetPersistentMethodName(i) == "StartSleep") { text = b.GetComponentInChildren<TMP_Text>(true); break; }
                    if (text != null) break;
                }
            if (text == null)
            {
                Log.InfoOnce("no label", "Wait: the clock's start button was not found; it keeps its \"Sleep\" label.");
                return;
            }
            _buttonText = text;
            _buttonOriginal = text.text;
            text.text = ButtonLabel;
        }

        // ---- Start (P3 body, spec §6.4) ----

        internal static void Start(WaitingUI ui)
        {
            var wc = WaitingController.instance;
            int secs = Rounded(ui.CurrentSleepSetTime);
            if (secs <= 0)
            {
                Game.Warning();
                return;   // the picker stays open
            }

            var gate = WaitGate.CheckStart();
            if (gate.Blocked)
            {
                if (gate.Message != null) Refuse(gate.Message);
                else Log.Verbose($"Wait: not started ({gate.Reason}).");
                ui.CloseTimeSetUI();
                End("refused at start");
                return;
            }

            _target = secs;
            _lastSeconds = secs;
            _lastSeen = 0;
            _lastSeenLate = 0;
            _stopReason = StopReason.None;
            _wakeHandled = false;
            _startedUnscaled = Time.unscaledTime;
            _stallSince = Time.unscaledTime;
            _startedFrame = Time.frameCount;
            State = Phase.Running;

            float speed = Cfg.Wait.SpeedFactor.Value >= 1f ? Cfg.Wait.SpeedFactor.Value : 0f;
            bool called = false;
            try
            {
                called = true;
                // ignorePlayerNeeds false, no ignored reasons: every vanilla need wake-up stays on.
                wc.Wait(secs, WaitingController.Type.Waiting, speed);
                ui.UpdateSleptTime(secs);
                // After Wait: its ShowClock → SetTimeOpen still reads hasAlarmClock.
                RestoreUi(touchGame: true);
                wc.hasAlarmClock = _prevHasAlarm;
                Log.Verbose($"Wait: started {Hm(secs)} at speed {(speed > 0f ? speed : SafeSleepSpeed())}.");
            }
            catch (Exception e)
            {
                Log.Exception("wait start", e);
                if (called && wc != null && wc.waitingType == WaitingController.Type.Waiting)
                {
                    _stopReason = StopReason.Cancelled;
                    try { wc.Wakeup(); } catch (Exception e2) { Log.Exception("wait start wakeup", e2); }
                }
                if (State != Phase.Idle) End("error");
            }
        }

        private static float StallLimit()
        {
            try
            {
                var t = TimeOfDayAzure.instance;
                if (t == null) return StallSeconds;
                return Math.Max(StallSeconds, t.actualSpeed * 60f / 1440f * 3f);   // actualSpeed: real minutes per game day
            }
            catch { return StallSeconds; }
        }

        private static float SafeSleepSpeed()
        {
            try { return PlayerStats.instance.tiredness.SleepTimeSpeed; }
            catch { return 0f; }
        }

        // ---- Stop and end (spec §6.5) ----

        private static void Stop(StopReason reason)
        {
            _stopReason = reason;
            try { WaitingController.instance.Wakeup(); }
            catch (Exception e)
            {
                Log.Exception("wait stop", e);
                End("stop failed");
            }
        }

        // Our onWakeup: vanilla Wakeup invokes it after waitingType = None and before sleepingTimeSeconds resets
        // (SRC/WaitingController.cs:380-401). A stale callback outside our wait (a later TV or chair wait after a
        // cancelled picker) does nothing.
        internal static void OnVanillaWakeup()
        {
            try
            {
                if (State != Phase.Running) return;
                _wakeHandled = true;
                var wc = WaitingController.instance;
                if (wc != null) _lastSeen = Math.Max(_lastSeen, wc.sleepingTimeSeconds);

                string why;
                switch (_stopReason)
                {
                    case StopReason.Tired:
                        why = "tired";
                        Game.Warning();
                        Game.Notify(WaitGate.StopText(PlayerStatsTiredness.Reason.Tiredness));
                        break;
                    case StopReason.Drunk:
                        why = "drunk";
                        Game.Warning();
                        Game.Notify(WaitGate.StoppedDrunk);
                        break;
                    case StopReason.Hurt:
                        why = "hurt";
                        Game.Warning();
                        Game.Notify("Stopped waiting: you got hurt.");
                        break;
                    case StopReason.Stalled:
                        why = "stalled";
                        Game.Warning();
                        Game.Notify("Time stopped passing, so the wait ended.");
                        break;
                    case StopReason.Cancelled:
                        why = "cancelled";
                        break;
                    default:
                        why = WhyVanilla(wc);
                        break;
                }
                End(why);
            }
            catch (Exception e)
            {
                Log.Exception("wait wakeup", e);
                State = Phase.Idle;
            }
        }

        // Why vanilla woke the player: the countdown ran out, a need, or something else (dialogue, a logic output, a
        // pass-out or an arrest), which the game explains itself.
        private static string WhyVanilla(WaitingController wc)
        {
            bool done = Vanilla.CurrentSleepSetTime != null && wc != null
                ? Vanilla.CurrentSleepSetTime(wc) <= 0
                : _lastSeen >= _target - 60;
            if (done) return "done";
            var stats = PlayerStats.instance;
            if (stats != null)
            {
                var r = stats.tiredness.CanSleep(waiting: true);
                if (r != PlayerStatsTiredness.Reason.None)
                {
                    Game.Warning();
                    Game.Notify(WaitGate.StopText(r));
                    return "needs: " + r;
                }
            }
            return "interrupted";
        }

        // Closes our picker (if it is still ours and open) and forgets the session.
        internal static void Abort()
        {
            try
            {
                var wc = WaitingController.instance;
                var ui = WaitingUI.instance;
                if (wc != null && ui != null && wc.waitingType == WaitingController.Type.None && ui.IsActive()) ui.CloseTimeSetUI();
            }
            catch (Exception e) { Log.Exception("wait abort", e); }
            End("aborted");
        }

        private static void End(string why, bool touchGame = true)
        {
            var was = State;
            try
            {
                RestoreUi(touchGame);
                if (was == Phase.Picking && touchGame && WaitingController.instance != null) WaitingController.instance.hasAlarmClock = _prevHasAlarm;
            }
            catch (Exception e) { Log.Exception("wait restore", e); }
            State = Phase.Idle;
            if (was == Phase.Running) Log.Verbose($"Wait: ended ({why}) after {Hm(_lastSeen)} of {Hm(_target)}.");
            else if (was == Phase.Picking) Log.Verbose($"Wait: picker ended ({why}).");
            _target = 0;
            _lastSeen = 0;
            _lastSeenLate = 0;
            _stopReason = StopReason.None;
            _startedFrame = -1;
        }

        // Puts back the slider maximum and the button text, only where they are still ours. Idempotent. With
        // touchGame false (scene load), only forgets: the objects belong to the old scene.
        private static void RestoreUi(bool touchGame)
        {
            if (touchGame)
            {
                if (_slider != null && _prevSliderMax > 0f && Mathf.Approximately(_slider.maxValue, _ourSliderMax))
                    _slider.maxValue = _prevSliderMax;
                if (_buttonText != null && _buttonText.text == ButtonLabel && _buttonOriginal != null)
                    _buttonText.text = _buttonOriginal;
            }
            _slider = null;
            _prevSliderMax = _ourSliderMax = -1f;
            _buttonText = null;
            _buttonOriginal = null;
        }

        internal static void OnSceneLoaded()
        {
            if (State != Phase.Idle) End("scene", touchGame: false);
            WaitGate.OnSceneLoaded();
        }

        // ---- Helpers ----

        private static void Refuse(string note)
        {
            Game.Warning();
            Game.Notify(note);
            Log.Verbose($"Wait: refused ({note}).");
        }

        // The duration snapped to StepMinutes, between one step (never under the game's 10 minutes) and MaxHours.
        internal static int Rounded(int raw)
        {
            if (raw <= 0) return 0;
            int step = Cfg.Wait.StepMinutes.Value * 60;
            if (step <= 0) step = 600;
            int r = Mathf.RoundToInt(raw / (float)step) * step;
            int min = Math.Max(600, step);
            int max = Math.Min(Cfg.Wait.MaxHours.Value * 3600, 86400);
            if (max < min) max = min;
            return Mathf.Clamp(r, min, max);
        }

        private static string Hm(int seconds)
        {
            if (seconds < 0) seconds = 0;
            return $"{seconds / 3600}:{seconds / 60 % 60:00}";
        }
    }
}

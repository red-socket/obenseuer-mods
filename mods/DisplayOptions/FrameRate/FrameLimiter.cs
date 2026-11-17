using System;
using UnityEngine;

namespace DisplayOptions
{
    // S-005 part 1 (spec §4). Vanilla sets Application.targetFrameRate to the saved refresh rate (or 60) on every call of
    // DisplaySave.ApplyFrameRateLimit (SRC/DisplaySave.cs:90-93), including while VSync is on (OptionsMenu.cs:715-726). In
    // borderless and windowed mode that is a second limiter next to VSync. With VSync on we write a safety ceiling far
    // above the refresh rate (VSyncCeiling), so VSync alone paces; with VSync off the player picks the cap. Only
    // targetFrameRate is written: never vSyncCount (persisted into Display.json at OptionsMenu.cs:1229), Display.json or
    // any DisplaySave field.
    internal static class FrameLimiter
    {
        internal static bool Broken, StoodDown;
        internal static int LastPfr;
        private static bool _pfrSeen, _haveWritten;
        private static int _lastWritten;

        internal static int VanillaTarget(int pfr) => pfr > 0 ? pfr : 60;

        // The rule (spec §4.1). The "fully off" case returns exactly what vanilla wrote.
        internal static int Target(int pfr, out string reason)
        {
            if (!Cfg.Hot.FixVSync && Cfg.Hot.CapMode == CapMode.GameDefault)
            {
                reason = "game default";
                return VanillaTarget(pfr);
            }
            if (QualitySettings.vSyncCount > 0)
            {
                reason = Cfg.Hot.FixVSync ? "VSync paces, safety ceiling" : "game default with VSync";
                return Cfg.Hot.FixVSync ? VSyncCeiling() : VanillaTarget(pfr);
            }
            switch (Cfg.Hot.CapMode)
            {
                case CapMode.Uncapped:
                    reason = "uncapped";
                    return -1;
                case CapMode.Custom:
                    reason = "custom cap";
                    return Mathf.Clamp(Cfg.Hot.CustomCap, 20, 500);
                case CapMode.MatchMonitor:
                    int hz = MonitorHz();
                    reason = hz > 0 ? "display rate" : "display rate unknown, game default";
                    return hz > 0 ? hz : VanillaTarget(pfr);
                default:
                    reason = "game default";
                    return VanillaTarget(pfr);
            }
        }

        // With VSync on (D1): not -1, which would leave nothing but the present call to hold the rate. Where VSync doesn't
        // block (a driver forcing it off, Fast Sync or Enhanced Sync, a mailbox present mode, a minimised window with Run In
        // Background) vanilla still held P or 60; this keeps a bound there. A limit at twice the refresh rate can't cause
        // the half-rate lock: VSync already holds every frame for a full refresh period, longer than the limiter's. The
        // 240 floor also covers a display rate read from the wrong monitor.
        internal static int VSyncCeiling() => Mathf.Max(2 * MonitorHz(), 240);

        // The real output's rate: the desktop in borderless and windowed, the mode in exclusive fullscreen.
        private static int MonitorHz()
        {
            try { return (int)Math.Round(Screen.currentResolution.refreshRateRatio.value); }
            catch (Exception) { return 0; }
        }

        internal static void Apply(int pfr)
        {
            if (Broken || StoodDown) return;
            try
            {
                LastPfr = pfr;
                _pfrSeen = true;
                int t = Target(pfr, out string reason);
                if (Application.targetFrameRate != t) Application.targetFrameRate = t;
                _lastWritten = t;
                _haveWritten = true;
                Log.Verbose($"Fps: target {t} (vSync {QualitySettings.vSyncCount}, {Cfg.Hot.CapMode}, {reason}).");
            }
            catch (Exception e)
            {
                Broken = true;
                Log.Exception("Fps: apply", e);
            }
        }

        // P2 and config changes: the value P1 last saw. If P1 never ran (inlined), the saved refresh rate.
        internal static int CurrentPfr()
        {
            if (_pfrSeen) return LastPfr;
            try
            {
                var ds = DisplaySave.instance;
                return ds != null && ds.displaySave != null ? ds.displaySave.preferredRefreshRate : 0;
            }
            catch (Exception) { return 0; }
        }

        internal static void Reapply() => Apply(CurrentPfr());

        // One frame after a scene load, and on LoadingDone (spec §4.4). Vanilla's own value means it wrote without our
        // postfix (inlined); anything else is another mod, and we leave the cap to it for the session.
        internal static void DriftCheck()
        {
            if (Broken || StoodDown || !_haveWritten) return;
            int now = Application.targetFrameRate;
            if (now == _lastWritten) return;
            if (now == VanillaTarget(CurrentPfr()))
            {
                Log.InfoOnce("fps inline", $"Fps: the game set its frame cap without Display Options (now {now}); applying it again.");
                Reapply();
                return;
            }
            StoodDown = true;
            Log.WarnOnce("fps drift", $"Fps: another mod sets the frame cap (now {now}); Display Options leaves it alone this session.");
        }

        // Live off and plugin teardown: vanilla's value.
        internal static void Restore()
        {
            if (!_haveWritten || StoodDown) return;
            try { Application.targetFrameRate = VanillaTarget(CurrentPfr()); }
            catch (Exception e) { Log.Exception("Fps: restore", e); }
        }
    }
}

using System;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FairJustice
{
    // Session-only jail state (spec §2.3): the prison in the loaded scene, the two grace timers and the sleep event
    // rolled in jail. Nothing here is saved.
    internal static class Jail
    {
        internal const string MainMenuScene = "Main Menu";   // SRC/PauseMenu.cs:450

        // P7/P8/P10/P11 patched ("jail state" group).
        internal static bool StatePatched;

        // Set by P7 and P8 and on every scene load; Unity's null check drops it once the scene is gone.
        internal static Prison Current;

        internal static bool InPrison => Current != null && Vanilla.InPrison(Current);

        // Seconds of player control left. AnyGrace mirrors "either > 0" so hot prefixes exit after one field read.
        internal static float ImprisonGraceLeft, ReleaseGraceLeft;
        internal static bool AnyGrace;

        // The optional after-load cell-door grace (D-6) was given for the current stay in jail. Cleared only by P7.
        internal static bool AfterLoadGraceUsed;

        // The event a sleep that started in jail rolled (P21), read by P22. A release can fire during that sleep.
        internal static SleepEvent FromJailEvent;

        internal static void OnSceneLoaded(Scene scene)
        {
            if (scene.name == MainMenuScene)
            {
                EndGraces("main menu");
                FromJailEvent = null;
                Current = null;
                return;
            }
            if (Current == null) Current = Object.FindFirstObjectByType<Prison>();
        }

        // True when a grace was started.
        internal static bool StartImprisonGrace(string why)
        {
            if (!Cfg.CellDoorGrace.Value || Cfg.CellDoorGraceSeconds.Value <= 0) return false;
            ImprisonGraceLeft = Cfg.CellDoorGraceSeconds.Value;
            Sync();
            Log.Verbose($"Cell-door grace started ({why}): {ImprisonGraceLeft:0} s.");
            return true;
        }

        internal static void StartReleaseGrace()
        {
            if (!Cfg.ReleaseGrace.Value || Cfg.ReleaseGraceSeconds.Value <= 0) return;
            ReleaseGraceLeft = Cfg.ReleaseGraceSeconds.Value;
            Sync();
            Log.Info($"Release grace started: {ReleaseGraceLeft:0} s of play.");
        }

        internal static void EndImprisonGrace()
        {
            ImprisonGraceLeft = 0f;
            Sync();
        }

        internal static void ResetReleaseGrace()
        {
            ReleaseGraceLeft = 0f;
            Sync();
        }

        // Plugin.Update: counts down only while the player has control.
        internal static void Tick()
        {
            if (!AnyGrace || !Game.HasControl) return;
            float dt = Time.unscaledDeltaTime;
            if (ImprisonGraceLeft > 0f)
            {
                ImprisonGraceLeft -= dt;
                if (ImprisonGraceLeft <= 0f)
                {
                    ImprisonGraceLeft = 0f;
                    Log.Verbose("Cell-door grace ended: time up.");
                }
            }
            if (ReleaseGraceLeft > 0f)
            {
                ReleaseGraceLeft -= dt;
                if (ReleaseGraceLeft <= 0f)
                {
                    ReleaseGraceLeft = 0f;
                    Log.Info("Release grace ended: time up.");
                }
            }
            Sync();
        }

        internal static void EndGraces(string why)
        {
            bool was = AnyGrace || ImprisonGraceLeft > 0f || ReleaseGraceLeft > 0f;
            ImprisonGraceLeft = 0f;
            ReleaseGraceLeft = 0f;
            AnyGrace = false;
            Graces.ResetSkipLog();
            if (was) Log.Info($"Grace ended: {why}.");
        }

        private static void Sync()
        {
            bool any = ImprisonGraceLeft > 0f || ReleaseGraceLeft > 0f;
            if (!any && AnyGrace) Graces.ResetSkipLog();
            AnyGrace = any;
        }
    }
}

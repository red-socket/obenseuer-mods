using System;
using HarmonyLib;
using UnityEngine;

namespace DisplayOptions
{
    // Shadow distance and sun cascade caps (spec §5.4). applyQualitySettings (SRC/OptionsMenu.cs:1260-1306) calls
    // SetQualityLevel, which resets everything, then writes shadowDistance (0/10/30/150 m) and shadowCascades (0/2/4/4)
    // from the menu. It runs at boot (main-menu StartInit), in every level (PauseMenu.Start → StartInit) and on Quality
    // Apply, so a postfix there is the one place to record the game's values and lower them. Nothing is written while
    // both caps are 0 and nothing was capped before.
    internal static class QualityCaps
    {
        internal static bool Broken, StoodDown;
        private static bool _recorded, _applied;
        private static float _gameDist;
        private static int _gameCasc;

        internal static void OnGameApplied()
        {
            if (Broken) return;
            try
            {
                _gameDist = QualitySettings.shadowDistance;
                _gameCasc = QualitySettings.shadowCascades;
                _recorded = true;
                _applied = false;   // SetQualityLevel just replaced whatever we wrote
                Reapply();
            }
            catch (Exception e)
            {
                Broken = true;
                Log.Exception("Shadows: quality caps", e);
            }
        }

        internal static void Reapply()
        {
            if (!_recorded || Broken || StoodDown) return;
            float maxDist = Cfg.Hot.MaxDist;
            int maxCasc = Cfg.Hot.MaxCasc;
            if (maxDist <= 0f && maxCasc <= 0 && !_applied) return;

            // Shadow quality Off keeps 0 m; shadow resolution Low keeps 0 cascades.
            float dist = maxDist > 0f ? Mathf.Min(_gameDist, maxDist) : _gameDist;
            int casc = _gameCasc <= 0 ? _gameCasc : maxCasc > 0 ? Mathf.Min(_gameCasc, maxCasc) : _gameCasc;
            if (QualitySettings.shadowDistance != dist) QualitySettings.shadowDistance = dist;
            if (QualitySettings.shadowCascades != casc) QualitySettings.shadowCascades = casc;
            _applied = dist != _gameDist || casc != _gameCasc;
            Log.Verbose($"Shadows: quality caps {_gameDist:0}→{dist:0} m, {_gameCasc}→{casc} cascades.");
        }

        // Stand-down and teardown: the game's own values.
        internal static void Restore()
        {
            if (!_recorded || !_applied) return;
            try
            {
                QualitySettings.shadowDistance = _gameDist;
                QualitySettings.shadowCascades = _gameCasc;
                _applied = false;
            }
            catch (Exception e) { Log.Exception("Shadows: quality restore", e); }
        }
    }

    // P3 (spec §3.1).
    [HarmonyPatch(typeof(OptionsMenu), nameof(OptionsMenu.applyQualitySettings))]
    internal static class ApplyQualitySettingsPatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Postfix() => QualityCaps.OnGameApplied();
    }
}

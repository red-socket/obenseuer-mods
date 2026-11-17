using System;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace BetterPlacement
{
    // The building menu's controls line (spec §3.8). H1 shows the held snap step in place of the rotation mode; H2 adds
    // the align and snap keys before the final "<Menu>)EXIT", in vanilla's "KEY)ACTION   " style. Labels follow vanilla:
    // InputManager.inputType picks keyboard or pad labels. Vanilla only rewrites controlsInfoText on menu events
    // (SRC/BuildingSystemMenu.cs:235, :253, :365, :379), so Refresh rewrites it when our state changes. The special line,
    // the warning text and the selection text are never touched (vanilla rewrites them every frame).
    internal static class Hint
    {
        internal static bool Patched;

        internal static void Refresh()
        {
            if (!Patched) return;
            try
            {
                var m = BuildingSystemMenu.instance;
                if (m == null || !m.menuIsOpen || Vanilla.ButtonInfo == null || Vanilla.ControlsInfoText == null) return;
                var text = Vanilla.ControlsInfoText(m);
                if (text != null) text.text = Vanilla.ButtonInfo(m);
            }
            catch (Exception e) { Log.Exception("hint refresh", e); }
        }

        private static bool Pad => InputManager.instance != null && InputManager.instance.inputType == InputManager.InputType.Controller;

        internal static string Segments()
        {
            var sb = new StringBuilder();
            bool pad = Pad;
            if (AlignPatches.Patched && Cfg.Align.Enabled.Value)
            {
                string key = null;
                if (pad)
                {
                    var b = Cfg.Align.PadButton.Value;
                    if (b != PadButton.None) key = Keys.PadLabel(b);
                }
                else if (Keys.Bound(Cfg.Align.Key) && !Keys.Suspended(Cfg.Align.Key)) key = Keys.Label(Cfg.Align.Key);
                switch (Cfg.Align.Mode.Value)
                {
                    case AlignMode.Toggle:
                        if (key != null) sb.Append(string.Format(Strings.AlignToggle, key, AlignInput.Active ? Strings.On : Strings.Off)).Append(Strings.Sep);
                        break;
                    case AlignMode.Always:
                        sb.Append(key != null ? string.Format(Strings.AlignAlways, key) : Strings.AlignAlwaysNoKey).Append(Strings.Sep);
                        break;
                    default:
                        if (key != null) sb.Append(string.Format(Strings.AlignHold, key)).Append(Strings.Sep);
                        break;
                }
            }
            if (RotationSnap.Patched && Cfg.Rotation.SnapEnabled.Value)
            {
                if (pad)
                {
                    var b = Cfg.Rotation.SnapPadButton.Value;
                    if (b != PadButton.None && !Keys.SnapPadSuspended)
                        sb.Append(string.Format(Strings.Snap, Keys.PadLabel(b), Cfg.Rotation.FineStep.Value)).Append(Strings.Sep);
                }
                else
                {
                    bool fine = Keys.Bound(Cfg.Rotation.SnapKey) && !Keys.Suspended(Cfg.Rotation.SnapKey);
                    bool coarse = Keys.Bound(Cfg.Rotation.CoarseKey) && !Keys.Suspended(Cfg.Rotation.CoarseKey);
                    if (fine)
                    {
                        sb.Append(string.Format(Strings.Snap, Keys.Label(Cfg.Rotation.SnapKey), Cfg.Rotation.FineStep.Value));
                        if (coarse) sb.Append(string.Format(Strings.Coarse, Keys.Label(Cfg.Rotation.CoarseKey), Cfg.Rotation.CoarseStep.Value));
                        sb.Append(Strings.Sep);
                    }
                    else if (coarse)
                        sb.Append(string.Format(Strings.Snap, Keys.Label(Cfg.Rotation.CoarseKey), Cfg.Rotation.CoarseStep.Value)).Append(Strings.Sep);
                }
            }
            return sb.ToString();
        }
    }

    // H1: BuildingSystemMenu.GetRotationMode() (SRC/BuildingSystemMenu.cs:898), private.
    [HarmonyPatch]
    internal static class RotationModeTextPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetRotationMode;

        private static void Postfix(ref string __result)
        {
            try
            {
                int step = RotationSnap.HeldStep;
                if (step > 0 && Cfg.ShowControlsHint.Value) __result = string.Format(Strings.HeldStep, step);
            }
            catch (Exception e) { Log.Exception("hint rotation mode", e); }
        }
    }

    // H2: BuildingSystemMenu.GetButtonInfo() (SRC/BuildingSystemMenu.cs:883), private.
    [HarmonyPatch]
    internal static class ButtonInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetButtonInfo;

        private static void Postfix(ref string __result)
        {
            try
            {
                if (!Cfg.ShowControlsHint.Value || string.IsNullOrEmpty(__result) || InputManager.instance == null) return;
                string extra = Hint.Segments();
                if (extra.Length == 0) return;
                string exit = InputManager.instance.GetKeyCodeDisplayText("Menu") + ")EXIT";
                int at = __result.LastIndexOf(exit, StringComparison.Ordinal);
                __result = at >= 0 ? __result.Insert(at, extra) : __result + Strings.Sep + extra.TrimEnd();
            }
            catch (Exception e) { Log.Exception("hint controls", e); }
        }
    }
}

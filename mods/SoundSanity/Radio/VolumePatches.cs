using System;
using System.Reflection;
using HarmonyLib;

namespace SoundSanity
{
    // VS1: VolumeSave.SaveVolume(string, float, string) (SRC/VolumeSave.cs:35): the options menu moved a slider. Only the
    // Music slider changes a radio gain; the mod reads the new level from the mixer on the next frame.
    [HarmonyPatch]
    internal static class SaveVolumePatch
    {
        private static MethodBase TargetMethod() => Vanilla.SaveVolume;

        private static void Postfix(string volumeName)
        {
            if (!Groups.MusicSlider.On) return;
            try
            {
                if (volumeName == "Music") Live.MarkDirty(Live.Pass.Radios | Live.Pass.Loops);
            }
            catch (Exception e) { Groups.MusicSlider.Fail(e); }
        }
    }

    // VS2: VolumeSave.GetVolume() (SRC/VolumeSave.cs:64): the saved levels were just applied to the mixer.
    [HarmonyPatch]
    internal static class GetVolumePatch
    {
        private static MethodBase TargetMethod() => Vanilla.GetVolume;

        private static void Postfix()
        {
            if (!Groups.MusicSlider.On) return;
            try { Live.MarkDirty(Live.Pass.Radios | Live.Pass.Loops); }
            catch (Exception e) { Groups.MusicSlider.Fail(e); }
        }
    }
}

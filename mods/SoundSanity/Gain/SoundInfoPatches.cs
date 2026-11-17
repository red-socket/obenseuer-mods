using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace SoundSanity
{
    // G1: AudioSourceMaker.FromSound (SRC/AudioSourceMaker.cs:106). Covers the creation volume of a registered SoundInfo's
    // source and every PlayMisc one-shot (MiscAudioPlayer calls FromSound right before Play()). CreateAudioSource's later
    // write of 0 for an inactive area still wins.
    [HarmonyPatch]
    internal static class FromSoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.FromSound;

        private static void Postfix(SoundInfo sound, AudioSource __result)
        {
            if (sound == null || __result == null || !Groups.GainCore.On) return;
            try
            {
                if (Registry.TryGet(sound, out var e)) Registry.ApplyAfterVanilla(sound, e, __result);
            }
            catch (Exception ex) { Groups.GainCore.Fail(ex); }
        }
    }

    // G2: SoundInfo.PlaySound() (SRC/SoundInfo.cs:158); PlaySound(GameObject) calls it. Vanilla writes SoundVolume only
    // behind its area gate, so the gain follows the same gate.
    [HarmonyPatch]
    internal static class PlaySoundPatch
    {
        private static MethodBase TargetMethod() => Vanilla.PlaySound;

        private static void Postfix(SoundInfo __instance) => GainPatches.AfterPlay(__instance);
    }

    // G3: SoundInfo.PlayDelayed(float) (SRC/SoundInfo.cs:206), same gate as PlaySound. The power-on hum starts here.
    [HarmonyPatch]
    internal static class PlayDelayedPatch
    {
        private static MethodBase TargetMethod() => Vanilla.PlayDelayed;

        private static void Postfix(SoundInfo __instance) => GainPatches.AfterPlay(__instance);
    }

    // G4: SoundInfo.SoundscapeAreaPlay() (SRC/SoundInfo.cs:238): the area came back; vanilla wrote SoundVolume to a playing
    // loop. The gain is recomputed here from the source's current group. Group changes that are not followed by an area
    // play (SetBackgroundSoundscape on the current area) are covered by RA2 (Radio/RadioPatches.cs).
    [HarmonyPatch]
    internal static class SoundscapeAreaPlayPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SoundscapeAreaPlay;

        private static void Postfix(SoundInfo __instance)
        {
            if (__instance == null || !Groups.GainCore.On) return;
            try
            {
                if (!Registry.TryGet(__instance, out var e)) return;
                if (__instance.source == null || !__instance.Looping || !__instance.playing) return;
                e.AreaStopped = false;
                Registry.ApplyAfterVanilla(__instance, e, __instance.source);
            }
            catch (Exception ex) { Groups.GainCore.Fail(ex); }
        }
    }

    // G5: SoundInfo.SoundscapeAreaStop() (SRC/SoundInfo.cs:257): vanilla holds the source at 0 until its area plays again.
    // Writes nothing; it only keeps live passes away from the source.
    [HarmonyPatch]
    internal static class SoundscapeAreaStopPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SoundscapeAreaStop;

        private static void Postfix(SoundInfo __instance)
        {
            if (__instance == null || !Groups.GainCore.On) return;
            try
            {
                if (Registry.TryGet(__instance, out var e) && __instance.source != null) e.AreaStopped = true;
            }
            catch (Exception ex) { Groups.GainCore.Fail(ex); }
        }
    }

    // G6: SoundscapeArea.AddSoundInfo (SRC/SoundscapeArea.cs:48): records which area owns a SoundInfo (Core/Areas.cs).
    [HarmonyPatch]
    internal static class AddSoundInfoPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddSoundInfo;

        private static void Postfix(SoundscapeArea __instance, SoundInfo soundInfo)
        {
            if (!Groups.AreaTracking.On) return;
            try { Areas.SetOwner(soundInfo, __instance); }
            catch (Exception ex) { Groups.AreaTracking.Fail(ex); }
        }
    }

    internal static class GainPatches
    {
        internal static void AfterPlay(SoundInfo si)
        {
            if (si == null || !Groups.GainCore.On) return;
            try
            {
                if (!Registry.TryGet(si, out var e) || si.source == null || !Registry.VanillaGate(si)) return;
                e.AreaStopped = false;
                Registry.ApplyAfterVanilla(si, e, si.source);
            }
            catch (Exception ex) { Groups.GainCore.Fail(ex); }
        }
    }
}

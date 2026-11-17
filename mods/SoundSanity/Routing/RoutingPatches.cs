using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;

namespace SoundSanity
{
    // R1: AudioSourceMaker.FromClip (SRC/AudioSourceMaker.cs:56). Almost every call ends at the first check: the game
    // already put the source on a group of the real mixer. What is left is a null group (no MiscAudioPlayer, as in the Open
    // Sewer Intro) or, with FixMainGroup on, the Main group.
    [HarmonyPatch]
    internal static class FromClipPatch
    {
        private static MethodBase TargetMethod() => Vanilla.FromClip;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(AudioSource __result)
        {
            if (__result == null || !Groups.Routing.On) return;
            try
            {
                var g = __result.outputAudioMixerGroup;
                if (g != null && g.audioMixer == Mixers.Main && g != Mixers.MainGroup) return;
                Router.Handle(__result, Router.Ctx.HookPath, "FromClip");
            }
            catch (Exception e) { Groups.Routing.Fail(e); }
        }
    }

    // R2: SoundInfo.CreateAudioSource (SRC/SoundInfo.cs:90). Its soundscape override (:104-113) only ever picks a group of
    // the real mixer, so this is expected to move nothing in 0.5.19; it stays as a guard and a log point.
    [HarmonyPatch]
    internal static class CreateAudioSourcePatch
    {
        private static MethodBase TargetMethod() => Vanilla.CreateAudioSource;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(SoundInfo __instance)
        {
            if (!Groups.Routing.On) return;
            try
            {
                var s = __instance?.source;
                if (s == null) return;
                AudioMixerGroup g = s.outputAudioMixerGroup;
                if (g != null && g.audioMixer == Mixers.Main && g != Mixers.MainGroup) return;
                Router.Handle(s, Router.Ctx.HookPath, "CreateAudioSource");
            }
            catch (Exception e) { Groups.Routing.Fail(e); }
        }
    }

    // R3: AudioSourceMaker.ResolveMixerGroups(root) (SRC/AudioSourceMaker.cs:44), called from FurniturePlaceable.Awake
    // (placed, loaded and adopted furniture) and for BuildingSystem previews. Vanilla resolves the copy's groups there;
    // this adds null groups and re-checks the root two frames later for sources added in Start.
    [HarmonyPatch]
    internal static class ResolveMixerGroupsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.ResolveMixerGroups;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(GameObject root)
        {
            if (root == null || !Groups.Routing.On || !Router.Active) return;
            try
            {
                Sweeps.SweepRoot(root, false, "furniture");
                Scheduler.QueueRoot(root, "furniture");
            }
            catch (Exception e) { Groups.Routing.Fail(e); }
        }
    }
}

using System;
using BeautifyEffect;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-073 brightness (spec §7c; both options OFF, flagged for in-game confirmation).
    // - Scene load: the code shows no reset. PauseMenu.Start -> OptionsMenu.StartInit -> SetBrightness
    //   (OptionsMenu.cs:337-338, :737-740) applies the saved value in every scene. The option only repeats that.
    // - Effect swaps: confirmed in code. Setting Beautify.profile loads the profile's brightness (BeautifyProfile.cs:515)
    //   during SMV and Wasteland swaps (SMVEffects.cs:210/235/341, WastelandTrigger.cs:35), which overwrites the player's
    //   setting. After a swap to a profile whose brightness is the neutral 1.0 (Normal, Wasteland, MainMenu), the saved
    //   setting is applied again; a deliberate value such as IntroRoom's 1.371 is kept. Stateless.
    internal static class Brightness
    {
        internal static bool SceneLoadOn, EffectsPatched;

        private static float Saved => DisplaySave.instance != null && DisplaySave.instance.displaySave != null
            ? DisplaySave.instance.displaySave.brightness / 100f
            : -1f;

        internal static void OnLoadingDone()
        {
            if (!SceneLoadOn || !Cfg.ReapplyBrightnessOnSceneLoad.Value) return;
            float v = Saved;
            if (v < 0f || EffectsController.instance == null) return;
            EffectsController.instance.ChangeBrightness(v);
            Log.Verbose($"Display: brightness {v:0.00} applied after the load.");
        }

        private static bool Neutral(BeautifyProfile p) => p != null && Mathf.Abs(p.brightness - 1f) < 0.0001f;

        internal static void AfterProfile(EffectsController ec, BeautifyProfile profile)
        {
            if (!EffectsPatched || !Cfg.ReapplyBrightnessAfterEffects.Value || ec == null || !Neutral(profile)) return;
            float v = Saved;
            if (v < 0f) return;
            ec.ChangeBrightness(v);
        }

        internal static void AfterDefault(EffectsController ec)
        {
            if (!EffectsPatched || !Cfg.ReapplyBrightnessAfterEffects.Value || ec == null) return;
            float v = Saved;
            var list = Vanilla.BeautifyList?.Invoke(ec);
            if (v < 0f || list == null) return;
            foreach (var b in list)
                if (b != null && b.beautify != null && Neutral(b.defaultProfile)) b.beautify.brightness = v;
        }
    }

    // EffectsController.ChangeBeautifyProfiles(BeautifyProfile).
    [HarmonyPatch(typeof(EffectsController), nameof(EffectsController.ChangeBeautifyProfiles), new[] { typeof(BeautifyProfile) })]
    internal static class ChangeBeautifyProfilesPatch
    {
        private static void Postfix(EffectsController __instance, BeautifyProfile profile)
        {
            try { Brightness.AfterProfile(__instance, profile); }
            catch (Exception e) { Log.Exception("Display: after profile", e); }
        }
    }

    // EffectsController.ChangeBeautifyProfilesToDefault().
    [HarmonyPatch(typeof(EffectsController), nameof(EffectsController.ChangeBeautifyProfilesToDefault))]
    internal static class ChangeBeautifyProfilesToDefaultPatch
    {
        private static void Postfix(EffectsController __instance)
        {
            try { Brightness.AfterDefault(__instance); }
            catch (Exception e) { Log.Exception("Display: after default profile", e); }
        }
    }
}

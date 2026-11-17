using System;
using HarmonyLib;
using UnityEngine;

namespace DisplayOptions
{
    // Distance cap on shadow-casting lamps (spec §5.3, D6). LightController.Update (SRC/LightController.cs:260-) measures
    // the player's distance on its own frames (every 9th, every frame with SmoothTransition, or when forced), returns early
    // when the distance moved less than 0.05 m (:309), and turns shadows Hard within ShadowOffDistance (:366-368) and None
    // beyond it (:378-381). We leave ShadowOffDistance alone (it also drives the volumetric beams and the strength fade) and
    // turn shadows None after vanilla, only on frames where vanilla re-evaluated (lastdist changed) and the lamp is beyond
    // the cap. Disabled controllers (shop previews force Hard every frame, :277-279) and NoShadows lamps are skipped.
    //
    // Lazy: Update is patched on the main thread the first time the effective cap is above 0, then left in place with an
    // early return. At the default (preset Off) LightController is never patched.
    internal static class LightCap
    {
        internal static bool Broken, StoodDown, Patched, Failed;
        private static bool _requested;
        private static float _prevCap;

        internal static void InitAtLaunch()
        {
            _prevCap = Cfg.Hot.LightCap;
            if (_prevCap > 0f) EnsurePatched();
        }

        // Plugin.Update: a config change asked for the patch.
        internal static void Tick()
        {
            if (!_requested) return;
            _requested = false;
            EnsurePatched();
            ApplyNow();
        }

        private static void EnsurePatched()
        {
            if (Patched || Failed || StoodDown) return;
            if (Compat.AnyForeign(Vanilla.LightUpdate, "the lamp shadow distance cap", "is off this session"))
            {
                StoodDown = true;
                return;
            }
            Patched = Plugin.PatchLazy("light-cap", new object[] { Vanilla.LightUpdate, Vanilla.LightLastDist, Vanilla.LightForceUpdate, Vanilla.LightCull },
                Vanilla.LightUpdate, typeof(LightUpdatePatch));
            Failed = !Patched;
        }

        internal static void OnConfigChanged()
        {
            float cap = Cfg.Hot.LightCap;
            bool released = _prevCap > 0f && (cap <= 0f || cap > _prevCap);
            _prevCap = cap;
            if (released) Release();
            if (cap > 0f)
            {
                if (!Patched) _requested = true;   // patched next frame on the main thread, then applied
                else ApplyNow();
            }
        }

        // Config change and LoadingDone: cap lamps that are already beyond the cap, without waiting for the player to move.
        // FindObjectsByType rather than the culling lists: CullingController.controllersAll omits moving lights.
        internal static void ApplyNow()
        {
            float cap = Cfg.Hot.LightCap;
            if (cap <= 0f || !Patched || Broken || StoodDown) return;
            int n = 0;
            foreach (var lc in UnityEngine.Object.FindObjectsByType<LightController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (lc == null || lc.disabled || lc.NoShadows) continue;
                var l = Vanilla.LightCull(lc);
                if (l == null || Vanilla.LightLastDist(lc) <= cap || l.shadows == LightShadows.None) continue;
                l.shadows = LightShadows.None;
                n++;
            }
            Log.Verbose($"Shadows: {n} lamps beyond {cap:0} m lost their shadows now.");
        }

        // Cap raised or off, stand-down and teardown: vanilla re-evaluates every shadowless lamp on its next Update instead
        // of waiting for the player to move 0.05 m. Lamps vanilla itself turned off are evaluated again too (harmless).
        internal static void Release()
        {
            if (!Patched) return;
            int n = 0;
            foreach (var lc in UnityEngine.Object.FindObjectsByType<LightController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (lc == null || lc.disabled || lc.NoShadows) continue;
                var l = Vanilla.LightCull(lc);
                if (l == null || l.shadows != LightShadows.None) continue;
                Vanilla.LightLastDist(lc) = -1f;
                Vanilla.LightForceUpdate(lc) = true;
                n++;
            }
            Log.Verbose($"Shadows: {n} lamps re-evaluated by the game.");
        }

        internal static void StandDown()
        {
            if (StoodDown) return;
            StoodDown = true;
            Plugin.Step("shadows (light release)", Release);
        }
    }

    // P4 (spec §3.1, §5.3). A transpiler on the setter at :368 would miss a lamp that is already Hard and then moves
    // beyond the cap, so this is a prefix/postfix pair comparing lastdist.
    [HarmonyPatch(typeof(LightController), "Update")]
    internal static class LightUpdatePatch
    {
        [HarmonyPriority(Priority.Last)]
        private static void Prefix(float ___lastdist, out float __state) => __state = ___lastdist;

        [HarmonyPriority(Priority.Last)]
        private static void Postfix(LightController __instance, float ___lastdist, Light ___cullLight, float __state)
        {
            float cap = Cfg.Hot.LightCap;
            if (cap <= 0f || LightCap.Broken || LightCap.StoodDown) return;
            if (___lastdist == __state || ___lastdist <= cap) return;   // vanilla did not evaluate, or within the cap
            try
            {
                if (__instance.disabled || __instance.NoShadows || ___cullLight == null) return;
                if (___cullLight.shadows != LightShadows.None) ___cullLight.shadows = LightShadows.None;
            }
            catch (Exception e)
            {
                LightCap.Broken = true;
                Log.Exception("Shadows: light cap", e);
            }
        }
    }
}

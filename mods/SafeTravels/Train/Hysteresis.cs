using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace SafeTravels
{
    // P5 (S-021 b, spec §3.5, §4.6). Vanilla releases a coupling the first frame its gap is over maxDistance
    // (SRC/TrackTrain.cs:304-306): one hitch, curve or autosave stall and the wagons part. Only AdjustTrainPosition(false)
    // reaches that release (ApplyValuesOnWagons every frame, and once from TrainShop.ShowTrainOnTrack); the load-time
    // UncoupleIfTooFar passes dontbreak = true and is untouched. The prefix (last, after other mods' prefixes) turns the
    // break off; the postfix releases once the gap has stayed too wide for GraceFrames distinct frames and GraceSeconds
    // of game time, or at once beyond HardBreakFactor × maxDistance. In between, vanilla AdjustTrainPosition keeps
    // pulling the wagon back. Releasing here is vanilla's own call site: AdjustTrainPosition checks coupledTo right
    // after GetDistance (:337).
    [HarmonyPatch(typeof(TrackTrain.TrainCoupler), nameof(TrackTrain.TrainCoupler.GetDistance), typeof(bool))]
    internal static class GetDistancePatch
    {
        internal static bool Patched;

        private sealed class Streak
        {
            internal int Frames;
            internal int LastFrame = -10;
            internal float Since;
        }

        private static readonly ConditionalWeakTable<TrackTrain.TrainCoupler, Streak> Streaks = new ConditionalWeakTable<TrackTrain.TrainCoupler, Streak>();
        private const float SummarySeconds = 10f;
        private static int _healed;
        private static float _nextSummary;

        [HarmonyPriority(Priority.Last)]
        private static void Prefix(ref bool dontbreak, out bool __state)
        {
            __state = false;
            try
            {
                __state = !dontbreak && Cfg.Decoupling.Enabled.Value;
                if (__state) dontbreak = true;
            }
            catch (Exception e) { Log.Exception("decoupling prefix", e); }
        }

        private static void Postfix(TrackTrain.TrainCoupler __instance, float __result, bool __state)
        {
            try
            {
                if (!__state || __instance.coupledTo == null || __instance.maxDistance <= 0f) return;
                var s = Streaks.GetValue(__instance, _ => new Streak());
                float d = Mathf.Abs(__result), max = __instance.maxDistance;
                if (d <= max)
                {
                    if (s.Frames > 0) Healed(s.Frames);
                    s.Frames = 0;
                    return;
                }
                if (d > max * Cfg.Decoupling.HardBreakFactor.Value)
                {
                    s.Frames = 0;
                    Log.Info($"Decoupling: released at once ({d:0.0} m, {Game.SceneName})");
                    __instance.Release();
                    return;
                }
                int frame = Time.frameCount;
                if (s.LastFrame == frame) return;                          // a second call for this coupler this frame
                if (s.LastFrame != frame - 1) { s.Frames = 0; s.Since = Time.time; }
                if (s.Frames == 0) s.Since = Time.time;
                s.Frames++;
                s.LastFrame = frame;
                if (s.Frames >= Cfg.Decoupling.GraceFrames.Value && Time.time - s.Since >= Cfg.Decoupling.GraceSeconds.Value)
                {
                    s.Frames = 0;
                    Log.Info($"Decoupling: released after a held gap ({d:0.0} m, {Game.SceneName})");
                    __instance.Release();
                }
            }
            catch (Exception e) { Log.Exception("decoupling", e); }
        }

        private static void Healed(int frames)
        {
            _healed++;
            if (!Log.VerboseOn || Time.unscaledTime < _nextSummary) return;
            _nextSummary = Time.unscaledTime + SummarySeconds;
            Log.Verbose($"Decoupling: {_healed} gap spikes healed (last after {frames} frames)");
            _healed = 0;
        }
    }
}

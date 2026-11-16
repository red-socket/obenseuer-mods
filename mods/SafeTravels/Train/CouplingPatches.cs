using System;
using HarmonyLib;
using UnityEngine;

namespace SafeTravels
{
    // P3/P4 (S-021 a, spec §3.3-3.4, §4.5). Vanilla couples whenever two active coupler triggers touch below maxSpeed
    // (SRC/TrackTrainCouplerTrigger.cs:23-77): parked trains whose triggers overlap at load, and any contact while
    // reversing, since the speed check is signed. Coupling.Enabled keeps it to slow contact that the player causes.
    // TrainCoupler.Couple itself is never gated, so saved links (LateStart, OnLoadGame) and shop purchases are unchanged.
    internal static class CouplingRules
    {
        internal static bool Patched;

        internal enum Speed { Ok, VanillaCrash, TooFast, RaisedLimit }

        // Our limit (Coupling.MaxSpeed, or the trigger's own maxSpeed, 2.5 on every prefab) against both trains' speed
        // in either direction. VanillaCrash: vanilla's own forward over-speed branch will run (its CrashCooldown), so we
        // let it. RaisedLimit: within our raised limit but over the trigger's, so vanilla would refuse; we couple the
        // way vanilla's own branch does.
        internal static Speed Check(TrackTrainCouplerTrigger self, TrackTrain a, TrackTrain b)
        {
            float own = self.maxSpeed;
            float cfg = Cfg.Coupling.MaxSpeed.Value;
            float limit = cfg > 0f ? cfg : own;
            bool vanillaCrash = own > 0f && (a.currentSpeed > own || b.currentSpeed > own);
            if (limit > 0f && Mathf.Max(Mathf.Abs(a.currentSpeed), Mathf.Abs(b.currentSpeed)) > limit)
                return vanillaCrash ? Speed.VanillaCrash : Speed.TooFast;
            return vanillaCrash ? Speed.RaisedLimit : Speed.Ok;
        }

        // Vanilla's couple branch (:44-47, :73-76), for the raised limit only.
        internal static void CoupleLikeVanilla(TrackTrainCouplerTrigger self, TrackTrainCouplerTrigger other)
        {
            if (other.active && self.cooldown == null) other.parent.Couple(self.parent);
        }

        internal static string Speeds(TrackTrain a, TrackTrain b) => $"{a.currentSpeed:0.00} / {b.currentSpeed:0.00} m/s";
    }

    [HarmonyPatch(typeof(TrackTrainCouplerTrigger), "OnTriggerEnter", typeof(Collider))]
    internal static class CouplerEnterPatch
    {
        private static bool Prefix(TrackTrainCouplerTrigger __instance, Collider other, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || !Cfg.Coupling.Enabled.Value) return true;
                if (!__instance.active || __instance.parent == null || __instance.parent.coupledTo != null) return true;   // vanilla returns itself
                if (other == null) return true;
                var o = other.transform.GetComponent<TrackTrainCouplerTrigger>();
                if (o == null) return true;
                var a = __instance.parent.parent;
                var b = o.parent?.parent;
                if (a == null || b == null) return false;   // vanilla would throw here (the other train's Start has not run)
                if (!TrainWatch.CouplingAllowedNow(out string why))
                {
                    Log.Verbose($"Coupling: refused ({why}; {CouplingRules.Speeds(a, b)})");
                    return false;
                }
                var speed = CouplingRules.Check(__instance, a, b);
                if (speed == CouplingRules.Speed.VanillaCrash) return true;
                if (speed == CouplingRules.Speed.TooFast)
                {
                    Log.Verbose($"Coupling: refused (too fast, {CouplingRules.Speeds(a, b)})");
                    return false;
                }
                if (Cfg.Coupling.RequireDriving.Value && !TrainWatch.Deliberate(a, b))
                {
                    Log.Verbose($"Coupling: refused (nobody driving; {CouplingRules.Speeds(a, b)})");
                    return false;
                }
                Log.Verbose($"Coupling: allowed ({CouplingRules.Speeds(a, b)})");
                if (speed == CouplingRules.Speed.RaisedLimit)
                {
                    CouplingRules.CoupleLikeVanilla(__instance, o);
                    return false;
                }
                return true;   // vanilla checks the other trigger's active flag and the crash cooldown, then couples
            }
            catch (Exception e)
            {
                Log.Exception("coupler enter", e);
                return true;
            }
        }
    }

    // The lever path: Toggle() sets `test` when the player enables a coupler, and the next Stay with a touching trigger
    // couples. No driving rule here (the press is deliberate). This prefix runs every physics step for each overlapping
    // pair, so it leaves at once unless `test` is set.
    [HarmonyPatch(typeof(TrackTrainCouplerTrigger), "OnTriggerStay", typeof(Collider))]
    internal static class CouplerStayPatch
    {
        private static float _lastWaitLog = float.NegativeInfinity;

        private static bool Prefix(TrackTrainCouplerTrigger __instance, Collider other, bool __runOriginal)
        {
            try
            {
                if (!__runOriginal || !Cfg.Coupling.Enabled.Value || !Vanilla.CouplerTest(__instance)) return true;
                if (!__instance.active || __instance.parent == null || __instance.parent.coupledTo != null) return true;
                if (other == null) return true;
                var o = other.transform.GetComponent<TrackTrainCouplerTrigger>();
                if (o == null) return true;
                var a = __instance.parent.parent;
                var b = o.parent?.parent;
                if (a == null || b == null) return false;
                if (!TrainWatch.CouplingAllowedNow(out string why))
                {
                    // `test` stays set (it is never saved), so the trains couple after the grace if they still touch.
                    if (Time.unscaledTime - _lastWaitLog > 1f)
                    {
                        _lastWaitLog = Time.unscaledTime;
                        Log.Verbose($"Coupling: lever waits ({why})");
                    }
                    return false;
                }
                var speed = CouplingRules.Check(__instance, a, b);
                if (speed == CouplingRules.Speed.VanillaCrash) return true;
                if (speed == CouplingRules.Speed.TooFast)
                {
                    Vanilla.CouplerTest(__instance) = false;   // vanilla clears it before its own speed check (:59)
                    Log.Verbose($"Coupling: lever refused (too fast, {CouplingRules.Speeds(a, b)})");
                    return false;
                }
                if (speed == CouplingRules.Speed.RaisedLimit)
                {
                    Vanilla.CouplerTest(__instance) = false;
                    CouplingRules.CoupleLikeVanilla(__instance, o);
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Log.Exception("coupler stay", e);
                return true;
            }
        }
    }
}

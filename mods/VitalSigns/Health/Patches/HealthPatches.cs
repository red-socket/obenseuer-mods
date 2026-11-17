using System;
using System.Reflection;
using HarmonyLib;
using OS.Items;

namespace VitalSigns
{
    // Context hooks of the damage log (spec §4): a prefix that pushes a frame and returns the depth (+1) in __state, and
    // a void finalizer that accounts and truncates the stack back to it. The finalizer always runs and lets any exception
    // through unchanged; nothing here returns false or writes a game value. Every body falls back to doing nothing.

    // H1: PlayerStatsHealth.Damage(float, DamageTypeNames, bool). The method forces silent for spores behind a mask
    // (SRC/PlayerStatsHealth.cs:103-106), so that is read here. Coexists with Time Keeper's read-only prefix/postfix.
    [HarmonyPatch]
    internal static class DamagePatch
    {
        private static MethodBase TargetMethod() => Vanilla.Damage;

        private static void Prefix(DamageTypeNames reason, bool silent, out int __state)
        {
            __state = 0;
            try
            {
                if (!Tracker.Live) return;
                bool eff = silent;
                if (!eff && reason == DamageTypeNames.Mushrooms)
                {
                    var face = FaceSlotController.instance;
                    eff = face != null && face.HasFacemaskEquipped();
                }
                __state = Tracker.Enter(new Frame { Kind = K.Damage, Reason = (short)reason, Silent = eff, Asleep = Game.Asleep });
            }
            catch (Exception e) { Log.Exception("Damage prefix", e); }
        }

        private static void Finalizer(int __state)
        {
            try { Tracker.Exit(__state); }
            catch (Exception e) { Log.Exception("Damage finalizer", e); }
        }
    }

    // H2: PlayerStatsHealth.UpdateStats(). The finalizer splits the tick exactly (Tracker.Split, spec §5.3).
    [HarmonyPatch]
    internal static class HealthTickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HealthUpdateStats;

        private static void Prefix(out TickState __state)
        {
            __state = default;
            try { __state = Tracker.TickStart(); }
            catch (Exception e) { Log.Exception("health tick prefix", e); }
        }

        private static void Finalizer(TickState __state)
        {
            try { Tracker.TickDone(__state); }
            catch (Exception e) { Log.Exception("health tick finalizer", e); }
        }
    }

    // H3: PlayerStatsHealth.UpdateValues(ItemConsumable), inside the consume call. Stores the fungal share of GetHealth on
    // H4's Item frame, or pushes "Food or drink" when the log-items group is off.
    [HarmonyPatch]
    internal static class HealthValuesPatch
    {
        private static MethodBase TargetMethod() => Vanilla.HealthUpdateValues;

        private static void Prefix(ItemConsumable itemConsumable, out int __state)
        {
            __state = 0;
            try
            {
                if (!Tracker.Live) return;
                float smv = Tracker.SmvShare(itemConsumable);
                if (Tracker.ItemsOn && Ctx.Depth > 0 && Ctx.Top.Kind == K.Item)
                {
                    Tracker.Account();
                    Ctx.Top.Smv = smv;
                    __state = Ctx.Depth + 1;   // Exit accounts and truncates to the current depth, keeping H4's frame
                    return;
                }
                __state = Tracker.Enter(new Frame { Kind = K.Item, Id = Ctx.Intern(Strings.FoodOrDrink), Smv = smv });
            }
            catch (Exception e) { Log.Exception("UpdateValues (health) prefix", e); }
        }

        private static void Finalizer(int __state)
        {
            try { Tracker.Exit(__state); }
            catch (Exception e) { Log.Exception("UpdateValues (health) finalizer", e); }
        }
    }

    // H4's state: the frame depth and the caffeine readings before the item.
    internal struct ItemState
    {
        internal int Depth;
        internal float Tta0, Cof0;
    }

    // H4: PlayerStats.UpdateValues(ItemData, ItemConsumable). Names the item (liquid-aware, GetUsedItem is pure) and
    // measures the caffeine it queued. Covers UseItemDirectly, PlayerAutoConsume (Fewer Clicks' Quick Eat) and the
    // breakdown auto-consume. The body is skipped when StatsProgressDisabled, so the deltas are then 0.
    [HarmonyPatch]
    internal static class StatsValuesPatch
    {
        private static MethodBase TargetMethod() => Vanilla.StatsUpdateValues;

        private static void Prefix(PlayerStats __instance, ItemData itemData, out ItemState __state)
        {
            __state = default;
            try
            {
                if (!Tracker.Live) return;
                Item it = null;
                try { it = itemData != null ? itemData.GetUsedItem(out _, out _) : null; }
                catch (Exception) { it = null; }
                __state.Tta0 = __instance.tiredness != null ? __instance.tiredness.tirednessToAdd : 0f;
                __state.Cof0 = Coffee(__instance);
                __state.Depth = Tracker.Enter(new Frame { Kind = K.Item, Id = Ctx.ItemLabel(it) });
            }
            catch (Exception e) { Log.Exception("UpdateValues prefix", e); }
        }

        private static void Finalizer(PlayerStats __instance, ItemState __state)
        {
            try
            {
                if (__state.Depth <= 0) return;
                float tta1 = __instance.tiredness != null ? __instance.tiredness.tirednessToAdd : 0f;
                float units = Math.Max(0f, __state.Tta0 - tta1) + Math.Max(0f, Coffee(__instance) - __state.Cof0);
                if (units > 0f && Ctx.Depth >= __state.Depth)
                {
                    var f = Ctx.At(__state.Depth - 1);
                    if (f.Kind == K.Item) CaffeineList.Add(Ctx.Label(f.Id), units);
                }
                Tracker.Exit(__state.Depth);
            }
            catch (Exception e) { Log.Exception("UpdateValues finalizer", e); }
        }

        private static float Coffee(PlayerStats ps) =>
            ps.unprocessedSpecialEffects != null && ps.unprocessedSpecialEffects.TryGetValue(ItemConsumable.SpecialEffect.Coffee, out var v) ? v : 0f;
    }

    // H5: PlayerStatsTiredness.UpdateStats(int). The only pool write in the method itself is the caffeine overdose
    // (SRC/PlayerStatsTiredness.cs:51-56); waits run inside it (OnWait) and get their own frame from H6.
    [HarmonyPatch]
    internal static class TirednessTickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.TirednessUpdateStats;

        private static void Prefix(out int __state)
        {
            __state = 0;
            try { if (Tracker.Live) __state = Tracker.Enter(new Frame { Kind = K.TirednessTick }); }
            catch (Exception e) { Log.Exception("tiredness tick prefix", e); }
        }

        private static void Finalizer(int __state)
        {
            try
            {
                Tracker.Exit(__state);
                if (__state > 0) CaffeineList.ClearIfSpent();
            }
            catch (Exception e) { Log.Exception("tiredness tick finalizer", e); }
        }
    }

    // H6: InteractableWait.OnWaiting(int), the declared base (InteractableWaitResident calls base). Keeps Boxing Job
    // damage and job pool effects off the caffeine overdose.
    [HarmonyPatch]
    internal static class WaitPatch
    {
        private static MethodBase TargetMethod() => Vanilla.WaitOnWaiting;

        private static void Prefix(InteractableWait __instance, out int __state)
        {
            __state = 0;
            try { if (Tracker.Live) __state = Tracker.Enter(new Frame { Kind = K.Wait, Id = Ctx.WaitLabel(__instance) }); }
            catch (Exception e) { Log.Exception("OnWaiting prefix", e); }
        }

        private static void Finalizer(int __state)
        {
            try { Tracker.Exit(__state); }
            catch (Exception e) { Log.Exception("OnWaiting finalizer", e); }
        }
    }

    // H7: PlayerStatsMushrooms.UpdateStats(int). The only health write inside is the cap HP = 100 - SMV
    // (SRC/PlayerStatsMushrooms.cs:72-75).
    [HarmonyPatch]
    internal static class SmvTickPatch
    {
        private static MethodBase TargetMethod() => Vanilla.MushroomsUpdateStats;

        private static void Prefix(out int __state)
        {
            __state = 0;
            try { if (Tracker.Live) __state = Tracker.Enter(new Frame { Kind = K.SmvTick }); }
            catch (Exception e) { Log.Exception("SMV tick prefix", e); }
        }

        private static void Finalizer(int __state)
        {
            try { Tracker.Exit(__state); }
            catch (Exception e) { Log.Exception("SMV tick finalizer", e); }
        }
    }

    // H8: Death.PlayerDeath(DamageTypeNames). Runs inside Damage or a stats tick, before the killing blow is accounted,
    // so the summary is only noted here and built on the next Update (Alerts.Tick). Godmode never reaches this method.
    [HarmonyPatch]
    internal static class DeathPatch
    {
        private static MethodBase TargetMethod() => Vanilla.PlayerDeath;

        private static void Prefix(Death __instance, DamageTypeNames reason)
        {
            try
            {
                if (Tracker.Live && __instance != null && !__instance.dead) Alerts.PendingDeath(reason);
            }
            catch (Exception e) { Log.Exception("PlayerDeath prefix", e); }
        }
    }
}

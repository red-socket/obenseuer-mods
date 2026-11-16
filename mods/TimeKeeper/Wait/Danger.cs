using System;
using NPC;
using UnityEngine;

namespace TimeKeeper
{
    // The "combat" part of the wait gate (spec §0, §6.6 rows 9-10). The game has no combat state, so danger is: police
    // pursuing the player, a robber chasing or attacking, or recent damage from a hazard. P6 records the damage time;
    // the NPC scan only runs on a key press and at start, never every frame.
    internal static class Danger
    {
        internal static bool Patched;

        // Unscaled time of the last hazard damage; far in the past until the player is hurt.
        internal static float LastHurt = -10000f;

        internal enum Threat { None, Police, Robber }

        // Hazards someone or something does to the player. Needs and item effects (hunger, thirst, bleeding,
        // withdrawal, radiation, mushrooms, SMV, poison, liquids, seizures, unspecified) are left to the game's own
        // need checks: bleeding over 33 already refuses and ends a wait through CanSleep.
        internal static bool IsHazard(DamageTypeNames reason)
        {
            switch (reason)
            {
                case DamageTypeNames.Violence:
                case DamageTypeNames.Gun:
                case DamageTypeNames.Explosion:
                case DamageTypeNames.Fire:
                case DamageTypeNames.Fall:
                case DamageTypeNames.Force:
                case DamageTypeNames.Drown:
                case DamageTypeNames.Gass:
                case DamageTypeNames.Electric:
                    return true;
                default:
                    return false;
            }
        }

        // Hurt within DangerSeconds. Inert when P6 is not patched.
        internal static bool RecentlyHurt()
        {
            if (!Patched) return false;
            int secs = Cfg.Wait.DangerSeconds.Value;
            return secs > 0 && Time.unscaledTime - LastHurt < secs;
        }

        // Police pursuing the player, or a robber chasing or aggroing them, in the current level. Police win: their note
        // is the more specific one. Same scan as Crime.StopPolicePursuits (SRC/Crime.cs:344-358), on the public
        // NPCDirector.globalDatabase.
        internal static Threat ScanNpcs()
        {
            var db = NPCDirector.globalDatabase;
            if (db == null) return Threat.None;
            bool robber = false;
            for (int i = 0; i < db.Count; i++)
            {
                var act = db[i]?.state?.CurrentActivity;
                if (act == null) continue;
                try
                {
                    if (act is Act_Police p)
                    {
                        if (p.IsPursuingPlayer && p.InCurrentLevel()) return Threat.Police;
                    }
                    else if (!robber && act is Act_Robber r)
                    {
                        if ((r.IsChasingPlayer() || r.IsAggroingPlayer()) && r.InCurrentLevel()) robber = true;
                    }
                }
                catch (Exception e) { Log.Exception("npc scan entry", e); }
            }
            return robber ? Threat.Robber : Threat.None;
        }
    }

    // P6: PlayerStatsHealth.Damage(float, DamageTypeNames, bool) (SRC/PlayerStatsHealth.cs:91), prefix and postfix.
    // Records the time only; changes no game state. A hurt counts only when health actually went down: Damage returns
    // early without touching health for gas while a charged gas mask is worn (:117-119) and for some mushroom cases,
    // and a gas TriggerHurt calls it every game second while the player stands inside (SRC/TriggerHurt.cs:161).
    [HarmonyLib.HarmonyPatch]
    internal static class DamagePatch
    {
        private static System.Reflection.MethodBase TargetMethod() => Vanilla.Damage;

        private static void Prefix(PlayerStatsHealth __instance, out float __state)
        {
            __state = __instance != null ? __instance.currentStatus : 0f;
        }

        private static void Postfix(PlayerStatsHealth __instance, float damage, DamageTypeNames reason, float __state)
        {
            if (damage > 0f && __instance != null && __instance.currentStatus < __state && Danger.IsHazard(reason))
                Danger.LastHurt = Time.unscaledTime;
        }
    }
}

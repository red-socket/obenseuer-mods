using System.Collections.Generic;
using HarmonyLib;
using Steamworks;

namespace GameHarness
{
    // A harness session must never touch the player's Steam account. The game only stops achievements while the real F1
    // console is open (Achievements.globalDisable); the harness runs console commands without opening it, so add_crime
    // and friends unlocked real achievements. Once the session is armed (first command), two layers block everything:
    // - the game's three Achievements.TriggerAchievement overloads, which every unlock and progress path goes through
    //   (82 call sites in SRC, ItemAchievementList included), so the test save does not record unlocks either;
    // - every write on Steamworks' SteamUserStats (the only Steam stats/achievement API the game calls, from
    //   Achievements.cs), as a backstop for any path that skips the first layer, mods included.
    internal static class AchievementGuard
    {
        internal static int Blocked;
        internal static readonly Dictionary<string, int> ByName = new Dictionary<string, int>();

        // Unlocks and Steam writes are logged every time; progress updates (which can repeat often) on the first one per name.
        internal static void Note(string name, string what, bool always)
        {
            Blocked++;
            ByName.TryGetValue(name, out int n);
            ByName[name] = n + 1;
            if (always || n == 0)
                Plugin.Log.LogWarning($"Blocked {what} '{name}' (harness session: no achievements or Steam stats).");
        }
    }

    [HarmonyPatch(typeof(Achievements), nameof(Achievements.TriggerAchievement), typeof(string))]
    internal static class TriggerAchievementPatch
    {
        private static bool Prefix(string name)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(name, "achievement unlock", always: true);
            return false;
        }
    }

    [HarmonyPatch(typeof(Achievements), nameof(Achievements.TriggerAchievement), typeof(string), typeof(float), typeof(bool))]
    internal static class TriggerAchievementFloatPatch
    {
        private static bool Prefix(string name)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(name, "achievement progress", always: false);
            return false;
        }
    }

    [HarmonyPatch(typeof(Achievements), nameof(Achievements.TriggerAchievement), typeof(string), typeof(int), typeof(bool))]
    internal static class TriggerAchievementIntPatch
    {
        private static bool Prefix(string name)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(name, "achievement progress", always: false);
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.SetAchievement))]
    internal static class SteamSetAchievementPatch
    {
        private static bool Prefix(string pchName, ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(pchName, "Steam SetAchievement", always: true);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.ClearAchievement))]
    internal static class SteamClearAchievementPatch
    {
        private static bool Prefix(string pchName, ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(pchName, "Steam ClearAchievement", always: true);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.IndicateAchievementProgress))]
    internal static class SteamIndicateProgressPatch
    {
        private static bool Prefix(string pchName, ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(pchName, "Steam IndicateAchievementProgress", always: true);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.SetStat), typeof(string), typeof(int))]
    internal static class SteamSetStatIntPatch
    {
        private static bool Prefix(string pchName, ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(pchName, "Steam SetStat", always: true);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.SetStat), typeof(string), typeof(float))]
    internal static class SteamSetStatFloatPatch
    {
        private static bool Prefix(string pchName, ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(pchName, "Steam SetStat", always: true);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.UpdateAvgRateStat))]
    internal static class SteamUpdateAvgRateStatPatch
    {
        private static bool Prefix(string pchName, ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note(pchName, "Steam UpdateAvgRateStat", always: true);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.StoreStats))]
    internal static class SteamStoreStatsPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note("(all)", "Steam StoreStats", always: false);
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(SteamUserStats), nameof(SteamUserStats.ResetAllStats))]
    internal static class SteamResetAllStatsPatch
    {
        private static bool Prefix(ref bool __result)
        {
            if (!Plugin.SessionActive) return true;
            AchievementGuard.Note("(all)", "Steam ResetAllStats", always: true);
            __result = false;
            return false;
        }
    }
}

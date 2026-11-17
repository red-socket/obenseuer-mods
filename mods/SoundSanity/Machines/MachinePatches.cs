using System;
using System.Reflection;
using HarmonyLib;

namespace SoundSanity
{
    // M1: CraftingBase.Start() (SRC/CraftingBase.cs:466), protected virtual and declared on CraftingBase; every station
    // subclass calls base.Start(), so one postfix sees them all.
    [HarmonyPatch]
    internal static class CraftingBaseStartPatch
    {
        private static MethodBase TargetMethod() => Vanilla.CraftingBaseStart;

        private static void Postfix(CraftingBase __instance)
        {
            if (!Groups.Machines.On) return;
            try { Stations.OnStart(__instance); }
            catch (Exception e) { Groups.Machines.Fail(e); }
        }
    }
}

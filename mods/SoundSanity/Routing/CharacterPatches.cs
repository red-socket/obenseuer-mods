using System;
using System.Reflection;
using HarmonyLib;

namespace SoundSanity
{
    // R6 (opt-in, Routing.FixCharacterSounds): residents and NPCs whose prefabs carry groups of the mixer copy (voices,
    // footsteps). Duplicate-only sweeps of the character's subtree; null groups on characters (bark voices) are never
    // touched. Patched with routing; the bodies return at once while the option is off.
    internal static class CharacterPatches
    {
        internal static void Sweep(UnityEngine.GameObject go, string where)
        {
            if (go == null || !Cfg.Routing.FixCharacterSounds.Value || !Groups.Characters.On || !Router.Active) return;
            Sweeps.SweepRoot(go, true, where);
        }
    }

    // TenementApartmentPrefabInfo.SpawnResidents (SRC/TenementApartmentPrefabInfo.cs:338); the spawned residents are in
    // its private npcList.
    [HarmonyPatch]
    internal static class SpawnResidentsPatch
    {
        private static MethodBase TargetMethod() => Vanilla.SpawnResidents;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(TenementApartmentPrefabInfo __instance)
        {
            if (__instance == null || !Cfg.Routing.FixCharacterSounds.Value || !Groups.Characters.On) return;
            try
            {
                var list = Vanilla.NpcList(__instance);
                if (list == null) return;
                foreach (var go in list) CharacterPatches.Sweep(go, "resident");
            }
            catch (Exception e) { Groups.Characters.Fail(e); }
        }
    }

    // NPCManager.AddNpcToList (SRC/NPCManager.cs:119): every NPC that joins the scene.
    [HarmonyPatch]
    internal static class AddNpcPatch
    {
        private static MethodBase TargetMethod() => Vanilla.AddNpcToList;

        [HarmonyPriority(Priority.Low)]
        private static void Postfix(NPCInfo npc)
        {
            if (npc == null || !Cfg.Routing.FixCharacterSounds.Value || !Groups.Characters.On) return;
            try { CharacterPatches.Sweep(npc.gameObject, "NPC"); }
            catch (Exception e) { Groups.Characters.Fail(e); }
        }
    }
}

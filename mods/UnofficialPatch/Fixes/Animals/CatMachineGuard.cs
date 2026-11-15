using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-058 owned cats and machines (spec §11b; OFF by default, #5978). TriggerHurt.Damage(int, Stats) (SRC/TriggerHurt.cs:138)
    // is how crafting machines with a hurt volume (the Small Jaw Crusher) kill whatever walks in. The prefix skips the
    // damage when the victim is a player-owned cat (item 15650 "Your Cat", tame, or following the player; checked
    // before damage because Animal.OnHurt clears tame, :797) and the TriggerHurt sits under a CraftingBase. The furnace
    // half of the report is dropped: Brick Furnace, Gas Furnace and Electric Kiln have no TriggerHurt and nothing deals
    // Electric damage in code. Stateless.
    internal static class CatMachineGuard
    {
        internal static bool Patched;

        private const int OwnedCatItemId = 15650;

        private static readonly Dictionary<int, bool> OnMachine = new Dictionary<int, bool>();
        private static readonly HashSet<int> Logged = new HashSet<int>();

        // Returns false to skip the damage.
        internal static bool Allow(TriggerHurt th, Stats stats)
        {
            if (!Patched || !Cfg.ProtectOwnedCatsFromMachines.Value || th == null) return true;
            if (!(stats is AnimalStats)) return true;
            var cat = stats.GetComponentInParent<Cat>();
            if (cat == null || !Owned(cat, stats)) return true;
            if (!IsMachine(th)) return true;
            if (Compat.Foreign(Vanilla.TriggerHurtDamage, "the owned-cat guard")) return true;
            if (Logged.Add(cat.GetInstanceID()))
                Log.Info($"Cats: kept your cat {cat.gameObject.name} from being hurt by {MachineResidue.Name(th.GetComponentInParent<CraftingBase>())}.");
            return false;
        }

        private static bool Owned(Cat cat, Stats stats)
        {
            if (cat.tame) return true;
            var ci = stats.GetComponent<CollectibleItem>();
            if (ci == null) ci = cat.GetComponent<CollectibleItem>();
            if (ci != null && ci.Item != null && ci.Item.ID == OwnedCatItemId) return true;
            var ac = AnimalController.instance;
            return ac != null && ac.followingAnimals != null && ac.followingAnimals.Contains(cat);
        }

        private static bool IsMachine(TriggerHurt th)
        {
            int id = th.GetInstanceID();
            if (!OnMachine.TryGetValue(id, out bool on))
            {
                on = th.GetComponentInParent<CraftingBase>() != null;
                OnMachine[id] = on;
            }
            return on;
        }

        internal static void OnLoadingDone()
        {
            OnMachine.Clear();
            Logged.Clear();
        }
    }

    // TriggerHurt.Damage(int, Stats), private.
    [HarmonyPatch]
    internal static class TriggerHurtDamagePatch
    {
        private static MethodBase TargetMethod() => Vanilla.TriggerHurtDamage;

        private static bool Prefix(TriggerHurt __instance, Stats stats)
        {
            try { return CatMachineGuard.Allow(__instance, stats); }
            catch (Exception e)
            {
                Log.Exception("Cats: machine guard", e);
                return true;
            }
        }
    }
}

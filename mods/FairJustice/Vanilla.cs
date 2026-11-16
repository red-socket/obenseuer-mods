using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace FairJustice
{
    // Private game members, read through Harmony's AccessTools rather than the publicized reference:
    // docs/modding-guide.md §4 notes that Mono's tolerance for direct private access was never tested.
    // A member that is missing logs one error and stays null; the features using it check and stay off (the pass-out
    // guard keeps its in-jail case and loses only the release-during-sleep case without comingSleepEvent).
    //
    // Never call Prison.InPrison(), AskForSentenceTime(), GetSentenceTime(), Crime.GetSentenceLengthInHours() or
    // TimeOfDayAzure.GetCurrentTime(..., false): they all Debug.Log on every call (spec §2.1).
    internal static class Vanilla
    {
        // Prison (SRC/Prison.cs:39-87)
        private static readonly AccessTools.FieldRef<Prison, bool> PrisonInPrison = Ref<Prison, bool>("inPrison");
        private static readonly AccessTools.FieldRef<Prison, int> PrisonSeconds = Ref<Prison, int>("prisonInSeconds");
        private static readonly AccessTools.FieldRef<Prison, Prison.Cell> PrisonActiveCell = Ref<Prison, Prison.Cell>("activeCell");
        internal static readonly AccessTools.FieldRef<Prison, Transform> OverflowPackagePoint = Ref<Prison, Transform>("overflowPackagePoint");
        internal static readonly AccessTools.FieldRef<Prison, GameObject> OverflowPackagePrefab = Ref<Prison, GameObject>("overflowPackagePrefab");

        // WaitingController (SRC/WaitingController.cs:34): the event rolled for the current sleep, null otherwise.
        private static readonly AccessTools.FieldRef<WaitingController, SleepEvent> ComingSleepEventRef =
            Ref<WaitingController, SleepEvent>("comingSleepEvent");

        // Crime.StopPolicePursuits() (SRC/Crime.cs:346), private; runs only in GoToPrison/TeleportToPrison in vanilla.
        internal static readonly Action<Crime> StopPolicePursuits = Delegate<Action<Crime>>(typeof(Crime), "StopPolicePursuits");

        // SleepEventController.RemoveStolenGoods() (SRC/SleepEventController.cs:509), private. Calling it runs our
        // P13 prefix (backpack rescue), which is intended.
        internal static readonly Action<SleepEventController> RemoveStolenGoods =
            Delegate<Action<SleepEventController>>(typeof(SleepEventController), "RemoveStolenGoods");

        // ItemOperations.SpawnItemToPosition(ItemStack, Vector3, RaycastHit, bool) (SRC/ItemOperations.cs:358), public.
        // Called by reflection (written before the mod referenced UnityEngine.PhysicsModule, where RaycastHit lives); a null
        // argument passes a zeroed RaycastHit, the same as vanilla's `default` (Crime.cs:503).
        private static readonly MethodInfo SpawnItemToPositionM = Method(typeof(ItemOperations), "SpawnItemToPosition");
        internal static bool CanSpawnItem => SpawnItemToPositionM != null;

        internal static void SpawnItemToPosition(ItemStack st, Vector3 pos, bool noExpirationTimer) =>
            SpawnItemToPositionM.Invoke(null, new object[] { st, pos, null, noExpirationTimer });

        // CollectibleItem.GetOrCreateItemData() (SRC/CollectibleItem.cs:394-405), private: the world item's ItemData, which
        // vanilla's own prompt passes to ItemAction.CanUse (:660). P25 uses it the same way.
        internal static readonly Func<CollectibleItem, ItemData> GetOrCreateItemDataF =
            Delegate<Func<CollectibleItem, ItemData>>(typeof(CollectibleItem), "GetOrCreateItemData");

        // Police limits (U-014): Crime.activePolices (SRC/Crime.cs:82, private; off-scene officers are in it too) and
        // Activity._NPC (SRC/NPC/Activity.cs:11, protected; null while the officer is off-scene).
        private static readonly AccessTools.FieldRef<Crime, List<NPC.Act_Police>> ActivePolicesRef =
            Ref<Crime, List<NPC.Act_Police>>("activePolices");
        private static readonly AccessTools.FieldRef<NPC.Activity, NPCController> ActivityNpcRef =
            Ref<NPC.Activity, NPCController>("_NPC");
        internal static bool HasPolice => ActivePolicesRef != null && ActivityNpcRef != null;

        // Door.direction (SRC/Door.cs:89), private: false while closing (with moving). Missing: a locked door that is
        // swinging shut is not counted as closed by the locked-door stop (the rest of it still works).
        private static readonly AccessTools.FieldRef<Door, bool> DoorDirectionRef = Ref<Door, bool>("direction");
        internal static bool DoorClosing(Door d) => DoorDirectionRef != null && d != null && d.moving && !DoorDirectionRef(d);
        internal static List<NPC.Act_Police> ActivePolices(Crime c) => ActivePolicesRef != null && c != null ? ActivePolicesRef(c) : null;
        internal static NPCController ActivityNpc(NPC.Activity a) => ActivityNpcRef != null && a != null ? ActivityNpcRef(a) : null;

        internal static bool InPrison(Prison p) => PrisonInPrison != null && p != null && PrisonInPrison(p);
        internal static int PrisonInSeconds(Prison p) => PrisonSeconds != null && p != null ? PrisonSeconds(p) : 0;
        internal static bool HasSeconds => PrisonSeconds != null;
        internal static Prison.Cell ActiveCell(Prison p) => PrisonActiveCell != null && p != null ? PrisonActiveCell(p) : null;
        internal static bool HasActiveCell => PrisonActiveCell != null;

        internal static SleepEvent ComingSleepEvent(WaitingController w) =>
            ComingSleepEventRef != null && w != null ? ComingSleepEventRef(w) : null;

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Log.Error($"{typeof(T).Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }

        private static MethodInfo Method(Type t, string name)
        {
            try
            {
                var m = AccessTools.Method(t, name);
                if (m == null) Log.Error($"{t.Name}.{name} not found; the feature using it is off.");
                return m;
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }

        private static D Delegate<D>(Type t, string name) where D : Delegate
        {
            try
            {
                var m = AccessTools.Method(t, name, Type.EmptyTypes);
                if (m == null)
                {
                    Log.Error($"{t.Name}.{name} not found; the feature using it is off.");
                    return null;
                }
                return AccessTools.MethodDelegate<D>(m, null, false);
            }
            catch (Exception e) { Log.Error($"{t.Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }
    }
}

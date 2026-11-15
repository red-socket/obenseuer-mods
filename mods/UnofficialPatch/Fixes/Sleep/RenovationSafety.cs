using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace UnofficialPatch
{
    // S-102 bed safety during a wall renovation (spec §13, narrowed by the user's correction of 2026-11-15).
    // Bed safety in the player's apartment is designed progression: the walls upgrade carries a TriggerSafe volume
    // named "Safe_trigger_enter" (Upgrade_PLAYER_Walls_(lvl 0) Bare: safety factor 0.3; lvl 1 Makeshift: 0.6;
    // lvl 2 Shabby to lvl 4 Fine: safe), and TriggerSafe.OnTriggerEnter (SRC/TriggerSafe.cs:17-40) writes it into
    // SleepEventController.playerInSafeArea / _playerSafetyFactor, which Sleep.Use (:179), Sleep.GetTitle (:233-235)
    // and SleepEventController.CalculateEvent read. The mod never changes that progression.
    // The case handled (optional, OFF by default; design.md D-1): while the walls are renovated,
    // TenementApartmentPrefabsContent.GetWall (:425-431) returns the under-construction prefab
    // (Upgrade_NPC_LARGE_Walls - Under_Construction*), which has no TriggerSafe at all, so the two fields keep whatever
    // the last trigger crossed or the save left. Whether the game means the flat to be unprotected during the work is
    // not known, so this is an option, not a fix. While it is on, and only for beds in the player's apartment
    // (extensions included), the three readers see what the finished walls' enter trigger would write: its base
    // value plus the addSafety of active TriggerSafeAdders (Teddy bear camera, 0.1) inside its boxes, so never more
    // than the finished walls. The values are applied in a prefix and restored in a finalizer: nothing new is ever
    // saved. The cases the user named (an extension bed outside the volume, #6320; a stale value after a teleport or
    // load outside a renovation) are not real in the 0.5.19 code and scene data (design.md S-102 "Dropped").
    internal static class RenovationSafety
    {
        internal static bool Patched;

        internal struct SafeState
        {
            internal bool Applied;
            internal bool InSafe;
            internal float Factor;
        }

        private const string EnterName = "Safe_trigger_enter";

        // Per-scene cache of the finished tier, keyed by the scene, the shown wall object and the completed tier.
        private static bool _cacheValid;
        private static int _cacheScene;
        private static int _cacheWallObject;
        private static UpgradeType.ApartmentWallType _cacheTier;
        private static TriggerSafe _tier;
        private static Matrix4x4[] _toLocal;
        private static Vector3[] _center, _half;

        private static readonly Dictionary<int, bool> BedInApartment = new Dictionary<int, bool>();

        // Sum of the adders inside the boxes, refreshed at most once a second (GetTitle runs every frame on hover).
        private static float _adderSum;
        private static float _adderTime = -10f;
        private static bool _loggedForCache;

        internal static Sleep PendingBed;

        internal static bool Live => Patched && Cfg.KeepWallSafetyDuringRenovation.Value;

        // The finished walls' enter trigger while the player's walls are being renovated and no live one exists.
        internal static bool TryGetTier(out TriggerSafe tier)
        {
            tier = null;
            var tc = TenementController.instance;
            if (tc == null) return false;
            var apt = tc.GetApartmentFromNumber(1);
            if (apt == null || apt.wallProgress == null) return false;
            var p = apt.prefabs != null ? apt.prefabs : apt.tenementApartmentPrefabs;
            if (p == null || p.outside || p.content == null) return false;

            var shown = p.content.currentWall;
            int scene = p.gameObject.scene.handle;
            int shownId = shown != null ? shown.GetInstanceID() : 0;
            if (!_cacheValid || scene != _cacheScene || shownId != _cacheWallObject || apt.currentWall != _cacheTier)
                Build(p, apt, shown, scene, shownId);
            tier = _tier;
            return tier != null;
        }

        private static void Build(TenementApartmentPrefabs p, TenementApartment apt, GameObject shown, int scene, int shownId)
        {
            _cacheValid = true;
            _cacheScene = scene;
            _cacheWallObject = shownId;
            _cacheTier = apt.currentWall;
            _tier = null;
            _toLocal = null;
            _adderTime = -10f;
            _loggedForCache = false;

            // A live enter trigger exists (for example the renovation placeholder went to another category): vanilla is right.
            if (shown != null && FindEnter(shown) != null) return;

            var tiers = p.content.GetTierPrefabs(TenementController.ApartmentUpgradeCategory.Walls);   // allocates; cached
            int i = (int)apt.currentWall;
            if (tiers == null || i < 0 || i >= tiers.Length || tiers[i] == null) return;
            var enter = FindEnter(tiers[i]);
            if (enter == null) return;

            // Instantiate(prefab, parent) keeps the prefab root's local pose, so a box's world matrix is
            // parent.localToWorld * (prefab asset's own localToWorld).
            var boxes = enter.GetComponents<BoxCollider>();
            _toLocal = new Matrix4x4[boxes.Length];
            _center = new Vector3[boxes.Length];
            _half = new Vector3[boxes.Length];
            for (int b = 0; b < boxes.Length; b++)
            {
                var world = p.transform.localToWorldMatrix * boxes[b].transform.localToWorldMatrix;
                _toLocal[b] = world.inverse;
                _center[b] = boxes[b].center;
                _half[b] = boxes[b].size * 0.5f;
            }
            _tier = enter;
        }

        private static TriggerSafe FindEnter(GameObject root)
        {
            foreach (var t in root.GetComponentsInChildren<TriggerSafe>(true))
                if (t != null && t.gameObject.name == EnterName) return t;
            return null;
        }

        private static bool PlayerInsideEnterBoxes()
        {
            var c = OpenSewerCharacterController.instance;
            return c != null && InsideEnterBoxes(c.transform.position);
        }

        // The bed belongs to the PlayerApartment furniture area (extensions included), or the player stands inside the
        // finished walls' enter volume.
        private static bool Qualifies(Sleep bed)
        {
            if (bed == null) return false;
            int id = bed.GetInstanceID();
            if (!BedInApartment.TryGetValue(id, out bool inApt))
            {
                var fp = bed.GetComponentInParent<FurniturePlaceable>();
                inApt = fp != null && fp.manager != null && fp.manager.area == Furniture.BuildingArea.PlayerApartment;
                BedInApartment[id] = inApt;
            }
            return inApt || PlayerInsideEnterBoxes();
        }

        private static bool InsideEnterBoxes(Vector3 pos)
        {
            if (_toLocal == null) return false;
            for (int b = 0; b < _toLocal.Length; b++)
            {
                var q = _toLocal[b].MultiplyPoint3x4(pos) - _center[b];
                if (Mathf.Abs(q.x) <= _half[b].x && Mathf.Abs(q.y) <= _half[b].y && Mathf.Abs(q.z) <= _half[b].z) return true;
            }
            return false;
        }

        // TriggerSafe.GetSafetyFactor adds each TriggerSafeAdder whose collider is inside the trigger. Counted here:
        // active adders under an enabled collider (the trigger reaches the adder through other.transform's children)
        // whose position is inside the finished walls' enter boxes. A Teddy bear camera in the backpack is inactive.
        private static float AdderSum()
        {
            float now = Time.realtimeSinceStartup;
            if (now - _adderTime < 1f) return _adderSum;
            _adderTime = now;
            float sum = 0f;
            foreach (var a in UnityEngine.Object.FindObjectsByType<TriggerSafeAdder>(FindObjectsSortMode.None))
            {
                if (a == null || !a.isActiveAndEnabled) continue;
                var col = a.GetComponentInParent<Collider>();
                if (col == null || !col.enabled) continue;
                if (InsideEnterBoxes(a.transform.position)) sum += a.addSafety;
            }
            _adderSum = sum;
            return sum;
        }

        // Writes what TriggerSafe.OnTriggerEnter would write for the finished tier.
        private static SafeState Apply(TriggerSafe tier)
        {
            var sec = SleepEventController.instance;
            if (sec == null || Vanilla.PlayerSafetyFactor == null) return default;
            var state = new SafeState { Applied = true, InSafe = sec.playerInSafeArea, Factor = Vanilla.PlayerSafetyFactor(sec) };
            if (tier.usesafetyfactor) sec.SetPlayerInSafe(tier.safetyFactor + AdderSum());
            else sec.SetPlayerInSafe(tier.safe);
            LogOnce(tier);
            return state;
        }

        internal static void Restore(SafeState s)
        {
            if (!s.Applied) return;
            var sec = SleepEventController.instance;
            if (sec == null || Vanilla.PlayerSafetyFactor == null) return;
            sec.playerInSafeArea = s.InSafe;
            Vanilla.PlayerSafetyFactor(sec) = s.Factor;
        }

        // Once per scene and tier (the cache key); GetTitle runs every frame while a bed is hovered.
        private static void LogOnce(TriggerSafe tier)
        {
            if (_loggedForCache) return;
            _loggedForCache = true;
            string what = tier.usesafetyfactor ? (tier.safetyFactor + _adderSum).ToString("0.00") : tier.safe ? "safe" : "unsafe";
            Log.Info($"Bed safety: walls are being renovated, so beds use the finished {_cacheTier} walls' safety ({what}).");
        }

        // Sleep.GetTitle / Sleep.Use.
        internal static SafeState ForBed(Sleep bed, bool use)
        {
            if (!Live || bed == null || bed.safe) return default;
            if (!TryGetTier(out var tier) || !Qualifies(bed)) return default;
            var s = Apply(tier);
            if (use && s.Applied) PendingBed = bed;
            return s;
        }

        // SleepEventController.CalculateEvent: the bed the player just used (within 3 m), or the player inside the volume.
        internal static SafeState ForEvent()
        {
            if (!Live || !TryGetTier(out var tier)) return default;
            bool ok = false;
            var c = OpenSewerCharacterController.instance;
            if (PendingBed != null && c != null && (PendingBed.transform.position - c.transform.position).sqrMagnitude <= 9f) ok = true;
            if (!ok) ok = PlayerInsideEnterBoxes();
            return ok ? Apply(tier) : default;
        }

        internal static void OnLoadingDone()
        {
            _cacheValid = false;
            _tier = null;
            _toLocal = null;
            _adderTime = -10f;
            PendingBed = null;
            BedInApartment.Clear();
        }
    }

    // Sleep.GetTitle(): the hover label ("Unsafe", "Safeish" or nothing) matches what sleeping will do.
    [HarmonyPatch(typeof(Sleep), nameof(Sleep.GetTitle))]
    internal static class SleepGetTitlePatch
    {
        private static void Prefix(Sleep __instance, out RenovationSafety.SafeState __state)
        {
            __state = default;
            try { __state = RenovationSafety.ForBed(__instance, false); }
            catch (Exception e) { Log.Exception("Bed safety: label", e); }
        }

        private static void Finalizer(RenovationSafety.SafeState __state)
        {
            try { RenovationSafety.Restore(__state); }
            catch (Exception e) { Log.Exception("Bed safety: label restore", e); }
        }
    }

    // Sleep.Use(): the pass-out events choice (:179).
    [HarmonyPatch(typeof(Sleep), nameof(Sleep.Use))]
    internal static class SleepUsePatch
    {
        private static void Prefix(Sleep __instance, out RenovationSafety.SafeState __state)
        {
            __state = default;
            try { __state = RenovationSafety.ForBed(__instance, true); }
            catch (Exception e) { Log.Exception("Bed safety: use", e); }
        }

        private static void Finalizer(RenovationSafety.SafeState __state)
        {
            try { RenovationSafety.Restore(__state); }
            catch (Exception e) { Log.Exception("Bed safety: use restore", e); }
        }
    }

    // SleepEventController.CalculateEvent(SleepEvent[]): the event roll.
    [HarmonyPatch(typeof(SleepEventController), nameof(SleepEventController.CalculateEvent), new[] { typeof(SleepEvent[]) })]
    internal static class CalculateEventPatch
    {
        private static void Prefix(out RenovationSafety.SafeState __state)
        {
            __state = default;
            try
            {
                __state = RenovationSafety.ForEvent();
                if (__state.Applied) Compat.Overlap(AccessTools.Method(typeof(SleepEventController), nameof(SleepEventController.CalculateEvent)),
                    "bed safety during renovation");
            }
            catch (Exception e) { Log.Exception("Bed safety: event roll", e); }
        }

        private static void Finalizer(RenovationSafety.SafeState __state)
        {
            RenovationSafety.PendingBed = null;
            try { RenovationSafety.Restore(__state); }
            catch (Exception e) { Log.Exception("Bed safety: event roll restore", e); }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NPC;
using UnityEngine;

namespace SafeTravels
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4, spec §3.10). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §2). Optional
    // members log one warning and the feature runs without the part that needs them.
    internal static class Vanilla
    {
        // ---- Toll (P1, P2) ----

        // InteractableMoney.Interact() / UpdateUseMessage() (SRC/InteractableMoney.cs:25, :51): declared overrides, no
        // subclasses in 0.5.19, so every coin slot and slot machine passes through them.
        internal static readonly MethodInfo MoneyInteract = Method(typeof(InteractableMoney), "Interact", Type.EmptyTypes);
        internal static readonly MethodInfo MoneyUseMessage = Method(typeof(InteractableMoney), "UpdateUseMessage", Type.EmptyTypes);

        // ---- Trains (P3-P9) ----

        // TrackTrainCouplerTrigger.OnTriggerEnter / OnTriggerStay (SRC/TrackTrainCouplerTrigger.cs:23, :50), private.
        internal static readonly MethodInfo CouplerEnter = Method(typeof(TrackTrainCouplerTrigger), "OnTriggerEnter", new[] { typeof(Collider) });
        internal static readonly MethodInfo CouplerStay = Method(typeof(TrackTrainCouplerTrigger), "OnTriggerStay", new[] { typeof(Collider) });

        // TrackTrainCouplerTrigger.test (:17), private: set only by Toggle()/Test(), the "Enable Coupling" lever.
        internal static readonly AccessTools.FieldRef<TrackTrainCouplerTrigger, bool> CouplerTest = Ref<TrackTrainCouplerTrigger, bool>("test");

        // TrackTrain.TrainCoupler.GetDistance(bool) / Release(bool) / Couple(TrainCoupler) (SRC/TrackTrain.cs:292, :243, :209).
        internal static readonly MethodInfo CouplerGetDistance = Method(typeof(TrackTrain.TrainCoupler), "GetDistance", new[] { typeof(bool) });
        internal static readonly MethodInfo CouplerRelease = Method(typeof(TrackTrain.TrainCoupler), "Release", new[] { typeof(bool) });
        internal static readonly MethodInfo CouplerCouple = Method(typeof(TrackTrain.TrainCoupler), "Couple", new[] { typeof(TrackTrain.TrainCoupler) });

        // TrackTrain.OnLoadingGame() (:861), public override; UncoupleIfTooFarAfterLoad() (:2023), private, the last
        // statement of OnLoadingGameDelay; RebuildTrainDataFromCouplers() (:2090), public static.
        internal static readonly MethodInfo TrainOnLoadingGame = Method(typeof(TrackTrain), "OnLoadingGame", Type.EmptyTypes);
        internal static readonly MethodInfo UncoupleIfTooFarAfterLoad = Method(typeof(TrackTrain), "UncoupleIfTooFarAfterLoad", Type.EmptyTypes);
        internal static readonly MethodInfo Rebuild = Method(typeof(TrackTrain), "RebuildTrainDataFromCouplers", Type.EmptyTypes);

        // TrainController.ResetTrainPosition(TrackTrain) (SRC/TrainController.cs:145), public: calls ResetTrackTrain.
        internal static readonly MethodInfo ResetTrainPosition = Method(typeof(TrainController), "ResetTrainPosition", new[] { typeof(TrackTrain) });

        // TrackTrain.originalFront/BackPositionOnSegment and originalFront/BackDirectionOnSegment (:675-681), private:
        // what ResetTrackTrain puts the axles back to (captured in OnLoadingGame, default 0/Forward for a train bought
        // this session). Rerail sets them to the axles' current values around its ResetTrainPosition call, so the
        // train is put back on the rails where it stands, then restores them.
        internal static readonly AccessTools.FieldRef<TrackTrain, float> TrainOrigFrontPos = Ref<TrackTrain, float>("originalFrontPositionOnSegment");
        internal static readonly AccessTools.FieldRef<TrackTrain, float> TrainOrigBackPos = Ref<TrackTrain, float>("originalBackPositionOnSegment");
        internal static readonly AccessTools.FieldRef<TrackTrain, Track_Segment.Direction> TrainOrigFrontDir =
            Ref<TrackTrain, Track_Segment.Direction>("originalFrontDirectionOnSegment");
        internal static readonly AccessTools.FieldRef<TrackTrain, Track_Segment.Direction> TrainOrigBackDir =
            Ref<TrackTrain, Track_Segment.Direction>("originalBackDirectionOnSegment");

        // TrackTrain.UncoupleWagons() (:2066), public: called by Rerail, checked for skipping patches only.
        internal static readonly MethodInfo UncoupleWagons = Method(typeof(TrackTrain), "UncoupleWagons", Type.EmptyTypes, optional: true);

        // ---- Unstuck ----

        // PlayerLocator.Teleport(Transform) (SRC/PlayerLocator.cs:43), public.
        internal static readonly MethodInfo PlayerTeleport = Method(typeof(PlayerLocator), "Teleport", new[] { typeof(Transform) });

        // FallDamage.airtime (SRC/FallDamage.cs:16), private: counts up while the player falls.
        internal static readonly AccessTools.FieldRef<FallDamage, float> FallAirtime = Ref<FallDamage, float>("airtime");

        // Prison.inPrison (SRC/Prison.cs:80), private. Never Prison.InPrison(): it logs on every call.
        internal static readonly AccessTools.FieldRef<Prison, bool> PrisonInPrison = Ref<Prison, bool>("inPrison");

        // Crime.activePolices (SRC/Crime.cs:82), private; off-scene officers are in it too.
        internal static readonly AccessTools.FieldRef<Crime, List<Act_Police>> CrimeActivePolices = Ref<Crime, List<Act_Police>>("activePolices");

        // InteractableLadder.playerClimbingGlobal (SRC/InteractableLadder.cs:31), protected static.
        internal static readonly FieldInfo LadderClimbing = Field(typeof(InteractableLadder), "playerClimbingGlobal");

        // OpenSewerCharacterController.disabledList (SRC/OpenSewerCharacterController.cs:42) and
        // PauseMenu.saveDisabledList (SRC/PauseMenu.cs:91), private: what blocks movement and what blocks saving.
        internal static readonly AccessTools.FieldRef<OpenSewerCharacterController, List<GameObject>> OccDisabledList =
            Ref<OpenSewerCharacterController, List<GameObject>>("disabledList");
        internal static readonly AccessTools.FieldRef<PauseMenu, List<GameObject>> SaveDisabledList = Ref<PauseMenu, List<GameObject>>("saveDisabledList");

        // Relay.disableSaving (SRC/Relay.cs:45), protected. Optional: without it the pause clone's child Relays are
        // destroyed instead.
        internal static readonly AccessTools.FieldRef<Relay, bool> RelayDisableSaving = Ref<Relay, bool>("disableSaving", optional: true);

        // ---- Keys ----

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private. The public CheckKey switches the input type to
        // keyboard when the key is held (:257-264), so the collision check uses this side-effect-free one. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey = BindingKeyDelegate();

        // ---- Helpers ----

        internal static bool Climbing
        {
            get
            {
                try { return LadderClimbing != null && LadderClimbing.GetValue(null) is bool b && b; }
                catch (Exception) { return false; }
            }
        }

        // Entries that are still alive (destroyed objects compare equal to null) and are not `ignore`.
        internal static int LiveCount(List<GameObject> list, GameObject ignore)
        {
            if (list == null) return 0;
            int n = 0;
            foreach (var go in list)
                if (go != null && go != ignore) n++;
            return n;
        }

        internal static MethodInfo Method(Type t, string name, Type[] args, bool optional = false)
        {
            try
            {
                var m = AccessTools.DeclaredMethod(t, name, args);
                if (m == null) Missing($"{t.Name}.{name}", null, optional);
                return m;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name, bool optional = false)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Missing($"{typeof(T).Name}.{name}", e, optional); return null; }
        }

        private static FieldInfo Field(Type t, string name, bool optional = false)
        {
            try
            {
                var f = AccessTools.Field(t, name);
                if (f == null) Missing($"{t.Name}.{name}", null, optional);
                return f;
            }
            catch (Exception e) { Missing($"{t.Name}.{name}", e, optional); return null; }
        }

        private static Func<KeyBindings, string, KeyCode> BindingKeyDelegate()
        {
            var m = Method(typeof(KeyBindings), "GetKey", new[] { typeof(string) }, optional: true);
            if (m == null) return null;
            try { return (Func<KeyBindings, string, KeyCode>)Delegate.CreateDelegate(typeof(Func<KeyBindings, string, KeyCode>), m); }
            catch (Exception e) { Missing("KeyBindings.GetKey", e, true); return null; }
        }

        private static void Missing(string what, Exception e, bool optional)
        {
            string why = e != null ? $" ({e.Message})" : "";
            if (optional) Log.Warn($"{what} not found{why}; the part of the feature using it is off.");
            else Log.Error($"{what} not found{why}; the feature using it is off.");
        }
    }
}

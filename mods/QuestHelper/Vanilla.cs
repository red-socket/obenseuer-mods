using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QuestHelper
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup / FeatureGroup check the members each group needs, so only the feature using it fails (spec §3.1).
    // Every member here was checked against SRC and the shipped Assembly-CSharp.dll (design.md §9).
    internal static class Vanilla
    {
        // ---- Patch targets (P1-P7, spec §3.2) ----

        // P1 SRC/TaskController.cs:306, public.
        internal static readonly MethodInfo EditTaskObjective = Method(typeof(TaskController), "EditTaskObjective",
            new[] { typeof(TaskStatusInfo), typeof(ObjectiveStatusInfo), typeof(ObjectiveStatus) });
        // P2 SRC/TaskPanel.cs:222, private.
        internal static readonly MethodInfo ShowTaskInfo = Method(typeof(TaskPanel), "ShowTaskInfo", new[] { typeof(int) });
        // P3 SRC/PauseNavigationHandler.cs:89, private; P4 :133, public.
        internal static readonly MethodInfo PauseNavUpdate = Method(typeof(PauseNavigationHandler), "Update", Type.EmptyTypes);
        internal static readonly MethodInfo HandleBackButtonClick = Method(typeof(PauseNavigationHandler), "HandleBackButtonClick", Type.EmptyTypes);
        // P5/P6: the overrides declared on GlobalState (SRC/GlobalState.cs:29-37).
        internal static readonly MethodInfo GlobalStateOnSavingGame = Method(typeof(GlobalState), "OnSavingGame", Type.EmptyTypes);
        internal static readonly MethodInfo GlobalStateOnLoadingGame = Method(typeof(GlobalState), "OnLoadingGame", Type.EmptyTypes);
        // P7 SRC/InventoryNavigationHandler.cs:400, public.
        internal static readonly MethodInfo GetButtonInfo = Method(typeof(InventoryNavigationHandler), "GetButtonInfo",
            new[] { typeof(int), typeof(InventoryNavigationHandler.CurrentInventoryWindow), typeof(InputManager.InputType) });

        // ---- Calls (public, resolved so a missing overload fails its group) ----

        // The three-argument overload is the only one that keeps `data` (SRC/GlobalState.cs:67, V12).
        internal static readonly MethodInfo SetState3 = Method(typeof(GlobalState), "SetState", new[] { typeof(string), typeof(bool), typeof(object) });
        internal static readonly MethodInfo EditTask = Method(typeof(TaskController), "EditTask",
            new[] { typeof(TaskInfo), typeof(TaskObjective), typeof(ObjectiveStatus), typeof(bool), typeof(bool), typeof(CurrentTaskStatus), typeof(bool), typeof(bool), typeof(bool) });
        internal static readonly MethodInfo EditTaskStatus = Method(typeof(TaskController), "EditTaskStatus", new[] { typeof(TaskStatusInfo), typeof(CurrentTaskStatus) });
        internal static readonly MethodInfo EditTaskCounter = Method(typeof(TaskController), "EditTaskCounter", new[] { typeof(TaskInfo), typeof(TaskObjective), typeof(int), typeof(bool) });
        internal static readonly MethodInfo AddTaskItem = Method(typeof(TaskItemsManager), "AddTaskItem",
            new[] { typeof(TaskItem), typeof(int), typeof(bool), typeof(TaskItemsManager.TaskItemInfo), typeof(bool), typeof(string), typeof(bool) });
        internal static readonly MethodInfo RefreshPanels = Method(typeof(InventoryNavigationHandler), "RefreshPanels", Type.EmptyTypes);
        // PauseNavigationHandler.ChangeMenuStateIfNecessary (SRC/PauseNavigationHandler.cs:239), private: syncs the pad
        // navigation with PauseMenu.currentMenu after our "Save game..." opens the save submenu. Optional.
        internal static readonly MethodInfo ChangeMenuState = Method(typeof(PauseNavigationHandler), "ChangeMenuStateIfNecessary", Type.EmptyTypes, optional: true);
        internal static readonly MethodInfo WaitForPendingMapWrite = Method(typeof(RevealCamera), "WaitForPendingMapWrite", Type.EmptyTypes);

        // ---- Fields ----

        // PauseMenu.saveDisabledList (SRC/PauseMenu.cs:91), private.
        internal static readonly AccessTools.FieldRef<PauseMenu, List<GameObject>> SaveDisabledList = Ref<PauseMenu, List<GameObject>>("saveDisabledList");

        // TaskPanel (SRC/TaskPanel.cs:9-67).
        internal static readonly AccessTools.FieldRef<TaskPanel, List<GameObject>> TaskList = Ref<TaskPanel, List<GameObject>>("taskList");
        internal static readonly AccessTools.FieldRef<TaskPanel, List<GameObject>> VisibleObjectives = Ref<TaskPanel, List<GameObject>>("visibleObjectives");
        internal static readonly AccessTools.FieldRef<TaskPanel, bool> TaskInfoPanelActive = Ref<TaskPanel, bool>("taskInfoPanelActive");
        internal static readonly AccessTools.FieldRef<TaskPanel, GameObject> TaskListPanel = Ref<TaskPanel, GameObject>("taskListPanel");
        internal static readonly AccessTools.FieldRef<TaskPanel, GameObject> ActiveTaskTitle = Ref<TaskPanel, GameObject>("activeTaskTitle");
        internal static readonly AccessTools.FieldRef<TaskPanel, GameObject> RenovationTaskTitle = Ref<TaskPanel, GameObject>("renovationTaskTitle");
        internal static readonly AccessTools.FieldRef<TaskPanel, GameObject> FinishedTaskTitle = Ref<TaskPanel, GameObject>("finishedTaskTitle");
        internal static readonly AccessTools.FieldRef<TaskPanel, GameObject> FinishedTaskTitlePrefab = Ref<TaskPanel, GameObject>("finishedTaskTitlePrefab");
        internal static readonly AccessTools.FieldRef<TaskPanel, GameObject> TaskPrefab = Ref<TaskPanel, GameObject>("taskPrefab");

        // ObjectiveUI (SRC/ObjectiveUI.cs:8-16), all private.
        internal static readonly AccessTools.FieldRef<ObjectiveUI, TextMeshProUGUI> ObjectiveTitle = Ref<ObjectiveUI, TextMeshProUGUI>("objectiveTitle");
        internal static readonly AccessTools.FieldRef<ObjectiveUI, GameObject> CannotBeCompleted = Ref<ObjectiveUI, GameObject>("cannotBeCompleted");
        internal static readonly AccessTools.FieldRef<ObjectiveUI, ObjectiveStatusInfo> ObjectiveStatusField = Ref<ObjectiveUI, ObjectiveStatusInfo>("objectiveStatus");

        // TaskUIPanel.statusActiveUnselected (SRC/TaskUIPanel.cs:37), private: the pin's sprite (D16).
        internal static readonly AccessTools.FieldRef<TaskUIPanel, Sprite> StatusActiveUnselected = Ref<TaskUIPanel, Sprite>("statusActiveUnselected");

        // Map (SRC/Map.cs:25-64), MapController.map (SRC/MapController.cs:85), GetLandMark (SRC/GetLandMark.cs), all private.
        internal static readonly AccessTools.FieldRef<Map, GameObject> LandmarkPrefab = Ref<Map, GameObject>("LandmarkPrefab");
        internal static readonly AccessTools.FieldRef<Map, GameObject> LandmarksArea = Ref<Map, GameObject>("LandmarksArea");
        internal static readonly AccessTools.FieldRef<Map, RectTransform> MapSize = Ref<Map, RectTransform>("mapsize");
        internal static readonly AccessTools.FieldRef<MapController, Map> ControllerMap = Ref<MapController, Map>("map");
        internal static readonly AccessTools.FieldRef<GetLandMark, Image> LandmarkImage = Ref<GetLandMark, Image>("image");
        internal static readonly AccessTools.FieldRef<GetLandMark, ToolTipInfo> LandmarkTip = Ref<GetLandMark, ToolTipInfo>("tipInfo");
        internal static readonly AccessTools.FieldRef<GetLandMark, LandmarkUI> LandmarkUi = Ref<GetLandMark, LandmarkUI>("landmarkUI");

        // GameObjectStateSave.currentGameObject (SRC/GameObjectStateSave.cs:13), private. Optional: QD-RAT then trusts the
        // scene path alone.
        internal static readonly AccessTools.FieldRef<GameObjectStateSave, GameObject> StateSaveObject =
            Ref<GameObjectStateSave, GameObject>("currentGameObject", optional: true);

        // Relay.disableSaving (SRC/Relay.cs:45), protected. Optional: without it the pause clone's child Relays are destroyed.
        internal static readonly AccessTools.FieldRef<Relay, bool> RelayDisableSaving = Ref<Relay, bool>("disableSaving", optional: true);

        // ---- Keys ----

        // KeyBindings.GetKey(string) (SRC/KeyBindings.cs:266), private and side-effect free. Optional.
        internal static readonly Func<KeyBindings, string, KeyCode> BindingKey = BindingKeyDelegate();

        // ---- Helpers ----

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

        // ---- Typed wrappers ----

        internal static List<GameObject> SaveLocks(PauseMenu pm) => pm != null && SaveDisabledList != null ? SaveDisabledList(pm) : null;

        internal static Map MapOf(MapController mc) => mc != null && ControllerMap != null ? ControllerMap(mc) : null;
    }
}

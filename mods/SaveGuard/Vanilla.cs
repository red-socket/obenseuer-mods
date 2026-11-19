using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using NPC;
using UnityEngine;

namespace SaveGuard
{
    // Private game members, read through Harmony's AccessTools rather than the publicized reference:
    // docs/modding-guide.md §4 notes that Mono's tolerance for direct private access was never tested.
    // Every accessor throws on failure; callers catch.
    internal static class Vanilla
    {
        private static readonly FieldInfo LevelNameField = AccessTools.Field(typeof(SaveController), "LevelName");
        private static readonly MethodInfo GetOldestAutosaveMethod = AccessTools.Method(typeof(SaveController), "GetOldestAutosave");
        private static readonly FieldInfo LadderClimbingField = AccessTools.Field(typeof(InteractableLadder), "playerClimbingGlobal");
        private static readonly MethodInfo SetCurrentCharacterMethod = AccessTools.Method(typeof(LoadMenu), "SetCurrentCharacter",
            new[] { typeof(SaveController.CharacterSaves), typeof(int) });
        private static readonly MethodInfo UpdateUIMethod = AccessTools.Method(typeof(LoadMenu), "UpdateUI", Type.EmptyTypes);

        private static readonly AccessTools.FieldRef<LoadMenu, SaveController.SaveInfo> CurrentSaveInfo = Ref<LoadMenu, SaveController.SaveInfo>("currentSaveInfo");
        private static readonly AccessTools.FieldRef<LoadMenu, int> CurrentCharacterNumber = Ref<LoadMenu, int>("currentCharacterNumber");
        private static readonly AccessTools.FieldRef<LoadMenu, GameObject> VersionWarningPanel = Ref<LoadMenu, GameObject>("VersionWarningPanel");
        private static readonly AccessTools.FieldRef<LoadMenu, string> FolderNameMismatchText = Ref<LoadMenu, string>("folderNameMismatchText");
        private static readonly AccessTools.FieldRef<LoadMenu, bool> MainMenuFlag = Ref<LoadMenu, bool>("mainMenu");
        private static readonly AccessTools.FieldRef<LoadMenu, GameObject> LoadingPanel = Ref<LoadMenu, GameObject>("loadingPanel");

        private static readonly AccessTools.FieldRef<PauseMenu, List<GameObject>> SaveDisabledList = Ref<PauseMenu, List<GameObject>>("saveDisabledList");
        private static readonly AccessTools.FieldRef<GameController, List<GameObject>> DisabledList = Ref<GameController, List<GameObject>>("disabledList");
        private static readonly AccessTools.FieldRef<Crime, List<Act_Police>> ActivePolices = Ref<Crime, List<Act_Police>>("activePolices");
        private static readonly AccessTools.FieldRef<Prison, bool> InPrison = Ref<Prison, bool>("inPrison");

        private static AccessTools.FieldRef<T, F> Ref<T, F>(string name)
        {
            try { return AccessTools.FieldRefAccess<T, F>(name); }
            catch (Exception e) { Log.Error($"{typeof(T).Name}.{name} not found ({e.Message}); the feature using it is off."); return null; }
        }

        // SaveController.LevelName: the active scene at the save, set at SRC/SaveController.cs:504; names the level file.
        internal static string LevelName => (string)LevelNameField.GetValue(null);
        internal static string GetOldestAutosave() => (string)GetOldestAutosaveMethod.Invoke(null, null);
        internal static bool LadderClimbing => (bool)LadderClimbingField.GetValue(null);

        internal static SaveController.SaveInfo GetCurrentSaveInfo(LoadMenu m) => CurrentSaveInfo(m);
        internal static void SetCurrentSaveInfo(LoadMenu m, SaveController.SaveInfo v) => CurrentSaveInfo(m) = v;
        internal static int GetCurrentCharacterNumber(LoadMenu m) => CurrentCharacterNumber(m);
        internal static GameObject GetVersionWarningPanel(LoadMenu m) => VersionWarningPanel(m);
        internal static string GetFolderNameMismatchText(LoadMenu m) => FolderNameMismatchText(m);
        internal static bool IsMainMenu(LoadMenu m) => MainMenuFlag(m);

        // What vanilla's ChangeCharacter does (LoadMenu.cs:384-408) for a given index: SetCurrentCharacter (:259) sets
        // currentCharacter, currentCharacterFolder, the name label and currentCharacterNumber; UpdateUI (:718) redraws
        // the slot list from currentCharacterNumber and clears the selected slot.
        internal static void SelectCharacter(LoadMenu m, int index)
        {
            SetCurrentCharacterMethod.Invoke(m, new object[] { m.Characters[index], index });
            UpdateUIMethod.Invoke(m, null);
        }

        // A load is starting or running: LoadMenu.LoadGameSlot shows loadingPanel and fades (LoadMenu.cs:530-553),
        // then LoadGameWithMigration sets SaveController.Loading while it reads the slot folder.
        // A check that throws counts as loading.
        internal static bool LoadInProgress()
        {
            try
            {
                if (SaveController.Loading) return true;
                var ls = LoadingScreen.instance;
                if (ls != null && (ls.Fading || ls.FadeActive)) return true;
                var lm = LoadMenu.instance;
                if (lm != null && LoadingPanel != null)
                {
                    var panel = LoadingPanel(lm);
                    if (panel != null && panel.activeInHierarchy) return true;
                }
                return false;
            }
            catch (Exception) { return true; }
        }

        internal static List<GameObject> GetSaveDisabledList(PauseMenu p) => SaveDisabledList(p);
        internal static List<GameObject> GetDisabledList(GameController g) => DisabledList(g);
        internal static List<Act_Police> GetActivePolices(Crime c) => ActivePolices(c);
        internal static bool GetInPrison(Prison p) => InPrison(p);

        // Any entry still alive (destroyed objects compare equal to null).
        internal static bool AnyAlive(List<GameObject> list)
        {
            if (list == null) return false;
            foreach (var go in list) if (go != null) return true;
            return false;
        }

        // Character folder of the running game, as SaveGame derives it (SanitizeCharacterName of the identity).
        internal static string CurrentCharacterFolder()
        {
            try
            {
                if (PlayerIdentity.identity == null) return null;
                return SaveController.SanitizeCharacterName(PlayerIdentity.identity.firstName, PlayerIdentity.identity.lastName);
            }
            catch (Exception) { return null; }
        }
    }
}

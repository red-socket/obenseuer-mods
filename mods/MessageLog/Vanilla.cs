using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using PixelCrushers.DialogueSystem;
using UnityEngine;

namespace MessageLog
{
    // Private game members and exact overloads, read through Harmony's AccessTools rather than the publicized
    // reference (docs/modding-guide.md §4). A required member that is missing logs one error and stays null;
    // Plugin.PatchGroup checks the members each group needs, so only the feature using it fails (spec §3).
    // Firstpass (the Dialogue System) is not publicized (Directory.Build.props), so its private members are named here.
    internal static class Vanilla
    {
        // ---- Capture (N1, D1) ----

        // N1 SRC/Notifications.cs:89, public, the only overload.
        internal static readonly MethodInfo CreateNotification = Method(typeof(Notifications), "CreateNotification",
            new[] { typeof(string), typeof(string), typeof(bool) });
        // D1 FP/PixelCrushers/DialogueSystem/DialogueSystemController.cs:1430, private, a SendMessage receiver
        // (ConversationView.NotifyParticipants, :548-574); it raises conversationLinePrepared.
        internal static readonly MethodInfo OnConversationLine = Method(typeof(DialogueSystemController), "OnConversationLine",
            new[] { typeof(Subtitle) });

        // ---- Window (W1, W2, H1) ----

        // W1 SRC/PauseNavigationHandler.cs:90, private (a Unity message, called directly). W2 :133, public.
        internal static readonly MethodInfo PauseNavUpdate = Method(typeof(PauseNavigationHandler), "Update", Type.EmptyTypes);
        internal static readonly MethodInfo HandleBackButtonClick = Method(typeof(PauseNavigationHandler), "HandleBackButtonClick", Type.EmptyTypes);
        // H1 SRC/InventoryNavigationHandler.cs:400; KeyCodeInfoToString :464, static, returns only the new part.
        internal static readonly MethodInfo GetButtonInfo = Method(typeof(InventoryNavigationHandler), "GetButtonInfo",
            new[] { typeof(int), typeof(InventoryNavigationHandler.CurrentInventoryWindow), typeof(InputManager.InputType) });
        internal static readonly MethodInfo KeyCodeInfoToString = Method(typeof(InventoryNavigationHandler), "KeyCodeInfoToString",
            new[] { typeof(string), typeof(string), typeof(string), typeof(string) });

        // ---- Search guard (T1-T5) ----

        // SRC/InputManager.cs:202-306 (public). GetKeyUp is not guarded.
        internal static readonly MethodInfo GetKeyDown = Method(typeof(InputManager), "GetKeyDown", new[] { typeof(string) });
        internal static readonly MethodInfo GetAuxiliaryReloadDown = Method(typeof(InputManager), "GetAuxiliaryReloadDown", Type.EmptyTypes);
        internal static readonly MethodInfo GetKeyDownKeyboardOnly = Method(typeof(InputManager), "GetKeyDownKeyboardOnly", new[] { typeof(string) });
        internal static readonly MethodInfo GetKey = Method(typeof(InputManager), "GetKey", new[] { typeof(string) });
        internal static readonly MethodInfo GetKeyWithHold = Method(typeof(InputManager), "GetKeyWithHold", new[] { typeof(string), typeof(int) });
        // GameController.disabledWritelockList (SRC/GameController.cs:25), private.
        internal static readonly AccessTools.FieldRef<GameController, List<GameObject>> DisabledWritelockList =
            Ref<GameController, List<GameObject>>("disabledWritelockList");

        // NotesPanelNavigation.keyboardUI (SRC/NotesPanelNavigation.cs:20), private. Optional: without it the Notes page
        // key stays off while that page is open (Game.NotesKeyboardVisible fails closed).
        internal static readonly AccessTools.FieldRef<NotesPanelNavigation, VirtualKeyboardUI> NotesKeyboard =
            Ref<NotesPanelNavigation, VirtualKeyboardUI>("keyboardUI", optional: true);

        // Relay.disableSaving (SRC/Relay.cs:45), protected. Optional: without it the pause clone's child Relays are destroyed.
        internal static readonly AccessTools.FieldRef<Relay, bool> RelayDisableSaving = Ref<Relay, bool>("disableSaving", optional: true);

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
    }
}

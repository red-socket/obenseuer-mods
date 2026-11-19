using System;
using HarmonyLib;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SaveGuard
{
    // While a Save Guard window has focus, the current EventSystem is disabled so clicks do not reach the
    // world-space menu underneath. In the main menu the game's navigation handler is paused too: it reads
    // Escape, Return, X and the pad buttons with Input.GetKeyDown in its own Update (MainMenuNavigationHandler.cs:
    // 124-141), which the EventSystem does not stop, so one B or Escape would also back out of the Load panel.
    // In game, while the damaged-load prompt is open over the pause menu, the pause menu's own input is paused the
    // same way: GameController.Update (reads Pause Menu / Menu while paused, GameController.cs:75) is disabled, and
    // PauseNavigationHandler.Update (A, X, Return, stick; PauseNavigationHandler.cs:90-131) is skipped by
    // PauseNavigationPatch, because that handler has no working flag of its own.
    // Only an EventSystem, handler or controller that Save Guard disabled is ever re-enabled.
    internal static class InputBlock
    {
        private static EventSystem _disabled;
        private static MainMenuNavigationHandler _nav;
        private static GameController _gameController;

        // Read by PauseNavigationPatch.
        internal static bool PauseNavigationSuspended { get; private set; }

        // suspendPauseMenu: the in-game damaged-load prompt has focus.
        internal static void Sync(bool block, bool suspendPauseMenu)
        {
            try
            {
                if (block)
                {
                    if (_disabled == null)
                    {
                        var es = EventSystem.current;
                        if (es != null && es.enabled)
                        {
                            es.enabled = false;
                            _disabled = es;
                        }
                    }
                    if (_nav == null)
                    {
                        var nav = MainMenuNavigationHandler.Instance;
                        if (nav != null && nav.ControlsEnabled)
                        {
                            nav.ControlsEnabled = false;
                            _nav = nav;
                        }
                    }
                    if (suspendPauseMenu) SuspendPauseMenu();
                    else if (EndOfFrame()) ResumePauseMenu();
                }
                else if (EndOfFrame()) Release();
            }
            catch (Exception e) { Log.Exception("input block", e); }
        }

        // The block is taken in any OnGUI pass but given back only in the Repaint pass. Unity sends queued keyboard
        // and mouse events to OnGUI in PreUpdate (PlayerLoop PreUpdate.IMGUISendQueuedEvents), before Update, so an
        // Escape that closes a window there is still Input.GetKeyDown(Escape) for every Update of that frame:
        // releasing at once would hand that same press to GameController (Pause Menu) or the menu navigation handler,
        // which would back out of the Load panel. Repaint runs after Update and LateUpdate (which also clears
        // InputManager.guiKeysDown), so by then the press has been seen by nobody but Save Guard. Pad input closes
        // windows from Plugin.Update and is released in the same frame's Repaint.
        private static bool EndOfFrame()
        {
            var e = Event.current;
            return e == null || e.type == EventType.Repaint;
        }

        // GameController has no OnEnable/OnDisable/LateUpdate, so disabling it only skips its Update. That is not
        // done for PauseNavigationHandler: its OnEnable restarts InitializeSlotArrays (PauseNavigationHandler.cs:
        // 479-516), which would move its selection back to the pause menu's main list while the Load panel shows.
        private static void SuspendPauseMenu()
        {
            PauseNavigationSuspended = true;
            if (_gameController == null)
            {
                var gc = GameController.instance;
                if (gc != null && gc.enabled)
                {
                    gc.enabled = false;
                    _gameController = gc;
                }
            }
        }

        private static void ResumePauseMenu()
        {
            PauseNavigationSuspended = false;
            try
            {
                if (_gameController != null) _gameController.enabled = true;   // destroyed with its scene: nothing to restore
            }
            catch (Exception e) { Log.Exception("GameController restore", e); }
            _gameController = null;
        }

        internal static void Release()
        {
            try
            {
                if (_disabled != null) _disabled.enabled = true;   // destroyed with its scene: nothing to restore
            }
            catch (Exception e) { Log.Exception("EventSystem restore", e); }
            _disabled = null;
            try
            {
                // An inactive handler sets ControlsEnabled itself when it is enabled again (MainMenuNavigationHandler.cs:565).
                if (_nav != null && _nav.isActiveAndEnabled) _nav.ControlsEnabled = true;
            }
            catch (Exception e) { Log.Exception("menu navigation restore", e); }
            _nav = null;
            ResumePauseMenu();
        }

        // A paused handler no longer shows the cursor when the mouse moves in controller mode
        // (UINavigationBase.ShowCursorIfNecessary, UINavigationBase.cs:384-399), so do the same here. Called from Plugin.Update.
        internal static void Update()
        {
            if ((_nav == null && !PauseNavigationSuspended) || Cursor.visible) return;
            if (Input.GetAxis("Mouse X") == 0f && Input.GetAxis("Mouse Y") == 0f) return;
            if (GameController.instance != null) GameController.instance.ChangeCursorStatus(CursorLockMode.None, true);
            else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (InputManager.instance != null) InputManager.instance.ChangeInputType(InputManager.InputType.Keyboard);
        }
    }

    // P4: PauseNavigationHandler.Update(), private (SRC/PauseNavigationHandler.cs:90). Skipped only while the in-game
    // damaged-load prompt has focus (InputBlock.PauseNavigationSuspended); it never touches a save.
    [HarmonyPatch(typeof(PauseNavigationHandler), "Update")]
    internal static class PauseNavigationPatch
    {
        private static bool Prefix() => !InputBlock.PauseNavigationSuspended;
    }
}

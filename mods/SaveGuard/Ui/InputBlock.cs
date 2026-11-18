using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace SaveGuard
{
    // While a Save Guard window has focus, the current EventSystem is disabled so clicks do not reach the
    // world-space menu underneath. In the main menu the game's navigation handler is paused too: it reads
    // Escape, Return, X and the pad buttons with Input.GetKeyDown in its own Update (MainMenuNavigationHandler.cs:
    // 124-141), which the EventSystem does not stop, so one B or Escape would also back out of the Load panel.
    // Only an EventSystem or handler that Save Guard disabled is ever re-enabled.
    internal static class InputBlock
    {
        private static EventSystem _disabled;
        private static MainMenuNavigationHandler _nav;

        internal static void Sync(bool block)
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
                }
                else Release();
            }
            catch (Exception e) { Log.Exception("input block", e); }
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
        }

        // The paused handler no longer shows the cursor when the mouse moves in controller mode
        // (UINavigationBase.ShowCursorIfNecessary, UINavigationBase.cs:384-399), so do the same here. Called from Plugin.Update.
        internal static void Update()
        {
            if (_nav == null || Cursor.visible) return;
            if (Input.GetAxis("Mouse X") == 0f && Input.GetAxis("Mouse Y") == 0f) return;
            if (GameController.instance != null) GameController.instance.ChangeCursorStatus(CursorLockMode.None, true);
            else { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; }
            if (InputManager.instance != null) InputManager.instance.ChangeInputType(InputManager.InputType.Keyboard);
        }
    }
}

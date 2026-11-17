using System;
using UnityEngine;
using UiButton = UnityEngine.UI.Button;

namespace VitalSigns
{
    // The pause menu entry "Vital Signs" (spec §9.4, D2), off by default: present when Access.PauseMenuEntry is on, or
    // forced on when the Character-page access failed at startup so the window always has a way in. Cloned from Resume
    // and placed after Quest Helper's entry (else after "Difficulty"), always above Safe Travels' entries. No patch:
    // PauseNavigationHandler.OnEnable rebuilds its button array from Main's children on every open
    // (SRC/PauseNavigationHandler.cs:479-499). Added and removed only while the menu is closed (a child that changes
    // while it is open would stay in the navigation array), so the setting works live both ways. The click opens the
    // window over the paused game and doesn't resume.
    internal static class PauseEntry
    {
        private const string Name = "VitalSigns Entry";
        private static UiButton _button;
        private static bool _failed;

        internal static bool Forced;   // the Character-page access failed at launch

        // Read live: the entry needs no patch.
        internal static bool Wanted => VitalWindow.Available && (Forced || Cfg.Access.PauseMenuEntry.Value);

        // The entry is in the pause menu now.
        internal static bool Present => _button != null;

        internal static void OnSceneLoaded()
        {
            _button = null;
            _failed = false;
            if (Wanted) Ensure();
        }

        internal static void Tick()
        {
            if (Wanted)
            {
                if (!_failed && _button == null) Ensure();
            }
            else if (_button != null) Remove();
        }

        private static void Remove()
        {
            var pm = PauseMenu.instance;
            if (pm != null && pm.PauseIsActive()) return;
            try { UnityEngine.Object.DestroyImmediate(_button.gameObject); }
            catch (Exception e) { Log.Exception("pause entry remove", e); }
            _button = null;
            Log.Verbose("Ui: pause menu entry removed.");
        }

        private static void Ensure()
        {
            var pm = PauseMenu.instance;
            if (pm == null || pm.resumeMenuButton == null) return;   // main menu and intro have no pause menu
            if (pm.PauseIsActive()) return;
            var parent = pm.resumeMenuButton.transform.parent;
            var existing = parent != null ? parent.Find(Name) : null;
            if (existing != null) { _button = existing.GetComponent<UiButton>(); return; }
            try { _button = PauseClone.Clone(pm.resumeMenuButton, Name, Strings.ModName, Click); }
            catch (Exception e)
            {
                Log.Exception("pause entry", e);
                _button = null;
            }
            if (_button == null) { _failed = true; return; }
            Log.Verbose("Ui: pause menu entry added.");
            Report(parent);
        }

        private static void Click()
        {
            try { VitalWindow.Open(); }
            catch (Exception e) { Log.Exception("pause entry click", e); }
        }

        // Compat (spec §13): the full list of Main's entries goes to the verbose log once.
        private static bool _reported;

        private static void Report(Transform main)
        {
            if (_reported || main == null) return;
            _reported = true;
            var all = new System.Collections.Generic.List<string>();
            foreach (Transform c in main)
                if (c != null && c.GetComponent<UiButton>() != null) all.Add(c.name);
            Log.Verbose("Compat: pause menu entries: " + string.Join(", ", all.ToArray()));
        }
    }
}

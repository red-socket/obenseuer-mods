using System;
using UnityEngine;
using UiButton = UnityEngine.UI.Button;

namespace QuestHelper
{
    // The pause menu entry "Quest Helper" / "Quest Helper (n)" (spec §4.1), cloned from Resume and placed right after
    // "Difficulty". No patch: PauseNavigationHandler.OnEnable rebuilds its button array from Main's children on every open
    // (SRC/PauseNavigationHandler.cs:490-494), so the D-pad reaches the entry and pad A or X/Return press it. The entry is
    // only added or removed while the menu is closed (an inactive child would stay in the navigation array); its label
    // may change at any time. The click opens the window over the paused game and does not resume.
    internal static class PauseEntry
    {
        private const string Name = "QuestHelper Entry";
        private static UiButton _button;
        private static bool _failed;

        internal static void OnSceneLoaded()
        {
            _button = null;
            _failed = false;
            Ensure();
        }

        internal static void Tick()
        {
            if (_failed || !HelperWindow.Available) return;
            if (_button == null) Ensure();
            else if (!_reported && Game.PauseOpen) ReportNeighbours(_button.transform.parent);
        }

        private static void Ensure()
        {
            if (!HelperWindow.Available) return;
            var pm = PauseMenu.instance;
            if (pm == null || pm.resumeMenuButton == null) return;   // main menu and intro have no pause menu
            if (pm.PauseIsActive()) return;
            var parent = pm.resumeMenuButton.transform.parent;
            var existing = parent != null ? parent.Find(Name) : null;
            if (existing != null) { _button = existing.GetComponent<UiButton>(); return; }
            try { _button = PauseClone.Clone(pm.resumeMenuButton, Name, Strings.PauseLabel(Detector.StuckCount), Click); }
            catch (Exception e)
            {
                Log.Exception("pause entry", e);
                _button = null;
            }
            if (_button == null) { _failed = true; return; }
            Log.Verbose("Doctor: pause menu entry added.");
        }

        internal static void Relabel()
        {
            if (_button != null) PauseClone.SetLabel(_button, Strings.PauseLabel(Detector.StuckCount));
        }

        private static void Click()
        {
            try { HelperWindow.Open(null); }
            catch (Exception e) { Log.Exception("pause entry click", e); }
        }

        // Compat (spec §10): Safe Travels' entries under Main (it anchors at "Quit to Menu", so the two never collide),
        // logged once; the full list of Main's entries goes to the verbose log. Logged on the first pause open, not when
        // our entry is added: Safe Travels adds its entries from its own sceneLoaded handler, which may run after ours,
        // and both mods only add entries while the menu is closed, so by the first open every entry is there.
        private static bool _reported;

        private static void ReportNeighbours(Transform main)
        {
            if (_reported || main == null) return;
            _reported = true;
            var all = new System.Collections.Generic.List<string>();
            var friends = new System.Collections.Generic.List<string>();
            foreach (Transform c in main)
            {
                if (c == null || c.GetComponent<UiButton>() == null) continue;
                all.Add(c.name);
                if (c.name != Name && c.name.StartsWith("SafeTravels", StringComparison.Ordinal)) friends.Add(c.name);
            }
            Log.Verbose("Compat: pause menu entries: " + string.Join(", ", all.ToArray()));
            if (friends.Count > 0) Log.Info($"Compat: the pause menu also has {string.Join(", ", friends.ToArray())} (Safe Travels); Quest Helper's entry sits after Difficulty.");
        }
    }
}

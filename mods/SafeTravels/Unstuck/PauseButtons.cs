using System;
using UnityEngine;
using UiButton = UnityEngine.UI.Button;

namespace SafeTravels
{
    // The pause menu entries (spec §4.4): "Get Unstuck" (Unstuck.PauseMenuButton, on by default) and "Rerail Train"
    // (Rerail.PauseMenuButton, off by default), cloned from Resume and placed just above "Quit to Menu". No patch:
    // PauseNavigationHandler.OnEnable rebuilds its button array from Main's direct Button children on every open
    // (SRC/PauseNavigationHandler.cs:490-494), so the D-pad and stick reach the entries, and pad A or X/Return press
    // them through HandleButtonClick, which honours `interactable`. Resume stays the default selection. Clones are only
    // added or removed while the menu is closed (an inactive child would stay in the navigation array), and they die
    // with the scene.
    internal static class PauseButtons
    {
        private static UiButton _unstuck, _rerail;
        private static bool _scheduled;
        private static bool _failed;

        internal static bool WantUnstuck => Unstuck.On && Cfg.Unstuck.PauseMenuButton.Value;
        internal static bool WantRerail => Rerail.On && Cfg.Rerail.PauseMenuButton.Value;

        internal static void OnSceneLoaded()
        {
            _unstuck = null;
            _rerail = null;
            _failed = false;
            _scheduled = true;
            Ensure();
        }

        // A config change: applied now if the menu is closed, otherwise when it closes (Tick).
        internal static void Schedule() => _scheduled = true;

        // Plugin.Update: applies a scheduled change, and retries lazily if the pause menu did not exist at scene load.
        internal static void Tick()
        {
            if (_failed) return;
            if (_scheduled || (WantUnstuck && _unstuck == null) || (WantRerail && _rerail == null)) Ensure();
        }

        private static void Ensure()
        {
            var pm = PauseMenu.instance;
            if (pm == null || pm.resumeMenuButton == null) return;   // main menu and intro have no pause menu
            if (pm.PauseIsActive()) return;                          // wait for the menu to close
            _scheduled = false;

            if (!WantUnstuck && _unstuck != null) { PauseClone.Remove(_unstuck); _unstuck = null; }
            if (!WantRerail && _rerail != null) { PauseClone.Remove(_rerail); _rerail = null; }

            if (WantUnstuck && _unstuck == null)
            {
                _unstuck = Create(pm, "SafeTravels Unstuck", Strings.UnstuckButton, rerail: false);
                // Keep the order Get Unstuck, Rerail Train, Quit to Menu.
                if (_unstuck != null && _rerail != null) _unstuck.transform.SetSiblingIndex(_rerail.transform.GetSiblingIndex());
            }
            if (WantRerail && _rerail == null)
                _rerail = Create(pm, "SafeTravels Rerail", Strings.RerailButton, rerail: true);
        }

        private static UiButton Create(PauseMenu pm, string name, string label, bool rerail)
        {
            // A pause menu that outlived the last scene still has our entry: keep it.
            var parent = pm.resumeMenuButton.transform.parent;
            var existing = parent != null ? parent.Find(name) : null;
            if (existing != null && existing.GetComponent<PauseEntry>() != null) return existing.GetComponent<UiButton>();

            UiButton b;
            try
            {
                b = PauseClone.Clone(pm.resumeMenuButton, name, label, () => Click(rerail));
            }
            catch (Exception e)
            {
                Log.Exception("pause entry", e);
                b = null;
            }
            if (b == null)
            {
                _failed = true;
                return null;
            }
            var entry = b.gameObject.AddComponent<PauseEntry>();
            entry.Init(rerail, label);
            Log.Verbose($"Unstuck: pause menu entry '{label}' added.");
            return b;
        }

        private static void Click(bool rerail)
        {
            try
            {
                if (PauseMenu.instance != null) PauseMenu.instance.Resume();
                if (rerail) Rerail.QueueFromPause();
                else Unstuck.QueueFromPause();
            }
            catch (Exception e) { Log.Exception("pause entry click", e); }
        }
    }

    // Greys the entry where the press could never work (dead, jail, arrest or pursuit, riding the minitrain, cooldown;
    // for Rerail also no train in range) and shows the cooldown in its label. Busy rows are checked after Resume
    // instead, since the pause menu itself disables controls. Refreshed on every pause open (OnEnable) and four times a
    // second while open (Update runs at timeScale 0).
    internal sealed class PauseEntry : MonoBehaviour
    {
        private const float RefreshSeconds = 0.25f;

        private bool _rerail;
        private string _label;
        private UiButton _button;
        private TextButtonColorEffectTMP _fx;
        private bool? _applied;
        private float _next;

        internal void Init(bool rerail, string label)
        {
            _rerail = rerail;
            _label = label;
        }

        private void OnEnable()
        {
            _applied = null;
            Refresh();
        }

        private void Update()
        {
            if (Time.unscaledTime < _next) return;
            Refresh();
        }

        private void Refresh()
        {
            _next = Time.unscaledTime + RefreshSeconds;
            try
            {
                if (_button == null) _button = GetComponent<UiButton>();
                if (_fx == null) _fx = GetComponentInChildren<TextButtonColorEffectTMP>(true);
                if (_button == null) return;

                bool blocked = (Death.instance != null && Death.instance.dead) || PlayGate.Jailed || PlayGate.Police || PlayGate.Riding;
                int cd = _rerail ? Rerail.CooldownLeft : Unstuck.CooldownLeft;
                if (!blocked && _rerail) blocked = Rerail.FindTarget(crosshair: false, out _) == null;
                bool on = !blocked && cd == 0;

                PauseClone.SetLabel(_button, cd > 0 ? Strings.WithCooldown(_label, cd) : _label);
                if (_applied == on) return;
                _applied = on;
                _button.interactable = on;
                if (_fx != null)
                {
                    if (on) _fx.Enable();
                    else _fx.Disable();
                }
            }
            catch (Exception e) { Log.Exception("pause entry refresh", e); }
        }
    }
}

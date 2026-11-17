#if QUESTHELPER_TESTS
using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using PixelCrushers.DialogueSystem;

namespace QuestHelper
{
    // Test-only setters for the in-game test plan's [T] steps (spec §2, §13), compiled only with
    // -p:QuestHelperTests=true (Save Guard's SAVEGUARD_FAULTS precedent). Never packaged. Set [Testing] Setup to a name
    // while a game is running; it applies once and resets itself to empty.
    internal static class Setups
    {
        private static ConfigEntry<string> _setup;

        private static readonly Dictionary<string, Action> All = new Dictionary<string, Action>(StringComparer.OrdinalIgnoreCase)
        {
            ["raimo-knows"] = () => DialogueLua.SetActorField(CasinoCoup.Raimo, "KnowsPlayer", true),
            ["toivo-lie"] = () => { DialogueLua.SetActorField(ToivoLie.Toivo, "Event1", true); DialogueLua.SetActorField(ToivoLie.Toivo, "Event4", true); },
            ["tanja-points5"] = () => DialogueLua.SetActorField(MiningPermit.Tanja, "Points", 5),
            ["age45"] = () => DialogueLua.SetActorField("Player", "Age", 45),
            ["eliana-first"] = () => DialogueLua.SetActorField(ElianaTalk.Eliana, "TalkedFirstTime", true),
        };

        internal static void Bind(ConfigFile c)
        {
            _setup = c.Bind("Testing", "Setup", "", "TEST BUILD ONLY: " + string.Join(", ", new List<string>(All.Keys).ToArray()));
            _setup.SettingChanged += (s, a) => Run();
            Log.Warn("Testing: this is a test build with state setters. Never package it.");
        }

        private static void Run()
        {
            string name = (_setup.Value ?? "").Trim();
            if (name.Length == 0) return;
            if (!All.TryGetValue(name, out var act) || TaskController.instance == null)
                Log.Warn($"Testing: no setup '{name}' or no game running.");
            else
            {
                try { act(); Log.Warn($"Testing: applied '{name}'."); }
                catch (Exception e) { Log.Exception("test setup", e); }
            }
            _setup.Value = "";
        }
    }
}
#endif

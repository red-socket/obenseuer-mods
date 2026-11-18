using System;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameHarness
{
    // Internal test harness: a localhost command channel that lets a script (tools/GameHarness/harness.py) drive the game.
    // Idle (no command received this session) it only listens on the port: the input patches see no virtual keys and
    // the save guard lets every save through. The first command arms the session; from then on saves are only allowed
    // for the test character (Saves/<TestCharacter>/), so a harness run can never write to the player's own saves.
    [BepInPlugin(Guid, Name, Version)]
    [BepInProcess("Obenseuer.exe")]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "redsocket.obenseuer.gameharness";
        public const string Name = "Game Harness";
        public const string Version = "0.1.0";
        public const string TestedGameVersion = "0.5.19";

        internal static Plugin Instance;
        internal static ManualLogSource Log;
        internal static ConfigEntry<int> Port;
        internal static ConfigEntry<string> TestCharacter;
        internal static ConfigEntry<bool> RunInBackground;

        // Set by the first command over the channel. Never reset: the game is a harness game from then on.
        internal static bool SessionActive;
        internal static int BlockedSaves;
        internal static float SceneLoadedAt;

        private Server _server;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Port = Config.Bind("Harness", "Port", 47913, "TCP port on 127.0.0.1 for harness.py. Localhost only.");
            TestCharacter = Config.Bind("Harness", "TestCharacter", "Harness_Test",
                "Save folder (Saves/<name>) of the test character. Once a harness session is active, saves for any other character are blocked.");
            RunInBackground = Config.Bind("Harness", "RunInBackground", true,
                "Keep the game running while its window is unfocused, so commands are served when another window has focus.");

            if (Application.version != TestedGameVersion)
                Log.LogWarning($"Built for {TestedGameVersion}, running on {Application.version}.");

            // One class at a time, so a failing patch cannot leave the save or achievement guards unapplied.
            var harmony = new Harmony(Guid);
            int patched = 0;
            foreach (var type in AccessTools.GetTypesFromAssembly(typeof(Plugin).Assembly))
            {
                if (type.GetCustomAttributes(typeof(HarmonyPatch), false).Length == 0) continue;
                try { harmony.CreateClassProcessor(type).Patch(); patched++; }
                catch (Exception e) { Log.LogError($"Patching {type.Name} failed: {e}"); }
            }
            Log.LogInfo($"{patched} patch classes applied.");

            SceneManager.sceneLoaded += (s, m) => { SceneLoadedAt = Time.realtimeSinceStartup; ApplyBackground(); };
            ApplyBackground();

            _server = new Server(Port.Value);
            _server.Start();
            Log.LogInfo($"{Name} {Version} listening on 127.0.0.1:{Port.Value}");
        }

        // The game's DisplaySave applies its own runInBackground on scene start, so reassert after each load.
        private static void ApplyBackground()
        {
            if (RunInBackground.Value) Application.runInBackground = true;
        }

        private void Update()
        {
            VirtualInput.Tick();
            _server.Drain(Commands.Dispatch);
        }

        private void OnDestroy() => _server?.Stop();

        internal static string CurrentCharacter()
        {
            try { return SaveController.SanitizeCharacterName(PlayerIdentity.identity.firstName, PlayerIdentity.identity.lastName); }
            catch { return "Esko_Virtanen"; }
        }

        internal static bool IsTestCharacter(string folder) =>
            string.Equals(Regex.Replace(folder ?? "", "[^\\w]", ""), TestCharacter.Value, StringComparison.Ordinal);
    }

    // Hard guard for the hard rule "never write to the player's saves". It only acts in an armed harness session.
    [HarmonyPatch(typeof(SaveController), nameof(SaveController.SaveGame))]
    internal static class SaveGuardPatch
    {
        private static bool Prefix(string savename)
        {
            if (!Plugin.SessionActive) return true;
            string who = Plugin.CurrentCharacter();
            if (Plugin.IsTestCharacter(who)) return true;
            Plugin.BlockedSaves++;
            Plugin.Log.LogError($"Blocked a save of character '{who}' (slot '{savename}'): harness sessions only save '{Plugin.TestCharacter.Value}'.");
            return false;
        }
    }
}

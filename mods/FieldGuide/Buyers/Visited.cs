using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine.SceneManagement;

namespace FieldGuide
{
    // The places the player has been in this playthrough (spec §9.2, D11), for Traders = Visited. Rebuilt each session
    // from the save folder, never stored: a save folder holds one <Scene>.tnmt per scene ever visited, because saving
    // copies the previous folder's scene files forward (SRC/SaveController.cs:532-550). The Quest Helper SaveFolder
    // pattern decides which folder: LastSaveFolderPath is set only by a load from disk (:1622, after
    // PlayerWillLoadGame), so it is scanned at LoadingDone only after such a load; GetCurrentSavePath() (:1302) is
    // scanned at SavingDone. Scenes loaded this session are added as they load. Every load clears the set first
    // (PlayerWillLoadGame, :1606), since it may be another character or an older save: the pause menu's Load
    // (SRC/LoadMenu.cs:551) never passes the main menu. A door is a save and a load too (SaveController.ChangeLevel),
    // so the set is rebuilt from the folder just saved, which holds every scene visited so far. The main menu also
    // clears it.
    internal static class Visited
    {

        private static readonly HashSet<string> Scanned = new HashSet<string>(StringComparer.Ordinal);
        private static readonly HashSet<string> Session = new HashSet<string>(StringComparer.Ordinal);
        private static bool _fileLoad;

        internal static int Version;

        internal static bool Any(HashSet<string> scenes)
        {
            foreach (var s in scenes)
                if (Session.Contains(s) || Scanned.Contains(s)) return true;
            return false;
        }

#if FIELDGUIDE_TESTS
        internal static HashSet<string> Union()
        {
            var u = new HashSet<string>(Scanned, StringComparer.Ordinal);
            u.UnionWith(Session);
            return u;
        }
#endif

        internal static void OnScene(Scene scene)
        {
            string name = scene.name;
            if (string.IsNullOrEmpty(name)) return;
            if (name == Game.MainMenuScene)
            {
                if (Scanned.Count > 0 || Session.Count > 0) Log.Verbose("Places: back at the main menu; cleared.");
                Reset();
                _fileLoad = false;
                return;
            }
            if (Session.Add(name)) Version++;
        }

        // Before the save's level loads: its sceneLoaded and the LoadingDone folder scan rebuild the set.
        internal static void OnPlayerWillLoadGame()
        {
            Reset();
            _fileLoad = true;
        }

        private static void Reset()
        {
            Scanned.Clear();
            Session.Clear();
            Version++;
        }

        internal static void OnLoadingDone()
        {
            if (!_fileLoad) return;
            _fileLoad = false;
            Scan(SaveController.LastSaveFolderPath, "load");
        }

        internal static void OnSavingDone()
        {
            var sc = SaveController.instance;
            if (sc != null) Scan(sc.GetCurrentSavePath(), "save");
        }

        private static void Scan(string folder, string why)
        {
            if (string.IsNullOrEmpty(folder)) return;
            try
            {
                if (!Directory.Exists(folder)) return;
                int before = Scanned.Count;
                foreach (var f in Directory.GetFiles(folder, "*.tnmt"))
                {
                    string n = Path.GetFileNameWithoutExtension(f);
                    if (string.IsNullOrEmpty(n) || n == "Info" || n == "Globals") continue;
                    Scanned.Add(n);
                }
                if (Scanned.Count != before) Version++;
                Log.Verbose($"Places: {Scanned.Count} places from the save folder ({why}), {Session.Count} this session.");
            }
            catch (Exception e)
            {
                Log.WarnOnce("places scan", $"Places: couldn't read the save folder ({e.Message}); Bought by names only " +
                    "traders in places visited this session.");
            }
        }
    }
}

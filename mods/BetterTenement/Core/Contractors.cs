using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;

namespace BetterTenement
{
    // Contractor lookups (spec §2.3). Ids and asset paths are from the 0.5.19 assets
    // (Assets/Content/Tenement/Contractors/*.asset).
    internal static class Contractors
    {
        // Scenes with a TenementResourceStorage. Bazaar Books has the fifth chest but no contractor.
        private static readonly Dictionary<string, string> SceneToId = new Dictionary<string, string>
        {
            { "Interior Tenement Deekula A", "SamuelJonasson" },
            { "Interior Bazaar Rob-in", "RobMadsen" },
            { "Interior Bazaar Engineer Office", "UrhoSiltanen" },
            { "Interior Mines OS Mining Services", "KenNakahara" },
            { "Interior Bazaar Books", null },
        };

        private static readonly string[] Paths =
        {
            "Assets/Content/Tenement/Contractors/Samuel Jonasson.asset",
            "Assets/Content/Tenement/Contractors/Rob Madsen.asset",
            "Assets/Content/Tenement/Contractors/Urho Siltanen.asset",
            "Assets/Content/Tenement/Contractors/Ken Nakahara.asset",
        };

        private static List<TenementContractor> _all;

        // The contractor whose chest this scene holds: the live one if a contractor was opened here, else the table.
        internal static string IdForActiveScene()
        {
            string live = null;
            try
            {
                var tc = TenementController.instance;
                if (tc != null && Vanilla.CurrentSceneContractor != null)
                {
                    var c = Vanilla.CurrentSceneContractor(tc);
                    if (c != null) live = c.Id;
                }
            }
            catch (Exception e) { Log.Exception("scene contractor", e); }
            string scene = SceneManager.GetActiveScene().name;
            SceneToId.TryGetValue(scene, out string table);
            if (live != null && table != null && live != table)
                Log.WarnOnce("contractor-mismatch-" + scene, $"Scene \"{scene}\" has contractor {live}, expected {table}; using {live}.");
            return live ?? table;
        }

        // The four contractors, each loaded once per session the way vanilla loads saved references.
        internal static List<TenementContractor> All
        {
            get
            {
                if (_all != null) return _all;
                var list = new List<TenementContractor>();
                foreach (var path in Paths)
                {
                    TenementContractor c = null;
                    try { c = SavableScriptableObject.LoadFromPath(path) as TenementContractor; }
                    catch (Exception e) { Log.Exception("load contractor", e); }
                    if (c == null) Log.WarnOnce("contractor-" + path, $"Contractor {path} could not be loaded; it is left out of \"who does this\" lines.");
                    else list.Add(c);
                }
                _all = list;
                return _all;
            }
        }

        // Contractors who offer this tier, in table order. Size follows vanilla UpgradeUI.cs:198.
        internal static List<TenementContractor> WhoCanDo(TenementUpgrade u, UpgradeType.ApartmentType size)
        {
            var result = new List<TenementContractor>();
            if (u == null) return result;
            foreach (var c in All)
            {
                try { if (c.CanUpgrade(u, size)) result.Add(c); }
                catch (Exception e) { Log.Exception("CanUpgrade", e); }
            }
            return result;
        }

        internal static string Names(List<TenementContractor> list, bool firstNames)
        {
            if (list == null || list.Count == 0) return null;
            var names = new List<string>();
            foreach (var c in list) names.Add(firstNames || list.Count > 1 ? Game.FirstName(c.contractorName) : c.contractorName);
            return string.Join(", ", names);
        }
    }
}

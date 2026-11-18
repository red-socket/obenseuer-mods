#if GRIDSENSE_TESTS
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx;
using BepInEx.Configuration;

namespace GridSense
{
    // Test build only (spec §10 T0b), compiled with -p:GridSenseTests=true (the Recipe Book precedent). Never packaged. At
    // the first level load it writes the panel and build-bar text for every console furniture and alt prefab to
    // BepInEx/GridSense-power-table.tsv and the log, and warns when an alt's text differs from its base prefab's. Diff the
    // file against the prefab dump and the expected values in .internal/test-plan.md. Reads prefabs only; changes nothing.
    internal static class PowerTable
    {
        internal static void Bind(ConfigFile c) => Log.Warn("Testing: this is a test build with the power table dump. Never package it.");

        internal static void Run()
        {
            var list = DeveloperConsole.Instance?.furniture?.furnitures;
            if (list == null)
            {
                Log.Warn("Testing: no console furniture list; power table skipped.");
                return;
            }
            var sb = new StringBuilder("id\ttitle\talt\tfull\tshort\n");
            int rows = 0, mismatches = 0;
            var seen = new HashSet<Furniture>();
            foreach (var f in list)
            {
                if (f == null || !seen.Add(f)) continue;
                int alts = f.AlternativePrefabs?.Length ?? 0;
                string baseText = null;
                for (int alt = 0; alt <= alts; alt++)
                {
                    var info = PowerInfo.Get(f, alt);
                    string full = info.IsEmpty ? "" : info.FullText, shortText = info.IsEmpty ? "" : info.ShortText;
                    string text = full + "|" + shortText;
                    if (alt == 0) baseText = text;
                    else if (text != baseText)
                    {
                        mismatches++;
                        Log.Warn($"Testing: {f.Id} alt {alt} differs from its base prefab: \"{text}\" vs \"{baseText}\".");
                    }
                    sb.Append(f.Id).Append('\t').Append(f.title).Append('\t').Append(alt).Append('\t').Append(full).Append('\t').Append(shortText).Append('\n');
                    rows++;
                }
            }
            string path = Path.Combine(Paths.BepInExRootPath, "GridSense-power-table.tsv");
            try { File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false)); }
            catch (Exception e) { Log.Exception("Testing: power table file", e); }
            Log.Warn($"Testing: power table: {rows} rows, {mismatches} alt mismatches, written to {path}.");
        }
    }
}
#endif

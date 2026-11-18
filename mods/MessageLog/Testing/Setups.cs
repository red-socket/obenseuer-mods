#if MESSAGELOG_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using BepInEx.Configuration;

namespace MessageLog
{
    // Test-only setters and dumps for the in-game test plan (spec §13), compiled only with -p:MessageLogTests=true
    // (Fair Market's FAIRMARKET_TESTS precedent). Never packaged. Set [Testing] Setup to "name args" while a game is
    // running; it applies once and resets itself to empty. Use spare slots: notify and burst go through the game's own
    // CreateNotification, so their toasts are saved like any other.
    internal static class Setups
    {
        private static readonly CultureInfo C = CultureInfo.InvariantCulture;
        private static ConfigEntry<string> _setup;

        private static readonly Dictionary<string, Action<string>> All = new Dictionary<string, Action<string>>(StringComparer.OrdinalIgnoreCase)
        {
            ["notify"] = a => { var (t, x) = TypeText(a); Notifications.instance.CreateNotification(t, x); },
            ["burst"] = a =>
            {
                int sp = a.IndexOf(' ');
                int n = Int(sp > 0 ? a.Substring(0, sp) : a);
                var (t, x) = TypeText(sp > 0 ? a.Substring(sp + 1) : "Item added!|Stick");
                for (int i = 0; i < n; i++) Notifications.instance.CreateNotification(t, x, silent: true);
            },
            ["fill"] = a => Fill(Int(a)),
            ["dump"] = a => Dump(),
            ["sidecar"] = a => SideCarInfo(),
            ["sidecar-ahead"] = a => Ahead(Int(a)),
            ["sidecar-other-char"] = a => Rewrite(l => l.StartsWith("character\t", StringComparison.Ordinal) ? "character\tSomeone_Else" : l),
            ["sidecar-v2"] = a => Rewrite(l => l.StartsWith("MessageLog\t", StringComparison.Ordinal) ? "MessageLog\t2" : l),
        };

        internal static void Bind(ConfigFile c)
        {
            _setup = c.Bind("Testing", "Setup", "", "TEST BUILD ONLY: " + string.Join(", ", All.Keys.ToArray()));
            _setup.SettingChanged += (s, a) => Run();
            Log.Warn("Testing: this is a test build with state setters. Never package it.");
        }

        private static int Int(string s) =>
            int.TryParse((s ?? "").Trim(), NumberStyles.Integer, C, out int v) ? v : throw new ArgumentException("a number is missing");

        private static (string, string) TypeText(string a)
        {
            int bar = (a ?? "").IndexOf('|');
            if (bar < 0) throw new ArgumentException("use <type>|<text>");
            return (a.Substring(0, bar), a.Substring(bar + 1));
        }

        private static void Run()
        {
            string line = (_setup.Value ?? "").Trim();
            if (line.Length == 0) return;
            int sp = line.IndexOf(' ');
            string name = sp > 0 ? line.Substring(0, sp) : line, args = sp > 0 ? line.Substring(sp + 1) : "";
            if (!All.TryGetValue(name, out var act) || GlobalState.instance == null)
                Log.Warn($"Testing: no setup '{name}' or no game running.");
            else
            {
                try { act(args); Log.Warn($"Testing: applied '{line}'."); }
                catch (Exception e) { Log.Exception("test setup", e); }
            }
            _setup.Value = "";
        }

        private static readonly string[] FillTypes =
        {
            "Item added!", "Money added!", "Crime committed:", "Mail: Test", "New Task", "Objective Complete", "Skill increased!", "Discovered",
        };

        // n synthetic notifications straight into the log (no toasts), with distinct texts so nothing merges.
        private static void Fill(int n)
        {
            for (int i = 0; i < n; i++)
                History.AddNotification(FillTypes[i % FillTypes.Length], "Test message " + i.ToString(C) + " with some text to make the line a typical length", false);
        }

        private static void Dump()
        {
            long lo = History.OldestSeq();
            Log.Warn($"Testing: notes {History.Notes.Count}, talk {History.Talk.Count}, pending {History.Pending.Count}, Seq {lo}..{History.NextSeq - 1}, " +
                     $"SavedSeq {History.SavedSeq}, character {History.Character ?? "-"}, version {History.Version}.");
            foreach (var cp in History.Cps) Log.Warn($"Testing: cp {cp.Info} -> {cp.Seq} (day {cp.Day} {cp.Hm})");
        }

        private static string Dir() => SaveController.LastSaveFolderPath ?? throw new InvalidOperationException("no save folder yet");

        private static void SideCarInfo()
        {
            string p = Path.Combine(Dir(), SideCar.FileName);
            if (!File.Exists(p)) { Log.Warn($"Testing: no {p}."); return; }
            var lines = File.ReadAllLines(p);
            Log.Warn($"Testing: {p}: {new FileInfo(p).Length / 1024} KB, {lines.Length} lines; {string.Join(" | ", lines.Take(3).ToArray())}; last cp: " +
                     (lines.LastOrDefault(l => l.StartsWith("cp\t", StringComparison.Ordinal)) ?? "-") +
                     $"; Info Date: {SideCar.ReadInfoDate(Dir()) ?? "-"}");
        }

        // k entries a day after the newest entry, then a checkpoint dated tomorrow: the next load of this slot hits the
        // "newer" rule and time-trims them.
        private static void Ahead(int k)
        {
            string p = Path.Combine(Dir(), SideCar.FileName);
            var lines = File.ReadAllLines(p).ToList();
            long seq = 0;
            int day = 0;
            foreach (var l in lines)
            {
                var f = l.Split('\t');
                if (f.Length >= 9 && f[0].Length == 1 && long.TryParse(f[1], NumberStyles.Integer, C, out long s))
                {
                    seq = Math.Max(seq, s);
                    int.TryParse(f[2], NumberStyles.Integer, C, out day);
                }
            }
            for (int i = 0; i < k; i++) lines.Add($"n\t{seq + 1 + i}\t{day + 1}\t600\t0\t0\t1\tTest ahead\tFuture message {i}");
            lines.Add($"cp\t{DateTimeOffset.Now.AddDays(1).ToString("o", C)}\t{seq + k}\t{day + 1}\t600");
            File.WriteAllText(p, string.Join("\n", lines.ToArray()) + "\n", new UTF8Encoding(false));
        }

        private static void Rewrite(Func<string, string> f)
        {
            string p = Path.Combine(Dir(), SideCar.FileName);
            var lines = File.ReadAllLines(p).Select(f).ToArray();
            File.WriteAllText(p, string.Join("\n", lines) + "\n", new UTF8Encoding(false));
        }
    }
}
#endif

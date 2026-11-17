using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using UnityEngine;

namespace QuestHelper
{
    // Where each objective became active (spec §5.1) and the fixes applied (§4.6 step 8), kept in memory and saved as one
    // plain string in GlobalState "questhelper.journal" (spec §8), written by P5 inside the game's own save (Globals.tnmt,
    // in Save Guard's transaction). Format, one record per line, tab-separated:
    //   v1
    //   t <taskId>
    //   r <taskId_objId> <scene> <mapId> <x> <y> <z> <giver>      (x/y/z empty when unknown)
    //   d <utcIso> <fixId> <variant|generic:op> <gameVersion> <backupKind>
    // An unknown version is kept as it is: nothing is read and nothing is written for that load.
    internal static class Records
    {
        internal const string Key = "questhelper.journal";
        private const string Version = "v1";
        private const int MaxDoctorLines = 50;

        internal sealed class Rec
        {
            internal string TaskId, Key, Scene, Map, Giver;
            internal Vector3? Pos;
            internal long Seq;
        }

        private enum LoadState { Waiting, Read, Blocked }

        private static readonly Dictionary<string, Rec> All = new Dictionary<string, Rec>();
        private static readonly List<string[]> Doctor = new List<string[]>();
        private static long _seq;
        private static LoadState _state = LoadState.Read;   // a game started from the menu with no save reads nothing
        private static int _errors;

        internal static bool ReadThisLoad => _state == LoadState.Read;

        internal static Rec Get(string key) => key != null && All.TryGetValue(key, out var r) ? r : null;

        // LoadingStarted / PlayerWillChangeLevel: the records in memory belong to the previous load until P6 reads the new
        // one; a new game (no globals to read) starts empty.
        internal static void OnLoadingStarted()
        {
            All.Clear();
            Doctor.Clear();
            Tracking.Selected = null;
            _state = LoadState.Waiting;
        }

        // The main menu: nothing of the previous character may reach the next game. A new game never sends
        // LoadingStarted (NewGame and SkipIntro load through LoadingScreen.LoadLevel), so this is the only reset before
        // its first save; it has no journal of ours to protect, so it may write.
        internal static void OnMainMenu()
        {
            All.Clear();
            Doctor.Clear();
            Tracking.Selected = null;
            _seq = 0;
            _state = LoadState.Read;
        }

        // LoadingDone without P6 (a new game, or the store group off): nothing of ours was in the save, so writing is safe.
        internal static void OnLoadingDone()
        {
            if (_state == LoadState.Waiting) _state = LoadState.Read;
        }

        // P1: a newer activation of the same objective overwrites the older one.
        internal static void Record(TaskStatusInfo tsi, ObjectiveStatusInfo osi)
        {
            if (_errors >= 3) return;
            try
            {
                string key = TaskState.Key(tsi, osi);
                var r = new Rec { TaskId = tsi.task.Id, Key = key, Scene = Game.SceneName, Map = Game.CurrentMapId, Seq = ++_seq };
                bool talking = Game.InConversation;
                Transform conversant = null;
                if (talking)
                {
                    try { conversant = PixelCrushers.DialogueSystem.DialogueManager.currentConversant; } catch { conversant = null; }
                }
                if (conversant != null) r.Pos = conversant.position;
                else if (PlayerLocator.instance != null && PlayerLocator.instance.root != null) r.Pos = PlayerLocator.instance.root.position;
                if (talking) r.Giver = GiverOf(conversant);
                All[key] = r;
                Log.Verbose($"Journal: {key} in {r.Scene} ({(r.Pos.HasValue ? r.Pos.Value.ToString() : "no position")}), giver '{r.Giver}'.");
            }
            catch (Exception e)
            {
                _errors++;
                Log.Exception("journal record", e);
                if (_errors >= 3) Log.Warn("Journal: recording stopped for this session after 3 errors.");
            }
        }

        private static string GiverOf(Transform conversant)
        {
            try
            {
                var info = conversant != null ? conversant.GetComponentInParent<NPCInfo>() : null;
                string name = info != null && info.character != null ? info.character.FullName : null;
                if (!string.IsNullOrEmpty(name)) return Game.Clean(name);
            }
            catch (Exception e) { Log.Exception("journal giver", e); }
            try { return Game.Clean(Game.GiverFromConversation(PixelCrushers.DialogueSystem.DialogueManager.lastConversationStarted)); }
            catch { return ""; }
        }

        internal static void AddDoctor(string fixId, string variant, string backupKind)
        {
            Doctor.Add(new[] { DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture), fixId, variant ?? "", Application.version, backupKind ?? "" });
            while (Doctor.Count > MaxDoctorLines) Doctor.RemoveAt(0);
        }

        // "2026-11-17 14:02 UTC: QD-RAT (counter)" for the window.
        internal static List<string> DoctorLines() =>
            Doctor.Select(d => $"{d[0].Replace('T', ' ').Replace("Z", " UTC")}: {d[1]}{(d[2].Length > 0 ? " (" + d[2] + ")" : "")}").ToList();

        // ---- P5 / P6 ----

        internal static string Serialize()
        {
            var sb = new StringBuilder();
            sb.Append(Version).Append('\n');
            if (!string.IsNullOrEmpty(Tracking.Selected)) sb.Append("t\t").Append(Game.Clean(Tracking.Selected)).Append('\n');
            int max = Cfg.Journal.MaxRecords.Value;
            var keep = All.Values.Where(r => TaskState.TaskActive(r.TaskId)).OrderByDescending(r => r.Seq).Take(max).OrderBy(r => r.Seq).ToList();
            foreach (var r in keep)
            {
                sb.Append("r\t").Append(Game.Clean(r.Key)).Append('\t').Append(Game.Clean(r.Scene)).Append('\t').Append(Game.Clean(r.Map)).Append('\t');
                if (r.Pos.HasValue) sb.Append(F(r.Pos.Value.x)).Append('\t').Append(F(r.Pos.Value.y)).Append('\t').Append(F(r.Pos.Value.z));
                else sb.Append("\t\t");
                sb.Append('\t').Append(Game.Clean(r.Giver)).Append('\n');
            }
            foreach (var d in Doctor)
                sb.Append("d\t").Append(string.Join("\t", d.Select(Game.Clean).ToArray())).Append('\n');
            // Drop what was not written, so memory and save agree.
            foreach (var k in All.Keys.Where(k => !keep.Exists(r => r.Key == k)).ToList()) All.Remove(k);
            return sb.ToString();
        }

        private static string F(float v) => v.ToString("0.0", CultureInfo.InvariantCulture);

        // P5 prefix body.
        internal static void OnSaving()
        {
            if (!ReadThisLoad || GlobalState.instance == null) return;
            GlobalState.instance.SetState(Key, true, Serialize());
            Log.Verbose($"Journal: saved {All.Count} record(s).");
        }

        // P6 postfix body.
        internal static void OnGlobalsLoaded()
        {
            All.Clear();
            Doctor.Clear();
            var raw = GlobalState.instance?.GetState(Key)?.data;
            if (raw == null) { _state = LoadState.Read; return; }
            string text = raw as string;
            var lines = text != null ? text.Split('\n') : new string[0];
            if (lines.Length == 0 || lines[0].Trim() != Version)
            {
                _state = LoadState.Blocked;
                Log.WarnOnce("journal version", $"Journal: the save's {Key} entry has an unknown format; it is kept as it is and not updated this session.");
                return;
            }
            foreach (var line in lines.Skip(1))
            {
                var f = line.Split('\t');
                if (f.Length == 0) continue;
                try
                {
                    if (f[0] == "t" && f.Length >= 2) Tracking.Selected = f[1];
                    else if (f[0] == "r" && f.Length >= 8)
                    {
                        int us = f[1].IndexOf('_');
                        if (us <= 0) continue;
                        var r = new Rec { Key = f[1], TaskId = f[1].Substring(0, us), Scene = f[2], Map = f[3], Giver = f[7], Seq = ++_seq };
                        if (P(f[4], out float x) && P(f[5], out float y) && P(f[6], out float z)) r.Pos = new Vector3(x, y, z);
                        All[r.Key] = r;
                    }
                    else if (f[0] == "d" && f.Length >= 6) Doctor.Add(f.Skip(1).Take(5).ToArray());
                }
                catch (Exception e) { Log.Exception("journal line", e); }
            }
            _state = LoadState.Read;
            Log.Verbose($"Journal: read {All.Count} record(s), {Doctor.Count} fix line(s).");
        }

        private static bool P(string s, out float v) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
    }
}

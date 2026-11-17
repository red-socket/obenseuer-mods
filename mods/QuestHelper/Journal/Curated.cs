using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FullSerializer;
using UnityEngine;

namespace QuestHelper
{
    // One requirement: a TaskItem id (or asset path), an item id and count, an RM or OC amount, a finished objective, or
    // "any" of several of those.
    internal sealed class Need
    {
        internal string TaskItem, ObjCompleted, Label;
        internal int Item, N = 1, Rm, Oc;
        // A liquid item (held in a container): litres needed and the lowest quality accepted, as the dialogue's
        // CheckForItemDialogueLiquidQuality(item, liquid, minQuality).
        internal double Liquid, MinQuality;
        internal bool ShowCount;
        internal List<Need> Any;
    }

    internal sealed class Row
    {
        internal string Key, Conv, Scene, Landmark, T1, T2, T3;   // "src" in the JSON is for the validator only
        internal readonly List<Need> Needs = new List<Need>();

        // The highest hint at or below the tier.
        internal string HintFor(int tier) =>
            tier >= 3 && !string.IsNullOrEmpty(T3) ? T3 : tier >= 2 && !string.IsNullOrEmpty(T2) ? T2 : tier >= 1 && !string.IsNullOrEmpty(T1) ? T1 : null;
    }

    internal sealed class LandmarkDef
    {
        internal string Id, Path, Map;
        internal Landmark Asset;
        internal bool Tried;
    }

    internal sealed class Checkpoint
    {
        internal string Title, Note, Where, ShowTask, ShowObjTask, ShowObj;
        internal readonly List<Need> Needs = new List<Need>();

        internal bool Shows()
        {
            if (!string.IsNullOrEmpty(ShowTask)) return TaskState.TaskAny(ShowTask);
            if (!string.IsNullOrEmpty(ShowObjTask)) return TaskState.ObjAny(ShowObjTask, ShowObj);
            return true;
        }
    }

    internal sealed class ChainTask
    {
        internal string Task, Title;
    }

    // Data/curated.json (spec §6), embedded in the DLL and read with the game's own FullSerializer parser. Every entry was
    // checked against the dialogue database, the task and TaskItem assets, Items.json and the landmark assets
    // (.internal/validate_curated.py). A load or validation failure turns off hints, blockers, curated pins and the
    // Bazaar page for the session; everything else keeps working.
    internal static class Curated
    {
        private const string Resource = "QuestHelper.Data.curated.json";

        internal static bool Ok;
        internal static readonly Dictionary<string, Row> Rows = new Dictionary<string, Row>();
        internal static readonly Dictionary<string, LandmarkDef> Landmarks = new Dictionary<string, LandmarkDef>();
        internal static readonly List<Checkpoint> Checkpoints = new List<Checkpoint>();
        internal static readonly List<ChainTask> Chain = new List<ChainTask>();

        internal static Row Get(string key) => Ok && key != null && Rows.TryGetValue(key, out var r) ? r : null;

        internal static void Load()
        {
            try
            {
                string json;
                using (var s = typeof(Curated).Assembly.GetManifestResourceStream(Resource))
                {
                    if (s == null) throw new InvalidDataException("embedded resource missing");
                    using (var r = new StreamReader(s)) json = r.ReadToEnd();
                }
                var res = fsJsonParser.Parse(json, out fsData root);
                if (res.Failed) throw new InvalidDataException(res.FormattedMessages);
                var top = root.AsDictionary;
                foreach (var l in List(top, "landmarks"))
                {
                    var d = l.AsDictionary;
                    var def = new LandmarkDef { Id = Str(d, "id"), Path = Str(d, "path"), Map = Game.MapIdOf(Str(d, "map")) };
                    if (string.IsNullOrEmpty(def.Id) || Landmarks.ContainsKey(def.Id)) throw new InvalidDataException("bad or duplicate landmark " + def.Id);
                    Landmarks[def.Id] = def;
                }
                foreach (var r in List(top, "rows"))
                {
                    var d = r.AsDictionary;
                    var row = new Row
                    {
                        Key = Str(d, "key"), Conv = Str(d, "conv"), Scene = Str(d, "scene"), Landmark = Str(d, "landmark"),
                        T1 = Str(d, "t1"), T2 = Str(d, "t2"), T3 = Str(d, "t3"),
                    };
                    if (string.IsNullOrEmpty(row.Key) || Rows.ContainsKey(row.Key)) throw new InvalidDataException("bad or duplicate row " + row.Key);
                    if (!string.IsNullOrEmpty(row.Landmark) && !Landmarks.ContainsKey(row.Landmark)) throw new InvalidDataException(row.Key + ": unknown landmark " + row.Landmark);
                    row.Needs.AddRange(Needs(d));
                    Rows[row.Key] = row;
                }
                foreach (var c in List(top, "checkpoints"))
                {
                    var d = c.AsDictionary;
                    var cp = new Checkpoint { Title = Str(d, "title"), Note = Str(d, "note"), Where = Str(d, "where"), ShowTask = Str(d, "showIfTask") };
                    string obj = Str(d, "showIfObjAny");
                    if (!string.IsNullOrEmpty(obj))
                    {
                        int us = obj.IndexOf('_');
                        cp.ShowObjTask = obj.Substring(0, us);
                        cp.ShowObj = obj.Substring(us + 1);
                    }
                    cp.Needs.AddRange(Needs(d));
                    Checkpoints.Add(cp);
                }
                foreach (var c in List(top, "chain"))
                {
                    var d = c.AsDictionary;
                    Chain.Add(new ChainTask { Task = Str(d, "task"), Title = Str(d, "title") });
                }
                Ok = true;
                Log.Info($"Data: {Rows.Count} rows, {Landmarks.Count} landmarks, {Checkpoints.Count} checkpoints (game {Str(top, "game")}).");
            }
            catch (Exception e)
            {
                Ok = false;
                Rows.Clear();
                Landmarks.Clear();
                Checkpoints.Clear();
                Chain.Clear();
                Log.Error($"Data: the hint data could not be read ({e.Message}); hints, map pins from hints and the Bazaar page are off this session.");
            }
        }

        // At the first LoadingDone: a landmark that does not load drops its rows' pins, with one warning.
        internal static Landmark LandmarkAsset(string id)
        {
            if (!Ok || id == null || !Landmarks.TryGetValue(id, out var def)) return null;
            if (!def.Tried)
            {
                def.Tried = true;
                try { def.Asset = SavableScriptableObject.LoadFromPath(def.Path) as Landmark; }
                catch (Exception e) { Log.Exception("landmark " + def.Path, e); }
                if (def.Asset == null) Log.Warn($"Data: landmark {def.Path} did not load; hints that use it get no pin.");
            }
            return def.Asset;
        }

        internal static void LoadLandmarks()
        {
            foreach (var id in Landmarks.Keys.ToList()) LandmarkAsset(id);
        }

        internal static string MapOf(string landmarkId) => landmarkId != null && Landmarks.TryGetValue(landmarkId, out var d) ? d.Map : null;

        private static IEnumerable<Need> Needs(Dictionary<string, fsData> d)
        {
            foreach (var n in List(d, "needs")) yield return ParseNeed(n.AsDictionary);
        }

        private static Need ParseNeed(Dictionary<string, fsData> d)
        {
            var n = new Need
            {
                TaskItem = Str(d, "taskItem"), ObjCompleted = Str(d, "objCompleted"), Label = Str(d, "label"),
                Item = Int(d, "item"), N = Math.Max(1, Int(d, "n")), Rm = Int(d, "rm"), Oc = Int(d, "oc"),
                Liquid = Dbl(d, "liquid"), MinQuality = Dbl(d, "minQuality"),
                ShowCount = d.TryGetValue("showCount", out var sc) && sc.IsBool && sc.AsBool,
            };
            if (d.ContainsKey("any")) n.Any = List(d, "any").Select(x => ParseNeed(x.AsDictionary)).ToList();
            return n;
        }

        private static List<fsData> List(Dictionary<string, fsData> d, string k) =>
            d.TryGetValue(k, out var v) && v.IsList ? v.AsList : new List<fsData>();

        private static string Str(Dictionary<string, fsData> d, string k) =>
            d.TryGetValue(k, out var v) && v.IsString ? v.AsString : null;

        private static double Dbl(Dictionary<string, fsData> d, string k)
        {
            if (!d.TryGetValue(k, out var v)) return 0;
            if (v.IsDouble) return v.AsDouble;
            if (v.IsInt64) return v.AsInt64;
            return 0;
        }

        private static int Int(Dictionary<string, fsData> d, string k)
        {
            if (!d.TryGetValue(k, out var v)) return 0;
            if (v.IsInt64) return (int)v.AsInt64;
            if (v.IsDouble) return (int)v.AsDouble;
            return 0;
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace SafeTravels
{
    // Consist repair (S-021 c, spec §4.7, #5988). The game keeps each consist twice: the coupler links and
    // TrainData.wagons, and reconciles them only on some paths (SRC/TrackTrain.cs:243-283, 2090-2189). After a load, a
    // coupling or an uncoupling (Dirty), once the scene is settled, Check compares the two and calls the game's own
    // RebuildTrainDataFromCouplers on the first mismatch. The mod never writes `wagons`, `invertedDirection` or the
    // driver itself.
    internal static class Consist
    {
        internal static bool Patched;

        // Set by P6/P7/P9 and Rerail. Read from LateUpdate only.
        internal static bool Dirty;

        private const int MaxDetachedRetries = 120;

        private static bool _linkEvent;          // a Release or Couple since the last repair
        private static string _lastRepair;       // reason of the last repair, until a check passes
        private static bool _gaveUp;
        private static int _detachedRetries;

        private static readonly List<TrackTrain> Trains = new List<TrackTrain>();
        private static readonly HashSet<string> Graph = new HashSet<string>();
        private static readonly HashSet<string> Listed = new HashSet<string>();
        private static readonly Queue<TrackTrain> Bfs = new Queue<TrackTrain>();
        private static readonly HashSet<TrackTrain> Done = new HashSet<TrackTrain>();

        // P6/P7: a link changed.
        internal static void LinkChanged()
        {
            Dirty = true;
            _linkEvent = true;
            _gaveUp = false;
        }

        internal static void OnSceneLoaded()
        {
            Dirty = false;
            _linkEvent = false;
            _lastRepair = null;
            _gaveUp = false;
            _detachedRetries = 0;
        }

        // Plugin.LateUpdate: at most one check per frame, only once the scene is settled.
        internal static void LateTick()
        {
            if (!Dirty || !Patched || !Cfg.Consist.Enabled.Value || !TrainWatch.Settled) return;
            Dirty = false;
            if (_gaveUp) return;
            Check();
        }

        private static void Check()
        {
            var tc = TrainController.instance;
            if (tc == null || tc.TrackTrains == null) return;
            Trains.Clear();
            foreach (var t in tc.TrackTrains.Values)
                if (t != null) Trains.Add(t);

            string reason = null;
            TrackTrain at = null;
            foreach (var t in Trains)
            {
                reason = Mismatch(tc, t, out bool detached);
                if (detached)
                {
                    if (++_detachedRetries <= MaxDetachedRetries) Dirty = true;
                    else Log.WarnOnce("consist detached", "Consist: a coupling points at a train that is not loaded; check skipped.");
                    if (_detachedRetries == 1) Log.Verbose($"Consist: check deferred (detached coupler on {t.GUID})");
                    return;
                }
                if (reason != null) { at = t; break; }
            }
            _detachedRetries = 0;

            if (reason == null)
            {
                if (_lastRepair != null) Log.Verbose($"Consist: repair confirmed ({_lastRepair})");
                else Log.Verbose($"Consist: check passed ({Trains.Count} trains)");
                _lastRepair = null;
                return;
            }
            if (_lastRepair == reason && !_linkEvent)
            {
                _gaveUp = true;
                Log.WarnOnce("consist unresolved|" + reason, $"Consist: the game's rebuild did not fix it ({reason} on {at.GUID}, {Game.SceneName}); left alone.");
                return;
            }

            TrackTrain.RebuildTrainDataFromCouplers();
            Done.Clear();
            foreach (var t in Trains)
            {
                if (t == null || t.train == null) continue;
                var driver = t.train.drivingLocomotive;
                if (driver != null && Done.Add(driver)) driver.UpdateWagonActivationStatus();
            }
            Log.Info($"Consist: repaired ({reason} on {at.GUID}, {Game.SceneName})");
            _lastRepair = reason;
            _linkEvent = false;
            Dirty = true;   // verify on the next frame
        }

        // The first rule t breaks, or null. `detached`: a coupler points at a deserialized object with no parent yet.
        private static string Mismatch(TrainController tc, TrackTrain t, out bool detached)
        {
            detached = false;
            var data = t.train;
            if (data == null || data.wagons == null) return "no train data";

            int self = 0;
            Listed.Clear();
            foreach (var w in data.wagons)
            {
                if (w == null || w.wagon == null) return "dead wagon entry";
                if (w.wagon == t) self++;
                if (w.wagon.train != data) return "wagon listed in another train";
                Listed.Add(w.wagon.GUID ?? "");
            }
            if (self != 1) return self == 0 ? "train missing from its own wagon list" : "train listed twice";

            foreach (var c in new[] { t.frontCoupler, t.backCoupler })
            {
                if (c == null || c.coupledTo == null) continue;
                if (c.coupledTo.parent == null) { detached = true; return null; }
                if (c.coupledTo.coupledTo != c)
                {
                    Log.WarnOnce("half link|" + t.GUID, $"Consist: one-sided coupling on {t.GUID}; left alone.");
                    continue;
                }
                if (c.coupledTo.parent.train != data) return "coupled trains in different consists";
            }

            // The coupler graph from t gives exactly the wagon list.
            Graph.Clear();
            Bfs.Clear();
            Graph.Add(t.GUID ?? "");
            Bfs.Enqueue(t);
            while (Bfs.Count > 0)
            {
                var n = Bfs.Dequeue();
                Neighbour(tc, n.frontCoupler);
                Neighbour(tc, n.backCoupler);
            }
            if (!Graph.SetEquals(Listed)) return "couplings and wagon list differ";
            return null;
        }

        private static void Neighbour(TrainController tc, TrackTrain.TrainCoupler c)
        {
            if (c == null || c.coupledTo == null) return;
            var other = c.coupledTo.parent;
            if (other == null && !string.IsNullOrEmpty(c.coupledTo.parentGUID)) other = tc.GetTrackTrain(c.coupledTo.parentGUID);
            if (other == null) return;
            if (Graph.Add(other.GUID ?? "")) Bfs.Enqueue(other);
        }
    }
}

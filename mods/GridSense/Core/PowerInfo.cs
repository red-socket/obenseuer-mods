using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace GridSense
{
    // One power consumer inside a furniture prefab (spec §4.1). Idle -1 = no idle value. Name is null for the flat part
    // (the sum of the prefab's PowerGridListeners).
    internal sealed class Part
    {
        public string Name;
        public int Active;
        public int Idle = -1;
        public bool Station;
    }

    // What a furniture prefab draws from and gives to the grid, read from the prefab asset only (spec §4.1). The rules
    // mirror PowerSource.UsesGrid (SRC/PowerSource.cs:214-230), Generator.Start (SRC/Generator.cs:40-48), the manufacturing
    // idle override (SRC/manufacturing.cs:157-174) and BatteryPowerSource.ManagePowerGrid (SRC/BatteryPowerSource.cs:228-264).
    // Never reads difficulty, grid, contract or billing state: difficulty scales the supply only (SRC/DifficultyInfo.cs:283).
    internal sealed class PowerInfo
    {
        internal static readonly PowerInfo Empty = new PowerInfo();

        public readonly List<Part> Parts = new List<Part>();
        public int Supply;                          // linked Generator output
        public int BatteryPerStack, BatterySlots;   // 0 = none / unknown
        public bool BatteryCharges;

        public bool IsEmpty => Parts.Count == 0 && Supply == 0 && BatteryPerStack == 0;

        private string _full, _short, _stand;
        internal string FullText => _full ?? (_full = PowerText.Full(this));
        internal string ShortText => _short ?? (_short = PowerText.Short(this));
        // P4's " - 50 W" for a display stand, built once; "" when there is nothing to show (Empty included).
        internal string StandSuffix => _stand ?? (_stand = IsEmpty || string.IsNullOrEmpty(ShortText) ? "" : " - " + ShortText);

        // Prefab values are serialized constants, so entries are never cleared. Keyed by reference; the prefab is kept so
        // a Furniture whose prefab changes (the build menu's throwaway item Furniture, SRC/BuildingSystem.cs:792) is read again.
        private sealed class Entry
        {
            public GameObject Prefab;
            public PowerInfo Info;
        }

        private sealed class KeyComparer : IEqualityComparer<(Furniture, int)>
        {
            public bool Equals((Furniture, int) a, (Furniture, int) b) => ReferenceEquals(a.Item1, b.Item1) && a.Item2 == b.Item2;
            public int GetHashCode((Furniture, int) k) => RuntimeHelpers.GetHashCode(k.Item1) * 31 + k.Item2;
        }

        private static readonly Dictionary<(Furniture, int), Entry> Cache = new Dictionary<(Furniture, int), Entry>(new KeyComparer());

        // Never throws, never returns null.
        internal static PowerInfo Get(Furniture f, int alt)
        {
            if ((object)f == null) return Empty;
            GameObject prefab = null;
            try
            {
                // Never previewPrefab (no power components), never FurnitureInfo.gameObject (a live instance with the idle
                // override, the battery's current draw and Generator's negated consumption), never Instantiate.
                prefab = f.GetPrefab(alt) ?? f.prefab;
                if (Cache.TryGetValue((f, alt), out var e) && ReferenceEquals(e.Prefab, prefab)) return e.Info;
                var info = prefab == null ? Empty : Read(prefab);
                Cache[(f, alt)] = new Entry { Prefab = prefab, Info = info };
                if (!info.IsEmpty && Log.VerboseOn) Log.Verbose($"Watts: {f.Id} alt {alt}: \"{info.FullText}\" / \"{info.ShortText}\"");
                return info;
            }
            catch (Exception ex)
            {
                string id = "?";
                try { id = f.Id; } catch { }
                Log.Exception($"Watts: {id} alt {alt} could not be read", ex);
                Cache[(f, alt)] = new Entry { Prefab = prefab, Info = Empty };
                return Empty;
            }
        }

        // The prefab asset's serialized values: Awake and Start have not run on it.
        private static PowerInfo Read(GameObject prefab)
        {
            var info = new PowerInfo();

            var batteries = prefab.GetComponentsInChildren<BatteryPowerSource>(true);
            var batteryListeners = new HashSet<PowerGridListener>();
            foreach (var b in batteries)
            {
                var l = Vanilla.BatteryListener(b);
                if (l != null) batteryListeners.Add(l);
            }

            foreach (var cb in prefab.GetComponentsInChildren<CraftingBase>(true))
            {
                var p = cb._Power;
                if (p == null) continue;
                // Generator.Start: a linked generator with positive output supplies the grid. Its _Power.powerConsumption is
                // never read. The Compressor (powergeneration -750) falls through as a 30 W consumer, which is its real draw.
                if (cb is Generator g && g.linkToGrid && g.powergeneration > 0)
                {
                    info.Supply += g.powergeneration;
                    continue;
                }
                // PowerSource.UsesGrid: AlwaysOn, fuel, combustion and kinetic machines never join the grid.
                if (cb.AlwaysOn || p.Type != PowerSource.PowerSourceType.Electricity || p.RequiresFuel || p.powerConsumption <= 0) continue;
                bool station = cb is manufacturing || cb is manufacturingProcess;   // growers and consumer Generators are not
                int idle = cb is manufacturing && cb.PowerConsumptionIdle > -1 ? cb.PowerConsumptionIdle : -1;   // Dishwasher inherits
                info.Parts.Add(new Part { Name = cb.ManuName, Active = p.powerConsumption, Idle = idle, Station = station });
            }

            int flat = 0;
            foreach (var l in prefab.GetComponentsInChildren<PowerGridListener>(true))
                if (!batteryListeners.Contains(l) && l.PowerConsumption > 0) flat += l.PowerConsumption;
            if (flat > 0) info.Parts.Add(new Part { Name = null, Active = flat });

            // A battery without a listener has no effect on the grid (ManagePowerGrid returns at once).
            foreach (var b in batteries)
            {
                if (Vanilla.BatteryListener(b) == null) continue;
                info.BatteryPerStack = Vanilla.BatteryPerStack(b);
                var storage = b.GetComponent<Storage>();
                info.BatterySlots = storage != null ? Math.Max(0, Vanilla.StorageSlots(storage)) : 0;
                info.BatteryCharges = b.bidirectional;
            }
            if (info.BatteryPerStack < 0) info.BatteryPerStack = 0;
            return info;
        }
    }
}

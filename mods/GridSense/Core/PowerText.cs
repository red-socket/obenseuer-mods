using System;
using System.Collections.Generic;
using UnityEngine;

namespace GridSense
{
    // Every player-facing string Grid Sense shows (spec §4.3). Plain text, no rich tags. Public text: humanized 2026-11-17;
    // run any changed wording through the humanizer again (CLAUDE.md). Numbers come from the game's own formatter, so they
    // match the crafting panel ("130 W/1.3 kW") and the recipe line character for character.
    internal static class PowerText
    {
        private const string Label = "Power: ";
        private const string Crafting = " crafting";
        private const string Idle = " idle";
        private const string CraftingOrIdle = ", crafting or idle";
        private const string Plus = "plus ";
        private const string Supplies = "supplies ";
        private const string WhileRunning = " while running";
        private const string PerChargedBattery = " per charged battery";
        private const string UpTo = "up to ";
        private const string PerBattery = " per battery";
        private const string Draws = "draws up to ";
        private const string OfSpareWhileCharging = " of spare power while charging";
        private const string WhileCharging = " while charging";
        private const string ToCharge = " to charge";

        // Shop and workbench panel: "Power: 1.3 kW crafting, 130 W idle".
        internal static string Full(PowerInfo info)
        {
            var clauses = new List<string>(3);
            string draw = FullDraw(info.Parts);
            if (draw != null) clauses.Add(draw);
            if (info.Supply > 0) clauses.Add(Supplies + Watts(info.Supply) + WhileRunning);
            if (info.BatteryPerStack > 0)
            {
                string s = Supplies + Watts(info.BatteryPerStack) + PerChargedBattery;
                if (info.BatterySlots > 0) s += ", " + UpTo + Watts(info.BatteryPerStack * info.BatterySlots);
                // Charging is a real grid draw: BatteryPowerSource.ManagePowerGrid (SRC/BatteryPowerSource.cs:236-244) sets
                // up to powerbalanceperstack x charging stacks as consumption, which reaches NetBalance and the bill.
                if (info.BatteryCharges)
                    s += "; " + (info.BatterySlots > 0
                        ? Draws + Watts(info.BatteryPerStack * info.BatterySlots) + OfSpareWhileCharging
                        : Draws + Watts(info.BatteryPerStack) + PerBattery + WhileCharging);
                clauses.Add(s);
            }
            return clauses.Count == 0 ? null : Label + string.Join("; ", clauses.ToArray());
        }

        // Build bar suffix: "1.3 kW (130 W idle)".
        internal static string Short(PowerInfo info)
        {
            var clauses = new List<string>(3);
            var parts = info.Parts;
            if (parts.Count == 1)
            {
                var p = parts[0];
                clauses.Add(p.Station && p.Idle > -1 ? Watts(p.Active) + " (" + Watts(p.Idle) + Idle + ")" : Watts(p.Active));
            }
            else if (parts.Count > 1)
            {
                var xs = new string[parts.Count];
                for (int i = 0; i < parts.Count; i++) xs[i] = Watts(parts[i].Active);
                clauses.Add(string.Join(" + ", xs));
            }
            if (info.Supply > 0) clauses.Add(Supplies + Watts(info.Supply));
            if (info.BatteryPerStack > 0)
            {
                string amount = info.BatterySlots > 0
                    ? UpTo + Watts(info.BatteryPerStack * info.BatterySlots)
                    : Watts(info.BatteryPerStack) + PerBattery;
                clauses.Add(Supplies + amount);
                if (info.BatteryCharges) clauses.Add("draws " + amount + ToCharge);
            }
            return clauses.Count == 0 ? null : string.Join(", ", clauses.ToArray());
        }

        private static string FullDraw(List<Part> parts)
        {
            if (parts.Count == 0) return null;
            if (parts.Count == 1) return Own(parts[0]);

            // Names only when every named part has a distinct, non-empty ManuName.
            var seen = new HashSet<string>(StringComparer.Ordinal);
            bool names = true, anyStation = false, anyIdle = false;
            foreach (var p in parts)
            {
                if (p.Station) anyStation = true;
                if (p.Station && p.Idle > -1) anyIdle = true;
                if (p.Name == null) continue;   // the flat part
                if (string.IsNullOrWhiteSpace(p.Name) || !seen.Add(p.Name.Trim())) names = false;
            }

            var xs = new List<string>(parts.Count);
            if (!anyIdle)
            {
                // "Stovetop 2.5 kW, Oven 1.5 kW, crafting or idle"
                string flat = null;
                foreach (var p in parts)
                {
                    if (p.Name == null) flat = Plus + Watts(p.Active);
                    else xs.Add(Named(p, Watts(p.Active), names));
                }
                string s = string.Join(", ", xs.ToArray());
                if (anyStation) s += CraftingOrIdle;
                if (flat != null) s = s.Length == 0 ? flat : s + ", " + flat;
                return s;
            }
            // Each part in its own form: "Stovetop 2.5 kW crafting, 250 W idle; Oven 1.5 kW, crafting or idle; plus 30 W"
            foreach (var p in parts)
                xs.Add(p.Name == null ? Plus + Watts(p.Active) : Named(p, Own(p), names));
            return string.Join("; ", xs.ToArray());
        }

        private static string Own(Part p)
        {
            if (p.Station && p.Idle > -1) return Watts(p.Active) + Crafting + ", " + Watts(p.Idle) + Idle;
            if (p.Station) return Watts(p.Active) + CraftingOrIdle;
            return Watts(p.Active);
        }

        private static string Named(Part p, string body, bool names) => names ? p.Name.Trim() + " " + body : body;

        // ManufacturingPowerUI.GetUnitString (W, kW, MW, current culture). Called with n > 0 only, so no "+" appears.
        internal static string Watts(int n)
        {
            var f = Vanilla.GetUnitString;
            if (f != null)
            {
                try { return f(n); }
                catch (Exception e)
                {
                    Log.Exception("Watts: GetUnitString", e);
                    Log.WarnOnce("units", "Watts: the game's unit formatter failed; using Grid Sense's copy of it.");
                }
            }
            return UnitString(n);
        }

        // Verbatim copy of SRC/ManufacturingPowerUI.cs:150-167, used only when the game's method is missing or fails.
        private static string UnitString(int power)
        {
            string text = string.Empty;
            if (power < 0) text = "+";
            power = Mathf.Abs(power);
            if (power >= 1000000) return text + Math.Round((float)power / 1000000f, 2) + " MW";
            if (power >= 1000) return text + Math.Round((float)power / 1000f, 2) + " kW";
            return text + power + " W";
        }
    }
}

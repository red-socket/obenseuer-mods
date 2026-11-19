using System;
using System.Globalization;
using UnityEngine;

namespace BetterTenement
{
    // Apartment value and rent, computed the way vanilla does (TenementApartment.cs:287-409, float arithmetic
    // included), with one value slot swapped for a candidate tier (spec §3.5.1).
    internal static class LedgerMath
    {
        internal enum ValueSlot
        {
            None,
            // General upgrades that carry a value (TenementApartment.GetValue).
            Stairwell, Facade, Courtyard, Sauna, SaunaAndPier, Elevator, Roof,
            // Apartment upgrades.
            Electricity, Water, Heating, Bathroom, Wall, Balcony, Balcony2, Kitchen
        }

        private static readonly ValueSlot[] AllSlots = (ValueSlot[])Enum.GetValues(typeof(ValueSlot));

        // Private statics in TenementApartment (ocDivider, rmDivider); the self-check notices if they change.
        private const float OcDivider = 20f, RmDivider = 200f;

        internal static bool IsApartmentSlot(ValueSlot s) => s >= ValueSlot.Electricity;

        // Any other general flag (water, heating, basement, workshop, mine, fence, greenhouse...) has no rent effect.
        internal static ValueSlot SlotOf(TenementUpgrade u)
        {
            if (u == null) return ValueSlot.None;
            if (u.upgradeElectricity) return ValueSlot.Electricity;
            if (u.upgradeWater) return ValueSlot.Water;
            if (u.upgradeHeatingMethod) return ValueSlot.Heating;
            if (u.upgradeBathroom) return ValueSlot.Bathroom;
            if (u.upgradeWalls) return ValueSlot.Wall;
            if (u.upgradeBalcony) return ValueSlot.Balcony;
            if (u.upgradeBalcony2) return ValueSlot.Balcony2;
            if (u.upgradeKitchen) return ValueSlot.Kitchen;
            if (u.upgradeStairs) return ValueSlot.Stairwell;
            if (u.upgradeOuterWalls) return ValueSlot.Facade;
            if (u.upgradeCourtyard) return ValueSlot.Courtyard;
            if (u.upgradeBasementSauna) return ValueSlot.Sauna;
            if (u.upgradeSaunaAndPier) return ValueSlot.SaunaAndPier;
            if (u.upgradeElevator) return ValueSlot.Elevator;
            if (u.upgradeRoof) return ValueSlot.Roof;
            return ValueSlot.None;
        }

        internal static TenementUpgrade Current(TenementApartment a, ValueSlot s)
        {
            var g = TenementController.instance != null ? TenementController.instance.general : null;
            switch (s)
            {
                case ValueSlot.Stairwell: return g?.GetCurrentStairwell();
                case ValueSlot.Facade: return g?.GetCurrentFacade();
                case ValueSlot.Courtyard: return g?.GetCurrentCourtyard();
                case ValueSlot.Sauna: return g?.GetCurrentSauna();
                case ValueSlot.SaunaAndPier: return g?.GetCurrentSaunaAndPier();
                case ValueSlot.Elevator: return g?.GetCurrentElevator();
                case ValueSlot.Roof: return g?.GetCurrentRoof();
                case ValueSlot.Electricity: return a?.GetCurrentElectricity();
                case ValueSlot.Water: return a?.GetCurrentWater();
                case ValueSlot.Heating: return a?.GetCurrentHeating();
                case ValueSlot.Bathroom: return a?.GetCurrentBathroom();
                case ValueSlot.Wall: return a?.GetCurrentWall();
                case ValueSlot.Balcony: return a?.GetCurrentBalcony();
                case ValueSlot.Balcony2: return a?.GetCurrentBalcony2();
                case ValueSlot.Kitchen: return a?.GetCurrentKitchen();
                default: return null;
            }
        }

        // The upgrade line a slot belongs to, for "is this tier above the current one".
        internal static TenementUpgrade[] Line(TenementApartment a, ValueSlot s)
        {
            var g = TenementController.instance != null ? TenementController.instance.general : null;
            TenementUpgradeGroup grp = null;
            switch (s)
            {
                case ValueSlot.Stairwell: grp = g?.stairsUpgrades; break;
                case ValueSlot.Facade: grp = g?.facadeUpgrades; break;
                case ValueSlot.Courtyard: grp = g?.courtyardUpgrades; break;
                case ValueSlot.Sauna: grp = g?.saunaUpgrades; break;
                case ValueSlot.SaunaAndPier: grp = g?.saunaAndPierUpgrades; break;
                case ValueSlot.Elevator: grp = g?.elevatorUpgrades; break;
                case ValueSlot.Roof: grp = g?.roofUpgrades; break;
                case ValueSlot.Electricity: grp = a?.type?.electricity; break;
                case ValueSlot.Water: grp = a?.type?.water; break;
                case ValueSlot.Heating: grp = a?.type?.heating; break;
                case ValueSlot.Bathroom: grp = a?.type?.bathroom; break;
                case ValueSlot.Wall: grp = a?.type?.walls; break;
                case ValueSlot.Balcony: grp = a?.type?.balcony; break;
                case ValueSlot.Balcony2: grp = a?.type?.balcony2; break;
                case ValueSlot.Kitchen: grp = a?.type?.kitchen; break;
            }
            return grp != null ? grp.updateGroup : null;
        }

        internal static int IndexIn(TenementUpgrade[] line, TenementUpgrade u)
        {
            if (line == null || u == null) return -1;
            for (int i = 0; i < line.Length; i++)
                if (TenementUpgrade.IsSameUpgrade(line[i], u)) return i;
            return -1;
        }

        internal static bool IsAbove(TenementUpgrade[] line, TenementUpgrade current, TenementUpgrade cand)
        {
            if (cand == null) return false;
            if (current == null) return true;
            if (TenementUpgrade.IsSameUpgrade(current, cand)) return false;
            int ic = IndexIn(line, current), iu = IndexIn(line, cand);
            if (ic < 0 || iu < 0) return true;
            return iu > ic;
        }

        // 5000 plus every current value-bearing tier; `replace` takes `cand` (null counts 0).
        internal static int RawSum(TenementApartment a, ValueSlot replace = ValueSlot.None, TenementUpgrade cand = null)
        {
            int sum = 5000;
            foreach (var s in AllSlots)
            {
                if (s == ValueSlot.None) continue;
                var u = s == replace ? cand : Current(a, s);
                if (u != null) sum += u.value;
            }
            return sum;
        }

        internal static int Value(TenementApartment a, int sum)
        {
            switch (a.type != null ? a.type.size : UpgradeType.ApartmentType.Small)
            {
                case UpgradeType.ApartmentType.Medium: return (int)((double)sum * 1.5);
                case UpgradeType.ApartmentType.Large: return sum * 2;
                default: return sum;
            }
        }

        internal static bool Rented(TenementApartment a) => a.resident != null && !a.moveStarted && !a.onlyFurniture;

        internal static int RawOc(TenementApartment a, int value, DifficultyInfo info)
        {
            if (!Rented(a) || a.resident.incomeOc == 0) return 0;
            return (int)info.GetRentIncome((int)((float)a.resident.incomeOc + (float)value / OcDivider));
        }

        internal static int RawRm(TenementApartment a, int value, DifficultyInfo info)
        {
            if (!Rented(a) || a.resident.incomeRm == 0) return 0;
            return (int)info.GetRentIncome((int)((float)a.resident.incomeRm + (float)value / RmDivider));
        }

        internal static int PerPaydayOc(int raw, DifficultyInfo info) => Mathf.RoundToInt((float)raw / info.GetNumberOfPayments());
        internal static float PerPaydayRm(int raw, DifficultyInfo info) => (float)Math.Round((float)raw / info.GetNumberOfPayments(), 2);

        // Our math against vanilla's for this flat. A mismatch means another mod (e.g. Obenomics) changed rent.
        internal static bool SelfCheck(TenementApartment a, DifficultyInfo info)
        {
            int v = Value(a, RawSum(a));
            if (v != a.GetValue()) return false;
            if (PerPaydayOc(RawOc(a, v, info), info) != a.GetOcIncome()) return false;
            return Math.Abs(PerPaydayRm(RawRm(a, v, info), info) - a.GetRmIncome()) < 0.0001f;
        }

        internal sealed class Effect
        {
            internal bool NoRentEffect, General, Above, Vacant, RentKnown = true;
            internal int ValueDelta;   // apartment: size-scaled for that flat; general: raw per flat
            internal int OcDelta;      // per payday
            internal float RmDelta;    // per payday
            internal string Unit = "W";
        }

        // `a` is the flat for apartment upgrades and ignored for general ones.
        internal static Effect Compute(TenementApartment a, TenementUpgrade u)
        {
            var e = new Effect();
            var info = Game.Difficulty;
            var tc = TenementController.instance;
            var slot = SlotOf(u);
            if (slot == ValueSlot.None || info == null || tc == null) { e.NoRentEffect = true; return e; }
            e.Unit = info.GetRentPaymentTimeUnit();

            if (IsApartmentSlot(slot))
            {
                if (a == null) { e.NoRentEffect = true; return e; }
                var cur = Current(a, slot);
                e.Above = IsAbove(Line(a, slot), cur, u);
                if (!e.Above) return e;
                int v0 = Value(a, RawSum(a)), v1 = Value(a, RawSum(a, slot, u));
                e.ValueDelta = v1 - v0;
                if (!Rented(a)) { e.Vacant = true; return e; }
                if (!SelfCheck(a, info)) { e.RentKnown = false; WarnRent(); return e; }
                e.OcDelta = PerPaydayOc(RawOc(a, v1, info), info) - PerPaydayOc(RawOc(a, v0, info), info);
                e.RmDelta = PerPaydayRm(RawRm(a, v1, info), info) - PerPaydayRm(RawRm(a, v0, info), info);
                return e;
            }

            e.General = true;
            var gcur = Current(null, slot);
            e.Above = IsAbove(Line(null, slot), gcur, u);
            if (!e.Above) return e;
            e.ValueDelta = u.value - (gcur != null ? gcur.value : 0);
            if (tc.apartments == null) return e;
            foreach (var flat in tc.apartments)
            {
                if (flat == null || (flat.GetOcIncome() == 0 && flat.GetRmIncome() == 0f)) continue;
                if (!SelfCheck(flat, info)) { e.RentKnown = false; WarnRent(); continue; }
                int v0 = Value(flat, RawSum(flat)), v1 = Value(flat, RawSum(flat, slot, u));
                e.OcDelta += PerPaydayOc(RawOc(flat, v1, info), info) - PerPaydayOc(RawOc(flat, v0, info), info);
                e.RmDelta += PerPaydayRm(RawRm(flat, v1, info), info) - PerPaydayRm(RawRm(flat, v0, info), info);
            }
            return e;
        }

        private static void WarnRent() =>
            Log.WarnOnce("rent-mismatch", "Rent from the game differs from the vanilla formula (another mod changes rent?); the Ledger shows \"rent n/a\".");

        // ---- formatting (TMP and IMGUI both read <color>) ----

        internal const string Green = "#9FD18B", Red = "#E06060";
        // On the game's light grey tooltip (#C7C7C7, black text) the pale pair has a contrast of about 1:1 and 2:1;
        // these darker ones reach 5:1 and 5.5:1 (test pass 3, observation 3).
        internal const string GreenOnLight = "#1F5A14", RedOnLight = "#8B1A1A";

        internal static string Signed(int v, bool onLight = false) =>
            v > 0 ? $"<color={(onLight ? GreenOnLight : Green)}>+{v}</color>"
            : v < 0 ? $"<color={(onLight ? RedOnLight : Red)}>{v}</color>" : "+0";

        internal static string Signed(float v, bool onLight = false)
        {
            string s = Math.Abs(v).ToString("0.##", CultureInfo.InvariantCulture);
            return v > 0.0001f ? $"<color={(onLight ? GreenOnLight : Green)}>+{s}</color>"
                : v < -0.0001f ? $"<color={(onLight ? RedOnLight : Red)}>-{s}</color>" : "+0";
        }

        // One line, without the contractor part. Null when there is nothing to say (tier at or below the current one).
        // `card`: the short form for the one-line card strip (no "(size-scaled)"; the tooltip and panel keep it).
        // `onLight`: the darker colours, for the floor plan tooltip.
        internal static string Describe(Effect e, bool card = false, bool onLight = false)
        {
            if (e.NoRentEffect) return "No rent effect";
            if (!e.Above) return null;
            string value = Signed(e.ValueDelta, onLight);
            string rent = $"{Signed(e.OcDelta, onLight)} OC {Signed(e.RmDelta, onLight)} RM/{e.Unit}";
            if (e.General)
            {
                string line = $"value {value} per flat" + (card ? "" : " (size-scaled)");
                if (!e.RentKnown) return line + " · rent n/a (changed by another mod)";
                return line + " · " + rent;
            }
            if (e.Vacant) return $"value {value} · rent rises once rented";
            if (!e.RentKnown) return $"{value} value · rent n/a (changed by another mod)";
            return $"{value} value · {rent}";
        }
    }
}

using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace NoFloorSpill
{
    internal enum RefuseMode { Delete, NearbyStorage, Vanilla }

    internal enum NoRoom { Drop, Delete }

    internal enum TargetOrder { SameItemFirst, SameItemOnly, Closest }

    // Every tunable value (spec §5). Entries are read live inside the helpers, so most changes take effect at once.
    // A group that was off at launch has no patches applied; it starts at the next launch (LaunchNote for the junk bool,
    // ModeLaunchNote for Refuse.Mode, whose "off" is Vanilla). No hotkeys.
    internal static class Cfg
    {
        private const string LaunchNote = " If this was off when the game started, it starts at the next launch.";
        private const string ModeLaunchNote = " Switching between Delete and NearbyStorage works at once. If Mode was Vanilla " +
            "when the game started, the other modes start at the next launch.";

        internal static ConfigEntry<bool> VerboseLogging;

        internal static ConfigEntry<RefuseMode> RefuseModeEntry;
        internal static ConfigEntry<string> RefuseItemIds;
        internal static ConfigEntry<NoRoom> RefuseWhenNoRoom;

        internal static ConfigEntry<bool> JunkEnabled, JunkUseFreedSlot, JunkToInventory, JunkToNearbyStorage;

        internal static ConfigEntry<float> Radius, MaxHeightDifference;
        internal static ConfigEntry<TargetOrder> Order;
        internal static ConfigEntry<bool> OnlyPlacedFurniture, SkipLocked;

        // Parsed ItemIds, rebuilt when the entry changes. Replaced as a whole, never mutated, so readers need no lock.
        internal static HashSet<int> RefuseIds = new HashSet<int> { 10010 };

        internal static void Bind(ConfigFile c)
        {
            VerboseLogging = c.Bind("General", "VerboseLogging", false,
                "Also log every amount the mod deletes, stores, moves or drops, added up per frame. That includes leftovers " +
                "put into a machine's freed slot or into your inventory.");

            RefuseModeEntry = c.Bind("Refuse", "Mode", RefuseMode.Delete,
                "What happens to ash (and other listed refuse) that no longer fits a full ash slot. " +
                "Delete: it is destroyed. Ash is used for compost, cement, tiles, paint and wood pulp, so empty the slot if you want it. " +
                "NearbyStorage: it goes into storage furniture you own near the stove (see the NearbyStorage section); " +
                "WhenNoRoom decides what happens to the rest. Vanilla: it drops on the floor as in the game. " +
                "Refuse never goes into your inventory. If the slot holds a different item, such as Electronic Waste, " +
                "the mod never deletes the incoming refuse. In NearbyStorage mode it goes to nearby storage; otherwise it " +
                "drops as in the game." + ModeLaunchNote);
            RefuseItemIds = c.Bind("Refuse", "ItemIds", "10010",
                "Comma-separated item IDs this applies to. 10010 is Ash. Add 14300 for Electronic Waste. " +
                "Other refuse is left to the game. Bad entries are logged once and ignored.");
            RefuseWhenNoRoom = c.Bind("Refuse", "WhenNoRoom", NoRoom.Drop,
                "NearbyStorage mode only: what happens to refuse that no nearby storage can take. Drop: it falls on the floor " +
                "as in the game. Delete: it is destroyed (only what did not fit a full ash slot). Nothing at the farmer " +
                "cooperative belongs to you, so this always applies there. Refuse that arrives while a save is still " +
                "loading skips nearby storage and drops as in the game, whatever this is set to.");

            JunkEnabled = c.Bind("Junk", "Enabled", true,
                "When a machine is full, the leftovers it gives back as an ingredient runs out (plastic trash from butterine " +
                "and glue, paper trash from salt, small glass bottles from ink, petri dishes from yeast) no longer drop on the floor." + LaunchNote);
            JunkUseFreedSlot = c.Bind("Junk", "UseFreedSlot", true,
                "Only for machines that put their results into a separate output storage: first put the leftover into the " +
                "slot its used-up ingredient just freed. On every other machine the mod leaves that slot free, because the " +
                "finished item goes there and would otherwise drop on the floor.");
            JunkToInventory = c.Bind("Junk", "ToInventory", true,
                "Then try your inventory, but only while that machine's window is open. The game has used the same rule for a " +
                "machine's main result since 0.5.06. Leftovers from ingredients that belong to someone else skip this step.");
            JunkToNearbyStorage = c.Bind("Junk", "ToNearbyStorage", true,
                "Then try storage furniture you own near the machine. Whatever is left drops next to the machine as in the game.");

            Radius = c.Bind("NearbyStorage", "Radius", 4f,
                new ConfigDescription("How far from the stove or machine to look for storage, in metres, measured horizontally.",
                    new AcceptableValueRange<float>(1f, 10f)));
            MaxHeightDifference = c.Bind("NearbyStorage", "MaxHeightDifference", 1.5f,
                new ConfigDescription("Storage more than this many metres above or below is ignored, so nothing goes to another floor.",
                    new AcceptableValueRange<float>(0.5f, 3f)));
            Order = c.Bind("NearbyStorage", "TargetOrder", TargetOrder.SameItemFirst,
                "SameItemFirst: storage that already holds the item comes first, then the closest. To choose where ash goes, " +
                "put one ash in that chest. SameItemOnly: only storage that already holds the item. Closest: nearest first.");
            OnlyPlacedFurniture = c.Bind("NearbyStorage", "OnlyPlacedFurniture", true,
                "Only storage furniture you placed. Turn it off to also use your fixed lockers and crates.");
            SkipLocked = c.Bind("NearbyStorage", "SkipLocked", true, "Skip locked storage.");

            Parse();
            RefuseItemIds.SettingChanged += (s, a) => Parse();
        }

        private static void Parse()
        {
            var ids = new HashSet<int>();
            foreach (var p in Split(RefuseItemIds.Value))
                if (int.TryParse(p, out int id) && id > 0) ids.Add(id);
                else Log.WarnOnce("ids|" + p, $"Refuse: ItemIds entry \"{p}\" is not an item ID; ignored.");
            RefuseIds = ids;
        }

        private static IEnumerable<string> Split(string value)
        {
            foreach (var part in (value ?? "").Split(','))
            {
                var s = part.Trim();
                if (s.Length > 0) yield return s;
            }
        }

        // For the startup line: "Delete; items 10010".
        internal static string RefuseSummary()
        {
            var ids = new List<int>(RefuseIds);
            ids.Sort();
            return RefuseModeEntry.Value + "; items " + (ids.Count == 0 ? "none" : string.Join(",", ids.ConvertAll(i => i.ToString()).ToArray()));
        }
    }
}

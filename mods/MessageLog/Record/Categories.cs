using System;
using System.Collections.Generic;

namespace MessageLog
{
    [Flags]
    internal enum Cat
    {
        None = 0, Crime = 1, Money = 2, Mail = 4, Quests = 8, Items = 16, Dialogue = 32, Other = 64,
    }

    // The game's real notification types mapped to the filter chips (spec §5.2). Matching always uses the stored English
    // type: an exact lookup, then the prefixes, then Other. Types collected from every CreateNotification call in SRC
    // and in our mods (2026-11-17); a type the game adds later lands in Other.
    internal static class Categories
    {
        // D7: item pickups dominate Other. Splitting them into their own chip is one constant until the user decides.
        internal static readonly bool SplitItems = false;

        private static readonly Dictionary<string, Cat> Exact = new Dictionary<string, Cat>(StringComparer.Ordinal)
        {
            // Crime (D8: Fair Justice's "Prison", "Jail" and "Stolen backpack" count as crime).
            ["Crime committed:"] = Cat.Crime,
            ["Invoice expired"] = Cat.Crime | Cat.Money,   // an unpaid real invoice becomes a crime
            ["Prison"] = Cat.Crime,
            ["Jail"] = Cat.Crime,
            ["Stolen backpack"] = Cat.Crime,
            // Money (Unofficial Patch's "Account frozen", Fair Market's "Invoice paid").
            ["Money added!"] = Cat.Money,
            ["Money removed!"] = Cat.Money,
            ["Rent paid"] = Cat.Money,
            ["Utilities paid"] = Cat.Money,
            ["Collateral paid"] = Cat.Money,
            ["Collateral returned"] = Cat.Money,
            ["Invoice expires today"] = Cat.Money,
            ["Subscribed to"] = Cat.Money,
            ["Unsubscribed from"] = Cat.Money,
            ["Room rented"] = Cat.Money,
            ["Account frozen"] = Cat.Money,
            ["Invoice paid"] = Cat.Money,
            // Quests.
            ["New Task"] = Cat.Quests,
            ["Task Finished"] = Cat.Quests,
            ["Task Failed"] = Cat.Quests,
            ["Task Abandoned"] = Cat.Quests,
            ["New Objective"] = Cat.Quests,
            ["Objective Complete"] = Cat.Quests,
            ["Objective Failed"] = Cat.Quests,
            ["Objective Updated"] = Cat.Quests,
        };

        private static readonly Dictionary<string, Cat> ItemTypes = new Dictionary<string, Cat>(StringComparer.Ordinal)
        {
            ["Item added!"] = Cat.Items,
            ["Items added!"] = Cat.Items,
            ["Item dropped!"] = Cat.Items,
            ["Item removed!"] = Cat.Items,
            ["Item removed"] = Cat.Items,
            ["New item"] = Cat.Items,
            ["New furniture"] = Cat.Items,
            ["Package collected!"] = Cat.Items,
        };

        private static readonly (string prefix, Cat cat)[] Prefixes =
        {
            ("Money added to bank account", Cat.Money),
            ("Money removed from bank account", Cat.Money),
            ("Mail: ", Cat.Mail),
        };

        internal static Cat Of(char kind, string head)
        {
            if (kind == 'd' || kind == 'c') return Cat.Dialogue;
            if (kind == 'g') return Cat.None;
            if (string.IsNullOrEmpty(head)) return Cat.Other;
            if (Exact.TryGetValue(head, out var c)) return c;
            foreach (var (prefix, cat) in Prefixes)
                if (head.StartsWith(prefix, StringComparison.Ordinal)) return cat;
            if (SplitItems)
            {
                if (ItemTypes.TryGetValue(head, out c)) return c;
                if (head.StartsWith("Items added (", StringComparison.Ordinal)) return Cat.Items;
            }
            return Cat.Other;
        }

        // The chips, in order (D7, D9: one at a time). Dialogue is hidden when nothing could be in it.
        internal static readonly Cat[] Chips = SplitItems
            ? new[] { Cat.None, Cat.Crime, Cat.Money, Cat.Mail, Cat.Quests, Cat.Items, Cat.Dialogue, Cat.Other }
            : new[] { Cat.None, Cat.Crime, Cat.Money, Cat.Mail, Cat.Quests, Cat.Dialogue, Cat.Other };

        internal static string ChipLabel(Cat c)
        {
            switch (c)
            {
                case Cat.None: return Strings.ChipAll;
                case Cat.Crime: return Strings.ChipCrime;
                case Cat.Money: return Strings.ChipMoney;
                case Cat.Mail: return Strings.ChipMail;
                case Cat.Quests: return Strings.ChipQuests;
                case Cat.Items: return Strings.ChipItems;
                case Cat.Dialogue: return Strings.ChipDialogue;
                default: return Strings.ChipOther;
            }
        }

        // The detail strip's category name ("Crime, Money" for an expired invoice).
        internal static string Name(Cat c)
        {
            var parts = new List<string>(2);
            if ((c & Cat.Crime) != 0) parts.Add(Strings.CatCrime);
            if ((c & Cat.Money) != 0) parts.Add(Strings.CatMoney);
            if ((c & Cat.Mail) != 0) parts.Add(Strings.CatMail);
            if ((c & Cat.Quests) != 0) parts.Add(Strings.CatQuests);
            if ((c & Cat.Items) != 0) parts.Add(Strings.CatItems);
            if ((c & Cat.Dialogue) != 0) parts.Add(Strings.CatDialogue);
            if (parts.Count == 0) parts.Add(Strings.CatOther);
            return string.Join(", ", parts.ToArray());
        }
    }
}

#if FAIRMARKET_TESTS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BepInEx.Configuration;
using OS.Items;
using UnityEngine;

namespace FairMarket
{
    // Test-only setters and dumps for the in-game test plan's [T] steps (spec §14), compiled only with
    // -p:FairMarketTests=true (Quest Helper's QUESTHELPER_TESTS precedent). Never packaged. Set [Testing] Setup to
    // "name arg arg" while a game is running; it applies once and resets itself to empty.
    internal static class Setups
    {
        private const int TestOwner = 106;
        private const int CertificateItem = 19030;   // Certificate of Authenticity
        private const int OtherLink = 15700;         // Aluminum Bar: "linked to another item"

        private static ConfigEntry<string> _setup;

        private static readonly Dictionary<string, Action<string[]>> All = new Dictionary<string, Action<string[]>>(StringComparer.OrdinalIgnoreCase)
        {
            ["invoice-real"] = a => Ic().CreateInvoice("Test invoice", new Money.money(500, 0f), 7),
            ["invoice-real-rm"] = a => Ic().CreateInvoice("Test invoice (RM)", new Money.money(0, 40f), 7),
            ["invoice-scam"] = a => Ic().CreateInvoice("Test scam", new Money.money(340, 0f), 7, null, isfake: true),
            ["invoice-lastday"] = a => Ic().CreateInvoice("Test last day", new Money.money(200, 0f), 0),
            ["invoice-zero"] = a =>
            {
                var (r, meta) = Ic().CreateInvoiceItem("Test zero", null, 0, Money.Currency.OC, 7);
                MailController.instance.SendMail("invoice", r, 1, meta, MailController.Target.Player);
            },
            ["invoice-letter-direct"] = a =>
            {
                var (r, meta) = Ic().CreateInvoiceItem("Test letter", null, 120, Money.Currency.OC, 7);
                ItemOperations.AddItem(r.Item, -1, null, meta);
            },
            ["stolen"] = a => ItemOperations.AddItem(ItemDatabase.instance.FetchItemByID(Int(a, 0)), TestOwner),
            ["cert-linked"] = a => Certs(Int(a, 0), Int(a, 1), Int(a, 0)),
            ["cert-other"] = a => Certs(Int(a, 0), Int(a, 1), OtherLink),
            ["cert-unlinked"] = a => Certs(Int(a, 0), Int(a, 1), -1),
            ["stock-dump"] = a => StockDump(string.Join(" ", a)),
            ["restock-near"] = a => RestockNear(),
            ["hours-dump"] = a => HoursDump(),
        };

        internal static void Bind(ConfigFile c)
        {
            _setup = c.Bind("Testing", "Setup", "", "TEST BUILD ONLY: " + string.Join(", ", All.Keys.ToArray()));
            _setup.SettingChanged += (s, a) => Run();
            Log.Warn("Testing: this is a test build with state setters. Never package it.");
        }

        private static InvoiceController Ic() => InvoiceController.instance ?? throw new InvalidOperationException("no InvoiceController");

        private static int Int(string[] a, int i) =>
            i < a.Length && int.TryParse(a[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : throw new ArgumentException("argument " + (i + 1) + " missing");

        private static void Run()
        {
            string line = (_setup.Value ?? "").Trim();
            if (line.Length == 0) return;
            var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!All.TryGetValue(parts[0], out var act) || GlobalState.instance == null)
                Log.Warn($"Testing: no setup '{parts[0]}' or no game running.");
            else
            {
                try { act(parts.Skip(1).ToArray()); Log.Warn($"Testing: applied '{line}'."); }
                catch (Exception e) { Log.Exception("test setup", e); }
            }
            _setup.Value = "";
        }

        // n Certificates of Authenticity whose value range fits item `forId`, linked to `link` (-1: unlinked).
        private static void Certs(int forId, int n, int link)
        {
            var cert = ItemDatabase.instance.FetchItemByID(CertificateItem);
            var item = ItemDatabase.instance.FetchItemByID(forId);
            if (cert == null || item == null) throw new ArgumentException("unknown item");
            for (int i = 0; i < n; i++)
            {
                var meta = cert.CloneMeta(fullCopy: true);
                var c = Item.GetMetaOfType<ItemCertificateData>(meta);
                c.linkedItem = link > 0 ? new ItemReference(link) : null;
                c.targetValue = item.GetItemSalePrice(item.CloneMeta());
                ItemOperations.AddItem(cert, -1, null, meta);
            }
        }

        private static void StockDump(string name)
        {
            var db = StorageSpawnCategoryDatabase.instance;
            var c = db != null ? db.FetchStorageSpawnCategory(name) : null;
            if (c == null) { Log.Warn($"Testing: no category '{name}'."); return; }
            var mats = Names.List(Cfg.Stock.Materials.Value).Select(s => int.TryParse(s, out int v) ? v : -1).ToArray();
            foreach (int m in mats)
            {
                int common = c.ItemCommon?.Count(x => x == m) ?? 0, rare = c.ItemRare?.Count(x => x == m) ?? 0;
                int liquid = c.MetaValues?.liquid?.liquidItems?.Count(r => r != null && r.ID == m) ?? 0;
                Log.Warn($"Testing: {name}: {m} common {common}, rare {rare}, liquid {liquid}.");
            }
            Log.Warn($"Testing: {name}: common list {c.ItemCommon?.Count ?? 0}, rare list {c.ItemRare?.Count ?? 0}, edited {StockPlan.EditedCategories.Contains(name)}.");
        }

        private static void RestockNear()
        {
            var player = PlayerLocator.instance != null ? PlayerLocator.instance.transform.position : Vector3.zero;
            Storage best = null;
            float bestD = 4f;
            foreach (var s in UnityEngine.Object.FindObjectsByType<Storage>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (s == null || !Vanilla.SpawnFromCategory(s)) continue;
                float d = Vector3.Distance(player, s.transform.position);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best == null) { Log.Warn("Testing: no category storage within 4 m."); return; }
            best.SetNextItemsToSpawn();
            best.SpawnItems();
            Log.Warn($"Testing: restocked '{best.title}' ({best.itemSpawnCategory}).");
        }

        private static void HoursDump()
        {
            var tod = TimeOfDayAzure.instance;
            foreach (var kv in HoursIndex.All)
            {
                var s = kv.Value;
                string now = tod != null ? (s.IsOpen(tod.currentTimeAndDay.weekDay, (int)tod.currentTimeAndDay.hours) ? "open" : "closed") : "?";
                Log.Warn($"Testing: hours: {kv.Key.GetType().Name} '{HoursIndex.Path((kv.Key as Component)?.transform)}' <- {s.Kind} '{s.Source}': {s.Week()} (now {now}).");
            }
        }
    }
}
#endif

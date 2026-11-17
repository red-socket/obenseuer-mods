using System;
using System.Collections.Generic;

namespace FairMarket
{
    // How a configured buyer finds its Trade (spec §6.2). The four default entries name a Trade by scene and GUID (read
    // from the exported scenes); a shared trader's GUID becomes "SharedTrader_<id>" in its Awake (SRC/Trade.cs:211-220),
    // so a Trade with no GUID of its own or a shared one falls back to its owner NPC. Custom entries match "Scene > Who"
    // with folded names: Who is the Trade's title, its Storage's title, the owner's name, or shared:<id>.
    internal sealed class CustomEntry
    {
        internal string Scene, Who, Text;
        internal Rules Rules;
    }

    internal static class TraderKeys
    {
        internal static string SceneOf(Trade t)
        {
            try { return t.gameObject.scene.name ?? ""; }
            catch { return ""; }
        }

        internal static bool MatchesSlot(Trade t, string scene, Cfg.BuyerSlot s)
        {
            if (t == null || s == null || scene != s.Scene) return false;
            string guid = t.GUID ?? "";
            if (guid == s.Guid) return true;
            bool noOwnGuid = guid.Length == 0 || guid.StartsWith("SharedTrader_", StringComparison.Ordinal);
            return noOwnGuid && t.owner != null && t.owner.ID > 0 && t.owner.ID == s.Owner;
        }

        internal static bool MatchesWho(Trade t, string whoFold)
        {
            if (t == null || string.IsNullOrEmpty(whoFold)) return false;
            if (whoFold.StartsWith("shared:", StringComparison.Ordinal))
                return !string.IsNullOrEmpty(t.sharedId) && Names.Fold(t.sharedId) == whoFold.Substring(7).Trim();
            if (Names.Fold(t.title) == whoFold) return true;
            try
            {
                var st = t.GetComponent<Storage>();
                if (st != null && Names.Fold(st.title) == whoFold) return true;
            }
            catch { }
            return t.owner != null && Names.Fold(t.owner.name) == whoFold;
        }

        // "Scene > Who: tokens; Scene > Who: tokens". Malformed entries are logged once and skipped.
        internal static List<CustomEntry> ParseCustom(string text)
        {
            var list = new List<CustomEntry>();
            if (string.IsNullOrEmpty(text)) return list;
            foreach (var raw in text.Split(';'))
            {
                string e = raw.Trim();
                if (e.Length == 0) continue;
                int gt = e.IndexOf('>');
                int colon = gt >= 0 ? e.IndexOf(':', gt) : -1;
                // "shared:<id>" has its own colon: take the one after it.
                if (colon > 0 && Names.Fold(e.Substring(gt + 1, colon - gt - 1)) == "shared")
                    colon = e.IndexOf(':', colon + 1);
                if (gt <= 0 || colon <= gt + 1)
                {
                    Log.WarnOnce("buyers custom|" + e, $"Buyers: Custom entry '{e}' should look like 'Scene > Who: tokens'; skipped.");
                    continue;
                }
                var c = new CustomEntry
                {
                    Scene = Names.Fold(e.Substring(0, gt)),
                    Who = Names.Fold(e.Substring(gt + 1, colon - gt - 1)),
                    Text = e,
                };
                c.Rules = Tokens.Parse(e.Substring(colon + 1), "Custom '" + e.Substring(0, colon).Trim() + "'");
                list.Add(c);
            }
            return list;
        }
    }
}

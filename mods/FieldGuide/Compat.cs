using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace FieldGuide
{
    // Other mods next to ours (spec §10, §11), copied from Recipe Book. Foreign() reports anyone but us that can skip or
    // rewrite a method we patch (bool prefixes, transpilers, finalizers); Overlap() reports additive neighbours. Our own
    // mods (KnownFriends) are designed to work together: they are logged as Info, everyone else as a warning. Scanned
    // once at the first level load (all plugins have loaded by then) and cached. Our patches stay on either way.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";
        internal const string RecipeBookGuid = "redsocket.obenseuer.recipebook";
        internal const string FairMarketGuid = "redsocket.obenseuer.fairmarket";

        private static readonly Dictionary<MethodBase, (string[] foreign, string[] friends, string[] all)> Cache =
            new Dictionary<MethodBase, (string[] foreign, string[] friends, string[] all)>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        // Fair Market's extra buyers (D18): its B1 postfix on Trade.CheckIfAllowed adds them. Turning its [Buyers] Enabled
        // on needs a restart (the postfix is patched only then), but turning it off works at once (B1 checks it live), so
        // the entry is kept and read live. FairMarketVersion goes into the Bought-by cache key and moves whenever one of
        // Fair Market's [Buyers] settings changes (its slot strings change what B1 accepts).
        private static ConfigEntry<bool> _fmEnabled;
        private static ConfigFile _fmConfig;
        private static bool _fmPatched;
        internal static int FairMarketVersion;

        internal static bool FairMarketBuyers
        {
            get
            {
                if (!_fmPatched || _fmEnabled == null) return false;
                try { return _fmEnabled.Value; }
                catch { return false; }
            }
        }

        // True when another (non-friend) mod can skip or rewrite m. Logs once.
        internal static bool Foreign(MethodBase m, string feature, string consequence)
        {
            var (foreign, friends, _) = Owners(m);
            if (friends.Length > 0 && Logged.Add("friend|" + Key(m)))
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
            if (foreign.Length == 0) return false;
            if (Logged.Add("foreign|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} {consequence}.");
            return true;
        }

        // Additive patches keep working next to other mods; every other owner is logged once per method.
        internal static void Overlap(MethodBase m, string feature)
        {
            var (_, _, all) = Owners(m);
            if (all.Length == 0 || !Logged.Add("overlap|" + Key(m))) return;
            var friends = all.Where(o => o.StartsWith(KnownFriends, StringComparison.Ordinal)).ToArray();
            var others = all.Where(o => !o.StartsWith(KnownFriends, StringComparison.Ordinal)).ToArray();
            if (friends.Length > 0)
                Log.Info($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
            if (others.Length > 0)
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", others)}; {feature} still applies where it can.");
        }

        // D18: Fair Market is loaded, its [Buyers] Enabled is on, and its postfix sits on Trade.CheckIfAllowed.
        // Read through its ConfigFile, so we need no reference to it; anything unexpected counts as off.
        internal static void ReadFairMarket()
        {
            Unhook();
            _fmEnabled = null;
            _fmPatched = false;
            FairMarketVersion++;
            try
            {
                if (!Chainloader.PluginInfos.TryGetValue(FairMarketGuid, out var info) || info?.Instance == null) return;
                if (!info.Instance.Config.TryGetEntry<bool>(new ConfigDefinition("Buyers", "Enabled"), out var entry)) return;
                var patches = Vanilla.TradeCheckIfAllowed != null ? Harmony.GetPatchInfo(Vanilla.TradeCheckIfAllowed) : null;
                _fmPatched = patches != null && patches.Postfixes.Any(p => p.owner == FairMarketGuid);
                if (!_fmPatched) return;
                _fmEnabled = entry;
                _fmConfig = info.Instance.Config;
                _fmConfig.SettingChanged += OnFairMarketSetting;
                if (FairMarketBuyers)
                    Log.WarnOnce("fm buyers", "Bought by: Fair Market's extra buyers are on. They are listed only for traders in " +
                        "the current place, since Field Guide checks those through the game's own trade rules.");
            }
            catch (Exception e)
            {
                Log.Exception("Compat: fair market", e);
                Unhook();
                _fmEnabled = null;
                _fmPatched = false;
            }
        }

        private static void OnFairMarketSetting(object sender, SettingChangedEventArgs a)
        {
            if (a?.ChangedSetting?.Definition?.Section != "Buyers") return;
            FairMarketVersion++;
            Plugin.Step("fair market (bought by)", BoughtBy.Clear);
            Plugin.Step("fair market (panel)", UpdateInfoPatch.Clear);
        }

        internal static void Unhook()
        {
            if (_fmConfig == null) return;
            try { _fmConfig.SettingChanged -= OnFairMarketSetting; }
            catch { }
            _fmConfig = null;
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static (string[] foreign, string[] friends, string[] all) Owners(MethodBase m)
        {
            if (m == null) return (new string[0], new string[0], new string[0]);
            if (!Cache.TryGetValue(m, out var owners))
            {
                owners = Scan(m);
                Cache[m] = owners;
            }
            return owners;
        }

        private static (string[] foreign, string[] friends, string[] all) Scan(MethodBase m)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return (new string[0], new string[0], new string[0]);
                var foreign = new HashSet<string>();
                var friends = new HashSet<string>();
                var all = new HashSet<string>(info.Owners.Where(o => o != null && o != Plugin.Guid));
                void Add(string owner)
                {
                    if (owner == null || owner == Plugin.Guid) return;
                    if (owner.StartsWith(KnownFriends, StringComparison.Ordinal)) friends.Add(owner);
                    else foreign.Add(owner);
                }
                foreach (var p in info.Prefixes)
                    if (p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)) Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers)) Add(p.owner);
                return (foreign.ToArray(), friends.ToArray(), all.OrderBy(x => x, StringComparer.Ordinal).ToArray());
            }
            catch (Exception e)
            {
                Log.Exception("compat scan", e);
                return (new string[0], new string[0], new string[0]);
            }
        }
    }
}

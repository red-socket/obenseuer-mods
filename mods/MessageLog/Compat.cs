using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Bootstrap;
using BepInEx.Configuration;
using HarmonyLib;

namespace MessageLog
{
    // Other mods (spec §11, §12): who else patches our targets, the other IMGUI windows we never open over, and the
    // Obenseuer PT-BR bridge. Read-only: nothing here changes another mod.
    internal static class Compat
    {
        private const string KnownFriends = "redsocket.obenseuer.";

        private static readonly Dictionary<MethodBase, (string[] foreign, string[] friends)> Cache =
            new Dictionary<MethodBase, (string[] foreign, string[] friends)>();
        private static readonly HashSet<string> Logged = new HashSet<string>();

        // ---- Foreign patches (copied from Fair Market) ----
        // Bool prefixes, transpilers and finalizers from any owner but us. Our own mods (Unofficial Patch's ConsoleGuard,
        // Vital Signs, Quest Helper, Fair Market, Recipe Book) are designed to work together and are logged at Verbose;
        // anyone else's can skip vanilla and gets a warning. Void prefixes and postfixes can't skip, so they are ignored.

        internal static void Foreign(MethodBase m, string feature, string consequence)
        {
            var (foreign, friends) = Owners(m);
            if (friends.Length > 0 && Logged.Add("friend|" + Key(m)))
                Log.Verbose($"Compat: {Key(m)} is also patched by {string.Join(", ", friends)} (one of our mods); {feature} is ordered to work with it.");
            if (foreign.Length > 0 && Logged.Add("foreign|" + Key(m)))
                Log.Warn($"Compat: {Key(m)} is also patched by {string.Join(", ", foreign)}; {feature} {consequence}.");
        }

        private static string Key(MethodBase m) => m == null ? "(null)" : m.DeclaringType?.Name + "." + m.Name;

        private static (string[] foreign, string[] friends) Owners(MethodBase m)
        {
            if (m == null) return (new string[0], new string[0]);
            if (!Cache.TryGetValue(m, out var owners))
            {
                owners = Scan(m);
                Cache[m] = owners;
            }
            return owners;
        }

        private static (string[] foreign, string[] friends) Scan(MethodBase m)
        {
            try
            {
                var info = Harmony.GetPatchInfo(m);
                if (info == null) return (new string[0], new string[0]);
                var foreign = new HashSet<string>();
                var friends = new HashSet<string>();
                void Add(string owner)
                {
                    if (owner == null || owner == Plugin.Guid) return;
                    if (owner.StartsWith(KnownFriends, StringComparison.Ordinal)) friends.Add(owner);
                    else foreign.Add(owner);
                }
                foreach (var p in info.Prefixes)
                    if (p.PatchMethod != null && p.PatchMethod.ReturnType == typeof(bool)) Add(p.owner);
                foreach (var p in info.Transpilers.Concat(info.Finalizers)) Add(p.owner);
                return (foreign.ToArray(), friends.ToArray());
            }
            catch (Exception e)
            {
                Log.Exception("compat scan", e);
                return (new string[0], new string[0]);
            }
        }

        // ---- Other windows (spec §8.2: one window at a time) ----
        // Message Log refuses to open over these, and closes its own window when one of them opens over it. A mod that
        // isn't installed is skipped. Save Guard's flag is a static property; the others are fields.

        private static readonly (string type, string member)[] OtherWindows =
        {
            ("VitalSigns.VitalWindow", "IsOpen"), ("QuestHelper.HelperWindow", "IsOpen"), ("FairMarket.InvoiceWindow", "IsOpen"),
            ("BetterTenement.DoctorWindow", "Open"), ("SaveGuard.GuardWindow", "AnyOpen"),
        };
        private static Func<bool>[] _otherReads;

        internal static string OtherWindowOpen()
        {
            try
            {
                if (_otherReads == null)
                {
                    _otherReads = new Func<bool>[OtherWindows.Length];
                    for (int i = 0; i < OtherWindows.Length; i++) _otherReads[i] = Reader(OtherWindows[i].type, OtherWindows[i].member);
                }
                for (int i = 0; i < _otherReads.Length; i++)
                {
                    var r = _otherReads[i];
                    if (r == null) continue;
                    bool open;
                    try { open = r(); }
                    catch { _otherReads[i] = null; continue; }
                    if (open) return OtherWindows[i].type;
                }
            }
            catch { }
            return null;
        }

        private static Func<bool> Reader(string typeName, string member)
        {
            var t = AccessTools.TypeByName(typeName);
            if (t == null) return null;
            var f = AccessTools.Field(t, member);
            if (f != null && f.IsStatic && f.FieldType == typeof(bool)) return () => (bool)f.GetValue(null);
            var p = AccessTools.Property(t, member);
            var g = p?.GetGetMethod(true);
            if (g != null && g.IsStatic && p.PropertyType == typeof(bool)) return () => (bool)g.Invoke(null, null);
            return null;
        }

        // ---- Obenseuer PT-BR (spec §5.3, D20) ----
        // The translation mod translates the game's text where it is drawn. Our IMGUI window is outside its reach, so
        // the rebuild asks its Translator for each stored English string. Resolved once by reflection; any exception
        // turns the bridge off for the session with one warning, and the window shows the raw text.
        internal static class PtBr
        {
            private const string PtBrGuid = "redsocket.obenseuer.ptbr";
            private static bool _resolved, _failed;
            private static FieldInfo _translatorField;
            private static ConfigEntry<bool> _enabled;
            private static MethodInfo _translate, _resolve;

            // Read at each model rebuild (main thread only).
            internal static bool Active
            {
                get
                {
                    if (!Cfg.Hot.TranslateWithPtBr || !Resolve()) return false;
                    try { return _enabled == null || _enabled.Value; }
                    catch (Exception e) { Fail(e); return false; }
                }
            }

            internal static string Translate(string s)
            {
                if (string.IsNullOrEmpty(s) || _failed || _translate == null) return null;
                try
                {
                    var tr = _translatorField.GetValue(null);
                    return tr == null ? null : _translate.Invoke(tr, new object[] { s }) as string;
                }
                catch (Exception e) { Fail(e); return null; }
            }

            // GenderMarkers.Resolve, when the mod has it; otherwise the text as it is.
            internal static string Gender(string s)
            {
                if (string.IsNullOrEmpty(s) || _failed || _resolve == null) return s;
                try { return _resolve.Invoke(null, new object[] { s }) as string ?? s; }
                catch (Exception e) { Fail(e); return s; }
            }

            private static bool Resolve()
            {
                if (_resolved) return !_failed;
                _resolved = true;
                try
                {
                    if (!Chainloader.PluginInfos.ContainsKey(PtBrGuid)) { _failed = true; return false; }
                    var plugin = AccessTools.TypeByName("ObenseuerPTBR.Plugin");
                    _translatorField = plugin != null ? AccessTools.Field(plugin, "Translator") : null;
                    var enabledField = plugin != null ? AccessTools.Field(plugin, "Enabled") : null;
                    _enabled = enabledField?.GetValue(null) as ConfigEntry<bool>;
                    var trType = _translatorField?.FieldType;
                    _translate = trType != null ? AccessTools.Method(trType, "Translate", new[] { typeof(string) }) : null;
                    var markers = AccessTools.TypeByName("ObenseuerPTBR.GenderMarkers");
                    _resolve = markers != null ? AccessTools.Method(markers, "Resolve", new[] { typeof(string) }) : null;
                    if (_translatorField == null || _translate == null || _translate.ReturnType != typeof(string))
                    {
                        _failed = true;
                        Log.Warn("PT-BR: the translation mod is running, but its translator wasn't found; the log shows English.");
                        return false;
                    }
                    Log.Info("PT-BR: the log is shown through the translation mod" + (_resolve == null ? " (no gender markers)." : "."));
                    return true;
                }
                catch (Exception e) { Fail(e); return false; }
            }

            private static void Fail(Exception e)
            {
                if (_failed) return;
                _failed = true;
                Log.Warn($"PT-BR: the translation bridge failed ({e.GetType().Name}: {e.Message}); the log shows English for the rest of this session.");
            }
        }
    }
}

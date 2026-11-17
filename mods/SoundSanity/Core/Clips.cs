using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;

namespace SoundSanity
{
    // Which clips count as radio music (spec §4.2). The default pattern extends the U-012 entry's with channels 2 and 4 of
    // every radio (hilla, nain_on_marjat), the Kurahaara greenhouse loop and the General Store loop; static, the switch
    // clicks and the bathroom radio effects are excluded. Results are cached per clip and dropped on a pattern change.
    internal static class Clips
    {
        private const RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

        private static Regex _music, _exclude;
        private static bool _compiled;
        private static readonly Dictionary<AudioClip, bool> Cache = new Dictionary<AudioClip, bool>();

        internal static void Recompile()
        {
            _music = Compile(Cfg.Radio.MusicClipPattern, Cfg.DefaultMusicPattern);
            _exclude = Compile(Cfg.Radio.ExcludeClipPattern, Cfg.DefaultExcludePattern);
            _compiled = true;
            Cache.Clear();
        }

        // An invalid pattern logs one warning per value and falls back to the default; the player's text stays as written.
        // An empty pattern matches nothing.
        private static Regex Compile(BepInEx.Configuration.ConfigEntry<string> e, string fallback)
        {
            string raw = e.Value ?? "";
            if (raw.Trim().Length == 0) return null;
            try { return new Regex(raw, Opts); }
            catch (ArgumentException ex)
            {
                Log.WarnOnce("regex|" + raw, $"Radio.{e.Definition.Key} is not a valid pattern ({ex.Message}); using the default until it is fixed.");
                return new Regex(fallback, Opts);
            }
        }

        internal static bool IsMusic(AudioClip clip)
        {
            if (clip == null) return false;
            if (Cache.TryGetValue(clip, out bool m)) return m;
            if (!_compiled) Recompile();
            string name = clip.name;
            m = _music != null && _music.IsMatch(name) && (_exclude == null || !_exclude.IsMatch(name));
            Cache[clip] = m;
            return m;
        }

        // Unloaded clips would otherwise pile up as dead keys.
        internal static void OnSceneLoaded() => Cache.Clear();
    }
}

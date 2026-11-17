using System;
using UnityEngine.Audio;

namespace SoundSanity
{
    // The game's real mixer and its groups (spec §3.1). The game ships two assets called MainMixer with the same group
    // names and IDs: Resources/sounds/MainMixer is the one VolumeSave writes the sliders to; AudioMixerController/MainMixer
    // is a copy some Addressables prefabs still point at. Mixers and groups are therefore always compared by instance,
    // never by name, and the hot paths compare against the cached groups below.
    internal static class Mixers
    {
        private static AudioMixer _main;
        private static bool _mainFromSliders;
        private static AudioMixerGroup _effects, _music, _mainGroup;

        // The mixer the volume sliders drive. VolumeSave is not DontDestroyOnLoad (SRC/VolumeSave.cs:23-33), so its
        // instance can be missing for a moment; the fallbacks are the game's own (AudioSourceMaker.MainMixer, then the
        // Resources asset), and the slider owner's mixer replaces them as soon as it is there.
        internal static AudioMixer Main
        {
            get
            {
                if (_main != null && _mainFromSliders) return _main;
                var vs = VolumeSave.instance;
                if (vs != null && Vanilla.VolumeSaveMixer != null)
                {
                    var m = Vanilla.VolumeSaveMixer(vs);
                    if (m != null)
                    {
                        if (_main != null && _main != m) Log.Warn($"Routing: the game's mixer changed (id {_main.GetInstanceID()} -> {m.GetInstanceID()}).");
                        if (_main != m) { _effects = _music = _mainGroup = null; }
                        _main = m;
                        _mainFromSliders = true;
                        Log.Info($"Main mixer '{m.name}' id={m.GetInstanceID()}");
                        return _main;
                    }
                }
                if (_main != null) return _main;
                _main = Vanilla.AsmMainMixer();
                if (_main == null) _main = UnityEngine.Resources.Load<AudioMixer>("Sounds/MainMixer");
                return _main;
            }
        }

        internal static AudioMixerGroup Exact(string name)
        {
            var m = Main;
            return m == null ? null : Array.Find(m.FindMatchingGroups(name), g => g.name == name);
        }

        // The game's default group for world sounds (MiscAudioPlayer.defaultMixerGroup), when it is on the real mixer.
        internal static AudioMixerGroup Effects
        {
            get
            {
                var main = Main;
                if (_effects != null && _effects.audioMixer == main) return _effects;
                var d = MiscAudioPlayer.instance != null ? MiscAudioPlayer.instance.defaultMixerGroup : null;
                return _effects = d != null && d.audioMixer == main ? d : Exact("Effects");
            }
        }

        internal static AudioMixerGroup Music
        {
            get
            {
                var main = Main;
                if (_music != null && _music.audioMixer == main) return _music;
                return _music = Exact("Music");
            }
        }

        // The mixer's root group "Main": only the Master slider applies to it.
        internal static AudioMixerGroup MainGroup
        {
            get
            {
                var main = Main;
                if (_mainGroup != null && _mainGroup.audioMixer == main) return _mainGroup;
                return _mainGroup = Exact("Main");
            }
        }
    }
}
